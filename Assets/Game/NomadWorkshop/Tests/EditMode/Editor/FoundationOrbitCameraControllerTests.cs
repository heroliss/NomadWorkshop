using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEngine;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class FoundationOrbitCameraControllerTests
    {
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
    }
}
