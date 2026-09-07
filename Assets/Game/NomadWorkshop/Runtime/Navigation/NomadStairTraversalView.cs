using System;
using Game.Framework.Common;
using Game.Framework.View;
using Game.NomadWorkshop.Foundation;
using R3;
using UnityEngine;

namespace Game.NomadWorkshop.Navigation
{
    /// <summary>实验的角色、携物与操作面板；只消费三维只读投影，所有路线操作经 Command。</summary>
    public sealed class NomadStairTraversalView : MonoViewBase
    {
        [SerializeField] private GameObject humanoidPrefab;
        [SerializeField] private RuntimeAnimatorController controller;
        [SerializeField] private Transform navigationSpace;
        [SerializeField] private Material canShell;
        [SerializeField] private Material canHardware;
        [SerializeField] private Material water;
        private ReadOnlyReactiveProperty<StairTraversalState> _read;
        private string _checkpoint;
        private float _moveSpeed;
        private StairTraversalState _previous;
        public ResidentHumanoidPresentation Resident { get; private set; }
        public Transform WaterCan { get; private set; }
        public ResidentStairFootIK FootIK { get; private set; }

        public void Configure(GameObject prefab, RuntimeAnimatorController animationController, Transform space,
            Material shell, Material hardware, Material fill)
        {
            humanoidPrefab = prefab; controller = animationController; navigationSpace = space;
            canShell = shell; canHardware = hardware; water = fill;
        }

        private void Start()
        {
            if (humanoidPrefab == null || controller == null || navigationSpace == null ||
                canShell == null || canHardware == null || water == null)
                throw new InvalidOperationException("携物楼梯实验缺少 Humanoid、导航空间或水罐材质。");
            var root = new GameObject("Stair Carrier · Shared Humanoid");
            root.transform.SetParent(transform, false);
            Resident = root.AddComponent<ResidentHumanoidPresentation>();
            if (!Resident.TryInitialize(humanoidPrefab, controller))
                throw new InvalidOperationException("携物楼梯实验无法接入共享 Humanoid。");
            WaterCan = FoundationWaterCanVisualFactory.Create(root.transform, canShell, canHardware, water,
                out Transform fill);
            fill.gameObject.SetActive(true);
            WaterCan.localPosition = FoundationWaterCanVisualFactory.GetGripPosition(Resident) -
                Vector3.up * FoundationWaterCanVisualFactory.GripHeight;
            FootIK = Resident.Animator.gameObject.AddComponent<ResidentStairFootIK>();
            FootIK.Configure(Resident.Animator, root.transform, navigationSpace);
            FoundationResidentCarryIK carry = Resident.Animator.gameObject.AddComponent<FoundationResidentCarryIK>();
            carry.Configure(Resident.Animator, root.transform, FootIK.ApplySupport);
            carry.SetWaterCanGrip(WaterCan.Find(FoundationWaterCanVisualFactory.PalmTargetName), WaterCan.Find("Can Body"));
            _read = this.ExecuteCommand(new GetStairTraversalStateCommand());
            Bag.Subscribe(_read, Apply);
        }

        private void Apply(StairTraversalState state)
        {
            Vector3 world = navigationSpace.TransformPoint(state.Position);
            Resident.transform.SetPositionAndRotation(world,
                navigationSpace.rotation * Quaternion.Euler(0f, state.Yaw, 0f));
            float distance = Vector3.Distance(state.Position, _previous.Position);
            float speed = distance < .5f && Time.unscaledDeltaTime > 0f ? distance / Time.unscaledDeltaTime : 0f;
            if (!state.Paused)
            {
                _moveSpeed = Mathf.Lerp(_moveSpeed, speed, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
                Resident.SetSemantic(state.Phase == StairTraversalPhase.Moving && _moveSpeed > .05f
                    ? ResidentAnimationSemantic.Move : ResidentAnimationSemantic.Idle);
            }
            Resident.SetPlaybackSpeed(state.Paused ? 0f : 1f);
            _previous = state;
        }

        private void OnGUI()
        {
            if (_read == null) return;
            StairTraversalState state = _read.CurrentValue;
            GUILayout.BeginArea(new Rect(20, 20, 310, 215), GUI.skin.box);
            GUILayout.Label("游牧工坊 · 持桶上下楼实验");
            GUILayout.Label((state.Paused ? "已暂停 · " : string.Empty) + state.Status);
            GUILayout.Label($"携水 {state.WaterMilliliters / 1000f:F1} L　高度 {state.Position.y:F2} m");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("上二层")) this.ExecuteCommand(new MoveStairTraversalCommand(1));
            if (GUILayout.Button("回一层")) this.ExecuteCommand(new MoveStairTraversalCommand(0));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(state.Paused ? "继续" : "暂停"))
                this.ExecuteCommand(new PauseStairTraversalCommand(!state.Paused));
            if (GUILayout.Button("停止搬运")) this.ExecuteCommand(new CancelStairTraversalCommand());
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("记录当前位置")) _checkpoint = this.ExecuteCommand(new CaptureStairTraversalCommand());
            GUI.enabled = !string.IsNullOrEmpty(_checkpoint);
            if (GUILayout.Button("恢复记录")) this.ExecuteCommand(new RestoreStairTraversalCommand(_checkpoint));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("独立动作验证 · 尚未接入多层建造");
            GUILayout.EndArea();
        }
    }
}
