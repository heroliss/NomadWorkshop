using Game.NomadWorkshop.Simulation.Persistence;
using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    public sealed class NomadStarterJourneyProfileTests
    {
        [Test]
        public void DefaultProfile_DeclaresPlayerIdentityAndSeatOnlyMicroCar()
        {
            NomadStarterJourneyProfile profile = NomadStarterJourneyProfile.Default;

            Assert.That(profile.ProfileId, Is.EqualTo(NomadStarterJourneyProfile.DefaultProfileId));
            Assert.That(profile.PlayerIdentity.IsPlayerAvatar, Is.True);
            Assert.That(profile.PlayerIdentity.DisplayName, Is.EqualTo("旅人"));
            Assert.That(profile.Vehicle.VehicleId, Is.EqualTo(NomadStarterVehicleProfile.MicroCarId));
            Assert.That(profile.Vehicle.SeatCount, Is.EqualTo(1));
            Assert.That(profile.Vehicle.HasSleepBerth, Is.False);
            Assert.That(profile.Vehicle.FacilitySlotCapacity, Is.EqualTo(0));
            Assert.That(profile.Vehicle.Capabilities,
                Is.EqualTo(NomadStarterVehicleCapability.DriverSeat));
        }

        [Test]
        public void SeatRest_IsSlowAndCostsHealthOverTime()
        {
            var wellbeing = new ResidentWellbeing(.7f, .7f, .8f, .2f, .9f);
            float initialFatigue = wellbeing.Fatigue;
            float initialHealth = wellbeing.Health;
            float initialMood = wellbeing.Mood;

            wellbeing.Advance(
                120f,
                ResidentWellbeingActivity.SeatRest,
                new ResidentWellbeingDrivers(.2f, .1f, false));

            Assert.That(wellbeing.Fatigue, Is.LessThan(initialFatigue));
            Assert.That(initialFatigue - wellbeing.Fatigue,
                Is.LessThan(NomadSeatRestRules.FatigueRecoveryPerSecond * 120f + .0001f));
            Assert.That(wellbeing.Health, Is.LessThan(initialHealth));
            Assert.That(wellbeing.Mood, Is.LessThan(initialMood));
        }

        [Test]
        public void SeatRest_RulesRejectInvalidStateAndClampToAvailableValues()
        {
            NomadSeatRestOutcome outcome = NomadSeatRestRules.Evaluate(600f, .1f, .02f);

            Assert.That(outcome.FatigueRecovered, Is.EqualTo(.1f).Within(.0001f));
            Assert.That(outcome.HealthLost, Is.EqualTo(.02f).Within(.0001f));
            Assert.That(outcome.MoodDelta, Is.LessThan(0f));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => NomadSeatRestRules.Evaluate(-1f, .1f, .5f));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => NomadSeatRestRules.Evaluate(1f, 1.1f, .5f));
        }

        [Test]
        public void SaveContract_PreservesIdentityAndNormalizesLegacyMissingName()
        {
            NomadWorkshopSaveData save = NomadWorkshopSaveContractTests.CreateValidSave();
            NomadResidentSaveData resident = save.Residents[0];
            resident.DisplayName = "阿澜";
            resident.Gender = NomadCharacterGender.Female;
            resident.AppearanceSeed = 9281;
            resident.IsPlayerAvatar = true;

            NomadWorkshopSaveContract.ValidateForSave(save);
            Assert.That(save.Residents[0].DisplayName, Is.EqualTo("阿澜"));
            Assert.That(save.Residents[0].Gender, Is.EqualTo(NomadCharacterGender.Female));
            Assert.That(save.Residents[0].AppearanceSeed, Is.EqualTo(9281));

            resident.DisplayName = null;
            NomadWorkshopSaveContract.PrepareAfterLoad(save);
            Assert.That(resident.DisplayName, Is.EqualTo(string.Empty));
        }

        [Test]
        public void SaveContract_RejectsMultiplePlayerAvatarsAndMalformedIdentity()
        {
            NomadWorkshopSaveData duplicate = NomadWorkshopSaveContractTests.CreateValidSave();
            duplicate.Residents[0].IsPlayerAvatar = true;
            duplicate.Residents.Add(new NomadResidentSaveData
            {
                ResidentId = "resident-c",
                PersonalInventoryId = "resident-c-hands",
                Pose = new QuantizedDeckPose(0, 0, 0),
                IsPlayerAvatar = true,
            });
            duplicate.Inventories.Add(new NomadInventorySaveData
            {
                InventoryId = "resident-c-hands",
                OwnerEntityId = "resident-c",
                Measure = ResourceMeasure.Item,
                CapacityBaseUnits = 1,
            });
            Assert.Throws<System.InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(duplicate));

            NomadWorkshopSaveData malformed = NomadWorkshopSaveContractTests.CreateValidSave();
            malformed.Residents[0].DisplayName = new string('x', NomadResidentIdentity.MaximumDisplayNameLength + 1);
            Assert.Throws<System.ArgumentException>(
                () => NomadWorkshopSaveContract.ValidateForSave(malformed));
        }
    }
}
