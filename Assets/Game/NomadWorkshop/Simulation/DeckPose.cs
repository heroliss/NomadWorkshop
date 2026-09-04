using System;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 车辆甲板局部平面上的确定性连续姿态。位置以毫米、朝向以 0.1 度量化；
    /// Unity Adapter 只负责把它投影为 Vector3 / Quaternion，不反向把浮点 Transform 当作业务真值。
    /// </summary>
    public readonly struct DeckPose : IEquatable<DeckPose>
    {
        public readonly int XMillimeters;
        public readonly int ZMillimeters;
        public readonly int YawDeciDegrees;
        public readonly int DeckLevel;

        public DeckPose(
            int xMillimeters,
            int zMillimeters,
            int yawDeciDegrees,
            int deckLevel = 0)
        {
            XMillimeters = xMillimeters;
            ZMillimeters = zMillimeters;
            YawDeciDegrees = NormalizeYaw(yawDeciDegrees);
            DeckLevel = deckLevel;
        }

        public double XMeters => XMillimeters / 1000d;
        public double ZMeters => ZMillimeters / 1000d;
        public double YawDegrees => YawDeciDegrees / 10d;

        public static DeckPose FromMeters(
            double xMeters,
            double zMeters,
            double yawDegrees,
            int deckLevel = 0) =>
            new(
                Quantize(xMeters, 1000, nameof(xMeters)),
                Quantize(zMeters, 1000, nameof(zMeters)),
                Quantize(yawDegrees, 10, nameof(yawDegrees)),
                deckLevel);

        /// <summary>
        /// 把设施局部的量化位置与朝向转换到同一甲板坐标。正 Yaw 与 Unity 绕 Y 轴一致：
        /// 俯视时局部 +X 会朝 -Z 旋转。
        /// </summary>
        public DeckPose TransformLocal(
            int localXMillimeters,
            int localZMillimeters,
            int localYawDeciDegrees = 0)
        {
            double radians = YawDegrees * Math.PI / 180d;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            double transformedX = XMillimeters +
                cosine * localXMillimeters + sine * localZMillimeters;
            double transformedZ = ZMillimeters -
                sine * localXMillimeters + cosine * localZMillimeters;
            return new DeckPose(
                RoundMillimeters(transformedX, nameof(localXMillimeters)),
                RoundMillimeters(transformedZ, nameof(localZMillimeters)),
                YawDeciDegrees + localYawDeciDegrees,
                DeckLevel);
        }

        public bool Equals(DeckPose other) =>
            XMillimeters == other.XMillimeters &&
            ZMillimeters == other.ZMillimeters &&
            YawDeciDegrees == other.YawDeciDegrees &&
            DeckLevel == other.DeckLevel;

        public override bool Equals(object obj) => obj is DeckPose other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(XMillimeters, ZMillimeters, YawDeciDegrees, DeckLevel);

        public override string ToString() =>
            $"({XMeters:0.###} m, {ZMeters:0.###} m, {YawDegrees:0.#} deg, deck {DeckLevel})";

        public static bool operator ==(DeckPose left, DeckPose right) => left.Equals(right);
        public static bool operator !=(DeckPose left, DeckPose right) => !left.Equals(right);

        internal static int NormalizeYaw(int yawDeciDegrees)
        {
            int normalized = yawDeciDegrees % 3600;
            return normalized < 0 ? normalized + 3600 : normalized;
        }

        internal static int Quantize(double value, int scale, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName, "姿态分量必须是有限数值。");

            double scaled = value * scale;
            if (scaled < int.MinValue || scaled > int.MaxValue)
                throw new ArgumentOutOfRangeException(parameterName, "姿态分量超出量化范围。");
            return (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }

        private static int RoundMillimeters(double value, string parameterName)
        {
            if (value < int.MinValue || value > int.MaxValue)
                throw new ArgumentOutOfRangeException(parameterName, "变换后的甲板坐标超出量化范围。");
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>
    /// 玩家操作辅助的吸附设置。位置或旋转步长为 0 时分别关闭该项；吸附只改变候选姿态，
    /// 不改变连续放置、碰撞或存档协议。
    /// </summary>
    public readonly struct DeckPlacementSnapSettings
    {
        public readonly int PositionStepMillimeters;
        public readonly int RotationStepDeciDegrees;
        public readonly int OriginXMillimeters;
        public readonly int OriginZMillimeters;

        public DeckPlacementSnapSettings(
            int positionStepMillimeters,
            int rotationStepDeciDegrees,
            int originXMillimeters = 0,
            int originZMillimeters = 0)
        {
            if (positionStepMillimeters < 0)
                throw new ArgumentOutOfRangeException(nameof(positionStepMillimeters));
            if (rotationStepDeciDegrees < 0 || rotationStepDeciDegrees > 3600)
                throw new ArgumentOutOfRangeException(nameof(rotationStepDeciDegrees));

            PositionStepMillimeters = positionStepMillimeters;
            RotationStepDeciDegrees = rotationStepDeciDegrees;
            OriginXMillimeters = originXMillimeters;
            OriginZMillimeters = originZMillimeters;
        }

        public static DeckPlacementSnapSettings Disabled => new(0, 0);

        /// <summary>当前 Foundation 推荐的默认辅助：0.2 m 平移与 45° 旋转吸附。</summary>
        public static DeckPlacementSnapSettings RecommendedDefault => new(200, 450);

        /// <summary>按位置与角度的独立开关生成候选姿态；中点使用远离零的确定性舍入。</summary>
        public DeckPose Apply(in DeckPose pose) =>
            new(
                SnapCoordinate(pose.XMillimeters, OriginXMillimeters, PositionStepMillimeters),
                SnapCoordinate(pose.ZMillimeters, OriginZMillimeters, PositionStepMillimeters),
                SnapYaw(pose.YawDeciDegrees, RotationStepDeciDegrees),
                pose.DeckLevel);

        private static int SnapCoordinate(int value, int origin, int step)
        {
            if (step == 0) return value;

            double stepCount = (value - (double)origin) / step;
            double snapped = origin +
                Math.Round(stepCount, MidpointRounding.AwayFromZero) * step;
            if (snapped < int.MinValue || snapped > int.MaxValue)
                throw new OverflowException("吸附后的甲板坐标超出量化范围。");
            return (int)snapped;
        }

        private static int SnapYaw(int yawDeciDegrees, int step)
        {
            if (step == 0) return yawDeciDegrees;

            int stepCount = (int)Math.Round(
                yawDeciDegrees / (double)step,
                MidpointRounding.AwayFromZero);
            return DeckPose.NormalizeYaw(stepCount * step);
        }
    }
}
