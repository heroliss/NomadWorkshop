using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Simulation;
using NUnit.Framework;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class NomadResidentIdentityTests
    {
        [Test]
        public void PrimaryResident_UsesStableStarterIdentity_AndReadModelKeepsIt()
        {
            var model = new NomadFoundationModel();
            FoundationResidentReadModel readModel =
                new(model.PrimaryResident);

            Assert.That(readModel.DisplayName, Is.EqualTo("旅人"));
            Assert.That(readModel.Gender, Is.EqualTo(NomadCharacterGender.Unspecified));
            Assert.That(readModel.AppearanceSeed, Is.EqualTo(1));
            Assert.That(readModel.IsPlayerAvatar, Is.True);
        }

        [Test]
        public void ResidentModelState_RejectsMalformedIdentity()
        {
            Assert.Throws<System.ArgumentException>(
                () => new FoundationResidentModelState(
                    "player",
                    1UL,
                    new string('x', NomadResidentIdentity.MaximumDisplayNameLength + 1)));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new FoundationResidentModelState(
                    "player",
                    1UL,
                    "旅人",
                    (NomadCharacterGender)99));
        }
    }
}
