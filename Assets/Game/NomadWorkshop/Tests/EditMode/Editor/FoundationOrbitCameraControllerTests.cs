using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEngine;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class FoundationOrbitCameraControllerTests
    {
        [Test]
        public void TwoPointerGesture_SeparatesCentroidOrbitAndPinchDistance()
        {
            bool dragged = FoundationTwoPointerGestureUtility.TryCalculate(
                new Vector2(80f, 100f),
                new Vector2(10f, -4f),
                new Vector2(220f, 100f),
                new Vector2(10f, -4f),
                out FoundationTwoPointerGesture dragGesture);
            bool pinched = FoundationTwoPointerGestureUtility.TryCalculate(
                new Vector2(70f, 100f),
                new Vector2(-10f, 0f),
                new Vector2(230f, 100f),
                new Vector2(10f, 0f),
                out FoundationTwoPointerGesture pinchGesture);

            Assert.That(dragged, Is.True);
            Assert.That(dragGesture.OrbitDeltaPixels, Is.EqualTo(new Vector2(10f, -4f)));
            Assert.That(dragGesture.PinchDeltaPixels, Is.Zero.Within(0.001f));
            Assert.That(pinched, Is.True);
            Assert.That(pinchGesture.OrbitDeltaPixels, Is.EqualTo(Vector2.zero));
            Assert.That(pinchGesture.PinchDeltaPixels, Is.EqualTo(20f).Within(0.001f));
        }

        [Test]
        public void TwoPointerGesture_RejectsNonFiniteInput()
        {
            bool succeeded = FoundationTwoPointerGestureUtility.TryCalculate(
                new Vector2(float.NaN, 0f),
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                out _);

            Assert.That(succeeded, Is.False);
        }

        [Test]
        public void OrbitAndZoom_KeepCameraFocusedAndClampPlayableRange()
        {
            var spaceObject = new GameObject("Camera Focus Space");
            var cameraObject = new GameObject("Foundation Camera");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                spaceObject.transform.SetPositionAndRotation(
                    new Vector3(3f, 1f, -2f),
                    Quaternion.Euler(0f, 25f, 0f));
                Vector3 focusLocal = new(1f, 0f, -0.5f);
                var controller = new FoundationOrbitCameraController(
                    camera,
                    spaceObject.transform,
                    focusLocal,
                    focusLocal + new Vector3(11f, 13f, -12f),
                    minimumDistance: 7f,
                    maximumDistance: 28f);
                float initialDistance = controller.Distance;

                controller.Zoom(120f);
                controller.Orbit(new Vector2(140f, 10000f));

                Assert.That(controller.Distance, Is.LessThan(initialDistance));
                Assert.That(controller.PitchDegrees, Is.EqualTo(24f).Within(0.001f));
                Vector3 focusWorld = spaceObject.transform.TransformPoint(focusLocal);
                Vector3 expectedForward = (focusWorld - camera.transform.position).normalized;
                Assert.That(
                    Vector3.Dot(camera.transform.forward, expectedForward),
                    Is.GreaterThan(0.999f));

                controller.Zoom(100000f);
                Assert.That(controller.Distance, Is.EqualTo(7f).Within(0.001f));
                controller.Zoom(-100000f);
                Assert.That(controller.Distance, Is.EqualTo(28f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(spaceObject);
            }
        }

        [Test]
        public void MotionFocusOffset_ShiftsOnlyFocusAndRejectsNonFiniteInput()
        {
            var spaceObject = new GameObject("Camera Motion Focus Space");
            var cameraObject = new GameObject("Foundation Motion Camera");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                var controller = new FoundationOrbitCameraController(
                    camera,
                    spaceObject.transform,
                    Vector3.zero,
                    new Vector3(0f, 10f, -10f));
                Vector3 before = camera.transform.position;

                controller.SetMotionFocusOffset(new Vector3(0f, 0f, .4f));
                Assert.That(controller.MotionFocusOffset, Is.EqualTo(new Vector3(0f, 0f, .4f)));
                Assert.That(camera.transform.position, Is.Not.EqualTo(before));
                Vector3 expectedFocus = new Vector3(0f, 0f, .4f);
                Assert.That(Vector3.Dot(camera.transform.forward,
                    (expectedFocus - camera.transform.position).normalized), Is.GreaterThan(.999f));

                controller.SetMotionFocusOffset(new Vector3(float.NaN, 0f, 0f));
                Assert.That(controller.MotionFocusOffset, Is.EqualTo(new Vector3(0f, 0f, .4f)));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(spaceObject);
            }
        }
    }
}
