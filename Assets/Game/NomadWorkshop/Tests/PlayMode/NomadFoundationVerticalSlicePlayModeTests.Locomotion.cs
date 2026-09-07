using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    public sealed partial class NomadFoundationVerticalSlicePlayModeTests
    {
        private NavMeshAgent[] NativeMotors() => _root.GetComponentsInChildren<NavMeshAgent>()
            .Where(x => x.name.StartsWith("Resident Motor · ")).ToArray();

        private IEnumerator PrepareNativeCohort()
        {
            yield return PrepareThreeResidents(stockedStation: true);
            var saved = CaptureStopCheckpoint();
            foreach (var resident in saved.Residents) resident.ThirstPermille = 800;
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            _context.ExecuteCommand(new SetFoundationSpeedCommand(1f));
            _system.ConfigureNativeLocomotionForTests(true);
            yield return null;
            Assert.That(NativeMotors().Length, Is.EqualTo(3), "正式移动必须拥有三个实际 NavMeshAgent。");
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
        }

        [UnityTest]
        public IEnumerator NativeCohort_UsesRealAgentsForSharedDrinking_WithoutBodyOverlapOrWaterLoss()
        {
            yield return PrepareNativeCohort();
            var read = ReadCohort();
            float minimumSeparation = float.PositiveInfinity;
            float deadline = Time.realtimeSinceStartup + 30f;
            bool observedPhysicalMovement = false;
            bool observedYield = false;
            long maximumPersonalStall = 0L;
            while (Time.realtimeSinceStartup < deadline && read.Residents.Any(x => x.CompletedDrinkCount.CurrentValue == 0))
            {
                yield return null;
                var motors = NativeMotors();
                observedPhysicalMovement |= motors.Any(x => x.velocity.sqrMagnitude > 0.04f);
                observedYield |= read.Residents.Any(x => x.CurrentTask.CurrentValue.Contains("让出通路"));
                maximumPersonalStall = System.Math.Max(maximumPersonalStall,
                    read.Residents.Max(x => x.MovementStallMilliseconds.CurrentValue));
                for (var a = 0; a < motors.Length; a++)
                for (var b = a + 1; b < motors.Length; b++)
                {
                    Vector3 separation = motors[a].transform.position - motors[b].transform.position;
                    separation.y = 0f;
                    minimumSeparation = Mathf.Min(minimumSeparation, separation.magnitude);
                }
            }
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            Assert.That(observedPhysicalMovement, Is.True);
            Assert.That(read.Residents.All(x => x.CompletedDrinkCount.CurrentValue > 0), Is.True, CohortDiagnostic());
            Assert.That(minimumSeparation, Is.GreaterThanOrEqualTo(0.32f), "半径 0.2 m 的三人应互相避让，不能穿过身体。");
            TestContext.WriteLine($"[NomadWorkshop.NativeMovement] shared drink · minSeparation={minimumSeparation:F3}m · " +
                $"maxPersonalStall={maximumPersonalStall}ms · observedYield={observedYield}");
            Assert.That(CalculateProjectedWaterTotal() + _model.StopWaterMilliliters.Value + _model.StopWasteMilliliters.Value +
                read.Residents.Skip(1).Sum(x => x.BodyWaterMilliliters.CurrentValue + x.BladderWasteMilliliters.CurrentValue),
                Is.EqualTo(80000));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator NativeCohort_PauseFreezesPhysicalMovement_AndRestoreReplacesOldMotors()
        {
            yield return PrepareNativeCohort();
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline && !NativeMotors().Any(x => x.velocity.sqrMagnitude > 0.04f))
                yield return null;
            Assert.That(NativeMotors().Any(x => x.velocity.sqrMagnitude > 0.04f), Is.True, CohortDiagnostic());
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            long pausedTick = _model.SimulationTick.Value;
            var oldMotors = NativeMotors();
            var positions = oldMotors.Select(x => x.transform.position).ToArray();
            var rotations = oldMotors.Select(x => x.transform.rotation).ToArray();
            for (var frame = 0; frame < 12; frame++) yield return null;
            Assert.That(_model.SimulationTick.Value, Is.EqualTo(pausedTick));
            for (var i = 0; i < oldMotors.Length; i++)
            {
                Assert.That(Vector3.Distance(oldMotors[i].transform.position, positions[i]), Is.LessThan(0.003f));
                Assert.That(Quaternion.Angle(oldMotors[i].transform.rotation, rotations[i]), Is.LessThan(0.1f));
                Assert.That(oldMotors[i].isStopped, Is.True);
            }

            var checkpoint = CaptureStopCheckpoint();
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            yield return null;
            Assert.That(oldMotors.All(x => x == null), Is.True, "旧居民执行者销毁后，原生移动实例也必须真正消失。");
            Assert.That(NativeMotors().Length, Is.EqualTo(3));
            Assert.That(NativeMotors().All(x => x.isStopped), Is.True);

            var fastForward = _context.ExecuteCommand(new RunFoundationSoakHarnessCommand(1000, 100));
            Assert.That(fastForward.MovementMode, Is.EqualTo("deterministic-corners"));
            Assert.That(fastForward.MaximumAbsoluteWaterDeviationMilliliters, Is.Zero);
            var read = ReadCohort();
            foreach (var resident in read.Residents)
            {
                var motor = NativeMotors().Single(x => x.name.EndsWith(resident.StableId));
                Vector3 difference = motor.transform.localPosition - resident.ResidentLocalPosition.CurrentValue;
                difference.y = 0f;
                Assert.That(difference.magnitude, Is.LessThan(0.02f), "显式快进结束后不能残留旧的物理位置。");
            }
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline && read.Residents.Any(x => x.CompletedDrinkCount.CurrentValue == 0))
                yield return null;
            Assert.That(read.Residents.All(x => x.CompletedDrinkCount.CurrentValue > 0), Is.True, CohortDiagnostic());
        }

        [UnityTest]
        public IEnumerator NativeCohort_BuildNavigationRebuild_PreservesPhysicalOwnersAndResumesNeeds()
        {
            yield return PrepareNativeCohort();
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline && !NativeMotors().Any(x => x.velocity.sqrMagnitude > 0.04f))
                yield return null;
            Assert.That(NativeMotors().Any(x => x.velocity.sqrMagnitude > 0.04f), Is.True);
            var motors = NativeMotors();
            yield return BuildFacility("field-kitchen", -2000, -1500);
            Assert.That(_model.BuildTransactionPhase.Value, Is.EqualTo(FoundationBuildTransactionPhase.Idle));
            Assert.That(NativeMotors().Select(x => x.GetInstanceID()), Is.EquivalentTo(motors.Select(x => x.GetInstanceID())),
                "普通建造只重建路径，不能用销毁/重新生成居民掩盖物理恢复问题。");
            deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline && ReadCohort().Residents.Any(x => x.CompletedDrinkCount.CurrentValue == 0))
                yield return null;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            Assert.That(ReadCohort().Residents.All(x => x.CompletedDrinkCount.CurrentValue > 0), Is.True, CohortDiagnostic());
            Assert.That(motors.All(x => x.isOnNavMesh && x.isStopped), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator NativeCohort_SoftOccupantLeavesClaimedSlot_BeforeWaitingResidentMovesCup()
        {
            _system.ConfigureResidentCountForTests(3);
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            Assert.That(_context.ExecuteCommand(new ResetFoundationForSoakHarnessCommand(1729)), Is.True);
            yield return BuildFacility("field-kitchen", 0, 0);
            yield return BuildFacility("field-kitchen", 3000, 0);
            var saved = CaptureStopCheckpoint();
            foreach (var resident in saved.Residents) resident.ThirstPermille = 200;
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            _system.ConfigureTimingsForTests(1f, 0.6f);
            // ConfigureTimingsForTests 同时缩短休整；这里需要明确保持闲人不自行结束休整的前提。
            _system.ConfigureLeisureTimingsForTests(1000f, 20f, 8f);
            _system.ConfigureNativeLocomotionForTests(true);
            yield return null;
            Assert.That(_context.ExecuteCommand(new TryStartFoundationCupMoveCommand("resident-01")), Is.True);
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            string moverId = "resident-01", occupantId = null;
            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline && occupantId == null)
            {
                yield return null;
                var read = ReadCohort();
                foreach (var candidate in read.Residents)
                {
                    if (candidate.StableId != moverId || candidate.ResidentPhase.CurrentValue is not
                        (FoundationResidentPhase.MovingToWorldItemSource or FoundationResidentPhase.MovingToWorldItemDestination)) continue;
                    var motor = NativeMotors().Single(x => x.name.EndsWith(candidate.StableId));
                    if (motor.pathPending || Vector3.Distance(motor.transform.position, motor.destination) < 0.7f) continue;
                    var soft = read.Residents.FirstOrDefault(x => x.StableId != candidate.StableId &&
                        x.ResidentPhase.CurrentValue == FoundationResidentPhase.Relaxing);
                    if (string.IsNullOrEmpty(soft.StableId)) continue;
                    occupantId = soft.StableId;
                    break;
                }
            }
            Assert.That(occupantId, Is.Not.Null, CohortDiagnostic());
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            var moverMotor = NativeMotors().Single(x => x.name.EndsWith(moverId));
            var occupantMotor = NativeMotors().Single(x => x.name.EndsWith(occupantId));
            // 明确构造物理阻挡：保留闲人的真实休整阶段，只把其脚底放到已领取的工作位。
            Vector3 occupiedPoint = moverMotor.destination;
            Assert.That(occupantMotor.Warp(occupiedPoint), Is.True);
            yield return null;
            Assert.That(Vector3.Distance(occupantMotor.transform.position, occupiedPoint), Is.LessThan(0.03f),
                "阻挡前提必须在实际 Agent 上成立。");
            var moving = ReadCohort().Residents.Single(x => x.StableId == moverId);
            int moves = moving.CompletedWorldItemMoveCount.CurrentValue;
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            bool observedYield = false;
            bool vacatedWhileWaiting = false;
            Vector3 previousOccupantPosition = occupantMotor.transform.position;
            float previousWallTime = Time.realtimeSinceStartup;
            // 此处包括等待让位、走到来源与带杯走到第二座厨房；0.6 m/s 的两段行程留出完整预算。
            deadline = Time.realtimeSinceStartup + 35f;
            while (Time.realtimeSinceStartup < deadline && moving.CompletedWorldItemMoveCount.CurrentValue == moves)
            {
                yield return null;
                float now = Time.realtimeSinceStartup;
                Vector3 currentOccupantPosition = occupantMotor.transform.position;
                var occupant = ReadCohort().Residents.Single(x => x.StableId == occupantId);
                if (!observedYield && occupant.ResidentPhase.CurrentValue == FoundationResidentPhase.Relaxing)
                    Assert.That(Vector3.Distance(currentOccupantPosition, occupiedPoint), Is.LessThan(0.003f),
                        "休整者仍在原阶段时必须固定脚底；引擎被动推开不算主动让路。");
                Assert.That(Vector3.Distance(currentOccupantPosition, previousOccupantPosition),
                    Is.LessThanOrEqualTo((now - previousWallTime) * 0.6f + 0.08f), "闲人必须实际走开，不能瞬移腾空工作位。");
                previousWallTime = now;
                previousOccupantPosition = currentOccupantPosition;
                vacatedWhileWaiting |= moving.CompletedWorldItemMoveCount.CurrentValue == moves &&
                    Vector3.Distance(currentOccupantPosition, occupiedPoint) > 0.4f;
                observedYield |= occupant.CurrentTask.CurrentValue.Contains("让出通路");
            }
            TestContext.WriteLine($"[NomadWorkshop.NativeMovement] soft occupant · id={occupantId} · " +
                $"textObserved={observedYield} · displacement={Vector3.Distance(occupantMotor.transform.position, occupiedPoint):F3}m");
            Assert.That(vacatedWhileWaiting, Is.True, CohortDiagnostic());
            Assert.That(observedYield, Is.True, "必须观察到正式让路意图，不能把碰撞挤走当作行走让位。");
            Assert.That(moving.CompletedWorldItemMoveCount.CurrentValue, Is.EqualTo(moves + 1), CohortDiagnostic());
            Assert.That(Vector3.Distance(occupantMotor.transform.position, occupiedPoint), Is.GreaterThan(0.4f));
        }

        [UnityTest]
        public IEnumerator NativeCohort_FiniteRoundtripAndRecall_Seed1729() => RunNativeRoundtrip(1729);

        [UnityTest]
        public IEnumerator NativeCohort_FiniteRoundtripAndRecall_Seed17311() => RunNativeRoundtrip(17311);

        private IEnumerator RunNativeRoundtrip(int seed)
        {
            _system.ConfigureResidentCountForTests(3);
            yield return ResetAndBuildSoakScenario(seed);
            yield return BuildFacility("driver-station", 3800, 3400);
            var saved = CaptureStopCheckpoint();
            foreach (var resident in saved.Residents) resident.ThirstPermille = 200;
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            _context.ExecuteCommand(new SetFoundationSpeedCommand(4f));
            _system.ConfigureNativeLocomotionForTests(true);
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Destination));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            yield return WaitNativeCondition(() => _model.JourneyStatus.Value == NomadJourneyStatus.Arrived,
                110f, "原生去程");
            Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(15_000_000_000_000L));
            Assert.That(_context.ExecuteCommand(new RequestFoundationStopWaterCommand(true)), Is.True);
            yield return WaitNativeCondition(() => !_model.StopWaterRequested.Value && !_model.StopWaterActive.Value,
                30f, "有限取水");
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(18000),
                _model.StopWorkFeedback.Value + " | " + CohortDiagnostic() + " | " + NativeBodyDiagnostic(NativeMotors()));
            if (seed == 1729)
            {
                // 同一真实旅程在有限补水后经过玩家存读档入口，再继续维修、清运、召回和返程。
                yield return UniTask.ToCoroutine(async () =>
                {
                    _context.ExecuteCommand(new SetFoundationPausedCommand(true));
                    long savedTick = _model.SimulationTick.Value;
                    var previousMotors = NativeMotors();
                    await _context.ExecuteCommandAsync(new SaveFoundationCheckpointCommand(PlayerTestSlot));
                    Assert.That(await _context.ExecuteCommandAsync(new LoadFoundationCheckpointCommand(PlayerTestSlot)), Is.True);
                    Assert.That(_model.SimulationTick.Value, Is.EqualTo(savedTick));
                    Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(18000));
                    await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                    Assert.That(previousMotors.All(x => x == null), Is.True);
                    _context.ExecuteCommand(new SetFoundationPausedCommand(false));
                });
            }
            int repairs = ReadCohort().Residents.Sum(x => x.CompletedWaterTankRepairCount.CurrentValue);
            Assert.That(_context.ExecuteCommand(new ForcePrimaryWaterTankFaultCommand()), Is.True);
            yield return WaitNativeCondition(() => ReadCohort().Residents.Sum(x => x.CompletedWaterTankRepairCount.CurrentValue) > repairs,
                40f, "实际消耗初始备件维修，腾出托盘");
            Assert.That(_context.ExecuteCommand(new RequestFoundationStopSpareCommand(true)), Is.True);
            yield return WaitNativeCondition(() => !_model.StopSpareRequested.Value && !_model.StopSpareActive.Value,
                30f, "实体备件补给");
            Assert.That(_model.StopSpareCount.Value, Is.EqualTo(1), "仅取回一只有限地点备件，且完整放到车辆托盘。" + _model.StopWorkFeedback.Value);
            yield return WaitNativeCondition(() => _model.ToiletHoldingWasteMilliliters.Value > 0, 30f, "产生真实污物");
            Assert.That(_context.ExecuteCommand(new RequestFoundationStopWasteCommand(true)), Is.True);
            yield return WaitNativeCondition(() => _model.StopWasteMilliliters.Value > 0 && _model.StopWasteActive.Value,
                30f, "带空桶返回前请求召回");
            long position = _model.JourneyPositionMicrometers.Value;
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Origin));
            yield return WaitNativeCondition(() => !_model.StopWasteActive.Value, 20f, "全员和空桶归车",
                () => Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(position),
                    "归车完成前，其他居民即使可驾驶也不能先开车。"));
            yield return WaitNativeCondition(() => _model.JourneyStatus.Value == NomadJourneyStatus.Arrived &&
                _model.JourneyPositionMicrometers.Value == 0L, 110f, "原生返程");
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(10_000_000_000_000L));
            Assert.That(ReadCohort().Residents.All(x => x.CompletedDrinkCount.CurrentValue > 0 &&
                x.CompletedToiletUseCount.CurrentValue > 0), Is.True, CohortDiagnostic());
            TestContext.WriteLine($"[NomadWorkshop.NativeMovement] roundtrip · seed={seed} · speed=4 · " +
                $"tick={_model.SimulationTick.Value} · water=80000mL · fuel=10L · siteWaste={_model.StopWasteMilliliters.Value}mL");
        }

        private IEnumerator WaitNativeCondition(System.Func<bool> condition, float wallSeconds, string stage,
            System.Action observation = null)
        {
            float deadline = Time.realtimeSinceStartup + wallSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                var read = ReadCohort();
                if (!read.Residents.All(x => x.ResidentHealth.CurrentValue > 0f))
                    Assert.Fail(stage + ": " + CohortDiagnostic());
                var motors = NativeMotors();
                var positions = motors.Select(x => x.transform.position).ToArray();
                for (var a = 0; a < positions.Length; a++)
                for (var b = a + 1; b < positions.Length; b++)
                {
                    Vector3 separation = positions[a] - positions[b];
                    separation.y = 0f;
                    if (separation.magnitude >= 0.32f) continue;
                    Assert.Fail($"{stage}：原生身体穿插，frame={Time.frameCount} tick={_model.SimulationTick.Value} " +
                        $"distance={separation.magnitude:F3}m；" + CohortDiagnostic() + " | " +
                        NativeBodyDiagnostic(motors));
                }
                if (read.Residents.Max(x => x.MovementStallMilliseconds.CurrentValue) >= 15000L)
                    Assert.Fail(stage + "：逐人移动不能被其他人的进展掩盖。" + CohortDiagnostic() + " | " + NativeBodyDiagnostic(motors));
                int total = CalculateProjectedWaterTotal() + _model.StopWaterMilliliters.Value + _model.StopWasteMilliliters.Value +
                    read.Residents.Skip(1).Sum(x => x.BodyWaterMilliliters.CurrentValue + x.BladderWasteMilliliters.CurrentValue);
                Assert.That(total, Is.EqualTo(80000), stage + "：每帧审计世界水量。");
                if (!condition()) observation?.Invoke();
            }
            Assert.That(condition(), Is.True, stage + "：实际 PlayerLoop 超时；" + CohortDiagnostic() +
                $" | water={_model.StopWaterMilliliters.Value} requested={_model.StopWaterRequested.Value} active={_model.StopWaterActive.Value}" +
                $" | waste={_model.StopWasteMilliliters.Value} requested={_model.StopWasteRequested.Value} active={_model.StopWasteActive.Value}" +
                $" | bucket={_model.WasteBucketCarrierId.Value} toilet={_model.ToiletHoldingWasteMilliliters.Value}" +
                $" | spare={_model.StopSpareCount.Value} requested={_model.StopSpareRequested.Value} active={_model.StopSpareActive.Value}" +
                $" | feedback={_model.StopWorkFeedback.Value}");
        }

        private static string NativeBodyDiagnostic(NavMeshAgent[] motors) =>
            string.Join(" | ", motors.Select(x => $"{x.name} pos={x.transform.position:F3} " +
                $"next={x.nextPosition:F3} goal={x.destination:F3} velocity={x.velocity:F3} stopped={x.isStopped} " +
                $"coupled={x.updatePosition} priority={x.avoidancePriority} pending={x.pathPending} " +
                $"hasPath={x.hasPath} status={x.pathStatus} remaining={x.remainingDistance:F3}"));
    }
}
