using System;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 纯模拟层内部复用的二维有向矩形。坐标沿甲板 X/Z，角度遵循 <see cref="DeckPose"/>
    /// 与 Unity Yaw 相同的约定；设施占地和世界物品放置必须共享同一套边界语义。
    /// </summary>
    internal readonly struct PlanarOrientedRectangle
    {
        public PlanarOrientedRectangle(
            double centerX,
            double centerZ,
            double halfWidth,
            double halfDepth,
            double yawDegrees)
        {
            CenterX = centerX;
            CenterZ = centerZ;
            HalfWidth = halfWidth;
            HalfDepth = halfDepth;

            double radians = yawDegrees * Math.PI / 180d;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            AxisXX = cosine;
            AxisXZ = -sine;
            AxisZX = sine;
            AxisZZ = cosine;
        }

        public double CenterX { get; }
        public double CenterZ { get; }
        public double HalfWidth { get; }
        public double HalfDepth { get; }
        public double AxisXX { get; }
        public double AxisXZ { get; }
        public double AxisZX { get; }
        public double AxisZZ { get; }

        public double ProjectedRadius(double axisX, double axisZ) =>
            HalfWidth * Math.Abs(AxisXX * axisX + AxisXZ * axisZ) +
            HalfDepth * Math.Abs(AxisZX * axisX + AxisZZ * axisZ);
    }

    internal static class PlanarOrientedRectangleGeometry
    {
        private const double GeometryToleranceMillimeters = 0.0001d;

        public static PlanarOrientedRectangle[] BuildRectangles(
            in DeckPose pose,
            ContinuousFacilityFootprint footprint)
        {
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));

            var result = new PlanarOrientedRectangle[footprint.Parts.Count];
            double poseRadians = pose.YawDegrees * Math.PI / 180d;
            double poseCosine = Math.Cos(poseRadians);
            double poseSine = Math.Sin(poseRadians);
            for (var i = 0; i < footprint.Parts.Count; i++)
            {
                DeckFootprintPart part = footprint.Parts[i];
                double centerX = pose.XMillimeters +
                                 poseCosine * part.LocalCenterXMillimeters +
                                 poseSine * part.LocalCenterZMillimeters;
                double centerZ = pose.ZMillimeters -
                                 poseSine * part.LocalCenterXMillimeters +
                                 poseCosine * part.LocalCenterZMillimeters;
                result[i] = new PlanarOrientedRectangle(
                    centerX,
                    centerZ,
                    part.WidthMillimeters * 0.5d,
                    part.DepthMillimeters * 0.5d,
                    pose.YawDegrees + part.LocalYawDeciDegrees / 10d);
            }

            return result;
        }

        public static bool Overlaps(
            in PlanarOrientedRectangle left,
            in PlanarOrientedRectangle right)
        {
            double deltaX = right.CenterX - left.CenterX;
            double deltaZ = right.CenterZ - left.CenterZ;
            return OverlapsOnAxis(left.AxisXX, left.AxisXZ, deltaX, deltaZ, left, right) &&
                   OverlapsOnAxis(left.AxisZX, left.AxisZZ, deltaX, deltaZ, left, right) &&
                   OverlapsOnAxis(right.AxisXX, right.AxisXZ, deltaX, deltaZ, left, right) &&
                   OverlapsOnAxis(right.AxisZX, right.AxisZZ, deltaX, deltaZ, left, right);
        }

        private static bool OverlapsOnAxis(
            double axisX,
            double axisZ,
            double deltaX,
            double deltaZ,
            in PlanarOrientedRectangle left,
            in PlanarOrientedRectangle right)
        {
            double distance = Math.Abs(deltaX * axisX + deltaZ * axisZ);
            double radius = left.ProjectedRadius(axisX, axisZ) +
                            right.ProjectedRadius(axisX, axisZ);
            // 边缘恰好接触允许摆放；微小容差只吸收三角函数误差，不创造可见间隙。
            return distance + GeometryToleranceMillimeters < radius;
        }
    }
}
