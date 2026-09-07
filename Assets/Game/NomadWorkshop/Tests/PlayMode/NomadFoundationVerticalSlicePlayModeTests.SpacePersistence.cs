using System;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Simulation.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    public sealed partial class NomadFoundationVerticalSlicePlayModeTests
    {
        [Test]
        public void ForeignFacilitySpace_RejectsCheckpointBeforeChangingWorld()
        {
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            string before = JsonUtility.ToJson(_context.ExecuteCommand(new CaptureFoundationCheckpointCommand()));
            var invalid = JsonUtility.FromJson<NomadWorkshopSaveData>(before);
            invalid.Facilities[0].SpaceSignature = new string('a', 64);
            var resident = _model.PrimaryResident;
            Assert.Throws<NotSupportedException>(() =>
                _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(invalid)));
            Assert.That(_model.PrimaryResident, Is.SameAs(resident), "拒绝检查点不能重建居民或释放当前工作所有权。");
            Assert.That(JsonUtility.ToJson(_context.ExecuteCommand(new CaptureFoundationCheckpointCommand())), Is.EqualTo(before));
        }
    }
}
