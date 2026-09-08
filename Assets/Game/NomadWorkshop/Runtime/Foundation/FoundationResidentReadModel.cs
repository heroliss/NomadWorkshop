using System;
using Game.NomadWorkshop.Simulation;
using R3;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>固定居民身份的只读数据束；所有属性始终引用同一居民记录，不能随当前执行者切换。</summary>
    public readonly struct FoundationResidentReadModel
    {
        internal readonly FoundationResidentModelState State;
        public readonly string StableId;
        public readonly ulong OwnerId;
        public readonly string DisplayName;
        public readonly NomadCharacterGender Gender;
        public readonly int AppearanceSeed;
        public readonly bool IsPlayerAvatar;
        public readonly ReadOnlyReactiveProperty<FoundationResidentPhase> ResidentPhase;
        public readonly ReadOnlyReactiveProperty<Vector3> ResidentLocalPosition;
        public readonly ReadOnlyReactiveProperty<float> ResidentLocalYawDegrees;
        public readonly ReadOnlyReactiveProperty<float> RemainingPathMeters;
        public readonly ReadOnlyReactiveProperty<long> MovementStallMilliseconds;
        public readonly ReadOnlyReactiveProperty<int> RemainingPathCorners;
        public readonly ReadOnlyReactiveProperty<string> ActivePathSummary;
        public readonly ReadOnlyReactiveProperty<bool> ResidentCarryingWater;
        public readonly ReadOnlyReactiveProperty<FoundationCarriedWorldItemState> ResidentCarriedWorldItem;
        public readonly ReadOnlyReactiveProperty<FoundationActionPlanProjection> LatestActionPlan;
        public readonly ReadOnlyReactiveProperty<float> ResidentThirst;
        public readonly ReadOnlyReactiveProperty<float> ResidentHealth;
        public readonly ReadOnlyReactiveProperty<float> ResidentEntertainment;
        public readonly ReadOnlyReactiveProperty<float> ResidentMood;
        public readonly ReadOnlyReactiveProperty<float> ResidentFatigue;
        public readonly ReadOnlyReactiveProperty<float> ResidentStress;
        public readonly ReadOnlyReactiveProperty<float> ResidentWorkEfficiency;
        public readonly ReadOnlyReactiveProperty<int> BodyWaterMilliliters;
        public readonly ReadOnlyReactiveProperty<int> BodyWaterCapacityMilliliters;
        public readonly ReadOnlyReactiveProperty<int> BladderWasteMilliliters;
        public readonly ReadOnlyReactiveProperty<int> BladderCapacityMilliliters;
        public readonly ReadOnlyReactiveProperty<float> ActionProgress;
        public readonly ReadOnlyReactiveProperty<FoundationFacilityWorkState> FacilityWork;
        public readonly ReadOnlyReactiveProperty<int> CompletedDrinkCount;
        public readonly ReadOnlyReactiveProperty<int> CompletedToiletUseCount;
        public readonly ReadOnlyReactiveProperty<int> CompletedLeisureCount;
        public readonly ReadOnlyReactiveProperty<int> CompletedDaydreamCount;
        public readonly ReadOnlyReactiveProperty<int> CompletedWanderCount;
        public readonly ReadOnlyReactiveProperty<int> CompletedGroundRestCount;
        public readonly ReadOnlyReactiveProperty<int> CompletedHobbyCount;
        public readonly ReadOnlyReactiveProperty<int> CompletedWorldItemMoveCount;
        public readonly ReadOnlyReactiveProperty<int> CompletedWaterTankRepairCount;
        public readonly ReadOnlyReactiveProperty<string> CurrentTask;
        public readonly ReadOnlyReactiveProperty<string> LastBlocker;

        /// <summary>绑定已有居民记录；不复制属性或订阅，也不取得执行器或账本的写权限。</summary>
        public FoundationResidentReadModel(FoundationResidentModelState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            State = state;
            StableId = state.StableId;
            OwnerId = state.OwnerId;
            DisplayName = state.DisplayName;
            Gender = state.Gender;
            AppearanceSeed = state.AppearanceSeed;
            IsPlayerAvatar = state.IsPlayerAvatar;
            ResidentPhase = state.ResidentPhase;
            ResidentLocalPosition = state.ResidentLocalPosition;
            ResidentLocalYawDegrees = state.ResidentLocalYawDegrees;
            RemainingPathMeters = state.RemainingPathMeters;
            MovementStallMilliseconds = state.MovementStallMilliseconds;
            RemainingPathCorners = state.RemainingPathCorners;
            ActivePathSummary = state.ActivePathSummary;
            ResidentCarryingWater = state.ResidentCarryingWater;
            ResidentCarriedWorldItem = state.ResidentCarriedWorldItem;
            LatestActionPlan = state.LatestActionPlan;
            ResidentThirst = state.ResidentThirst;
            ResidentHealth = state.ResidentHealth;
            ResidentEntertainment = state.ResidentEntertainment;
            ResidentMood = state.ResidentMood;
            ResidentFatigue = state.ResidentFatigue;
            ResidentStress = state.ResidentStress;
            ResidentWorkEfficiency = state.ResidentWorkEfficiency;
            BodyWaterMilliliters = state.BodyWaterMilliliters;
            BodyWaterCapacityMilliliters = state.BodyWaterCapacityMilliliters;
            BladderWasteMilliliters = state.BladderWasteMilliliters;
            BladderCapacityMilliliters = state.BladderCapacityMilliliters;
            ActionProgress = state.ActionProgress;
            FacilityWork = state.FacilityWork;
            CompletedDrinkCount = state.CompletedDrinkCount;
            CompletedToiletUseCount = state.CompletedToiletUseCount;
            CompletedLeisureCount = state.CompletedLeisureCount;
            CompletedDaydreamCount = state.CompletedDaydreamCount;
            CompletedWanderCount = state.CompletedWanderCount;
            CompletedGroundRestCount = state.CompletedGroundRestCount;
            CompletedHobbyCount = state.CompletedHobbyCount;
            CompletedWorldItemMoveCount = state.CompletedWorldItemMoveCount;
            CompletedWaterTankRepairCount = state.CompletedWaterTankRepairCount;
            CurrentTask = state.CurrentTask;
            LastBlocker = state.LastBlocker;
        }
    }
}
