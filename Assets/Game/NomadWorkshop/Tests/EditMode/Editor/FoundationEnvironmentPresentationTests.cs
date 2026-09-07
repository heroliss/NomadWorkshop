using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class FoundationEnvironmentPresentationTests
    {
        private readonly List<Object> _owned = new();
        private static readonly int ST = Shader.PropertyToID("_BaseMap_ST");

        [TearDown]
        public void TearDown()
        {
            for (int i = _owned.Count-1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        [Test]
        public void SignedDistance_PreservesTiling_UsesEachSurfaceMetres_AndRestoresWithoutDrift()
        {
            FoundationEnvironmentVisual binding = Create(out Renderer ground, out Renderer track, out Transform scenery);
            var block = new MaterialPropertyBlock();
            block.SetVector(ST, new Vector4(1.5f, 2, .2f, .3f)); block.SetFloat("_Probe", 17);
            ground.SetPropertyBlock(block);
            binding.transform.SetPositionAndRotation(new Vector3(45, 7, -23), Quaternion.Euler(0, 71, 0));
            Vector3 origin = binding.transform.position;
            using (var presentation = new FoundationEnvironmentPresentation(binding.transform))
            {
                presentation.Render(1500000);
                Assert.That(Uv(ground), Is.EqualTo(new Vector4(1.5f, 2, .2f, .8f)));
                Assert.That(Uv(track).w, Is.EqualTo(1f/14f).Within(.000001f));
                Assert.That(binding.transform.InverseTransformPoint(scenery.position).z, Is.EqualTo(2.5f).Within(.00001f));
                Vector3 savedPosition = scenery.position; Vector4 savedUv = Uv(ground);
                for (int i = 0; i < 80; i++) presentation.Render(1500000);
                Assert.That(scenery.position, Is.EqualTo(savedPosition), "重复/暂停投影不能继续移动。");
                presentation.Render(-1500000);
                Assert.That(Uv(track).w, Is.EqualTo(13f/14f).Within(.000001f));
                Assert.That(binding.transform.InverseTransformPoint(scenery.position).z, Is.EqualTo(5.5f).Within(.00001f));
                presentation.Render(61500000);
                Assert.That(Vector3.Distance(scenery.position, savedPosition), Is.LessThan(.00001f), "60 米循环回到相同地景位置。");
                presentation.Render(0); presentation.Render(1500000);
                Assert.That(Uv(ground), Is.EqualTo(savedUv)); Assert.That(scenery.position, Is.EqualTo(savedPosition));
                Assert.That(binding.transform.position, Is.EqualTo(origin), "地表根必须保持静止。");
                ground.GetPropertyBlock(block); Assert.That(block.GetFloat("_Probe"), Is.EqualTo(17));
                Assert.That(ground.sharedMaterial.GetVector(ST), Is.EqualTo(new Vector4(1, 1, 0, 0)), "共享材质不能被滚动状态污染。");
            }
            Assert.That(Uv(ground), Is.EqualTo(new Vector4(1.5f, 2, .2f, .3f)));
            track.GetPropertyBlock(block); Assert.That(block.isEmpty, Is.True);
            Assert.That(binding.transform.InverseTransformPoint(scenery.position).z, Is.EqualTo(4).Within(.00001f));
        }

        [TestCase(0)] // 空引用。
        [TestCase(1)] // 重复表面。
        [TestCase(2)] // 引用环境以外对象。
        [TestCase(3)] // 同时移动几何和 UV。
        [TestCase(4)] // 零长度。
        [TestCase(5)] // 非有限长度。
        [TestCase(6)] // 嵌套地景。
        public void InvalidBinding_IsRejectedBeforeAnyPositionOrPropertyChanges(int invalid)
        {
            var binding = Create(out Renderer ground, out _, out Transform scenery);
            var data = new SerializedObject(binding);
            var second = data.FindProperty("scrollingSurfaces").GetArrayElementAtIndex(1);
            if (invalid < 3)
            {
                Renderer renderer = invalid == 0 ? null : invalid == 1 ? ground : MakeRenderer(NewRoot("foreign").transform);
                second.FindPropertyRelative("renderer").objectReferenceValue = renderer;
            }
            if (invalid == 3) ground.transform.SetParent(scenery, true);
            if (invalid == 4) second.FindPropertyRelative("metersPerUvUnit").floatValue = 0;
            if (invalid == 5) second.FindPropertyRelative("metersPerUvUnit").floatValue = float.NaN;
            if (invalid == 6)
            {
                var child = new GameObject("nested"); child.transform.SetParent(scenery, false);
                var groups = data.FindProperty("sceneryGroups"); groups.arraySize = 2;
                groups.GetArrayElementAtIndex(1).objectReferenceValue = child.transform;
            }
            data.ApplyModifiedPropertiesWithoutUndo(); Vector3 before = scenery.position;
            Assert.Throws<ArgumentException>(() => new FoundationEnvironmentPresentation(binding.transform));
            Assert.That(scenery.position, Is.EqualTo(before));
            var block = new MaterialPropertyBlock(); ground.GetPropertyBlock(block); Assert.That(block.isEmpty, Is.True);
        }

        [Test]
        public void LegacyEnvironment_StillUsesFourMetreGround_AndSixtyMetreOutcrops()
        {
            GameObject root = NewRoot("legacy"); Renderer ground = MakeRenderer(root.transform);
            ground.sharedMaterial.name = "NW1_Ground";
            var part = new GameObject("Desert outcrop_0"); part.transform.SetParent(root.transform, false);
            part.transform.localPosition = new Vector3(12, 0, 29);
            using var presentation = new FoundationEnvironmentPresentation(root.transform);
            presentation.Render(-2000000);
            Assert.That(Uv(ground).w, Is.EqualTo(.5f)); Assert.That(part.transform.localPosition.z, Is.EqualTo(-29));
        }

        [Test]
        public void DestroyedModel_DisposesSafely_AndReleasedOwnerCannotRender()
        {
            var binding = Create(out _, out _, out _);
            var presentation = new FoundationEnvironmentPresentation(binding.transform);
            Object.DestroyImmediate(binding.gameObject);
            Assert.DoesNotThrow(() => presentation.Dispose()); presentation.Dispose();
            Assert.Throws<ObjectDisposedException>(() => presentation.Render(0));
        }

        private FoundationEnvironmentVisual Create(out Renderer ground, out Renderer track, out Transform scenery)
        {
            GameObject root = NewRoot("environment"); var binding = root.AddComponent<FoundationEnvironmentVisual>();
            ground = MakeRenderer(root.transform); track = MakeRenderer(root.transform);
            scenery = new GameObject("replaceable scenery").transform; scenery.SetParent(root.transform, false);
            scenery.localPosition = new Vector3(12, -2.3f, 4);
            var data = new SerializedObject(binding); var surfaces = data.FindProperty("scrollingSurfaces"); surfaces.arraySize = 2;
            for (int i = 0; i < 2; i++)
            {
                var item = surfaces.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("renderer").objectReferenceValue = i == 0 ? ground : track;
                item.FindPropertyRelative("metersPerUvUnit").floatValue = i == 0 ? 6 : 1.4f;
            }
            var groups = data.FindProperty("sceneryGroups"); groups.arraySize = 1; groups.GetArrayElementAtIndex(0).objectReferenceValue = scenery;
            data.ApplyModifiedPropertiesWithoutUndo(); return binding;
        }

        private GameObject NewRoot(string name) { var root = new GameObject(name); _owned.Add(root); return root; }
        private Renderer MakeRenderer(Transform root)
        {
            var child = new GameObject("surface"); child.transform.SetParent(root, false);
            Renderer renderer = child.AddComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); _owned.Add(material);
            renderer.sharedMaterial = material; return renderer;
        }
        private static Vector4 Uv(Renderer renderer) { var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); return block.GetVector(ST); }
    }
}
