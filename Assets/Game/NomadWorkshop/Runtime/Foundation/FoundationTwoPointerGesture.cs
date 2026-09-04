using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>一帧双指输入解析出的设备无关镜头意图；单位仍是屏幕像素，由 View 决定灵敏度。</summary>
    public readonly struct FoundationTwoPointerGesture
    {
        public FoundationTwoPointerGesture(
            Vector2 orbitDeltaPixels,
            float pinchDeltaPixels)
        {
            OrbitDeltaPixels = orbitDeltaPixels;
            PinchDeltaPixels = pinchDeltaPixels;
        }

        /// <summary>两根手指质心的位移，用于轨道旋转。</summary>
        public Vector2 OrbitDeltaPixels { get; }

        /// <summary>当前指距减去上一帧指距；正数表示张开手指、镜头拉近。</summary>
        public float PinchDeltaPixels { get; }
    }

    /// <summary>
    /// 把触屏原始位置转换为镜头意图，不直接访问 Input System 或相机，因此可以用确定性测试锁定手势语义。
    /// </summary>
    public static class FoundationTwoPointerGestureUtility
    {
        public static bool TryCalculate(
            Vector2 firstPosition,
            Vector2 firstDelta,
            Vector2 secondPosition,
            Vector2 secondDelta,
            out FoundationTwoPointerGesture gesture)
        {
            if (!IsFinite(firstPosition) ||
                !IsFinite(firstDelta) ||
                !IsFinite(secondPosition) ||
                !IsFinite(secondDelta))
            {
                gesture = default;
                return false;
            }

            Vector2 previousFirst = firstPosition - firstDelta;
            Vector2 previousSecond = secondPosition - secondDelta;
            float currentDistance = Vector2.Distance(firstPosition, secondPosition);
            float previousDistance = Vector2.Distance(previousFirst, previousSecond);
            gesture = new FoundationTwoPointerGesture(
                (firstDelta + secondDelta) * 0.5f,
                currentDistance - previousDistance);
            return true;
        }

        private static bool IsFinite(Vector2 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y);
    }
}
