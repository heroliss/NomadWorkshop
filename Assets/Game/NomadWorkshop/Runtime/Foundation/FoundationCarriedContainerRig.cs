using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 手提液体容器的模型绑定：握持、开口、可见分件和手臂避让体积属于同一表现实例。
    /// 仅描述模型与姿态输入，不持有物品身份/水量，不移动逻辑身体，也不负责交接时机。
    /// 更换模型时保留外层米制、底面 Y=0 的根，在编辑期重绑引用并验证实际动作。
    /// </summary>
    public sealed class FoundationCarriedContainerRig : MonoBehaviour
    {
        /// <summary>局部有向盒；frame 仅定位该盒，不要求是可见 Mesh 或单位立方体。</summary>
        [Serializable]
        public struct ClearanceVolume
        {
            public Transform frame;
            public Bounds bounds;
            public ClearanceVolume(Transform frame, Bounds bounds) { this.frame = frame; this.bounds = bounds; }
        }

        [SerializeField] private Transform carryPivot;
        [SerializeField] private Transform rightPalm;
        [SerializeField] private Transform opening;
        [SerializeField] private Transform closure;
        [SerializeField] private Transform fillIndicator;
        [SerializeField] private ClearanceVolume[] clearanceVolumes = Array.Empty<ClearanceVolume>();
        [SerializeField, Tooltip("地面拾放的最大身体局部位移；须与握点、现有 Avatar 的臂长/足部支撑一起验收。")]
        private Vector3 groundReachOffset = new(0f, -.55f, .20f);

        public Transform CarryPivot => carryPivot;
        public Transform RightPalm => rightPalm;
        public Transform Opening => opening;
        public Transform Closure => closure;
        public Transform FillIndicator => fillIndicator;
        public IReadOnlyList<ClearanceVolume> ClearanceVolumes => clearanceVolumes;
        public Vector3 CarryPivotLocalPosition => transform.InverseTransformPoint(carryPivot.position);
        public Vector3 OpeningLocalPosition => transform.InverseTransformPoint(opening.position);
        public Vector3 GroundReachOffset => groundReachOffset;
        /// <summary>静止罐体相对根的保守横向半宽，用于拾放轨迹绕开膝盖，不是库存占地。</summary>
        public float LateralClearance => Mathf.Max(Mathf.Abs(LocalClearanceBounds.min.x), Mathf.Abs(LocalClearanceBounds.max.x));

        /// <summary>编辑期或灰盒工厂配置；调用方仍拥有实例。配置错误立即抛异常，不静默回退到旧锚点。</summary>
        public void Configure(Transform pivot, Transform palm, Transform mouth, Transform cap, Transform fill,
            ClearanceVolume[] volumes, Vector3 groundOffset)
        {
            carryPivot = pivot; rightPalm = palm; opening = mouth; closure = cap; fillIndicator = fill;
            clearanceVolumes = (ClearanceVolume[])(volumes ?? throw new ArgumentNullException(nameof(volumes))).Clone();
            groundReachOffset = groundOffset;
            ValidateBindings();
        }

        /// <summary>确认必需角色、引用归属、米制根及正体积；需要在实际 Prefab 实例上再次调用。</summary>
        public void ValidateBindings()
        {
            Require(carryPivot, "携行枢轴"); Require(rightPalm, "掌心"); Require(opening, "罐口");
            Require(closure, "封盖"); Require(fillIndicator, "液位显示");
            if (Vector3.Distance(transform.localScale, Vector3.one) > .0001f)
                throw new InvalidOperationException(name + "：容器外层根必须保持单位缩放。");
            if (!Finite(groundReachOffset) || groundReachOffset.y > 0f || groundReachOffset.magnitude > 1f)
                throw new InvalidOperationException(name + "：地面拾放位移无效。");
            if (clearanceVolumes == null || clearanceVolumes.Length == 0)
                throw new InvalidOperationException(name + "：缺少罐体避让体积。");
            foreach (var volume in clearanceVolumes)
            {
                Require(volume.frame, "避让坐标");
                Vector3 size = volume.bounds.size, scale = volume.frame.lossyScale;
                if (!Finite(size) || !Finite(volume.bounds.center) || !Finite(scale) ||
                    size.x <= 0f || size.y <= 0f || size.z <= 0f || scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
                    throw new InvalidOperationException(name + "：避让体积或缩放无效。");
            }
        }

        /// <summary>按 Avatar 臂长与本罐体在握点内侧的真实范围标定携行目标，返回居民根局部坐标。</summary>
        public Vector3 GetGripPosition(ResidentHumanoidPresentation humanoid)
        {
            Animator animator = humanoid.Animator;
            Transform upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            float length = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
            Vector3 shoulder = humanoid.transform.InverseTransformPoint(upper.position);
            Bounds body = LocalClearanceBounds;
            float inside = shoulder.x >= 0f ? CarryPivotLocalPosition.x - body.min.x : body.max.x - CarryPivotLocalPosition.x;
            return new Vector3(shoulder.x + Mathf.Sign(shoulder.x) * (inside + .10f), shoulder.y - length * .72f, .08f);
        }

        private Bounds LocalClearanceBounds
        {
            get
            {
                bool first = true; Bounds result = default;
                foreach (var volume in clearanceVolumes)
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 p = transform.InverseTransformPoint(volume.frame.TransformPoint(volume.bounds.center +
                        Vector3.Scale(volume.bounds.extents, new Vector3(x, y, z))));
                    if (first) { result = new Bounds(p, Vector3.zero); first = false; }
                    else result.Encapsulate(p);
                }
                return result;
            }
        }

        private void Require(Transform value, string role)
        {
            if (value == null || (value != transform && !value.IsChildOf(transform)) ||
                !Finite(transform.InverseTransformPoint(value.position)))
                throw new InvalidOperationException(name + "：缺少或越界的容器绑定：" + role);
        }

        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
