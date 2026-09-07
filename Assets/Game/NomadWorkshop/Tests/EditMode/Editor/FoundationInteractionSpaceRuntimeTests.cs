using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class FoundationInteractionSpaceRuntimeTests
    {
        private NomadFacilityDefinition _definition;
        private Dictionary<string, NomadFacilityDefinition> _definitions;
        private FoundationInteractionSpaceRuntime _runtime;

        [SetUp]
        public void SetUp()
        {
            _definition = ScriptableObject.CreateInstance<NomadFacilityDefinition>();
            _definition.ConfigureForTests(
                "workbench", "工作台", NomadFacilityFunction.Storage, false,
                new[] { new NomadFacilityFootprintPartDefinition(Vector2.zero, Vector2.one) },
                new[] { new NomadFacilityInteractionGroupDefinition("work", true, new[]
                {
                    new NomadFacilityInteractionSlotDefinition("left", Vector2.zero),
                    new NomadFacilityInteractionSlotDefinition("right", new Vector2(0.1f, 0f)),
                }) },
                false, Vector2.zero, 0f, Vector3.one, Color.white);
            _definitions = new Dictionary<string, NomadFacilityDefinition>
                { { _definition.Id, _definition } };
            _runtime = new FoundationInteractionSpaceRuntime(350);
            Rebuild(Facility("a", 0), Facility("b", 2), Facility("c", 5));
        }

        [TearDown]
        public void TearDown()
        {
            _runtime.Dispose();
            UnityEngine.Object.DestroyImmediate(_definition);
        }

        [Test]
        public void IndependentResidents_OwnDifferentSpaces_ReleaseOnlyTheirOwnLease()
        {
            Assert.That(_runtime.TryAcquire(1, Slot("a"), out var first), Is.True);
            Assert.That(_runtime.TryAcquire(2, Slot("b"), out var second), Is.True);
            Assert.That(_runtime.ActiveLeaseCount, Is.EqualTo(2));
            Assert.That(_runtime.TryAcquire(3, Slot("a", "right"), out _), Is.False);
            Assert.That(_runtime.TryAcquire(1, Slot("c"), out _), Is.False,
                "同一居民不能同时占用两处工作位置。");
            first.Dispose();
            Assert.That(second.IsActive, Is.True);
            Assert.That(_runtime.IsAvailable(Slot("a").Address), Is.True);
            Assert.That(_runtime.TryAcquire(3, Slot("b"), out _), Is.False);
            Assert.That(_runtime.BuildCommittedOccupiedMask("a", _definition), Is.Zero);
            Assert.That(_runtime.BuildCommittedOccupiedMask("b", _definition), Is.EqualTo(3UL));
        }

        [Test]
        public void RebuildUnrelatedFacilities_PreservesLiveHandles()
        {
            _runtime.TryAcquire(1, Slot("a"), out var first);
            _runtime.TryAcquire(2, Slot("b"), out var second);
            Assert.That(Rebuild(Facility("a", 0), Facility("b", 2), Facility("c", 8)), Is.True);
            Assert.That(first.IsActive && second.IsActive, Is.True);
            Assert.That(_runtime.TryAcquire(3, Slot("b", "right"), out _), Is.False);
            first.Dispose();
            Assert.That(second.IsActive, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RebuildMergedSpaces_RevokesAllConflictingResidents_WithoutOrderWinner(bool reverse)
        {
            ulong firstOwner = reverse ? 2UL : 1UL;
            _runtime.TryAcquire(firstOwner, Slot("a"), out var first);
            _runtime.TryAcquire(3UL - firstOwner, Slot("b"), out var second);
            _runtime.TryAcquire(3, Slot("c"), out var unaffected);
            FoundationFacilityState[] facilities =
                { Facility("a", 0), Facility("b", 0.25f), Facility("c", 5) };
            if (reverse) Array.Reverse(facilities);
            Assert.That(Rebuild(facilities), Is.False);
            Assert.That(first.IsActive || second.IsActive, Is.False);
            Assert.That(unaffected.IsActive, Is.True);
            Assert.That(_runtime.ActiveLeaseCount, Is.EqualTo(1));
            Assert.That(_runtime.TryAcquire(firstOwner, Slot("a"), out var replacement), Is.True);
            first.Dispose();
            second.Dispose();
            Assert.That(replacement.IsActive, Is.True);
            Assert.That(_runtime.TryAcquire(4, Slot("b"), out _), Is.False);
        }

        [Test]
        public void RemovedSlot_RevokesOnlyItsOccupant_OldLeaseCannotReleaseReplacement()
        {
            _runtime.TryAcquire(1, Slot("a"), out var old);
            _runtime.TryAcquire(2, Slot("b"), out var retained);
            Assert.That(Rebuild(Facility("b", 2)), Is.False);
            Assert.That(old.IsActive, Is.False);
            Assert.That(retained.IsActive, Is.True);
            Rebuild(Facility("a", 0), Facility("b", 2));
            Assert.That(_runtime.TryAcquire(1, Slot("a"), out var replacement), Is.True);
            old.Dispose();
            Assert.That(replacement.IsActive, Is.True);
            Assert.That(_runtime.ActiveLeaseCount, Is.EqualTo(2));
        }

        [Test]
        public void CheckpointRebuild_RevokesRuntimeOwnership_EvenWithIdenticalTopology()
        {
            _runtime.TryAcquire(1, Slot("a"), out var old);
            _runtime.RebuildCommitted(
                new[] { Facility("a", 0) }, _definitions, reacquireActiveSpace: false);
            Assert.That(old.IsActive, Is.False);
            Assert.That(_runtime.TryAcquire(1, Slot("a"), out var replacement), Is.True);
            old.Dispose();
            Assert.That(replacement.IsActive, Is.True);
        }

        [Test]
        public void WorldDisposal_RevokesAllResidents_AndRejectsNewWork()
        {
            _runtime.TryAcquire(1, Slot("a"), out var first);
            _runtime.TryAcquire(2, Slot("b"), out var second);
            _runtime.Dispose();
            _runtime.Dispose();
            first.Dispose();
            Assert.That(first.IsActive || second.IsActive, Is.False);
            Assert.That(_runtime.ActiveLeaseCount, Is.Zero);
            Assert.That(_runtime.IsAvailable(Slot("a").Address), Is.False);
            Assert.Throws<ObjectDisposedException>(() => _runtime.TryAcquire(1, Slot("a"), out _));
            Assert.Throws<ObjectDisposedException>(() => Rebuild(Facility("a", 0)));
        }

        private bool Rebuild(params FoundationFacilityState[] facilities) =>
            _runtime.RebuildCommitted(facilities, _definitions, reacquireActiveSpace: true);

        private static FoundationFacilityState Facility(string id, float x) =>
            new(id, "workbench", DeckPose.FromMeters(x, 0, 0));

        private static InteractionSpaceSlot Slot(string id, string slot = "left") =>
            new(new InteractionSlotAddress(id, "work", slot), default);
    }
}
