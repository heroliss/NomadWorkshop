using System;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// Foundation 俯视镜头的表现状态。它只拥有轨道角度与距离，不读取输入、不发送业务 Command；
    /// 因而鼠标、手柄或触屏 View 都能复用同一套限位和镜头姿态。
    /// </summary>
    public sealed class FoundationOrbitCameraController
    {
        private readonly Camera _camera;
        private readonly Transform _focusSpace;
        private readonly Vector3 _focusLocalPosition;
        private readonly float _minimumDistance;
        private readonly float _maximumDistance;
        private readonly float _minimumPitch;
        private readonly float _maximumPitch;

        public FoundationOrbitCameraController(
            Camera camera,
            Transform focusSpace,
            Vector3 focusLocalPosition,
            Vector3 initialCameraLocalPosition,
            float minimumDistance = 7f,
            float maximumDistance = 28f,
            float minimumPitch = 24f,
            float maximumPitch = 78f)
        {
            _camera = camera != null
                ? camera
                : throw new ArgumentNullException(nameof(camera));
            _focusSpace = focusSpace != null
                ? focusSpace
                : throw new ArgumentNullException(nameof(focusSpace));
            _focusLocalPosition = focusLocalPosition;
            _minimumDistance = Mathf.Max(0.5f, minimumDistance);
            _maximumDistance = Mathf.Max(_minimumDistance, maximumDistance);
            _minimumPitch = Mathf.Clamp(minimumPitch, 1f, 89f);
            _maximumPitch = Mathf.Clamp(maximumPitch, _minimumPitch, 89f);

            Vector3 offset = initialCameraLocalPosition - focusLocalPosition;
            Distance = Mathf.Clamp(offset.magnitude, _minimumDistance, _maximumDistance);
            float horizontal = Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z);
            PitchDegrees = Mathf.Clamp(
                Mathf.Atan2(offset.y, horizontal) * Mathf.Rad2Deg,
                _minimumPitch,
                _maximumPitch);
            YawDegrees = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            Apply();
        }

        public float Distance { get; private set; }
        public float YawDegrees { get; private set; }
        public float PitchDegrees { get; private set; }

        public void Orbit(Vector2 pointerDelta, float degreesPerPixel = 0.18f)
        {
            if (!float.IsFinite(pointerDelta.x) || !float.IsFinite(pointerDelta.y)) return;
            float sensitivity = Mathf.Max(0f, degreesPerPixel);
            YawDegrees = Mathf.Repeat(YawDegrees + pointerDelta.x * sensitivity, 360f);
            PitchDegrees = Mathf.Clamp(
                PitchDegrees - pointerDelta.y * sensitivity,
                _minimumPitch,
                _maximumPitch);
            Apply();
        }

        public void Zoom(float inputDelta, float metersPerInputUnit = 1f)
        {
            if (!float.IsFinite(inputDelta)) return;
            Distance = Mathf.Clamp(
                Distance - inputDelta * Mathf.Max(0f, metersPerInputUnit),
                _minimumDistance,
                _maximumDistance);
            Apply();
        }

        public void Apply()
        {
            float yaw = YawDegrees * Mathf.Deg2Rad;
            float pitch = PitchDegrees * Mathf.Deg2Rad;
            float horizontal = Mathf.Cos(pitch) * Distance;
            Vector3 localOffset = new(
                Mathf.Sin(yaw) * horizontal,
                Mathf.Sin(pitch) * Distance,
                Mathf.Cos(yaw) * horizontal);
            Vector3 focusWorld = _focusSpace.TransformPoint(_focusLocalPosition);
            _camera.transform.position = _focusSpace.TransformPoint(
                _focusLocalPosition + localOffset);
            _camera.transform.rotation = Quaternion.LookRotation(
                focusWorld - _camera.transform.position,
                _focusSpace.up);
        }
    }
}
