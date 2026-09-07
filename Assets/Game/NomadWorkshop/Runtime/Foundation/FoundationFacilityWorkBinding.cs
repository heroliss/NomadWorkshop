using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>水流专用端点用途；额外的特效点不增加逻辑工位容量。</summary>
    public enum FoundationFacilityContactRole { WaterOutlet, WaterInlet, EffectOrigin }
    /// <summary>已有灌注机制的表现方向，不定义库存交接时机。</summary>
    public enum FoundationFacilityWaterFlow { None, FillCan, ReceiveFromCan }
    /// <summary>旋转轴在部件静止局部坐标中，平移轴在父节点坐标中。</summary>
    public enum FoundationFacilityMotionKind { Rotate, Translate }

    /// <summary>归一个精确动作绑定所有的接触点；id 在该绑定内唯一，节点名称不参与运行期解析。</summary>
    [Serializable]
    public struct FoundationFacilityContactBinding
    {
        [SerializeField] private string id;
        [SerializeField] private FoundationFacilityContactRole role;
        [SerializeField] private Transform target;
        public string Id => id;
        public FoundationFacilityContactRole Role => role;
        public Transform Target => target;
        public FoundationFacilityContactBinding(string id, FoundationFacilityContactRole role, Transform target)
        { this.id = id; this.role = role; this.target = target; }
    }

    /// <summary>
    /// 一个活动部件的静止姿态与行程。旋转量为度，平移量为父坐标单位；导入工具负责把米制方向转换进实际父级。
    /// 运行期只从已捕获的静止姿态投影，重复进度、暂停或恢复不会叠加运动。
    /// </summary>
    [Serializable]
    public sealed class FoundationFacilityMotionBinding
    {
        [SerializeField] private string id;
        [SerializeField] private Transform target;
        [SerializeField] private FoundationFacilityMotionKind kind;
        [SerializeField] private Vector3 localAxis;
        [SerializeField] private float amount;
        [NonSerialized] private Vector3 _restPosition;
        [NonSerialized] private Quaternion _restRotation;
        public string Id => id;
        public Transform Target => target;
        public FoundationFacilityMotionKind Kind => kind;
        public Vector3 LocalAxis => localAxis;
        public float Amount => amount;

        public FoundationFacilityMotionBinding(string id, Transform target, FoundationFacilityMotionKind kind,
            Vector3 localAxis, float amount)
        { this.id = id; this.target = target; this.kind = kind; this.localAxis = localAxis; this.amount = amount; }

        internal void Validate(Transform root, string owner)
        {
            FoundationFacilityWorkBinding.RequireId(id, owner + "/机构");
            FoundationFacilityWorkBinding.RequireChild(root, target, owner + "/" + id);
            if (!Enum.IsDefined(typeof(FoundationFacilityMotionKind), kind) ||
                !float.IsFinite(amount) || !float.IsFinite(localAxis.x) || !float.IsFinite(localAxis.y) ||
                !float.IsFinite(localAxis.z) || localAxis.sqrMagnitude < .000001f)
                throw new InvalidOperationException(owner + "/" + id + "：机构类型、轴或行程无效。");
        }

        internal void CaptureRest() { _restPosition = target.localPosition; _restRotation = target.localRotation; }
        internal void Reset() { target.localPosition = _restPosition; target.localRotation = _restRotation; }
        internal void Apply(float envelope)
        {
            if (kind == FoundationFacilityMotionKind.Rotate)
                target.localRotation = _restRotation * Quaternion.AngleAxis(amount * envelope, localAxis.normalized);
            else target.localPosition = _restPosition + localAxis.normalized * (amount * envelope);
        }
    }

    /// <summary>
    /// 精确组、可选 Slot 与业务阶段对应的表现配方。空 Slot 表示该组所有候选站位；与具体 Slot 的重叠匹配会被拒绝。
    /// 接触点数量可变，水流显式选择一个正确用途的端点 id；机构、点及其生命周期归设施表现实例所有。
    /// </summary>
    [Serializable]
    public sealed class FoundationFacilityWorkBinding
    {
        [SerializeField] private string id;
        [SerializeField] private string groupId;
        [SerializeField] private string slotId;
        [SerializeField] private FoundationResidentPhase phase;
        [SerializeField] private FoundationFacilityContactBinding[] contacts;
        [SerializeField] private FoundationFacilityMotionBinding[] motions;
        [SerializeField] private FoundationFacilityWaterFlow waterFlow;
        [SerializeField] private string waterEndpointId;
        [NonSerialized] private Transform _waterEndpoint;

        public string Id => id;
        public string GroupId => groupId;
        public string SlotId => slotId ?? string.Empty;
        public FoundationResidentPhase Phase => phase;
        public FoundationFacilityWaterFlow WaterFlow => waterFlow;
        public string WaterEndpointId => waterEndpointId;
        public IReadOnlyList<FoundationFacilityContactBinding> Contacts => contacts;
        public IReadOnlyList<FoundationFacilityMotionBinding> Motions => motions;
        internal Transform WaterEndpoint => _waterEndpoint;

        public FoundationFacilityWorkBinding(string id, string groupId, string slotId, FoundationResidentPhase phase,
            FoundationFacilityContactBinding[] contacts, FoundationFacilityMotionBinding[] motions,
            FoundationFacilityWaterFlow waterFlow = FoundationFacilityWaterFlow.None, string waterEndpointId = "")
        {
            this.id = id; this.groupId = groupId; this.slotId = slotId; this.phase = phase;
            this.contacts = (FoundationFacilityContactBinding[])(contacts ?? throw new ArgumentNullException(nameof(contacts))).Clone();
            this.motions = (FoundationFacilityMotionBinding[])(motions ?? throw new ArgumentNullException(nameof(motions))).Clone();
            this.waterFlow = waterFlow; this.waterEndpointId = waterEndpointId;
        }

        internal bool Matches(in FoundationFacilityWorkState work) => work.Active && phase == work.Phase &&
            groupId == work.GroupId && (SlotId.Length == 0 || SlotId == work.SlotId);

        internal void Validate(Transform root, NomadFacilityDefinition definition)
        {
            RequireId(id, "设施动作"); RequireId(groupId, id + "/工作组");
            if (slotId != null && slotId.Length != 0 && string.IsNullOrWhiteSpace(slotId))
                throw new InvalidOperationException(id + "：Slot 必须是明确 id 或空字符串。");
            if (!Enum.IsDefined(typeof(FoundationResidentPhase), phase) ||
                !Enum.IsDefined(typeof(FoundationFacilityWaterFlow), waterFlow) || contacts == null || motions == null)
                throw new InvalidOperationException(id + "：动作类型或绑定数组无效。");
            if (definition != null)
            {
                var groups = definition.InteractionGroups.Where(g => g.GroupId == groupId).ToArray();
                if (groups.Length != 1 || (SlotId.Length > 0 && !groups[0].AlternativeSlots.Any(s => s.SlotId == SlotId)))
                    throw new InvalidOperationException(id + "：工作组/Slot 不属于设施定义 " + definition.Id + "：" + groupId + "/" + SlotId);
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            _waterEndpoint = null;
            foreach (FoundationFacilityContactBinding point in contacts)
            {
                RequireId(point.Id, id + "/接触点"); RequireChild(root, point.Target, id + "/" + point.Id);
                if (!ids.Add(point.Id) || !Enum.IsDefined(typeof(FoundationFacilityContactRole), point.Role))
                    throw new InvalidOperationException(id + "：重复接触点 id 或未知用途：" + point.Id);
                if (point.Id != waterEndpointId) continue;
                FoundationFacilityContactRole required = waterFlow == FoundationFacilityWaterFlow.FillCan
                    ? FoundationFacilityContactRole.WaterOutlet : FoundationFacilityContactRole.WaterInlet;
                if (waterFlow == FoundationFacilityWaterFlow.None || point.Role != required)
                    throw new InvalidOperationException(id + "：水流端点用途不匹配：" + point.Id);
                _waterEndpoint = point.Target;
            }
            if ((waterFlow != FoundationFacilityWaterFlow.None && _waterEndpoint == null) ||
                (waterFlow == FoundationFacilityWaterFlow.None && !string.IsNullOrEmpty(waterEndpointId)))
                throw new InvalidOperationException(id + "：缺少水流所需的明确端点。");
            ids.Clear();
            var targets = new HashSet<Transform>();
            foreach (FoundationFacilityMotionBinding motion in motions)
            {
                if (motion == null) throw new InvalidOperationException(id + "：空机构绑定。");
                motion.Validate(root, id);
                if (!ids.Add(motion.Id) || !targets.Add(motion.Target))
                    throw new InvalidOperationException(id + "：重复机构 id 或同一部件被多次驱动。");
            }
        }

        internal static void ValidateAll(FoundationFacilityWorkBinding[] bindings, Transform root, NomadFacilityDefinition definition)
        {
            if (bindings == null || bindings.Length == 0) throw new InvalidOperationException("设施缺少动作绑定。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < bindings.Length; i++)
            {
                var current = bindings[i];
                if (current == null) throw new InvalidOperationException("设施包含空动作绑定。");
                current.Validate(root, definition);
                if (!ids.Add(current.Id)) throw new InvalidOperationException("重复设施动作 id：" + current.Id);
                for (int j = 0; j < i; j++)
                {
                    var other = bindings[j];
                    if (current.GroupId == other.GroupId && current.Phase == other.Phase &&
                        (current.SlotId.Length == 0 || other.SlotId.Length == 0 || current.SlotId == other.SlotId))
                        throw new InvalidOperationException("设施动作路由重叠：" + other.Id + " / " + current.Id);
                    if (current.GroupId != other.GroupId && current.Motions.Any(m => other.Motions.Any(n => n.Target == m.Target)))
                        throw new InvalidOperationException("不同工作组不能并行驱动同一部件：" + other.Id + " / " + current.Id);
                }
            }
        }

        internal static void RequireId(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() != value)
                throw new InvalidOperationException(label + "：需要非空且无首尾空白的稳定 id。");
        }
        internal static void RequireChild(Transform root, Transform node, string label)
        {
            if (node == null || node == root || !node.IsChildOf(root))
                throw new InvalidOperationException(label + "：节点必须属于本设施的可替换子树。");
        }
    }
}
