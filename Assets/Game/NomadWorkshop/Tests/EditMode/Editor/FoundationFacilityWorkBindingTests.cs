using System;
using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class FoundationFacilityWorkBindingTests
    {
        private GameObject _root;
        private FoundationFacilityArtRig _rig;
        private Transform _valve, _lid, _inlet;
        private Material _material;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("binding-test");
            _rig = _root.AddComponent<FoundationFacilityArtRig>();
            _valve = Child("arbitrary valve name"); _lid = Child("arbitrary lid name"); _inlet = Child("arbitrary inlet name");
            _material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _rig.ConfigureDispenser(_valve, _lid, _inlet, _material, _material);
        }

        [TearDown]
        public void TearDown() { UnityEngine.Object.DestroyImmediate(_root); UnityEngine.Object.DestroyImmediate(_material); }

        private Transform Child(string name)
        {
            var node = new GameObject(name).transform;
            node.SetParent(_root.transform, false);
            return node;
        }
        private FoundationFacilityContactBinding Point(string id, Transform target,
            FoundationFacilityContactRole role = FoundationFacilityContactRole.WaterInlet) => new(id, role, target);
        private FoundationFacilityWorkBinding Receive(string id = "receive", string group = "drink-and-deliver", string slot = "",
            FoundationFacilityContactBinding[] points = null, string endpoint = "inlet") =>
            new(id, group, slot, FoundationResidentPhase.DeliveringWater, points ?? new[] { Point("inlet", _inlet) },
                Array.Empty<FoundationFacilityMotionBinding>(), FoundationFacilityWaterFlow.ReceiveFromCan, endpoint);

        [Test]
        public void ExactSlots_ResolveSelectedEndpoint_WithExtraOptionalPoints()
        {
            Transform right = Child("new high port");
            _rig.ConfigureBindings(Receive("left", slot: "left", points: new[]
            {
                Point("unused", _valve, FoundationFacilityContactRole.EffectOrigin), Point("inlet", _inlet),
                Point("second-unused", _lid, FoundationFacilityContactRole.EffectOrigin)
            }), Receive("right", slot: "right", points: new[] { Point("inlet", right) }));
            Assert.That(_rig.TryGetWaterInlet(new("facility", "drink-and-deliver", "right", FoundationResidentPhase.DeliveringWater, .5f), out Transform target), Is.True);
            Assert.That(target, Is.SameAs(right));
            Assert.That(_rig.TryGetWaterInlet(new("facility", "drink-and-deliver", "center", FoundationResidentPhase.DeliveringWater, .5f), out _), Is.False);
            Assert.That(_rig.TryGetWaterInlet(new("facility", "drink-and-deliver", "right", FoundationResidentPhase.Drinking, .5f), out _), Is.False);
        }

        [Test]
        public void DuplicateActionIds_AreRejected() => Assert.Throws<InvalidOperationException>(() =>
            _rig.ConfigureBindings(Receive(slot: "left"), Receive(slot: "right")));

        [Test]
        public void DuplicatePointIds_AreRejected() => Assert.Throws<InvalidOperationException>(() =>
            _rig.ConfigureBindings(Receive(points: new[] { Point("inlet", _inlet), Point("inlet", _valve) })));

        [Test]
        public void MissingRequiredEndpoint_AreRejected() => Assert.Throws<InvalidOperationException>(() =>
            _rig.ConfigureBindings(Receive(endpoint: "not-bound")));

        [Test]
        public void WrongEndpointRole_IsRejected() => Assert.Throws<InvalidOperationException>(() =>
            _rig.ConfigureBindings(Receive(points: new[] { Point("inlet", _inlet, FoundationFacilityContactRole.WaterOutlet) })));

        [TestCase("unknown-group", "")]
        [TestCase("drink-and-deliver", "unknown-slot")]
        public void UnknownGroupOrSlot_IsRejectedAgainstRealDefinition(string group, string slot)
        {
            _rig.ConfigureBindings(Receive(group: group, slot: slot));
            var definition = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(
                "Assets/Game/NomadWorkshop/ArtFirstPass/Definitions/NW_Facility_DrinkingStation.asset");
            Assert.That(definition, Is.Not.Null);
            Assert.Throws<InvalidOperationException>(() => _rig.ValidateAgainst(definition));
        }

        [Test]
        public void ForeignModelPoint_IsRejected()
        {
            var foreign = new GameObject("other facility");
            try { Assert.Throws<InvalidOperationException>(() => _rig.ConfigureBindings(Receive(points: new[] { Point("inlet", foreign.transform) }))); }
            finally { UnityEngine.Object.DestroyImmediate(foreign); }
        }

        [Test]
        public void WildcardAndSpecificSlotOverlap_IsRejected() => Assert.Throws<InvalidOperationException>(() =>
            _rig.ConfigureBindings(Receive(), Receive("specific", slot: "left")));

        [Test]
        public void InvalidAxis_AndConcurrentPartOwners_AreRejected()
        {
            FoundationFacilityWorkBinding Action(string id, string group, Vector3 axis) => new(id, group, "",
                FoundationResidentPhase.RepairingFacility, Array.Empty<FoundationFacilityContactBinding>(),
                new[] { new FoundationFacilityMotionBinding("door", _lid, FoundationFacilityMotionKind.Rotate, axis, 70f) });
            Assert.Throws<InvalidOperationException>(() => _rig.ConfigureBindings(Action("invalid", "service", Vector3.zero)));
            Assert.Throws<InvalidOperationException>(() => _rig.ConfigureBindings(Action("a", "service", Vector3.right), Action("b", "other", Vector3.right)));
        }

        [Test]
        public void ConfiguredMotion_UsesParentCoordinates_AndAbsoluteProgressWithoutDrift()
        {
            Transform parent = Child("rotated scaled imported parent");
            parent.localRotation = Quaternion.Euler(12f, 37f, 21f); parent.localScale = new Vector3(1.2f, .8f, 2f);
            _lid.SetParent(parent, false); _lid.localPosition = new Vector3(.1f, .2f, .3f);
            _valve.localRotation = Quaternion.Euler(10f, 20f, 30f);
            Vector3 closedPosition = _lid.localPosition;
            Quaternion closedRotation = _valve.localRotation;
            _rig.ConfigureBindings(new FoundationFacilityWorkBinding("service", "service", "", FoundationResidentPhase.RepairingFacility,
                Array.Empty<FoundationFacilityContactBinding>(), new[]
                {
                    new FoundationFacilityMotionBinding("drawer", _lid, FoundationFacilityMotionKind.Translate, Vector3.left, .63f),
                    new FoundationFacilityMotionBinding("valve", _valve, FoundationFacilityMotionKind.Rotate, Vector3.up, -80f)
                }));
            var work = new FoundationFacilityWorkState("fixture", "service", "one", FoundationResidentPhase.RepairingFacility, .5f);
            for (int i = 0; i < 100; i++) { _rig.ResetWorkPose(); _rig.Apply(work, null); }
            Assert.That(Vector3.Distance(_lid.position, parent.TransformPoint(closedPosition + Vector3.left * .63f)), Is.LessThan(.00001f));
            Assert.That(Quaternion.Angle(_valve.localRotation, closedRotation * Quaternion.AngleAxis(-80f, Vector3.up)), Is.LessThan(.01f));
            _rig.ResetWorkPose();
            Assert.That(_lid.localPosition, Is.EqualTo(closedPosition));
            Assert.That(Quaternion.Angle(_valve.localRotation, closedRotation), Is.LessThan(.05f),
                "Transform 对四元数归一化后允许浮点舍入；比较实际角差，不要求逐分量相同。");
            Assert.That(_rig.IsWorking, Is.False);
        }
    }
}
