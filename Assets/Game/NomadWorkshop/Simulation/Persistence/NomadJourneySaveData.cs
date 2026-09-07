using System;

namespace Game.NomadWorkshop.Simulation.Persistence
{
    /// <summary>
    /// v5 首段旅途的精确业务状态；单位与 Session 一致，不用千分比或整数毫升继续积分。
    /// 驾驶权和到岗请求版本不落盘，读取后必须重新到岗。
    /// </summary>
    [Serializable]
    public sealed class NomadJourneySaveData
    {
        public string RouteId = string.Empty;
        public string OriginId = string.Empty;
        public string DestinationSiteId = string.Empty;
        public long LengthMillimeters;
        public int SpeedMillimetersPerSecond;
        public int FuelNanolitersPerMillimeter;
        public long PositionMicrometers;
        public long FuelPicoliters;
        public NomadJourneyEndpoint Destination;

        /// <summary>兼容 Unity JSON 把旧版 null 嵌套对象恢复为全默认 DTO；只接受完全空白的旧语义。</summary>
        public bool IsEmpty => string.IsNullOrEmpty(RouteId) && string.IsNullOrEmpty(OriginId) &&
            string.IsNullOrEmpty(DestinationSiteId) && LengthMillimeters == 0 &&
            SpeedMillimetersPerSecond == 0 && FuelNanolitersPerMillimeter == 0 &&
            PositionMicrometers == 0 && FuelPicoliters == 0 && Destination == NomadJourneyEndpoint.None;

        /// <summary>复制业务真值，不保存运行期租约或对象身份。</summary>
        public static NomadJourneySaveData FromSnapshot(in NomadJourneySnapshot snapshot) => new()
        {
            RouteId = snapshot.Route.Id,
            OriginId = snapshot.Route.OriginId,
            DestinationSiteId = snapshot.Route.DestinationId,
            LengthMillimeters = snapshot.Route.LengthMicrometers / 1000L,
            SpeedMillimetersPerSecond = snapshot.Route.SpeedMillimetersPerSecond,
            FuelNanolitersPerMillimeter = snapshot.Route.FuelNanolitersPerMillimeter,
            PositionMicrometers = snapshot.PositionMicrometers,
            FuelPicoliters = snapshot.FuelPicoliters,
            Destination = snapshot.Destination,
        };

        /// <summary>校验路线、整数范围和目标后返回独立快照；失败不修改 DTO 或任何运行世界。</summary>
        public NomadJourneySnapshot ToValidatedSnapshot()
        {
            var route = new NomadJourneyRoute(RouteId, OriginId, DestinationSiteId, LengthMillimeters,
                SpeedMillimetersPerSecond, FuelNanolitersPerMillimeter);
            var snapshot = new NomadJourneySnapshot(route, PositionMicrometers, FuelPicoliters, Destination);
            using var validation = new NomadJourneySession(route, 0L);
            validation.Restore(snapshot);
            return snapshot;
        }
    }
}
