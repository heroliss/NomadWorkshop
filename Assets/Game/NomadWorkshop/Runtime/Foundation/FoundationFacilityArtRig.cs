using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 自有设施 Prefab 的分件与接触锚点。WorldView 每帧先复位，再投影所有精确到岗的操作；
    /// 不自行计时、查询逻辑或创建库存。多个功能组可以同时影响同一设施的不同部件。
    /// </summary>
    public sealed class FoundationFacilityArtRig : MonoBehaviour
    {
        [SerializeField] private NomadFacilityFunction function;
        [SerializeField] private Transform valve;
        [SerializeField] private Transform serviceDoor;
        [SerializeField] private Transform fillLid;
        [SerializeField] private Transform outlet;
        [SerializeField] private Transform inlet;
        [SerializeField] private LineRenderer hose;
        [SerializeField] private LineRenderer water;
        [SerializeField] private Renderer conditionIndicator;
        [SerializeField] private FoundationFacilityWorkBinding[] workBindings = Array.Empty<FoundationFacilityWorkBinding>();
        private MaterialPropertyBlock _conditionProperties;
        // Unity 热重载也会保存未标 SerializeField 的私有可序列化字段；派生缓存必须重建，不能恢复旧空数组或源资产引用。
        [NonSerialized] private FoundationFacilityWorkBinding[] _bindings;

        public Transform Valve => valve;
        public Transform ServiceDoor => serviceDoor;
        public Transform FillLid => fillLid;
        public Transform Inlet => inlet;
        public LineRenderer Water => water;
        public LineRenderer Hose => hose;
        public Renderer ConditionIndicator => conditionIndicator;
        public bool IsWorking { get; private set; }
        public IReadOnlyList<FoundationFacilityWorkBinding> Bindings => GetBindings();

        /// <summary>供自有资产生成工具接线；三只分件、出水口与补水口均为必填锚点。</summary>
        public void ConfigureTank(Transform valvePivot, Transform servicePivot, Transform lidPivot,
            Transform waterOutlet, Transform waterInlet, Renderer warningLens, Material hoseMaterial, Material waterMaterial)
        {
            function = NomadFacilityFunction.VehicleWaterTank;
            valve = valvePivot; serviceDoor = servicePivot; fillLid = lidPivot;
            outlet = waterOutlet; inlet = waterInlet;
            conditionIndicator = warningLens;
            CreateLines(hoseMaterial, waterMaterial);
            ConfigureBindings(CreateDefaultBindings());
        }

        /// <summary>饮水站只需要龙头、补水盖和入水口；检修门属于水箱专有部件。</summary>
        public void ConfigureDispenser(Transform tapPivot, Transform lidPivot, Transform waterInlet,
            Material hoseMaterial, Material waterMaterial)
        {
            function = NomadFacilityFunction.DrinkingStation;
            valve = tapPivot; fillLid = lidPivot; inlet = waterInlet;
            CreateLines(hoseMaterial, waterMaterial);
            ConfigureBindings(CreateDefaultBindings());
        }

        private void Awake() => CaptureRest();

        private void CaptureRest()
        {
            foreach (var binding in GetBindings())
                foreach (var motion in binding.Motions) motion.CaptureRest();
            // URP/Lit 需要线条生成法线；旧序列化 Prefab 也在实例化时补齐。
            if (hose != null) hose.generateLightingData = true;
            if (water != null) water.generateLightingData = true;
        }

        /// <summary>编辑期配置同机制模型；先完整校验再替换配置，不支持工作中热改机构及静止姿态。</summary>
        public void ConfigureBindings(params FoundationFacilityWorkBinding[] bindings)
        {
            if (Application.isPlaying) throw new InvalidOperationException("设施动作绑定只能在编辑期配置。");
            FoundationFacilityWorkBinding.ValidateAll(bindings, transform, null);
            ValidateFlowOwnership(bindings);
            workBindings = (FoundationFacilityWorkBinding[])bindings.Clone();
            _bindings = workBindings;
            CaptureRest();
        }

        /// <summary>在导入/创建世界之前核对动作归组，拒绝未知工作组和 Slot；不修改设施定义或玩法容量。</summary>
        public void ValidateAgainst(NomadFacilityDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            FoundationFacilityWorkBinding.ValidateAll(GetBindings(), transform, definition);
        }

        /// <summary>只解析当前精确到岗动作的入水点；不从第一只或最近的模型锚点推断。</summary>
        public bool TryGetWaterInlet(in FoundationFacilityWorkState work, out Transform target)
        {
            FoundationFacilityWorkBinding binding = Resolve(work);
            target = binding != null && binding.WaterFlow == FoundationFacilityWaterFlow.ReceiveFromCan
                ? binding.WaterEndpoint : null;
            return target != null;
        }

        private FoundationFacilityWorkBinding Resolve(in FoundationFacilityWorkState work)
        {
            foreach (var binding in GetBindings()) if (binding.Matches(work)) return binding;
            return null;
        }

        private FoundationFacilityWorkBinding[] GetBindings()
        {
            if (_bindings != null) return _bindings;
            // 旧 Prefab 的固定字段仅作为过渡配方输入；两种资产都进入同一校验和投影实现。
            var candidate = workBindings is { Length: > 0 } ? workBindings : CreateDefaultBindings();
            FoundationFacilityWorkBinding.ValidateAll(candidate, transform, null);
            ValidateFlowOwnership(candidate);
            _bindings = candidate;
            return _bindings;
        }

        private void ValidateFlowOwnership(FoundationFacilityWorkBinding[] bindings)
        {
            string group = null;
            foreach (var binding in bindings)
            {
                if (binding.WaterFlow == FoundationFacilityWaterFlow.None) continue;
                if (water == null || (binding.WaterFlow == FoundationFacilityWaterFlow.FillCan && hose == null))
                    throw new InvalidOperationException(binding.Id + "：设施缺少水流/软管 Renderer。");
                if (group != null && group != binding.GroupId)
                    throw new InvalidOperationException("当前水流 Renderer 只能归一个互斥工作组，不能被并行工位共用。");
                group = binding.GroupId;
            }
        }

        private FoundationFacilityWorkBinding[] CreateDefaultBindings()
        {
            FoundationFacilityContactBinding Point(string id, FoundationFacilityContactRole role, Transform target) => new(id, role, target);
            FoundationFacilityMotionBinding Rotate(string id, Transform target, Vector3 direction, float degrees) =>
                new(id, target, FoundationFacilityMotionKind.Rotate, target != null ? target.InverseTransformDirection(direction) : Vector3.zero, degrees);
            var empty = Array.Empty<FoundationFacilityContactBinding>();
            if (function == NomadFacilityFunction.VehicleWaterTank)
            {
                Vector3 doorDelta = serviceDoor != null ? serviceDoor.parent.InverseTransformVector(transform.up * .52f) : Vector3.zero;
                return new[]
                {
                    new FoundationFacilityWorkBinding("fill-can", "water-pickup", "", FoundationResidentPhase.PickingUpWater,
                        new[] { Point("outlet", FoundationFacilityContactRole.WaterOutlet, outlet) },
                        new[] { Rotate("valve", valve, transform.forward, 95f) }, FoundationFacilityWaterFlow.FillCan, "outlet"),
                    new FoundationFacilityWorkBinding("service", "service-valve", "", FoundationResidentPhase.RepairingFacility, empty,
                        new[] { new FoundationFacilityMotionBinding("service-door", serviceDoor, FoundationFacilityMotionKind.Translate, doorDelta, doorDelta.magnitude) }),
                    new FoundationFacilityWorkBinding("receive-stop-water", "water-pickup", "", FoundationResidentPhase.DeliveringStopWater,
                        new[] { Point("inlet", FoundationFacilityContactRole.WaterInlet, inlet) },
                        new[] { Rotate("fill-lid", fillLid, transform.right, 105f) }, FoundationFacilityWaterFlow.ReceiveFromCan, "inlet")
                };
            }
            if (function == NomadFacilityFunction.DrinkingStation)
                return new[]
                {
                    new FoundationFacilityWorkBinding("receive-water", "drink-and-deliver", "", FoundationResidentPhase.DeliveringWater,
                        new[] { Point("inlet", FoundationFacilityContactRole.WaterInlet, inlet) },
                        new[] { Rotate("fill-lid", fillLid, transform.right, 105f) }, FoundationFacilityWaterFlow.ReceiveFromCan, "inlet"),
                    new FoundationFacilityWorkBinding("drink", "drink-and-deliver", "", FoundationResidentPhase.Drinking, empty,
                        new[] { Rotate("tap", valve, transform.right, 24f) })
                };
            throw new InvalidOperationException("该设施没有旧版默认动作配方，请显式配置绑定。");
        }

        /// <summary>局部警示灯响应真实故障/临界状态；灯色不改写共享材质，也不启动独立闪烁时钟。</summary>
        public void ApplyCondition(bool faulted, bool critical)
        {
            if (conditionIndicator == null) return;
            _conditionProperties ??= new MaterialPropertyBlock();
            Color color = faulted ? new Color(1f, .095f, .025f) :
                critical ? new Color(1f, .45f, .07f) : new Color(.08f, .20f, .16f);
            _conditionProperties.SetColor("_BaseColor", color);
            _conditionProperties.SetColor("_EmissionColor", color * (faulted ? 1.6f : critical ? 1.2f : 0f));
            conditionIndicator.SetPropertyBlock(_conditionProperties);
        }

        /// <summary>每个表现帧调用一次，随后可按不同居民/功能组多次 Apply。取消与读档无需回放收尾动画。</summary>
        public void ResetWorkPose()
        {
            foreach (var binding in GetBindings())
                foreach (var motion in binding.Motions) motion.Reset();
            if (hose != null) hose.enabled = false;
            if (water != null) water.enabled = false;
            IsWorking = false;
        }

        /// <summary>调用方已按设施实例筛选；仅表现本设施支持的真实阶段，水罐仍由世界拥有。</summary>
        public void Apply(in FoundationFacilityWorkState work, Transform physicalCan)
        {
            FoundationFacilityWorkBinding binding = Resolve(work);
            if (binding == null) return;
            IsWorking = true;
            float envelope = WorkEnvelope(work.Progress);
            foreach (var motion in binding.Motions) motion.Apply(envelope);
            if (physicalCan == null) return;
            var container = physicalCan.GetComponent<FoundationCarriedContainerRig>();
            if (container == null) throw new InvalidOperationException("设施操作的容器缺少罐口绑定。");
            Vector3 mouth = container.Opening.position;
            if (binding.WaterFlow == FoundationFacilityWaterFlow.FillCan && work.Progress is > .16f and < .84f)
            {
                Vector3 nozzle = mouth + Vector3.up * .085f;
                DrawCurve(hose, binding.WaterEndpoint.position, nozzle, transform.forward * .26f + Vector3.up * .15f);
                DrawCurve(water, nozzle, mouth, Vector3.zero);
            }
            else if (binding.WaterFlow == FoundationFacilityWaterFlow.ReceiveFromCan && work.Progress is > .2f and < .8f)
                DrawCurve(water, mouth, binding.WaterEndpoint.position, Vector3.zero);
        }

        /// <summary>从同一阶段的绝对进度得到开合包络；暂停与同快照重投影不会累计误差。</summary>
        public static float WorkEnvelope(float progress) =>
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / .16f)) *
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - progress) / .16f));

        private static void DrawCurve(LineRenderer line, Vector3 start, Vector3 end, Vector3 bend)
        {
            line.enabled = true;
            for (int i = 0; i < line.positionCount; i++)
            {
                float t = i / (float)(line.positionCount - 1);
                Vector3 point = Vector3.Lerp(start, end, t);
                point += Mathf.Sin(t * Mathf.PI) * bend;
                line.SetPosition(i, point);
            }
        }

        private void CreateLines(Material hoseMaterial, Material waterMaterial)
        {
            hose = CreateLine("Working fill hose", hoseMaterial, .047f);
            water = CreateLine("Active water transfer", waterMaterial, .019f);
        }

        private LineRenderer CreateLine(string label, Material material, float width)
        {
            var root = new GameObject(label);
            root.transform.SetParent(transform, false);
            LineRenderer line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 12;
            line.widthMultiplier = width;
            line.numCornerVertices = 3;
            line.numCapVertices = 3;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.generateLightingData = true;
            line.enabled = false;
            return line;
        }
    }
}
