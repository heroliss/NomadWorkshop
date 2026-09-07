using System;
using Game.NomadWorkshop.Simulation;
using Game.NomadWorkshop.Navigation;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 一名居民的运行期执行所有者。需求、随机游标随居民存活，行动租约、路径与计时一起取消；
    /// 共享库存和空间账本由世界拥有，不随居民销毁。System 负责决策、导航查询和 Model 投影。
    /// 当前执行同步发生在模拟主线程；路径与租约不进入检查点。
    /// </summary>
    internal sealed class FoundationResidentExecution : IDisposable
    {
        internal FoundationResidentExecution(FoundationResidentModelState state) =>
            State = state ?? throw new ArgumentNullException(nameof(state));

        internal FoundationResidentModelState State { get; }
        internal string StableId => State.StableId;
        internal ulong OwnerId => State.OwnerId;
        internal int ActionSequence;
        internal int LeisureSequence;
        internal long DecisionSequence;
        internal long BladderOpportunitySequence;
        internal long WorkActionSequence;
        internal string LastPublishedDecisionDiagnostic = string.Empty;
        internal ResidentWaterCycle WaterCycle;
        internal ResidentWellbeing Wellbeing;
        internal FoundationResidentPhase Phase;
        internal HaulTaskLease ActiveHaul;
        internal FoundationStopVisitExecution StopVisit;
        internal DeckPose? LastDeckCheckpointPose;
        internal ResidentWaterActionLease ActiveWaterAction;
        internal PlacementRegionMoveLease ActiveWorldItemMove;
        internal PlacementRegionUseLease ActiveRepairPartUse;
        internal FoundationInteractionSpaceLease InteractionSpace;
        internal NomadJourneySession.NomadDriverLease DriverLease;
        internal long DriverDestinationRevision;
        internal string ActiveRepairTargetFacilityInstanceId = string.Empty;
        internal string ActiveWaterSourceFacilityInstanceId = string.Empty;
        internal string ActiveWaterTargetFacilityInstanceId = string.Empty;
        internal FoundationItemPlacementState WaterCanContactPlacement;
        internal PlacementRegionReservation WaterCanParkingReservation;
        internal FoundationWaterCanLocation WaterCanParkingLocation;
        internal bool StopWaterDeliveredBeforeParking;
        internal bool RetryStopWaterAfterParking;
        internal bool DrinkAfterActiveHaul;
        internal FoundationResidentMoveIntent MoveIntent;
        internal bool HasMoveIntent;
        internal Vector3[] PathCorners = Array.Empty<Vector3>();
        internal DeckResidentMotor Motor;
        internal Vector3 NativePathTarget;
        internal float NativeProgressRemainingMeters = float.PositiveInfinity;
        internal float NativeStallSeconds;
        internal float NativeRetrySeconds;
        internal string YieldingForResidentId = string.Empty;
        internal bool HasNativeDetour;
        internal bool NativeDetourReached;
        internal Vector3 NativeDetourTarget;
        internal Vector3 NativeDetourOrigin;
        internal float NativeDetourElapsed;
        internal float DockingYawDegrees;
        internal bool HasDockingYaw;
        internal int NextPathCornerIndex;
        internal float RouteRetryRemaining;
        internal float DecisionRetryRemaining;
        internal float PhaseDuration;
        internal float PhaseRemaining;
        internal float WorkEfficiency = 1f;
        internal float LeisureOutcomeScale = 1f;
        internal FoundationLeisureKind LeisureKind;

        /// <summary>
        /// 先撤销未提交的资源预约和物品事务，再释放站位；每种租约保证提交后不倒扣。
        /// Haul 已取出的货物仍保留在 Carrier，取消不是送回来源；调用方需保留或安排实际续送。
        /// 返回是否涉及实体物品，供 System 刷新搬运与落位投影。保留需求和随机游标，避免取消刷状态。
        /// </summary>
        internal bool CancelAction()
        {
            Motor?.Stop();
            // 行动取消先撤销驾驶权，之后的资源回滚与站位释放期间也不允许车辆继续移动。
            DriverLease?.Dispose();
            DriverLease = null;
            bool worldItemsChanged = ActiveWorldItemMove != null || ActiveRepairPartUse != null;
            ActiveHaul?.Dispose();
            ActiveHaul = null;
            StopVisit?.Dispose();
            StopVisit = null;
            ActiveWaterAction?.Dispose();
            ActiveWaterAction = null;
            ActiveWorldItemMove?.Dispose();
            ActiveWorldItemMove = null;
            ActiveRepairPartUse?.Dispose();
            ActiveRepairPartUse = null;
            WaterCanParkingReservation?.Dispose();
            WaterCanParkingReservation = null;
            WaterCanContactPlacement = default;
            StopWaterDeliveredBeforeParking = false;
            RetryStopWaterAfterParking = false;
            ReleaseInteractionSpace();
            ActiveRepairTargetFacilityInstanceId = string.Empty;
            ActiveWaterSourceFacilityInstanceId = string.Empty;
            ActiveWaterTargetFacilityInstanceId = string.Empty;
            DrinkAfterActiveHaul = false;
            LeisureOutcomeScale = 1f;
            LeisureKind = FoundationLeisureKind.None;
            PhaseDuration = 0f;
            PhaseRemaining = 0f;
            DriverDestinationRevision = 0L;
            ClearMoveIntent();
            ClearPath();
            return worldItemsChanged;
        }

        internal bool ReleaseInteractionSpace()
        {
            bool changed = InteractionSpace != null;
            InteractionSpace?.Dispose();
            InteractionSpace = null;
            return changed;
        }

        internal void ClearMoveIntent()
        {
            MoveIntent = default;
            HasMoveIntent = false;
            RouteRetryRemaining = 0f;
        }

        internal void ClearPath()
        {
            Motor?.Stop();
            ClearNativeDetour();
            PathCorners = Array.Empty<Vector3>();
            NextPathCornerIndex = 0;
            HasDockingYaw = false;
            DockingYawDegrees = 0f;
        }

        // 侧让只是当前移动执行的临时引擎目标，不能替换业务终点或进入资源/存档账本。
        internal void ClearNativeDetour()
        {
            YieldingForResidentId = string.Empty;
            HasNativeDetour = false;
            NativeDetourReached = false;
            NativeDetourElapsed = 0f;
        }

        public void Dispose()
        {
            CancelAction();
            Motor?.Dispose();
            Motor = null;
        }
    }
}
