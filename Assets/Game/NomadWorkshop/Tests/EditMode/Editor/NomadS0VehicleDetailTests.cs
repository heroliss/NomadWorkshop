using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class NomadS0VehicleDetailTests
    {
        [Test]
        public void VehicleEdgeAndCanopyDetails_AreReplaceableAndStayInsideVisualEnvelope()
        {
            const string path = "Assets/Game/NomadWorkshop/ArtFirstPass/Prefabs/NW5_Vehicle.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            Transform detail = prefab.transform.Find("NW17_VisualDetail");
            Assert.That(detail, Is.Not.Null, "车辆可替换视觉细节根节点缺失");
            Assert.That(detail.childCount, Is.GreaterThanOrEqualTo(14));
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty,
                "车辆封边与棚下灯罩不应改变碰撞或导航");
            Renderer[] renderers = detail.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Assert.That(renderers.All(r => r.sharedMaterial != null &&
                r.sharedMaterial.shader.name == "Universal Render Pipeline/Lit"), Is.True);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            Assert.That(bounds.min.x, Is.GreaterThanOrEqualTo(-5.45f));
            Assert.That(bounds.max.x, Is.LessThanOrEqualTo(5.45f));
            Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(0.02f));
            Assert.That(bounds.max.y, Is.LessThanOrEqualTo(2.50f));
            Assert.That(bounds.min.z, Is.GreaterThanOrEqualTo(-4.05f));
            Assert.That(bounds.max.z, Is.LessThanOrEqualTo(4.05f));
        }
    }
}
