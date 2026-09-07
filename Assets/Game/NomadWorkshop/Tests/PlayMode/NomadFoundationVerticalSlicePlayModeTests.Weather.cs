using System.Collections;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    public sealed partial class NomadFoundationVerticalSlicePlayModeTests
    {
        [UnityTest]
        public IEnumerator SandstormVisual_RealParticleSimulationEmitsMovingDustWithoutEngineErrors()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            _system.ConfigurePhysiologyForTests(600f, 0f, 0f);
            const int seed = 24_061;
            _context.ExecuteCommand(new ResetFoundationForSoakHarnessCommand(seed));
            yield return null;
            long stormTick = (NomadEnvironmentSchedule.Default.Project(seed, 0L).SandstormStartSimulationTick + 9L) / 10L * 10L;
            var result = _context.ExecuteCommand(new RunFoundationSoakHarnessCommand(stormTick, 1000, 0));
            Assert.That(result.CompletedRequestedDuration, Is.True);
            Assert.That(_model.CurrentWeather.Value, Is.EqualTo(NomadWeatherKind.Sandstorm));
            var particles = _worldView.GetComponentInChildren<ParticleSystem>();
            Assert.That(particles, Is.Not.Null, "天气投影必须激活真实粒子对象。");
            yield return null;
            // 模拟账本推进不能替代引擎帧；实际发射与速度计算才能暴露粒子模块的配置错误。
            particles.Simulate(0.4f, true, false, true);
            var emitted = new ParticleSystem.Particle[256];
            int count = particles.GetParticles(emitted);
            Assert.That(count, Is.GreaterThan(0));
            Vector3 before = emitted[0].position;
            uint identity = emitted[0].randomSeed;
            particles.Simulate(0.1f, true, false, true);
            count = particles.GetParticles(emitted);
            int tracked = System.Array.FindIndex(emitted, 0, count, p => p.randomSeed == identity);
            Assert.That(tracked, Is.GreaterThanOrEqualTo(0), "该粒子的最短寿命尚未结束。");
            Assert.That(emitted[tracked].position.x, Is.LessThan(before.x));
            Assert.That(emitted[tracked].position.z, Is.LessThan(before.z));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
