using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Navigation;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 自有设施 Prefab 的静态空间输入，供编辑期生成定义和启动时核对来源。
    /// Frame/Slot 为本组件根内的显式引用；动画接触仍由 ArtRig 配置，不逐帧重写玩法空间。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoundationFacilitySpaceAuthoring : MonoBehaviour
    {
        [Serializable]
        public struct Footprint
        {
            public string id;
            public Transform frame;
            public Vector2 sizeMeters;
        }

        [Serializable]
        public struct Region
        {
            public string id;
            public Transform frame;
            public Vector2 sizeMeters;
            public float edgeInsetMeters;
            public string[] acceptedCategories;
        }

        [SerializeField] private Footprint[] footprints = Array.Empty<Footprint>();
        [SerializeField] private FacilityInteractionGroup[] interactionGroups = Array.Empty<FacilityInteractionGroup>();
        [SerializeField] private Region[] placementRegions = Array.Empty<Region>();
        public IReadOnlyList<Footprint> Footprints => Array.AsReadOnly(footprints);
        public IReadOnlyList<Region> PlacementRegions => Array.AsReadOnly(placementRegions);
        public IReadOnlyList<FacilityInteractionGroup> InteractionGroups => Array.AsReadOnly(interactionGroups);

        /// <summary>
        /// 转换合法水平矩形和站位，保留局部支撑高度。拒绝剪切、反射、倾斜面及非零站位高度，
        /// 不把三维几何静默投影成当前单层无法表达的区域，不由渲染 Bounds 猜占地。
        /// </summary>
        public FoundationFacilitySpaceSnapshot CreateSnapshot()
        {
            // View 保留模型根的 localScale，定义却以米为单位；缩放应放在根内的制作层级中。
            if (!Finite(transform.localScale) || Vector3.Distance(transform.localScale, Vector3.one) > .00001f)
                throw new InvalidOperationException("设施模型根必须为单位缩放，请将缩放移到内部模型层级。");
            if (footprints == null || interactionGroups == null || placementRegions == null)
                throw new InvalidOperationException("设施空间输入数组不能为空。");
            var parts = new List<NomadFacilityFootprintPartDefinition>();
            var groups = new List<NomadFacilityInteractionGroupDefinition>();
            var regions = new List<NomadPlacementRegionDefinition>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Footprint part in footprints)
            {
                RequireId(part.id, ids, "占地");
                Rectangle(part.frame, part.sizeMeters, out Vector3 center, out Vector2 size, out float yaw);
                parts.Add(new NomadFacilityFootprintPartDefinition(new Vector2(center.x, center.z), size, yaw));
            }
            ids.Clear();
            foreach (FacilityInteractionGroup group in interactionGroups)
            {
                if (group == null) throw new InvalidOperationException("空间交互组引用为空。");
                RequireChild(group.transform); group.ValidateOrThrow(); RequireId(group.GroupId, ids, "交互组");
                var slots = new List<NomadFacilityInteractionSlotDefinition>();
                foreach (FacilityInteractionSlot slot in group.AlternativeSlots)
                {
                    RequireChild(slot.transform);
                    Frame(slot.transform, out Vector3 p, out _, out _, out float yaw);
                    if (Mathf.Abs(p.y) > .0005f) throw new InvalidOperationException("当前单层站位必须在设施根 Y=0 平面："+slot.SlotId);
                    slots.Add(new NomadFacilityInteractionSlotDefinition(slot.SlotId, new Vector2(p.x, p.z), yaw));
                }
                groups.Add(new NomadFacilityInteractionGroupDefinition(group.GroupId, group.RequiredForOperation, slots.ToArray()));
            }
            ids.Clear();
            foreach (Region region in placementRegions)
            {
                RequireId(region.id, ids, "放置区域");
                Rectangle(region.frame, region.sizeMeters, out Vector3 p, out Vector2 size, out float yaw);
                if (p.y < -.0005f || !float.IsFinite(region.edgeInsetMeters) || region.edgeInsetMeters < 0)
                    throw new InvalidOperationException("支撑高度和区域内缩必须为非负有限值："+region.id);
                // 内缩以设施根中的真实米数表示，不跟随非均匀缩放成为两个不同值。
                if (region.edgeInsetMeters*2 >= Mathf.Min(size.x, size.y))
                    throw new InvalidOperationException("区域内缩不能覆盖整个支撑面："+region.id);
                if (region.acceptedCategories == null || region.acceptedCategories.Length == 0)
                    throw new InvalidOperationException("区域必须声明可接收物品类别："+region.id);
                regions.Add(new NomadPlacementRegionDefinition(region.id, new Vector2(p.x, p.z), size, yaw, p.y,
                    region.edgeInsetMeters, region.acceptedCategories));
            }
            return new FoundationFacilitySpaceSnapshot(parts, groups, regions);
        }

        private void Rectangle(Transform frame, Vector2 authoredSize, out Vector3 center, out Vector2 size, out float yaw)
        {
            if (!float.IsFinite(authoredSize.x) || !float.IsFinite(authoredSize.y) || authoredSize.x <= 0 || authoredSize.y <= 0)
                throw new InvalidOperationException("矩形必须有正数有限尺寸。");
            Frame(frame, out center, out Vector3 right, out Vector3 forward, out yaw);
            size = Vector2.Scale(authoredSize, new Vector2(right.magnitude, forward.magnitude));
            if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x < .05f || size.y < .05f)
                throw new InvalidOperationException("生成矩形必须为有限尺寸且不能小于当前模拟的 0.05 米最小尺寸。");
        }

        private void Frame(Transform frame, out Vector3 center, out Vector3 right, out Vector3 forward, out float yaw)
        {
            RequireChild(frame);
            Matrix4x4 relative = transform.worldToLocalMatrix * frame.localToWorldMatrix;
            center = relative.MultiplyPoint3x4(Vector3.zero);
            right = relative.MultiplyVector(Vector3.right); forward = relative.MultiplyVector(Vector3.forward);
            Vector3 up = relative.MultiplyVector(Vector3.up);
            if (!Finite(center) || !Finite(right) || !Finite(forward) || !Finite(up) || right.magnitude < .0001f || forward.magnitude < .0001f ||
                up.y <= 0 || Mathf.Abs(right.normalized.y) > .0001f || Mathf.Abs(forward.normalized.y) > .0001f ||
                Vector3.Dot(Vector3.Cross(forward.normalized, right.normalized), Vector3.up) < .9999f ||
                Mathf.Abs(Vector3.Dot(right.normalized, forward.normalized)) > .0001f || Vector3.Dot(up.normalized, Vector3.up) < .9999f)
                throw new InvalidOperationException("空间标记须为无剪切/反射的水平坐标系："+frame.name);
            yaw = Mathf.Atan2(forward.x, forward.z)*Mathf.Rad2Deg;
        }

        private void RequireChild(Transform frame)
        { if (frame == null || !frame.IsChildOf(transform)) throw new InvalidOperationException("空间标记必须明确引用设施根内的 Transform。"); }
        private static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        private static void RequireId(string id, HashSet<string> ids, string kind)
        { if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || !ids.Add(id)) throw new InvalidOperationException(kind+"需要无空白且唯一的稳定 id。"); }

#if UNITY_EDITOR
        /// <summary>制作配方填入显式引用，CreateSnapshot 校验合法性；不支持运行中热改空间。</summary>
        public void ConfigureForEditor(Footprint[] parts, FacilityInteractionGroup[] groups, Region[] regions)
        {
            if (Application.isPlaying) throw new InvalidOperationException("不能在 Play 中配置设施空间。");
            footprints = (Footprint[])parts.Clone(); interactionGroups = (FacilityInteractionGroup[])groups.Clone();
            placementRegions = (Region[])regions.Clone();
        }
#endif
    }
}
