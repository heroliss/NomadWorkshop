using System;
using System.Collections.Generic;
using Game.Framework.Common;
using Game.Framework.Systems;
using Game.NomadWorkshop.Simulation;
using UnityEngine;

namespace Game.NomadWorkshop.Navigation
{
    /// <summary>
    /// 隔离验证“连续路径 → 两人会车 → 共享交互组排队 → 精确贴靠 → 真实物品交接”的逻辑所有者。
    /// NavMesh 状态经 Utility 访问，门和手持物只由 View 呈现。
    /// </summary>
    public sealed class NomadNavigationSpikeSystem : MonoSystemBase
    {
        private const string AgentAId = "resident-a";
        private const string AgentBId = "resident-b";
        private const ulong AgentAOwner = 0xA01UL;
        private const ulong AgentBOwner = 0xB01UL;

        [SerializeField] private FacilityInteractionGroup cabinetInteraction;
        [SerializeField] private Vector3 openPathStart = new(-6f, 0f, 3.5f);
        [SerializeField] private Vector3 openPathEnd = new(6f, 0f, 3.5f);
        [SerializeField] private Vector3 obstaclePathStart = new(-6f, 0f, 0f);
        [SerializeField] private Vector3 obstaclePathEnd = new(6f, 0f, 0f);
        [SerializeField] private Vector3 residentAStart = new(-6f, 0f, -1.9f);
        [SerializeField] private Vector3 residentAEnd = new(6f, 0f, -1.9f);
        [SerializeField] private Vector3 residentBStart = new(6f, 0f, -2.5f);
        [SerializeField] private Vector3 residentBEnd = new(-6f, 0f, -2.5f);

        [Header("交互表现节奏")]
        [SerializeField, Min(0.01f)] private float doorSeconds = 0.55f;
        [SerializeField, Min(0.1f)] private float dockingPositionSpeed = 1.4f;
        [SerializeField, Min(1f)] private float dockingAngularSpeed = 360f;
        [SerializeField, Min(1f)] private float recoverySeconds = 5f;
        [SerializeField, Min(2f)] private float hardTimeoutSeconds = 14f;

        private static readonly ResourceId SpikeItem = new("navigation-spike-item");

        private NomadNavigationSpikeModel _model;
        private DeckNavigationUtility _navigation;
        private ResourceFlowLedger _resourceFlow;
        private ResourceInventory _cabinetInventory;
        private ResourceInventory _residentAInventory;
        private ResourceInventory _residentBInventory;
        private ReservationLedger _interactionReservations;
        private ReservationLease _activeGroupLease;
        private ProcessTaskLease _activeLease;
        private FacilityInteractionSlot _activeSlot;
        private string _activeAgentId;
        private bool _activeIsResidentA;
        private float _phaseElapsed;
        private bool _recoveryAttempted;
        private int _taskSequence;
        private bool _running;

        public void ConfigureRuntime(
            FacilityInteractionGroup configuredCabinetInteraction,
            bool fastMode)
        {
            cabinetInteraction = configuredCabinetInteraction != null
                ? configuredCabinetInteraction
                : throw new ArgumentNullException(nameof(configuredCabinetInteraction));
            if (!fastMode) return;
            doorSeconds = 0.06f;
            dockingPositionSpeed = 8f;
            dockingAngularSpeed = 1440f;
            recoverySeconds = 2f;
            hardTimeoutSeconds = 8f;
        }

        private void Start()
        {
            _model = this.GetModel<NomadNavigationSpikeModel>();
            _navigation = this.GetUtility<DeckNavigationUtility>();
            RestartScenario();
        }

        private void Update()
        {
            if (!_running) return;

            float deltaTime = Time.unscaledDeltaTime;
            _phaseElapsed += deltaTime;
            UpdateAgentProjection();
            UpdateMinimumSeparation();

            switch (_model.Phase.Value)
            {
                case NavigationInteractionSpikePhase.Crossing:
                    TickCrossing();
                    break;
                case NavigationInteractionSpikePhase.FirstApproach:
                case NavigationInteractionSpikePhase.SecondApproach:
                    TickApproach();
                    break;
                case NavigationInteractionSpikePhase.FirstDocking:
                case NavigationInteractionSpikePhase.SecondDocking:
                    TickDocking(deltaTime);
                    break;
                case NavigationInteractionSpikePhase.FirstOpening:
                case NavigationInteractionSpikePhase.SecondOpening:
                    TickOpening(deltaTime);
                    break;
                case NavigationInteractionSpikePhase.FirstClosing:
                case NavigationInteractionSpikePhase.SecondClosing:
                    TickClosing(deltaTime);
                    break;
            }

            TickRecoveryOrTimeout();
        }

        public void RestartScenario()
        {
            if (_model == null || _navigation == null) return;

            ReleaseActiveLease();
            _running = false;
            _model.ResetProjection();
            cabinetInteraction.ValidateOrThrow();

            try
            {
                _navigation.BuildNow();
                if (!_navigation.TryCalculateCompletePath(openPathStart, openPathEnd, out DeckNavPathProbe open))
                    throw new InvalidOperationException("空旷路径不是完整 NavMesh 路径。");
                if (!_navigation.TryCalculateCompletePath(
                        obstaclePathStart,
                        obstaclePathEnd,
                        out DeckNavPathProbe obstacle))
                    throw new InvalidOperationException("旋转障碍两侧没有完整 NavMesh 路径。");

                _model.OpenPathLengthRatio.Value = open.LengthRatio;
                _model.OpenPathCorners.Value = open.Corners.Count;
                _model.ObstaclePathLengthRatio.Value = obstacle.LengthRatio;
                _model.ObstaclePathCorners.Value = obstacle.Corners.Count;
                if (open.LengthRatio > 1.03f)
                    throw new InvalidOperationException(
                        $"空旷路径仍有明显折行，长度比为 {open.LengthRatio:F3}。");
                if (obstacle.LengthRatio <= 1.005f)
                    throw new InvalidOperationException(
                        $"旋转柜体没有形成可观察绕行，长度比为 {obstacle.LengthRatio:F3}。");

                _interactionReservations = new ReservationLedger();
                _resourceFlow = new ResourceFlowLedger(_interactionReservations);
                _cabinetInventory = new ResourceInventory(
                    "navigation-spike-cabinet",
                    2,
                    new ResourceQuantity(SpikeItem, 2));
                _residentAInventory = new ResourceInventory("navigation-spike-resident-a", 1);
                _residentBInventory = new ResourceInventory("navigation-spike-resident-b", 1);
                _taskSequence = 0;
                RefreshInventoryProjection();

                if (!_navigation.TryWarp(AgentAId, residentAStart) ||
                    !_navigation.TryWarp(AgentBId, residentBStart) ||
                    !_navigation.TrySetDestination(AgentAId, residentAEnd) ||
                    !_navigation.TrySetDestination(AgentBId, residentBEnd))
                    throw new InvalidOperationException("居民无法投放到 NavMesh 或设置会车终点。");

                _model.IsReady.Value = true;
                SetPhase(NavigationInteractionSpikePhase.Crossing, "两名居民沿连续路径相向会车");
                _running = true;
            }
            catch (Exception exception)
            {
                Block(exception.Message);
            }
        }

        protected override void OnDestroy()
        {
            ReleaseActiveLease();
            base.OnDestroy();
        }

        private void TickCrossing()
        {
            bool aArrived = _navigation.IsArrived(AgentAId, 0.16f);
            bool bArrived = _navigation.IsArrived(AgentBId, 0.16f);
            if (aArrived) _navigation.Stop(AgentAId);
            if (bArrived) _navigation.Stop(AgentBId);
            if (!aArrived || !bArrived) return;

            _model.CrossingCompleted.Value = true;
            if (!TryBeginInteraction(
                    AgentAId,
                    AgentAOwner,
                    _residentAInventory,
                    true,
                    out string blocker))
            {
                Block($"居民 A 无法开始柜门交互：{blocker}");
                return;
            }

            if (!ProbeResidentBContention()) return;
            SetPhase(NavigationInteractionSpikePhase.FirstApproach, "居民 A 前往已预留的柜门候选位");
        }

        private void TickApproach()
        {
            if (!_navigation.IsArrived(_activeAgentId, _activeSlot.ApproachTolerance)) return;

            _navigation.Stop(_activeAgentId);
            SetPhase(
                _activeIsResidentA
                    ? NavigationInteractionSpikePhase.FirstDocking
                    : NavigationInteractionSpikePhase.SecondDocking,
                $"{ResidentLabel} 正在精确贴靠 {_activeSlot.SlotId}");
        }

        private void TickDocking(float deltaTime)
        {
            if (!_navigation.AdvanceDocking(
                    _activeAgentId,
                    _activeSlot.WorldPosition,
                    _activeSlot.WorldRotation,
                    dockingPositionSpeed,
                    dockingAngularSpeed,
                    deltaTime,
                    _activeSlot.DockingPositionTolerance,
                    _activeSlot.DockingAngleTolerance))
                return;

            SetPhase(
                _activeIsResidentA
                    ? NavigationInteractionSpikePhase.FirstOpening
                    : NavigationInteractionSpikePhase.SecondOpening,
                $"{ResidentLabel} 已贴靠，设施执行器开门");
        }

        private void TickOpening(float deltaTime)
        {
            _model.DoorOpenProgress.Value = Mathf.Clamp01(
                _model.DoorOpenProgress.Value + deltaTime / doorSeconds);
            if (_model.DoorOpenProgress.Value < 1f) return;

            try
            {
                _activeLease.Commit();
                _activeLease = null;
                _model.CompletedInteractionCount.Value++;
                RefreshInventoryProjection();
                if (_activeIsResidentA && !ProbeResidentBContention()) return;
                SetPhase(
                    _activeIsResidentA
                        ? NavigationInteractionSpikePhase.FirstClosing
                        : NavigationInteractionSpikePhase.SecondClosing,
                    $"{ResidentLabel} 已取得真实物品，设施执行器关门");
            }
            catch (Exception exception)
            {
                Block($"物品交接失败：{exception.Message}");
            }
        }

        private void TickClosing(float deltaTime)
        {
            _model.DoorOpenProgress.Value = Mathf.Clamp01(
                _model.DoorOpenProgress.Value - deltaTime / doorSeconds);
            if (_model.DoorOpenProgress.Value > 0f) return;

            _navigation.ResumeAutomaticRotation(_activeAgentId);
            _navigation.Stop(_activeAgentId);
            ReleaseActiveGroupLease();
            if (_activeIsResidentA)
            {
                if (!TryBeginInteraction(
                        AgentBId,
                        AgentBOwner,
                        _residentBInventory,
                        false,
                        out string blocker))
                {
                    Block($"居民 B 在柜门释放后仍无法开始交互：{blocker}");
                    return;
                }
                SetPhase(NavigationInteractionSpikePhase.SecondApproach, "居民 B 接替使用同一柜门组");
                return;
            }

            _model.Phase.Value = NavigationInteractionSpikePhase.Completed;
            _model.CurrentStatus.Value = "完成：连续会车、互斥候选位、开门与两次真实交接均成立";
            _model.LastBlocker.Value = string.Empty;
            _running = false;
        }

        private bool TryBeginInteraction(
            string agentId,
            ulong ownerId,
            ResourceInventory residentInventory,
            bool isResidentA,
            out string blocker)
        {
            if (!TrySelectBestSlot(agentId, out FacilityInteractionSlot slot, out float _))
            {
                blocker = "三个候选停靠位均不可达";
                return false;
            }

            if (!_interactionReservations.TryAcquire(
                    ownerId,
                    new[] { cabinetInteraction.ReservationKey },
                    out ReservationLease groupLease))
            {
                blocker = "InteractionUnavailable";
                return false;
            }

            var request = new ProcessTaskRequest(
                $"navigation-spike-pickup-{++_taskSequence}",
                ownerId,
                "从共享柜门取出一件备件",
                new[] { new InventoryResourceQuantity(_cabinetInventory, SpikeItem, 1) },
                new[] { new InventoryResourceQuantity(residentInventory, SpikeItem, 1) },
                new[] { $"inventory-handoff:{cabinetInteraction.GroupId}" });
            if (!_resourceFlow.TryReserveProcess(
                    request,
                    out ProcessTaskLease lease,
                    out ResourceFlowBlocker flowBlocker))
            {
                groupLease.Dispose();
                blocker = flowBlocker.Reason.ToString();
                return false;
            }

            if (!_navigation.TrySetDestination(agentId, slot.WorldPosition))
            {
                lease.Dispose();
                groupLease.Dispose();
                blocker = "NavMeshAgent 拒绝候选停靠位";
                return false;
            }

            _activeGroupLease = groupLease;
            _activeLease = lease;
            _activeSlot = slot;
            _activeAgentId = agentId;
            _activeIsResidentA = isResidentA;
            if (isResidentA) _model.ResidentASelectedSlot.Value = slot.SlotId;
            else _model.ResidentBSelectedSlot.Value = slot.SlotId;
            blocker = string.Empty;
            return true;
        }

        private bool ProbeResidentBContention()
        {
            if (!TrySelectBestSlot(AgentBId, out _, out _))
            {
                Block("居民 B 在互斥探针前没有任何可达候选位。");
                return false;
            }

            if (_interactionReservations.TryAcquire(
                    AgentBOwner,
                    new[] { cabinetInteraction.ReservationKey },
                    out ReservationLease unexpected))
            {
                unexpected.Dispose();
                Block("同一柜门组被两名居民同时预留。");
                return false;
            }

            _model.ReservationContentionCount.Value++;
            return true;
        }

        private bool TrySelectBestSlot(
            string agentId,
            out FacilityInteractionSlot selected,
            out float selectedCost)
        {
            selected = null;
            selectedCost = float.PositiveInfinity;
            if (!_navigation.TryGetSnapshot(agentId, out NavigationAgentSnapshot snapshot) ||
                !snapshot.IsOnNavMesh)
                return false;

            IReadOnlyList<FacilityInteractionSlot> slots = cabinetInteraction.AlternativeSlots;
            for (var i = 0; i < slots.Count; i++)
            {
                FacilityInteractionSlot slot = slots[i];
                if (slot == null || !_navigation.TryCalculateCompletePath(
                        snapshot.Position,
                        slot.WorldPosition,
                        out DeckNavPathProbe path))
                    continue;

                Vector3 direction = slot.WorldPosition - snapshot.Position;
                float turnCost = direction.sqrMagnitude <= 0.0001f
                    ? 0f
                    : Vector3.Angle(snapshot.Velocity.sqrMagnitude > 0.01f
                        ? snapshot.Velocity
                        : direction,
                        direction) * 0.002f;
                float cost = path.PathLength + turnCost;
                if (cost >= selectedCost) continue;
                selected = slot;
                selectedCost = cost;
            }
            return selected != null;
        }

        private void TickRecoveryOrTimeout()
        {
            NavigationInteractionSpikePhase phase = _model.Phase.Value;
            if (phase is NavigationInteractionSpikePhase.Completed or
                NavigationInteractionSpikePhase.Blocked or
                NavigationInteractionSpikePhase.Booting)
                return;

            if (!_recoveryAttempted && _phaseElapsed >= recoverySeconds)
            {
                bool replanned = phase switch
                {
                    NavigationInteractionSpikePhase.Crossing =>
                        _navigation.TrySetDestination(AgentAId, residentAEnd) &
                        _navigation.TrySetDestination(AgentBId, residentBEnd),
                    NavigationInteractionSpikePhase.FirstApproach or
                    NavigationInteractionSpikePhase.SecondApproach =>
                        _activeSlot != null &&
                        _navigation.TrySetDestination(_activeAgentId, _activeSlot.WorldPosition),
                    _ => true,
                };
                if (!replanned)
                {
                    Block($"{phase} 阶段重新寻路失败。");
                    return;
                }

                _recoveryAttempted = true;
                _model.StuckRecoveryCount.Value++;
            }

            if (_phaseElapsed >= hardTimeoutSeconds)
                Block($"{phase} 阶段超过 {hardTimeoutSeconds:F1} 秒仍未完成。");
        }

        private void SetPhase(NavigationInteractionSpikePhase phase, string status)
        {
            _model.Phase.Value = phase;
            _model.CurrentStatus.Value = status;
            _phaseElapsed = 0f;
            _recoveryAttempted = false;
        }

        private void UpdateAgentProjection()
        {
            if (_navigation.TryGetSnapshot(AgentAId, out NavigationAgentSnapshot a))
                _model.ResidentAPosition.Value = a.Position;
            if (_navigation.TryGetSnapshot(AgentBId, out NavigationAgentSnapshot b))
                _model.ResidentBPosition.Value = b.Position;
        }

        private void UpdateMinimumSeparation()
        {
            if (!_navigation.TryGetSnapshot(AgentAId, out NavigationAgentSnapshot a) ||
                !_navigation.TryGetSnapshot(AgentBId, out NavigationAgentSnapshot b))
                return;
            float separation = Vector3.Distance(a.Position, b.Position);
            if (separation < _model.MinimumAgentSeparation.Value)
                _model.MinimumAgentSeparation.Value = separation;
        }

        private void RefreshInventoryProjection()
        {
            _model.CabinetItemCount.Value = _cabinetInventory.GetAmount(SpikeItem);
            _model.ResidentAItemCount.Value = _residentAInventory.GetAmount(SpikeItem);
            _model.ResidentBItemCount.Value = _residentBInventory.GetAmount(SpikeItem);
        }

        private void Block(string reason)
        {
            ReleaseActiveLease();
            _running = false;
            if (_model == null) return;
            _model.Phase.Value = NavigationInteractionSpikePhase.Blocked;
            _model.CurrentStatus.Value = "连续导航 Harness 已阻塞";
            _model.LastBlocker.Value = reason ?? "未知错误";
        }

        private void ReleaseActiveLease()
        {
            _activeLease?.Dispose();
            _activeLease = null;
            ReleaseActiveGroupLease();
        }

        private void ReleaseActiveGroupLease()
        {
            _activeGroupLease?.Dispose();
            _activeGroupLease = null;
        }

        private string ResidentLabel => _activeIsResidentA ? "居民 A" : "居民 B";
    }
}
