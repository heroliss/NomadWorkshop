using System;
using System.Collections.Generic;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using Game.NomadWorkshop.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class FoundationFacilitySpaceAuthoringTests
    {
        private GameObject _root;
        private FoundationFacilitySpaceAuthoring _source;
        private NomadFacilityDefinition _definition;
        private Transform _body, _tray;
        private FacilityInteractionGroup _work, _service;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("空间测试设施");
            _source = _root.AddComponent<FoundationFacilitySpaceAuthoring>();
            _body = Node("底座", _root.transform, Vector3.zero);
            _tray = Node("托盘", _root.transform, new Vector3(0, 1.2f, 0));
            _work = Group("work", new Vector3(-.4f, 0, -1), new Vector3(.4f, 0, -1));
            _service = Group("service", new Vector3(0, 0, 1));
            Configure();
            _definition = ScriptableObject.CreateInstance<NomadFacilityDefinition>();
            FoundationFacilitySpaceSnapshot source = _source.CreateSnapshot();
            _definition.ConfigureForEditor("test", "测试设施", NomadFacilityFunction.Storage, false,
                source.Footprints.ToArray(), source.Groups.ToArray(), source.Regions.ToArray(),
                false, Vector2.zero, 0, Vector3.one, Color.white);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_definition);
        }

        [Test]
        public void NestedFrames_ResolveRootMetersYawHeightAndNonUniformSize()
        {
            var parent = Node("模型内部坐标", _root.transform, new Vector3(1, .3f, 2));
            parent.localRotation = Quaternion.Euler(0, 90, 0);
            parent.localScale = new Vector3(2, 1, 3);
            _tray.SetParent(parent, false);
            _tray.localPosition = new Vector3(.5f, .9f, 0);
            var region = _source.CreateSnapshot().Regions.Single();
            Assert.That(region.LocalCenterMeters.x, Is.EqualTo(1).Within(.0001f));
            Assert.That(region.LocalCenterMeters.y, Is.EqualTo(1).Within(.0001f));
            Assert.That(region.SupportHeightMeters, Is.EqualTo(1.2f).Within(.0001f));
            Assert.That(region.SizeMeters.x, Is.EqualTo(1.2f).Within(.0001f));
            Assert.That(region.SizeMeters.y, Is.EqualTo(1.2f).Within(.0001f));
            Assert.That(region.LocalYawDegrees, Is.EqualTo(90).Within(.0001f));
        }

        [Test]
        public void WorldPlacementAndObjectNames_DoNotChangeSignature()
        {
            string before = _source.CreateSnapshot().Signature;
            _root.transform.SetPositionAndRotation(new Vector3(12, 5, -8), Quaternion.Euler(0, 71, 0));
            _tray.name = "替换后的模型托盘";
            Assert.That(_source.CreateSnapshot().Signature, Is.EqualTo(before));
        }

        [TestCase("tilt")]
        [TestCase("shear")]
        [TestCase("reflection")]
        [TestCase("rootScale")]
        [TestCase("raisedSlot")]
        [TestCase("smallRegion")]
        [TestCase("outsideRoot")]
        public void UnsupportedGeometry_IsRejectedBeforeGeneratingDefinition(string condition)
        {
            string before = EditorJsonUtility.ToJson(_definition);
            if (condition == "tilt") _tray.localRotation = Quaternion.Euler(10, 0, 0);
            if (condition == "reflection") _tray.localScale = new Vector3(-1, 1, 1);
            if (condition == "rootScale") _root.transform.localScale = Vector3.one * 2;
            if (condition == "raisedSlot") _work.AlternativeSlots[0].transform.localPosition += Vector3.up;
            if (condition == "smallRegion") _tray.localScale = Vector3.one * .001f;
            if (condition == "shear")
            {
                var parent = Node("非均匀父级", _root.transform, Vector3.zero);
                parent.localScale = new Vector3(2, 1, 1);
                _tray.SetParent(parent, false);
                _tray.localRotation = Quaternion.Euler(0, 45, 0);
            }
            if (condition == "outsideRoot") _tray.SetParent(null, true);
            try
            {
                Assert.Throws<InvalidOperationException>(() => NomadFacilitySpaceBaker.Bake(_definition, _root));
                Assert.That(EditorJsonUtility.ToJson(_definition), Is.EqualTo(before));
            }
            finally { if (condition == "outsideRoot") _tray.SetParent(_root.transform, true); }
        }

        [Test]
        public void DuplicateGroupsAndRegions_AreRejected()
        {
            Configure(new[] { _work, _work });
            Assert.Throws<InvalidOperationException>(() => _source.CreateSnapshot());
            Configure();
            var region = _source.PlacementRegions[0];
            _source.ConfigureForEditor(_source.Footprints.ToArray(), new[] { _work, _service }, new[] { region, region });
            Assert.Throws<InvalidOperationException>(() => _source.CreateSnapshot());
        }

        [Test]
        public void BakingKeepsIdentity_AndAcceptsLegacyOnlyWhileBaselineSpaceMatches()
        {
            string baseline = _definition.CaptureSpaceSnapshot().Signature;
            NomadFacilitySpaceBaker.Bake(_definition, _root);
            Assert.That(_definition.Id, Is.EqualTo("test"));
            Assert.That(_definition.GeneratedSpaceSignature, Is.EqualTo(baseline));
            Assert.DoesNotThrow(() => _definition.ValidateSavedSpaceSignature(null));
            _tray.localPosition += Vector3.up * .1f;
            NomadFacilitySpaceBaker.Bake(_definition, _root);
            Assert.Throws<NotSupportedException>(() => _definition.ValidateSavedSpaceSignature(null));
            Assert.Throws<NotSupportedException>(() => _definition.ValidateSavedSpaceSignature(baseline));
            Assert.DoesNotThrow(() => _definition.ValidateSavedSpaceSignature(_definition.GeneratedSpaceSignature));
            // 重复生成不能把变更后的空间重新宣称为无版本旧档基线。
            NomadFacilitySpaceBaker.Bake(_definition, _root);
            Assert.Throws<NotSupportedException>(() => _definition.ValidateSavedSpaceSignature(string.Empty));
        }

        [Test]
        public void StartupRejectsUnbakedMovedOrManuallyEditedSpace()
        {
            var data = new SerializedObject(_definition);
            data.FindProperty("prefab").objectReferenceValue = _root;
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.Throws<InvalidOperationException>(() => _definition.ValidateModelSpaceSnapshot());
            NomadFacilitySpaceBaker.Bake(_definition, _root);
            _work.AlternativeSlots[0].transform.localPosition += Vector3.right * .1f;
            Assert.Throws<InvalidOperationException>(() => _definition.ValidateModelSpaceSnapshot());
            NomadFacilitySpaceBaker.Bake(_definition, _root);
            data.Update();
            data.FindProperty("placementRegions").GetArrayElementAtIndex(0)
                .FindPropertyRelative("supportHeightMeters").floatValue += .1f;
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.Throws<InvalidOperationException>(() => _definition.ValidateModelSpaceSnapshot());
        }

        [Test]
        public void NewDefinition_GeneratesWithoutManualSpace_AndNeverInventsLegacyCompatibility()
        {
            Object.DestroyImmediate(_definition);
            _definition = ScriptableObject.CreateInstance<NomadFacilityDefinition>();
            NomadFacilitySpaceBaker.Bake(_definition, _root);
            Assert.That(_definition.GeneratedSpaceSignature, Is.EqualTo(_source.CreateSnapshot().Signature));
            Assert.Throws<NotSupportedException>(() => _definition.ValidateSavedSpaceSignature(null));
            NomadFacilitySpaceBaker.Bake(_definition, _root);
            Assert.Throws<NotSupportedException>(() => _definition.ValidateSavedSpaceSignature(null));
        }

        [Test]
        public void CandidateCountCanChange_ButGroupCapacityRemainsOne_AndSeparatedGroupsAreIndependent()
        {
            var third = Slot(_work.transform, "extra", new Vector3(.9f, 0, -1));
            _work.ConfigureRuntime("work", true, _work.AlternativeSlots.Concat(new[] { third }).ToArray());
            NomadFacilitySpaceBaker.Bake(_definition, _root);
            Assert.That(_definition.InteractionGroups[0].AlternativeSlots.Count, Is.EqualTo(3));
            using var runtime = new FoundationInteractionSpaceRuntime(350);
            runtime.RebuildCommitted(new[] { new FoundationFacilityState("fixture", "test", default) },
                new Dictionary<string, NomadFacilityDefinition> { { "test", _definition } }, false);
            InteractionSpaceSlot At(string group, string slot) => new(new InteractionSlotAddress("fixture", group, slot), default);
            Assert.That(runtime.TryAcquire(1, At("work", "slot-0"), out var first), Is.True);
            Assert.That(runtime.IsAvailable(At("work", "extra").Address), Is.False);
            Assert.That(runtime.TryAcquire(2, At("work", "extra"), out _), Is.False);
            Assert.That(runtime.TryAcquire(2, At("service", "slot-0"), out var second), Is.True);
            first.Dispose(); second.Dispose();
        }

        [Test]
        public void GroupCapacity_DoesNotLockAnotherGroupsRemoteAlternative_AndSurvivesRebuild()
        {
            var near = Slot(_service.transform, "near-work", new Vector3(-.4f, 0, -1));
            _service.ConfigureRuntime("service", true, _service.AlternativeSlots.Concat(new[] { near }).ToArray());
            NomadFacilitySpaceBaker.Bake(_definition, _root);
            using var runtime = new FoundationInteractionSpaceRuntime(350);
            var facilities = new[] { new FoundationFacilityState("fixture", "test", default) };
            var definitions = new Dictionary<string, NomadFacilityDefinition> { { "test", _definition } };
            runtime.RebuildCommitted(facilities, definitions, false);
            InteractionSpaceSlot At(string group, string slot) => new(new InteractionSlotAddress("fixture", group, slot), default);
            Assert.That(runtime.TryAcquire(1, At("work", "slot-0"), out var work), Is.True);
            Assert.That(runtime.TryAcquire(2, At("service", "near-work"), out _), Is.False, "共享站位仍互斥。");
            Assert.That(runtime.TryAcquire(2, At("service", "slot-0"), out var service), Is.True, "另一组的远端站位不受未选择候选的传递占用影响。");
            Assert.That(runtime.RebuildCommitted(facilities, definitions, true), Is.True);
            Assert.That(work.IsActive && service.IsActive, Is.True);
            Assert.That(runtime.TryAcquire(3, At("work", "slot-1"), out _), Is.False);
            work.Dispose();
            Assert.That(runtime.TryAcquire(3, At("work", "slot-1"), out var replacement), Is.True);
            replacement.Dispose(); service.Dispose();
        }

        private void Configure(FacilityInteractionGroup[] groups = null) => _source.ConfigureForEditor(
            new[] { new FoundationFacilitySpaceAuthoring.Footprint { id = "body", frame = _body, sizeMeters = Vector2.one } },
            groups ?? new[] { _work, _service },
            new[] { new FoundationFacilitySpaceAuthoring.Region { id = "tray", frame = _tray,
                sizeMeters = new Vector2(.6f, .4f), edgeInsetMeters = .02f, acceptedCategories = new[] { "tool-small" } } });

        private FacilityInteractionGroup Group(string id, params Vector3[] positions)
        {
            var node = Node(id, _root.transform, Vector3.zero);
            var group = node.gameObject.AddComponent<FacilityInteractionGroup>();
            group.ConfigureRuntime(id, true, positions.Select((p, i) => Slot(node, "slot-" + i, p)).ToArray());
            return group;
        }

        private static FacilityInteractionSlot Slot(Transform parent, string id, Vector3 p)
        {
            var slot = Node(id, parent, p).gameObject.AddComponent<FacilityInteractionSlot>();
            slot.ConfigureRuntime(id, ResidentAnimationSemantic.Pickup, null);
            return slot;
        }

        private static Transform Node(string name, Transform parent, Vector3 position)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false); node.localPosition = position;
            return node;
        }
    }
}
