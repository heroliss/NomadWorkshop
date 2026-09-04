using System;
using System.Collections.Generic;
using Game.Framework.Common;
using Game.Framework.View;
using R3;
using UnityEngine;

namespace Game.NomadWorkshop.Navigation
{
    /// <summary>只消费只读投影的 3D/诊断 View：门、手持物和路径线均不参与任务结算。</summary>
    public sealed class NomadNavigationSpikeView : MonoViewBase
    {
        [SerializeField] private Transform doorPivot;
        [SerializeField] private Quaternion doorClosedLocalRotation = Quaternion.identity;
        [SerializeField] private float doorOpenAngle = -105f;
        [SerializeField] private GameObject residentACargo;
        [SerializeField] private GameObject residentBCargo;

        private NavigationSpikeReadModel _readModel;
        private DeckNavigationUtility _navigation;
        private float _doorProgress;
        private bool _residentACarrying;
        private bool _residentBCarrying;
        private Material _pathMaterialA;
        private Material _pathMaterialB;
        private LineRenderer _pathA;
        private LineRenderer _pathB;
        private readonly List<Material> _runtimeTintMaterials = new();

        public void ConfigureRuntime(
            Transform configuredDoorPivot,
            GameObject configuredResidentACargo,
            GameObject configuredResidentBCargo)
        {
            doorPivot = configuredDoorPivot != null
                ? configuredDoorPivot
                : throw new ArgumentNullException(nameof(configuredDoorPivot));
            doorClosedLocalRotation = doorPivot.localRotation;
            residentACargo = configuredResidentACargo != null
                ? configuredResidentACargo
                : throw new ArgumentNullException(nameof(configuredResidentACargo));
            residentBCargo = configuredResidentBCargo != null
                ? configuredResidentBCargo
                : throw new ArgumentNullException(nameof(configuredResidentBCargo));
        }

        protected override void Awake()
        {
            base.Awake();
            if (doorPivot == null || residentACargo == null || residentBCargo == null)
                throw new InvalidOperationException("NomadNavigationSpikeView 的门轴或手持物引用未接线。");

            _readModel = this.ExecuteCommand(new GetNavigationSpikeReadModelCommand());
            _navigation = this.GetUtility<DeckNavigationUtility>();
            ApplyRuntimeTints();
            Bag.Subscribe(_readModel.DoorOpenProgress, value => _doorProgress = value);
            Bag.Subscribe(_readModel.ResidentAItemCount, value => _residentACarrying = value > 0);
            Bag.Subscribe(_readModel.ResidentBItemCount, value => _residentBCarrying = value > 0);
        }

        private void Start()
        {
            _pathA = CreatePathLine("Resident A Path", new Color(1f, 0.58f, 0.16f), out _pathMaterialA);
            _pathB = CreatePathLine("Resident B Path", new Color(0.16f, 0.72f, 1f), out _pathMaterialB);
        }

        private void Update()
        {
            doorPivot.localRotation = doorClosedLocalRotation *
                                      Quaternion.Euler(0f, doorOpenAngle * _doorProgress, 0f);
            residentACargo.SetActive(_residentACarrying);
            residentBCargo.SetActive(_residentBCarrying);
            UpdatePathLine(_pathA, _navigation.GetCurrentPathCorners("resident-a"));
            UpdatePathLine(_pathB, _navigation.GetCurrentPathCorners("resident-b"));
        }

        protected override void OnDestroy()
        {
            if (_pathMaterialA != null) Destroy(_pathMaterialA);
            if (_pathMaterialB != null) Destroy(_pathMaterialB);
            for (var i = 0; i < _runtimeTintMaterials.Count; i++)
            {
                if (_runtimeTintMaterials[i] != null) Destroy(_runtimeTintMaterials[i]);
            }
            _runtimeTintMaterials.Clear();
            base.OnDestroy();
        }

        private void OnGUI()
        {
            const float width = 410f;
            GUILayout.BeginArea(new Rect(14f, 14f, width, 350f), GUI.skin.box);
            GUILayout.Label("游牧工坊 · 连续导航与交互位 Harness");
            GUILayout.Label($"阶段：{_readModel.Phase.CurrentValue}");
            GUILayout.Label(_readModel.CurrentStatus.CurrentValue);
            GUILayout.Space(6f);
            GUILayout.Label(
                $"空旷路径：{_readModel.OpenPathLengthRatio.CurrentValue:F3}× / " +
                $"{_readModel.OpenPathCorners.CurrentValue} 拐点");
            GUILayout.Label(
                $"旋转柜体绕行：{_readModel.ObstaclePathLengthRatio.CurrentValue:F3}× / " +
                $"{_readModel.ObstaclePathCorners.CurrentValue} 拐点");
            GUILayout.Label(
                $"会车：{(_readModel.CrossingCompleted.CurrentValue ? "完成" : "进行中")}  " +
                $"最小间距 {_readModel.MinimumAgentSeparation.CurrentValue:F2} m  " +
                $"恢复 {_readModel.StuckRecoveryCount.CurrentValue}");
            GUILayout.Space(6f);
            GUILayout.Label(
                $"候选位：A={DisplaySlot(_readModel.ResidentASelectedSlot.CurrentValue)}  " +
                $"B={DisplaySlot(_readModel.ResidentBSelectedSlot.CurrentValue)}");
            GUILayout.Label(
                $"互斥等待 {_readModel.ReservationContentionCount.CurrentValue} 次  " +
                $"完成交接 {_readModel.CompletedInteractionCount.CurrentValue}/2");
            GUILayout.Label(
                $"物品：柜内 {_readModel.CabinetItemCount.CurrentValue}  " +
                $"A 携带 {_readModel.ResidentAItemCount.CurrentValue}  " +
                $"B 携带 {_readModel.ResidentBItemCount.CurrentValue}");
            if (!string.IsNullOrEmpty(_readModel.LastBlocker.CurrentValue))
            {
                GUI.color = new Color(1f, 0.45f, 0.3f);
                GUILayout.Label($"阻塞：{_readModel.LastBlocker.CurrentValue}");
                GUI.color = Color.white;
            }
            GUILayout.Space(8f);
            if (GUILayout.Button("重新运行 Harness", GUILayout.Height(28f)))
                this.ExecuteCommand(new RestartNavigationSpikeCommand());
            GUILayout.EndArea();
        }

        private LineRenderer CreatePathLine(string objectName, Color color, out Material material)
        {
            var lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.startWidth = 0.045f;
            line.endWidth = 0.045f;
            line.numCapVertices = 3;
            line.positionCount = 0;
            Shader shader = Shader.Find("Sprites/Default");
            material = shader != null ? new Material(shader) : null;
            if (material != null)
            {
                material.color = color;
                line.sharedMaterial = material;
            }
            line.startColor = color;
            line.endColor = color;
            return line;
        }

        private void ApplyRuntimeTints()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                            Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("连续导航 Harness 找不到可用的 Lit Shader。");

            var materials = new Dictionary<Color32, Material>();
            NavigationSpikeTint[] tints = transform.root.GetComponentsInChildren<NavigationSpikeTint>(true);
            for (var i = 0; i < tints.Length; i++)
            {
                NavigationSpikeTint tint = tints[i];
                Renderer renderer = tint.GetComponent<Renderer>();
                if (renderer == null) continue;

                Color32 key = tint.Color;
                if (!materials.TryGetValue(key, out Material material))
                {
                    material = new Material(shader)
                    {
                        name = $"M_NavigationSpike_{key.r:X2}{key.g:X2}{key.b:X2}",
                    };
                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", tint.Color);
                    if (material.HasProperty("_Color"))
                        material.SetColor("_Color", tint.Color);
                    if (material.HasProperty("_Smoothness"))
                        material.SetFloat("_Smoothness", 0.22f);
                    materials.Add(key, material);
                    _runtimeTintMaterials.Add(material);
                }
                renderer.sharedMaterial = material;
            }
        }

        private static void UpdatePathLine(LineRenderer line, Vector3[] corners)
        {
            if (line == null) return;
            line.positionCount = corners?.Length ?? 0;
            if (corners == null) return;
            for (var i = 0; i < corners.Length; i++)
                line.SetPosition(i, corners[i] + Vector3.up * 0.06f);
        }

        private static string DisplaySlot(string slotId) =>
            string.IsNullOrEmpty(slotId) ? "等待选择" : slotId;
    }
}
