using System;
using Game.Framework;
using Game.Framework.Common;
using Game.Framework.Systems;
using UnityEngine;

namespace Game.NomadWorkshop.Navigation
{
    /// <summary>
    /// 两层持桶实验的逻辑所有者。路线、身体和携物身份独立于 Animator；
    /// 恢复撤销旧 Motor 的 PlayerLoop 所有权后创建新实例，View 保留同一个水罐表现。
    /// </summary>
    public sealed class NomadStairTraversalSystem : MonoSystemBase
    {
        public const float UpperHeight = 3.2f;
        public static readonly Vector3 LowerGoal = new(-2f, 0f, 5.6f);
        public static readonly Vector3 UpperGoal = new(-2f, UpperHeight, 5.6f);
        private const ulong CarrierOwner = 1UL;
        private NomadStairTraversalModel _model;
        private DeckNavigationUtility _navigation;
        private NomadStairTrafficGate _traffic;
        private DeckResidentMotor _motor;
        private DisposableBag _motionBag;
        private float _elapsed;
        private Vector3 _goal;

        private void Start()
        {
            _model = this.GetModel<NomadStairTraversalModel>();
            _navigation = this.GetUtility<DeckNavigationUtility>();
            _traffic = transform.parent != null
                ? transform.parent.GetComponentInChildren<NomadStairTrafficGate>(true)
                : null;
            _navigation.BuildNow();
            if (!TryEndpoint(LowerGoal, out Vector3 start) || !TryEndpoint(UpperGoal, out _))
                throw new InvalidOperationException("跨层实验的上下平台缺少同高度 NavMesh。");
            CreateMotor(start, 180f);
            Publish(0, StairTraversalPhase.Ready, false, "已持有 8 L 水，准备上楼");
        }

        private void Update()
        {
            if (_motor == null) return;
            StairTraversalState state = _model.State.Value;
            bool moving = state.Phase == StairTraversalPhase.Moving && !state.Paused;
            _motor.ConfigureMotion(1.15f, 1f, moving);
            if (!moving) return;
            _motor.CommitPhysicalMovement();
            _elapsed += Time.unscaledDeltaTime;
            if (_motor.TryFinishDocking(_goal, true, state.TargetFloor == 1 ? 270f : 90f))
            {
                ReleaseTraffic();
                Publish(state.TargetFloor, StairTraversalPhase.Arrived, false,
                    state.TargetFloor == 1 ? "已把水带到二层" : "已把水带回一层");
                return;
            }
            if (_elapsed > 45f)
            {
                _motor.Stop();
                ReleaseAtSafeEndpoint();
                Publish(state.TargetFloor, StairTraversalPhase.Blocked, false, "路线未完成，请查看碰撞与导航证据");
                return;
            }
            Publish(state.TargetFloor, StairTraversalPhase.Moving, false,
                state.TargetFloor == 1 ? "持桶上楼" : "持桶下楼");
        }

        public bool MoveToFloor(int floor)
        {
            if (_motor == null || floor is < 0 or > 1) return false;
            bool alreadyOwned = _traffic != null && _traffic.CurrentOwner == CarrierOwner;
            if (_traffic != null && !_traffic.TryAcquire(CarrierOwner)) return false;
            Vector3 exact = floor == 1 ? UpperGoal : LowerGoal;
            if (!TryEndpoint(exact, out Vector3 target) ||
                !_navigation.TryCalculateCompleteLocalPath(_motor.LocalPosition, target, out DeckNavPathProbe path) ||
                Mathf.Abs(path.SampledEnd.y - exact.y) > .08f || !_motor.SetDestination(target))
            {
                if (!alreadyOwned) ReleaseTraffic();
                return false;
            }
            _goal = exact;
            _elapsed = 0f;
            Publish(floor, StairTraversalPhase.Moving, _model.State.Value.Paused,
                floor == 1 ? "持桶上楼" : "持桶下楼");
            return true;
        }

        public void SetPaused(bool paused)
        {
            if (_motor == null) return;
            if (paused) _motor.Suspend();
            StairTraversalState state = _model.State.Value;
            Publish(state.TargetFloor, state.Phase, paused, state.Status);
        }

        /// <summary>就地停止并保留水；未回到已验证的平台停靠点前仍独占通道，原持有人可以重新选楼层。</summary>
        public void Cancel()
        {
            if (_motor == null) return;
            _motor.Stop();
            ReleaseAtSafeEndpoint();
            Publish(_model.State.Value.TargetFloor, StairTraversalPhase.Cancelled, false, "已停在当前踏面，水仍在手中");
        }

        public string CaptureCheckpoint()
        {
            if (_motor == null) return string.Empty;
            StairTraversalState state = _model.State.Value;
            return JsonUtility.ToJson(new Checkpoint
            {
                version = 1, position = _motor.LocalPosition, yaw = _motor.LocalYaw,
                targetFloor = state.TargetFloor, moving = state.Phase == StairTraversalPhase.Moving,
                paused = state.Paused, waterMilliliters = state.WaterMilliliters,
            });
        }

        /// <summary>拒绝无效或另一高度的数据；验证失败不停止当前执行，不触碰正式游戏存档。</summary>
        public bool RestoreCheckpoint(string json)
        {
            if (_motor == null || string.IsNullOrWhiteSpace(json)) return false;
            Checkpoint saved;
            try { saved = JsonUtility.FromJson<Checkpoint>(json); }
            catch (ArgumentException) { return false; }
            if (saved == null || saved.version != 1 || saved.waterMilliliters != 8000 ||
                saved.targetFloor is < 0 or > 1 || !Finite(saved.position.x) || !Finite(saved.position.y) ||
                !Finite(saved.position.z) || !Finite(saved.yaw) ||
                !_navigation.TrySampleLocalPosition(saved.position, .25f, out Vector3 sample) ||
                Mathf.Abs(sample.y - saved.position.y) > .23f) return false;
            Vector3 exact = saved.targetFloor == 1 ? UpperGoal : LowerGoal;
            if (saved.moving && (!TryEndpoint(exact, out Vector3 target) ||
                !_navigation.TryCalculateCompleteLocalPath(sample, target, out _))) return false;
            bool needsTraffic = saved.moving || !IsSafeEndpoint(saved.position);
            bool alreadyOwned = _traffic != null && _traffic.CurrentOwner == CarrierOwner;
            if (needsTraffic && _traffic != null && !_traffic.TryAcquire(CarrierOwner)) return false;
            try { CreateMotor(saved.position, saved.yaw); }
            catch
            {
                if (needsTraffic && !alreadyOwned) ReleaseTraffic();
                throw;
            }
            if (!needsTraffic) ReleaseTraffic();
            Publish(saved.targetFloor, StairTraversalPhase.Ready, saved.paused, "已恢复携水检查点");
            return !saved.moving || MoveToFloor(saved.targetFloor);
        }

        private bool TryEndpoint(Vector3 exact, out Vector3 sampled) =>
            _navigation.TrySampleLocalPosition(exact, .12f, out sampled) && Mathf.Abs(sampled.y - exact.y) <= .08f;

        private void CreateMotor(Vector3 position, float yaw)
        {
            // 新实例先完成绑定，异常时旧执行仍然有效；成功后才撤旧所有权。
            DisposableBag candidateBag = Bag.CreateChild();
            try
            {
                DeckResidentMotor candidate = _navigation.CreateResidentMotor("stair-carrier", position, yaw, .22f, .2f);
                candidateBag.Add(candidate);
                _motionBag?.Dispose();
                _motionBag = candidateBag;
                _motor = candidate;
            }
            catch
            {
                candidateBag.Dispose();
                throw;
            }
        }

        private void Publish(int floor, StairTraversalPhase phase, bool paused, string status) =>
            _model.State.Value = new StairTraversalState(_motor.LocalPosition, _motor.LocalYaw, floor,
                phase, paused, 8000, status);

        private static bool Finite(float number) => !float.IsNaN(number) && !float.IsInfinity(number);

        // 实验按完整平台间搬运持有租约，不能仅凭高度判断已离开顶级踏板。
        private static bool IsSafeEndpoint(Vector3 position) =>
            Vector3.Distance(position, LowerGoal) <= .12f || Vector3.Distance(position, UpperGoal) <= .12f;

        private void ReleaseAtSafeEndpoint()
        {
            if (IsSafeEndpoint(_motor.LocalPosition)) ReleaseTraffic();
        }

        private void ReleaseTraffic()
        {
            if (_traffic != null) _traffic.Release(CarrierOwner);
        }

        protected override void OnDestroy()
        {
            ReleaseTraffic();
            base.OnDestroy();
        }

        [Serializable]
        private sealed class Checkpoint
        {
            public int version;
            public Vector3 position;
            public float yaw;
            public int targetFloor;
            public bool moving;
            public bool paused;
            public int waterMilliliters;
        }

    }
}
