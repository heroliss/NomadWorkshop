using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class NomadS0VisualDetailTests
    {
        [TestCase("Assets/Game/NomadWorkshop/ArtFirstPass/Prefabs/NW1_Driver.prefab", 6)]
        [TestCase("Assets/Game/NomadWorkshop/ArtFirstPass/Prefabs/NW1_Easel.prefab", 6)]
        public void WarmSampleDetails_AreReplaceableAndStayOutOfGameplayGeometry(string path, int minimumDetailCount)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            Transform detail = prefab.transform.Find("NW16_VisualDetail");
            Assert.That(detail, Is.Not.Null, "可替换视觉细节根节点缺失");
            Assert.That(detail.childCount, Is.GreaterThanOrEqualTo(minimumDetailCount));
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty,
                "视觉细节不应改变设施碰撞或导航");
            Renderer[] renderers = detail.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Assert.That(renderers.All(r => r.sharedMaterial != null &&
                r.sharedMaterial.shader.name == "Universal Render Pipeline/Lit"), Is.True);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(-.001f));
            Assert.That(bounds.max.y, Is.LessThanOrEqualTo(path.Contains("Driver") ? 1.10f : 1.65f));
            Assert.That(Mathf.Abs(bounds.min.x), Is.LessThanOrEqualTo(path.Contains("Driver") ? .55f : .66f));
            Assert.That(Mathf.Abs(bounds.max.x), Is.LessThanOrEqualTo(path.Contains("Driver") ? .55f : .66f));
        }
    }
}
