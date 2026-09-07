using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class FoundationRoofPresentationTests
    {
        private readonly List<GameObject> _roots = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in _roots) if (root != null) Object.DestroyImmediate(root);
            _roots.Clear();
        }

        [Test]
        public void CutawayAndDispose_PreserveOriginalEnableShadowCollisionAndLightStates()
        {
            var binding = CreateBinding(out Renderer on, out Renderer off, out Renderer disabled);
            off.shadowCastingMode = ShadowCastingMode.Off;
            disabled.enabled = false; disabled.shadowCastingMode = ShadowCastingMode.TwoSided;
            on.forceRenderingOff = true;
            var light = binding.gameObject.AddComponent<Light>();
            var presentation = new FoundationRoofPresentation(binding);
            try
            {
                Assert.That(on.shadowCastingMode, Is.EqualTo(ShadowCastingMode.ShadowsOnly));
                Assert.That(on.enabled, Is.True);
                Assert.That(off.enabled, Is.False);
                Assert.That(off.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(disabled.enabled, Is.False);
                presentation.ToggleExteriorPreference();
                Assert.That(on.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
                Assert.That(off.enabled, Is.True);
                Assert.That(disabled.enabled, Is.False);
                Assert.That(disabled.shadowCastingMode, Is.EqualTo(ShadowCastingMode.TwoSided));
                presentation.SetBuildCutaway(true);
                foreach (Collider collider in binding.GetComponentsInChildren<Collider>(true))
                {
                    Assert.That(collider.enabled, Is.True);
                    Assert.That(collider.gameObject.activeInHierarchy, Is.True);
                }
                Assert.That(light.enabled && light.gameObject.activeInHierarchy, Is.True);
                Assert.That(on.forceRenderingOff, Is.True);
            }
            finally { presentation.Dispose(); }
            presentation.Dispose();
            Assert.That(on.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
            Assert.That(off.enabled, Is.True);
            Assert.That(disabled.enabled, Is.False);
            Assert.That(disabled.shadowCastingMode, Is.EqualTo(ShadowCastingMode.TwoSided));
            Assert.Throws<ObjectDisposedException>(() => presentation.ToggleExteriorPreference());
        }

        [TestCase(0)] // 空引用。
        [TestCase(1)] // 同组重复。
        [TestCase(2)] // 越过模型根。
        [TestCase(3)] // 跨组重复。
        public void InvalidBinding_DoesNotPartiallyChangeEarlierRenderers(int invalidKind)
        {
            var binding = CreateBinding(out Renderer first, out Renderer second, out _);
            var foreign = CreateBinding(out Renderer outside, out _, out _);
            Renderer invalid = invalidKind switch { 0 => null, 1 => first, 2 => outside, _ => second };
            SetReferences(binding, first, invalid);
            Assert.Throws<ArgumentException>(() => new FoundationRoofPresentation(
                invalidKind == 3 ? new[] { binding, binding } : new[] { binding }));
            Assert.That(first.enabled, Is.True);
            Assert.That(first.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On),
                "失败必须发生在任何 Renderer 修改之前。");
        }

        [Test]
        public void DestroyedModel_DoesNotPreventSessionDisposal()
        {
            var binding = CreateBinding(out _, out _, out _);
            var presentation = new FoundationRoofPresentation(binding);
            Object.DestroyImmediate(binding.gameObject);
            Assert.DoesNotThrow(() => presentation.Dispose());
            Assert.That(presentation.IsDisposed, Is.True);
        }

        private FoundationRoofVisual CreateBinding(out Renderer first, out Renderer second, out Renderer third)
        {
            var root = new GameObject("roof binding test"); _roots.Add(root);
            var binding = root.AddComponent<FoundationRoofVisual>();
            Renderer Create()
            {
                var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
                child.transform.SetParent(root.transform, false);
                return child.GetComponent<Renderer>();
            }
            first = Create(); second = Create(); third = Create();
            SetReferences(binding, first, second, third);
            return binding;
        }

        private static void SetReferences(FoundationRoofVisual binding, params Renderer[] renderers)
        {
            var data = new SerializedObject(binding);
            var property = data.FindProperty("roofRenderers");
            property.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
