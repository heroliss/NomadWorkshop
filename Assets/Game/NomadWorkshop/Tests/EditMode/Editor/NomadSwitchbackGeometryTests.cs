using System.Linq;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor.Tests
{
    /// <summary>检查落盘的实际网格和支承关系，防止能走通的隐藏替身掩盖断裂或悬空结构。</summary>
    public sealed class NomadSwitchbackGeometryTests
    {
        private Scene _scene, _previous;
        private bool _owns;
        private Transform _nav;

        [SetUp]
        public void SetUp()
        {
            _previous = SceneManager.GetActiveScene();
            _scene = SceneManager.GetSceneByPath(NomadSwitchbackStairPipeline.ScenePath);
            _owns = !_scene.IsValid() || !_scene.isLoaded;
            if (_owns) _scene = EditorSceneManager.OpenScene(NomadSwitchbackStairPipeline.ScenePath, OpenSceneMode.Additive);
            _nav = _scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NavMeshSurface>()).Single().transform;
        }

        [TearDown]
        public void TearDown()
        {
            if (_previous.IsValid() && _previous.isLoaded) SceneManager.SetActiveScene(_previous);
            if (_owns && _scene.IsValid()) EditorSceneManager.CloseScene(_scene, true);
        }

        [Test]
        public void EighteenThinFoldedTreads_HaveSameVisibleAndPhysicalMeshes_InOppositeFlights()
        {
            var steps = _nav.GetComponentsInChildren<MeshFilter>().Where(f => f.name.Contains("Actual Tread"))
                .OrderBy(f => f.name).ToArray();
            Assert.That(steps.Length, Is.EqualTo(18));
            for (int i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                var collider = step.GetComponent<MeshCollider>();
                Assert.That(collider, Is.Not.Null);
                Assert.That(collider.sharedMesh, Is.SameAs(step.sharedMesh));
                Assert.That(collider.convex, Is.False, "薄折边凹截面不能以凸包填平。");
                Assert.That(step.GetComponent<MeshRenderer>().enabled, Is.True);
                Assert.That(step.sharedMesh.bounds.size.y, Is.InRange(.069f,.071f));
                Assert.That(step.sharedMesh.bounds.size.x, Is.EqualTo(1.65f).Within(.001f));
                Assert.That(step.sharedMesh.bounds.size.z, Is.EqualTo(.30f).Within(.001f));
                Vector3 top = _nav.InverseTransformPoint(step.transform.TransformPoint(new Vector3(0,0,.15f)));
                Assert.That(top.y, Is.EqualTo((i + 1) * 3.2f / 18f).Within(.001f));
                Assert.That(top.x, Is.EqualTo(i < 9 ? 1.2f : 3.07f).Within(.001f));
                Vector3 direction = _nav.InverseTransformDirection(step.transform.forward);
                Assert.That(direction.z, Is.EqualTo(i < 9 ? -1f : 1f).Within(.001f));
            }
            Assert.That(_nav.GetComponentsInChildren<Collider>().Any(c => c.name.Contains("Ramp")), Is.False);
        }

        [Test]
        public void LandingRetainsDeclaredTurningArea_AndUpperRailEndsHaveActualSupportPosts()
        {
            Transform landing = _nav.Find("Carrier Turning Landing");
            var collider = landing.GetComponent<BoxCollider>();
            Assert.That(Vector3.Scale(collider.size,landing.localScale).x, Is.EqualTo(3.52f).Within(.001f));
            Assert.That(Vector3.Scale(collider.size,landing.localScale).z, Is.EqualTo(1.65f).Within(.001f));
            Assert.That(landing.localPosition.y + landing.localScale.y*.5f, Is.EqualTo(1.6f).Within(.001f));
            var posts = _nav.GetComponentsInChildren<MeshRenderer>().Where(r => r.name == "Upper Guard Post").ToArray();
            Assert.That(posts.Length, Is.EqualTo(7));
            foreach (var rail in _nav.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.StartsWith("Upper ") && r.name.EndsWith(" Rail")))
            foreach (float end in new[] {-1f,1f})
            {
                Vector3 endpoint = rail.transform.TransformPoint(Vector3.up * end);
                Assert.That(posts.Any(p => { Bounds b = p.bounds; b.Expand(.003f); return b.Contains(endpoint); }),
                    Is.True, rail.name + " 端点不能悬空。");
            }
        }
    }
}
