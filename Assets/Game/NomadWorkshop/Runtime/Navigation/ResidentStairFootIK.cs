using UnityEngine;

namespace Game.NomadWorkshop.Navigation
{
    /// <summary>
    /// 跨层实验的局部足部接触 Adapter，挂在 Animator 上，仅修正表现。
    /// 有界向下射线只接受本导航空间的踏面，不把头顶楼板或角色身体当脚下支撑。
    /// </summary>
    public sealed class ResidentStairFootIK : MonoBehaviour
    {
        private Animator _animator;
        private Transform _resident;
        private Transform _surfaceRoot;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        public float LeftContactWeight { get; private set; }
        public float RightContactWeight { get; private set; }
        public Vector3 LeftTarget { get; private set; }
        public Vector3 RightTarget { get; private set; }
        public float PelvisOffset { get; private set; }
        public float LeftTargetError { get; private set; }
        public float RightTargetError { get; private set; }

        public void Configure(Animator animator, Transform resident, Transform surfaceRoot)
        {
            _animator = animator; _resident = resident; _surfaceRoot = surfaceRoot;
        }

        /// <summary>
        /// 由携物 IK 的同一次回调调用，先设置踏面支撑并返回骨盆世界位移，供手臂采用修正后的肩高。
        /// 本组件不单独订阅 OnAnimatorIK，避免依赖多个组件回调顺序或重复施加骨盆位移。
        /// </summary>
        public Vector3 ApplySupport()
        {
            if (!isActiveAndEnabled || _animator == null || _surfaceRoot == null) return Vector3.zero;
            Vector3 animatedLeft = _animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            Vector3 animatedRight = _animator.GetIKPosition(AvatarIKGoal.RightFoot);
            LeftContactWeight = Evaluate(AvatarIKGoal.LeftFoot, _animator.leftFeetBottomHeight, out Vector3 left,
                out Quaternion leftRotation, out bool leftSupport);
            RightContactWeight = Evaluate(AvatarIKGoal.RightFoot, _animator.rightFeetBottomHeight, out Vector3 right,
                out Quaternion rightRotation, out bool rightSupport);
            // 胶囊体可能已经跨上前一级，后脚仍应踩在低一级。只挪脚会超过腿长；
            // 骨盆小幅下降给腿保留弯曲空间，不改变逻辑根、水罐或碰撞体的位置。
            // 胶囊体前缘与后脚可跨两个踏步（每级约 18 cm），限幅需覆盖这一实际高度差。
            PelvisOffset = Mathf.Clamp(Mathf.Min((left.y-animatedLeft.y)*LeftContactWeight,
                (right.y-animatedRight.y)*RightContactWeight), -.38f, 0f);
            // 骨盆下降不能把摆动脚带进前方高踏面；只有将要穿面时才增加这只脚的接触。
            if (leftSupport && animatedLeft.y + PelvisOffset < left.y) LeftContactWeight = 1f;
            if (rightSupport && animatedRight.y + PelvisOffset < right.y) RightContactWeight = 1f;
            _animator.bodyPosition += Vector3.up * PelvisOffset;
            Apply(AvatarIKGoal.LeftFoot, LeftContactWeight, left, leftRotation);
            Apply(AvatarIKGoal.RightFoot, RightContactWeight, right, rightRotation);
            LeftTarget = left; RightTarget = right;
            return Vector3.up * PelvisOffset;
        }

        private void LateUpdate()
        {
            if (_animator == null) return;
            // Animator 求解之后量实际骨骼，避免测试把 Update 中上一帧 IK 目标与本帧根位移相减。
            LeftTargetError = Vector3.Distance(_animator.GetBoneTransform(HumanBodyBones.LeftFoot).position, LeftTarget);
            RightTargetError = Vector3.Distance(_animator.GetBoneTransform(HumanBodyBones.RightFoot).position, RightTarget);
        }

        private float Evaluate(AvatarIKGoal foot, float soleHeight, out Vector3 target, out Quaternion rotation,
            out bool hasSupport)
        {
            Vector3 animated = _animator.GetIKPosition(foot);
            target = animated;
            rotation = _animator.GetIKRotation(foot);
            hasSupport = false;
            int count = Physics.RaycastNonAlloc(animated + Vector3.up * .32f, Vector3.down, _hits,
                .85f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            RaycastHit support = default;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                if (hit.collider is CharacterController || !hit.collider.transform.IsChildOf(_surfaceRoot) ||
                    hit.normal.y < .8f || hit.distance >= nearest) continue;
                nearest = hit.distance;
                support = hit;
            }
            if (float.IsPositiveInfinity(nearest))
            {
                return 0f;
            }
            float animatedLift = Mathf.Max(0f, animated.y - _resident.position.y - soleHeight);
            hasSupport = true;
            float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.045f, .14f, animatedLift));
            // 抬起的脚保留原来的摆动轨迹；强迫空中脚贴地会扭膝，也会把骨盆压得过低。
            target.y = support.point.y + soleHeight;
            target.y = Mathf.Clamp(target.y, animated.y - .38f, animated.y + .38f);
            rotation = Quaternion.FromToRotation(rotation * Vector3.up, support.normal) * rotation;
            return weight;
        }

        private void Apply(AvatarIKGoal foot, float weight, Vector3 target, Quaternion rotation)
        {
            _animator.SetIKPositionWeight(foot, weight);
            _animator.SetIKRotationWeight(foot, weight);
            if (weight <= 0f) return;
            _animator.SetIKPosition(foot, target);
            _animator.SetIKRotation(foot, rotation);
        }
    }
}
