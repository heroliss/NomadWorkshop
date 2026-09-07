using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Navigation;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        [SerializeField, Tooltip("正式运行使用 NavMeshAgent 局部避让；同步 Soak 显式使用角点快进，不提供交通证据。")]
        private bool nativeLocomotion = true;
        private bool _synchronousLocomotion;
        private bool UsesNativeLocomotion => nativeLocomotion && !_synchronousLocomotion;

        private readonly struct SynchronousLocomotionScope : IDisposable
        {
            private readonly NomadFoundationSystem _system;
            internal SynchronousLocomotionScope(NomadFoundationSystem system)
            {
                _system = system;
                system.SuspendNativeLocomotion();
                system._synchronousLocomotion = true;
            }
            public void Dispose()
            {
                _system._synchronousLocomotion = false;
                _system.RebindNativeLocomotion();
            }
        }

        private DeckResidentMotor EnsureResidentMotor(FoundationResidentExecution resident) =>
            resident.Motor ??= _navigation.CreateResidentMotor(resident.StableId,
                resident.State.ResidentLocalPosition.Value, resident.State.ResidentLocalYawDegrees.Value);

        private void ReadNativeLocomotionFrame()
        {
            if (!UsesNativeLocomotion) return;
            // 建造、恢复与显式姿态迁移可能发生在上次 Physics 步之后；身体约束读取当前 Collider。
            Physics.SyncTransforms();
            foreach (var resident in _residents)
            {
                var motor = EnsureResidentMotor(resident);
                if (!motor.IsOnNavMesh)
                {
                    if (!motor.Warp(resident.State.ResidentLocalPosition.Value, resident.State.ResidentLocalYawDegrees.Value))
                        continue;
                    if (resident.PathCorners.Length > 0) motor.SetDestination(
                        resident.HasNativeDetour ? resident.NativeDetourTarget : resident.NativePathTarget);
                }
                motor.CommitPhysicalMovement();
                ReadNativePose(resident);
            }
            // 所有人使用同一帧已经提交的脚底决定让路，避免顺序混合旧/新身体位置。
            foreach (var resident in _residents) ConfigureNativeMotion(resident);
        }

        private void ReadNativePose(FoundationResidentExecution resident)
        {
            if (resident.Motor == null) return;
            resident.State.ResidentLocalPosition.Value = ToNavigationPoint(resident.Motor.LocalPosition);
            resident.State.ResidentLocalYawDegrees.Value = resident.Motor.LocalYaw;
        }

        private void ConfigureNativeMotion(FoundationResidentExecution resident)
        {
            resident.Motor?.ConfigureMotion(residentMoveSpeed, _model.SimulationSpeed.Value,
                UsesNativeLocomotion && !_model.IsPaused.Value &&
                _model.BuildTransactionPhase.Value == FoundationBuildTransactionPhase.Idle &&
                resident.PathCorners.Length > 0 && !resident.NativeDetourReached && !ShouldWaitForYield(resident));
        }

        private bool ShouldWaitForYield(FoundationResidentExecution resident)
        {
            foreach (var other in _residents)
                if (other.YieldingForResidentId == resident.StableId && other.PathCorners.Length > 0 &&
                    HorizontalDistance(resident.State.ResidentLocalPosition.Value,
                        other.State.ResidentLocalPosition.Value) < 0.65f)
                    return true;
            return false;
        }

        private void SuspendNativeLocomotion()
        {
            foreach (var resident in _residents)
            {
                if (UsesNativeLocomotion) ReadNativePose(resident);
                resident.Motor?.Suspend();
            }
        }

        // 重建/快进已经显式改写业务位置后，只从业务同步到物理，不能反向读回旧 Agent 覆盖新世界。
        private void RebindNativeLocomotion()
        {
            foreach (var resident in _residents)
            {
                if (resident.Motor == null) continue;
                resident.ClearNativeDetour();
                if (!resident.Motor.Warp(resident.State.ResidentLocalPosition.Value, resident.State.ResidentLocalYawDegrees.Value))
                    continue;
                if (resident.PathCorners.Length > 0) resident.Motor.SetDestination(resident.NativePathTarget);
                ConfigureNativeMotion(resident);
            }
        }

        private bool AssignNativePath(Vector3 sampledGoal)
        {
            _resident.ClearNativeDetour();
            _resident.NativePathTarget = sampledGoal;
            if (!UsesNativeLocomotion) return true;
            var motor = EnsureResidentMotor(_resident);
            if (!motor.IsOnNavMesh && !motor.Warp(_resident.State.ResidentLocalPosition.Value,
                    _resident.State.ResidentLocalYawDegrees.Value)) return false;
            _resident.NativeProgressRemainingMeters = float.PositiveInfinity;
            _resident.NativeStallSeconds = 0f;
            _resident.NativeRetrySeconds = 0f;
            _resident.State.MovementStallMilliseconds.Value = 0L;
            bool assigned = motor.SetDestination(sampledGoal);
            ConfigureNativeMotion(_resident);
            return assigned;
        }

        private bool AdvanceNativeResidentPath(float deltaTime)
        {
            var motor = EnsureResidentMotor(_resident);
            ReadNativePose(_resident);
            if (_resident.HasNativeDetour)
            {
                AdvanceNativeDetour(deltaTime);
                return false;
            }
            Vector3 exactGoal = _resident.PathCorners[_resident.PathCorners.Length - 1];
            bool blockedAtGoal = HasOtherResidentAt(_resident, exactGoal);
            if (!blockedAtGoal && motor.TryFinishDocking(exactGoal, _resident.HasDockingYaw, _resident.DockingYawDegrees))
            {
                ReadNativePose(_resident);
                _resident.State.MovementStallMilliseconds.Value = 0L;
                if (_resident.State.LastBlocker.Value.StartsWith("TrafficWaiting")) _resident.State.LastBlocker.Value = string.Empty;
                ClearActivePath();
                return true;
            }

            float remaining = motor.RemainingDistance;
            if (!float.IsInfinity(remaining) && !float.IsNaN(remaining))
                SetFloat(_resident.State.RemainingPathMeters, remaining);
            // 绕着被占目标晃动也有位移；只有剩余路程取得新进展才清零等待，不能被绕圈掩盖。
            if (motor.PathComplete && !float.IsInfinity(remaining) && !float.IsNaN(remaining) &&
                _resident.NativeProgressRemainingMeters - remaining > 0.02f)
            {
                _resident.NativeProgressRemainingMeters = remaining;
                _resident.NativeStallSeconds = 0f;
                if (_resident.State.LastBlocker.Value.StartsWith("TrafficWaiting")) _resident.State.LastBlocker.Value = string.Empty;
            }
            else _resident.NativeStallSeconds += Mathf.Max(0f, deltaTime);
            SetLong(_resident.State.MovementStallMilliseconds, (long)(_resident.NativeStallSeconds * 1000f));
            // 散步或尚未开始的爱好都不值得长期占住通路；必要工作仍保留原资源所有权。
            // 只释放这次可选行动的工作位，不走“到达”分支，也不发放休闲完成收益。
            if (_resident.NativeStallSeconds >= 3f &&
                _resident.Phase is (FoundationResidentPhase.MovingToLeisure or FoundationResidentPhase.MovingToHobby) &&
                _resident.StopVisit == null &&
                _resident.DriverLease == null && _resident.ActiveHaul == null &&
                _resident.ActiveWaterAction == null && _resident.ActiveWorldItemMove == null &&
                _resident.ActiveRepairPartUse == null)
            {
                ReleaseActiveInteractionSpace(publishProjection: true);
                ClearActivePath();
                ClearActiveMoveIntent();
                _resident.LeisureKind = FoundationLeisureKind.None;
                _resident.LeisureOutcomeScale = 1f;
                _resident.State.LastBlocker.Value = string.Empty;
                _resident.DecisionRetryRemaining = Mathf.Max(0.1f, residentDecisionRetrySeconds);
                SetResidentPhase(FoundationResidentPhase.Idle, "通路暂时拥挤，已释放未开始的休闲活动并重新安排");
                return false;
            }
            _resident.NativeRetrySeconds -= deltaTime;
            if (_resident.NativeStallSeconds < 1f || _resident.NativeRetrySeconds > 0f) return false;
            _resident.NativeRetrySeconds = 2f;
            _resident.State.LastBlocker.Value = "TrafficWaiting · 等待居民让路，保留已取得的资源与工作位";
            TryYieldSoftResidents(_resident, exactGoal);
            TryYieldMovingResident(_resident);
            // 只重算引擎路径，不重复领取物品/容量，也不重启正在携带的业务事务。
            if (!motor.PathPending) motor.SetDestination(_resident.NativePathTarget);
            ConfigureNativeMotion(_resident);
            return false;
        }

        private bool HasOtherResidentAt(FoundationResidentExecution moving, Vector3 point)
        {
            float clearance = _navigation.NavigationAgentRadiusMeters * 2f - 0.02f;
            foreach (var other in _residents)
                if (other != moving && HorizontalDistance(point, other.State.ResidentLocalPosition.Value) < clearance)
                    return true;
            return false;
        }

        private void TryYieldSoftResidents(FoundationResidentExecution moving, Vector3 goal)
        {
            foreach (var other in _residents)
                TryYieldSoftResident(moving, other, goal);
        }

        // 请求者的两秒重试可能一直错过“完成爱好 → Idle → 再次占位”的短窗口。
        // 原生闲人在重选行动前回应已经存在的等待；不轮询新目标、不改确定性 Soak 决策序列。
        private bool TryRespondToWaitingTraffic()
        {
            if (!UsesNativeLocomotion) return false;
            foreach (var moving in _residents)
            {
                if (moving.PathCorners.Length == 0 || moving.NativeStallSeconds < 1f) continue;
                if (TryYieldSoftResident(moving, _resident, moving.PathCorners[moving.PathCorners.Length - 1]))
                    return true;
            }
            return false;
        }

        private bool TryYieldSoftResident(FoundationResidentExecution moving, FoundationResidentExecution other, Vector3 goal)
        {
            if (other == moving || other.StopVisit != null || other.InteractionSpace is { IsActive: true } ||
                other.ActiveHaul != null || other.ActiveWorldItemMove != null || other.ActiveRepairPartUse != null ||
                other.Phase is not (FoundationResidentPhase.Relaxing or FoundationResidentPhase.Idle or
                    FoundationResidentPhase.WaitingForFacility or FoundationResidentPhase.RestingOnGround)) return false;
            Vector3 position = other.State.ResidentLocalPosition.Value;
            if (HorizontalDistance(position, moving.State.ResidentLocalPosition.Value) > 0.8f &&
                HorizontalDistance(position, goal) > 0.6f) return false;
            // 固定顺序只用于候选空地搜索，不增设网格预约或消耗居民决策随机流。
            for (var direction = 0; direction < 8; direction++)
            {
                float angle = (direction + (int)(other.OwnerId % 8UL)) * 45f * Mathf.Deg2Rad;
                Vector3 candidate = position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.85f;
                if (HorizontalDistance(candidate, goal) < 0.65f || HasOtherResidentAt(other, candidate) ||
                    !IsResidentPoseClear(deckLayout.LocalToPose(candidate))) continue;
                using var scope = UseResident(other);
                if (!TryAssignTravelPath(candidate)) continue;
                if (!YieldRouteKeepsBodyClearance(other, other.PathCorners))
                {
                    ClearActivePath();
                    continue;
                }
                _resident.LeisureKind = FoundationLeisureKind.None;
                _resident.PhaseRemaining = 0f;
                SetResidentPhase(FoundationResidentPhase.MovingToLeisure, $"为 {moving.StableId} 让出通路与工作位");
                // 请求者先等出身体净空；让位者仍需与其他行走者互相避让。
                _resident.YieldingForResidentId = moving.StableId;
                ConfigureNativeMotion(_resident);
                return true;
            }
            return false;
        }

        private void TryYieldMovingResident(FoundationResidentExecution requester)
        {
            if (requester.HasNativeDetour || !string.IsNullOrEmpty(requester.YieldingForResidentId)) return;
            foreach (var yielding in _residents)
            {
                // 编号只打破一对相遇者的对称选择；不改变 Agent 避让优先级，也不让任何身体穿过别人。
                if (yielding.OwnerId <= requester.OwnerId || yielding.PathCorners.Length == 0 ||
                    yielding.HasNativeDetour || !string.IsNullOrEmpty(yielding.YieldingForResidentId) ||
                    yielding.NativeStallSeconds < .75f || yielding.Motor == null) continue;
                Vector3 start = yielding.State.ResidentLocalPosition.Value;
                if (HorizontalDistance(start, requester.State.ResidentLocalPosition.Value) > .85f) continue;
                for (int direction = 0; direction < 8; direction++)
                {
                    float angle = (direction + (int)(yielding.OwnerId % 8UL)) * 45f * Mathf.Deg2Rad;
                    Vector3 candidate = start + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .95f;
                    if (HasOtherResidentAt(yielding, candidate) || !IsResidentPoseClear(deckLayout.LocalToPose(candidate)) ||
                        !TryCalculateTravelPath(start, candidate, out DeckNavPathProbe path) || path.PathLength > 1.7f ||
                        !YieldRouteKeepsBodyClearance(yielding, path.Corners)) continue;
                    if (!yielding.Motor.SetDestination(path.SampledEnd)) continue;
                    yielding.HasNativeDetour = true;
                    yielding.NativeDetourReached = false;
                    yielding.NativeDetourTarget = ToNavigationPoint(path.SampledEnd);
                    yielding.NativeDetourOrigin = start;
                    yielding.NativeDetourElapsed = 0f;
                    yielding.YieldingForResidentId = requester.StableId;
                    yielding.State.LastBlocker.Value = "TrafficYielding · 保留当前任务与携物，侧让后继续原路";
                    ConfigureNativeMotion(yielding);
                    return;
                }
            }
        }

        private void AdvanceNativeDetour(float deltaTime)
        {
            var resident = _resident;
            resident.NativeDetourElapsed += Mathf.Max(0, deltaTime);
            resident.NativeStallSeconds += Mathf.Max(0, deltaTime);
            SetLong(resident.State.MovementStallMilliseconds, (long)(resident.NativeStallSeconds * 1000f));
            if (!resident.NativeDetourReached && !HasOtherResidentAt(resident, resident.NativeDetourTarget) &&
                resident.Motor.TryFinishDocking(resident.NativeDetourTarget, false, 0f))
            {
                ReadNativePose(resident);
                resident.NativeDetourReached = true;
            }
            bool requesterPassed = true;
            foreach (var other in _residents)
                if (other.StableId == resident.YieldingForResidentId)
                    requesterPassed = HorizontalDistance(other.State.ResidentLocalPosition.Value,
                        resident.NativeDetourOrigin) > .95f;
            // 失败侧让有界返回原意图；不清零总停滞，避免反复侧让掩盖永久堵塞。
            if ((resident.NativeDetourReached && requesterPassed) || resident.NativeDetourElapsed >= 5f)
            {
                resident.ClearNativeDetour();
                resident.Motor.SetDestination(resident.NativePathTarget);
                resident.State.LastBlocker.Value = string.Empty;
            }
            ConfigureNativeMotion(resident);
        }

        private bool YieldRouteKeepsBodyClearance(FoundationResidentExecution yielding, IReadOnlyList<Vector3> corners)
        {
            Vector3 start = yielding.State.ResidentLocalPosition.Value;
            foreach (var other in _residents)
            {
                if (other == yielding) continue;
                Vector3 body = other.State.ResidentLocalPosition.Value;
                // 已经贴近身体时允许向外走，但不能先穿过对方再到空地；终点空闲不足以证明路线可让。
                float clearance = Mathf.Min(_navigation.NavigationAgentRadiusMeters * 2f + 0.02f,
                    HorizontalDistance(start, body) - 0.01f);
                Vector3 previous = start;
                foreach (Vector3 corner in corners)
                {
                    Vector3 segment = corner - previous;
                    Vector3 offset = body - previous;
                    segment.y = offset.y = 0f;
                    float t = segment.sqrMagnitude > 0.000001f
                        ? Mathf.Clamp01(Vector3.Dot(offset, segment) / segment.sqrMagnitude) : 0f;
                    if (HorizontalDistance(previous + segment * t, body) < clearance) return false;
                    previous = corner;
                }
            }
            return true;
        }

#if UNITY_EDITOR
        /// <summary>原生移动夹具显式启用；既有确定性规则夹具在配置定义时关闭。</summary>
        public void ConfigureNativeLocomotionForTests(bool enabled)
        {
            SuspendNativeLocomotion();
            nativeLocomotion = enabled;
            if (enabled) return;
            foreach (var resident in _residents)
            {
                resident.Motor?.Dispose();
                resident.Motor = null;
            }
        }
#endif
    }
}
