using System.Collections;
using Game.Framework.Storage;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using Game.NomadWorkshop.Simulation;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>
    /// 证明连续建造、NavMesh 可回滚确认、居民实体搬水与表现均经过同一框架组合根。
    /// </summary>
    public sealed class NomadFoundationVerticalSlicePlayModeTests
    {
        private Scene _previousScene;
        private Scene _sliceScene;
        private GameObject _root;
        private DeckLayoutDefinition _layout;
        private NomadFacilityDefinition[] _definitions;
        private NomadFoundationContext _context;
        private NomadFoundationModel _model;
        private NomadFoundationSystem _system;
        private NomadFoundationWorldView _worldView;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousScene = SceneManager.GetActiveScene();
            _sliceScene = SceneManager.CreateScene("NomadFoundationVerticalSlicePlayModeTest");
            Assert.That(SceneManager.SetActiveScene(_sliceScene), Is.True);

            _layout = ScriptableObject.CreateInstance<DeckLayoutDefinition>();
            _layout.ConfigureForTests(-4, -3, 9, 7, 1.2f);
            _definitions = CreateDefinitions();

            _root = new GameObject("Foundation Test Root");
            _root.SetActive(false);
            _context = _root.AddComponent<NomadFoundationContext>();

            GameObject data = CreateChild(_root.transform, "Data");
            _model = data.AddComponent<NomadFoundationModel>();
            CreateNavigationInfrastructure(_root.transform, _layout);

            GameObject logic = CreateChild(_root.transform, "Logic");
            _system = logic.AddComponent<NomadFoundationSystem>();
            _system.ConfigureDefinitionsForTests(_layout, _definitions);

            GameObject presentation = CreateChild(_root.transform, "Presentation");
            GameObject deck = CreateChild(presentation.transform, "Vehicle Deck Root");
            Camera camera = CreateChild(presentation.transform, "Camera").AddComponent<Camera>();
            Light light = CreateChild(presentation.transform, "Light").AddComponent<Light>();
            _worldView = presentation.AddComponent<NomadFoundationWorldView>();
            _worldView.ConfigureForTests(_layout, _definitions, deck.transform, camera, light);

            _root.SetActive(true);
            _system.ConfigureTimingsForTests(16f, 100f);
            yield return null;

            Assert.That(_model.IsReady.Value, Is.True);
            Assert.That(
                _context.GetUtility<IStorageUtility>(),
                Is.Not.Null,
                "Foundation Context 应提供同一框架存储能力；未来迁到全局根时业务 Command 不变。");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_layout != null) Object.Destroy(_layout);
            if (_definitions != null)
            {
                for (var i = 0; i < _definitions.Length; i++)
                {
                    if (_definitions[i] != null) Object.Destroy(_definitions[i]);
                }
            }
            yield return null;

            if (_previousScene.IsValid() && _previousScene.isLoaded)
                SceneManager.SetActiveScene(_previousScene);
            if (_sliceScene.IsValid() && _sliceScene.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(_sliceScene);
                while (unload != null && !unload.isDone) yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ResidentGraybox_RootRepresentsFeetAndTouchesDeckSurface()
        {
            yield return null;

            Transform resident = _worldView.transform.Find("Vehicle Deck Root/Resident 01");
            Assert.That(resident, Is.Not.Null);
            Assert.That(
                _model.ResidentLocalPosition.Value.y,
                Is.EqualTo(0f).Within(0.0001f),
                "模拟位置应代表居民脚底，而不是胶囊中心。");
            Assert.That(
                resident.localPosition.y,
                Is.EqualTo(0f).Within(0.0001f),
                "运行时生成的 Resident 根节点应落在甲板表面。");

            Transform body = resident.Find("Body");
            Assert.That(body, Is.Not.Null);
            Renderer renderer = body.GetComponent<Renderer>();
            Assert.That(renderer, Is.Not.Null);
            float deckSurfaceWorldY = resident.parent.TransformPoint(Vector3.zero).y;
            Assert.That(
                renderer.bounds.min.y,
                Is.EqualTo(deckSurfaceWorldY).Within(0.01f),
                "胶囊自身可以用半高偏移中心，但底部必须接触甲板，不能把半高再加到根节点。");
        }

        [UnityTest]
        public IEnumerator UnifiedSimulationClock_PausesAndProjectsOneCalendarSnapshot()
        {
            yield return null;
            long runningTick = _model.SimulationTick.Value;
            Assert.That(runningTick, Is.GreaterThan(0L));

            NomadCalendarSnapshot projected = NomadCalendarPolicy.Default.Project(runningTick);
            Assert.That(_model.LifeDay.Value, Is.EqualTo(projected.LifeDay));
            Assert.That(_model.LifeMinuteOfDay.Value, Is.EqualTo(projected.LifeMinuteOfDay));
            Assert.That(
                _model.LifeDayProgressPermille.Value,
                Is.EqualTo(projected.LifeDayProgressPermille));
            Assert.That(_model.ClimateYear.Value, Is.EqualTo(projected.ClimateYear));
            Assert.That(_model.SeasonIndex.Value, Is.EqualTo(projected.SeasonIndex));
            Assert.That(
                _model.ClimateWeekInSeason.Value,
                Is.EqualTo(projected.ClimateWeekInSeason));
            Assert.That(
                _model.SeasonProgressPermille.Value,
                Is.EqualTo(projected.SeasonProgressPermille));

            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            long pausedTick = _model.SimulationTick.Value;
            yield return null;
            yield return null;
            Assert.That(
                _model.SimulationTick.Value,
                Is.EqualTo(pausedTick),
                "暂停必须冻结唯一模拟 Tick，而不仅是隐藏日历变化。");

            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            yield return null;
            Assert.That(_model.SimulationTick.Value, Is.GreaterThan(pausedTick));
        }

        [UnityTest]
        public IEnumerator BuildMode_OwnsPersistentDiagnosticsAndSynchronizedSnapGrid()
        {
            Transform sourceVisual = FindFacilityVisual("initial-vehicle-water-tank");
            Transform sourceSlots = sourceVisual.Find("Interaction Slots (build mode)");
            Transform grid = _worldView.transform.Find(
                "Vehicle Deck Root/Optional Placement Grid");
            Assert.That(_model.InteractionMode.Value, Is.EqualTo(FoundationInteractionMode.Observe));
            Assert.That(sourceSlots.gameObject.activeSelf, Is.False);
            Assert.That(grid.gameObject.activeSelf, Is.False);
            Assert.That(_model.PositionSnapMillimeters.Value, Is.EqualTo(200));
            Assert.That(_model.RotationSnapDeciDegrees.Value, Is.EqualTo(450));

            _context.ExecuteCommand(new EnterFoundationBuildModeCommand());
            yield return null;

            Assert.That(_model.InteractionMode.Value, Is.EqualTo(FoundationInteractionMode.Build));
            Assert.That(sourceSlots.gameObject.activeSelf, Is.True);
            Assert.That(grid.gameObject.activeSelf, Is.True);
            Mesh gridMesh = grid.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.That(
                gridMesh.vertexCount,
                Is.EqualTo(392),
                "10.8m × 8.4m 甲板应由 0.2m 真实吸附线生成 55+43 个四边形。 ");

            _context.ExecuteCommand(new SetFoundationPositionSnapCommand(600));
            Assert.That(
                gridMesh.vertexCount,
                Is.EqualTo(136),
                "10.8m × 8.4m 甲板的 0.6m 网格应有 19+15 条线。 ");
            _context.ExecuteCommand(new ExitFoundationBuildModeCommand());
            Assert.That(sourceSlots.gameObject.activeSelf, Is.False);
            Assert.That(grid.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator CounterClockwiseRotation_UsesDefaultFortyFiveDegreeStep()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new EnterFoundationBuildModeCommand());
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new RotateFacilityPreviewCommand(-1));
            yield return null;

            Assert.That(
                _model.PlacementPreview.Value.Pose.YawDeciDegrees,
                Is.EqualTo(3150),
                "右键发送的 -1 方向应按默认 45° 逆时针旋转并规范化为 315°。 ");
        }

        [UnityTest]
        public IEnumerator EdgeDockingSlots_PreviewAndCommittedNavMeshAgreeTheyAreUnreachable()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(0, 3800));
            for (var i = 0; i < 4; i++)
                _context.ExecuteCommand(new RotateFacilityPreviewCommand(1));
            yield return null;

            FoundationPlacementPreviewState preview = _model.PlacementPreview.Value;
            Assert.That(preview.Pose, Is.EqualTo(new DeckPose(0, 3800, 1800)));
            Assert.That(preview.RealtimeReachabilityEvaluated, Is.True);
            Assert.That(preview.ReachableInteractionSlotCount, Is.Zero);
            Assert.That(
                preview.Failure,
                Is.EqualTo(FoundationPlacementFailure.RequiredInteractionUnreachable),
                "甲板边缘应按与 NavMesh Agent 相同的净空在预览期判红。 ");
            Assert.That(preview.CanConfirm, Is.True, "不可达仍是允许落地的软警告。 ");

            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            yield return WaitForBuildTransaction();

            FoundationFacilityAccessState committed = FindAccess(
                _context.ExecuteCommand(new GetFoundationFacilityAccessCommand()),
                "facility-0001");
            Assert.That(committed.CommittedAccess, Is.EqualTo(FoundationFacilityAccess.Unreachable));
            Assert.That(committed.DisplayReachableSlotCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FortyFiveDegreeAdjacentFacilities_PreviewAndCommittedSlotMasksAgree()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return BuildFacility("drinking-station", 0, 0);

            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(800, 800));
            _context.ExecuteCommand(new RotateFacilityPreviewCommand(1));
            yield return null;

            FoundationPlacementPreviewState preview = _model.PlacementPreview.Value;
            Assert.That(preview.Pose, Is.EqualTo(new DeckPose(800, 800, 450)));
            Assert.That(preview.CanConfirm, Is.True, "停靠位冲突是软警告，不应禁止玩家确认。 ");
            Assert.That(
                preview.ReachableInteractionSlotCount,
                Is.LessThan(preview.InteractionSlotCount),
                "45° 邻接时，落入既有实体净空的幽灵停靠位必须在确认前判红。 ");

            var previewMask = new bool[preview.InteractionSlotCount];
            for (var i = 0; i < previewMask.Length; i++)
                previewMask[i] = preview.IsInteractionSlotReachable(i);

            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            yield return WaitForBuildTransaction();

            FoundationFacilityAccessState committed = FindAccess(
                _context.ExecuteCommand(new GetFoundationFacilityAccessCommand()),
                "facility-0002");
            Assert.That(committed.InteractionSlotCount, Is.EqualTo(previewMask.Length));
            for (var i = 0; i < previewMask.Length; i++)
                Assert.That(
                    committed.IsDisplaySlotReachable(i),
                    Is.EqualTo(previewMask[i]),
                    $"第 {i} 个停靠位的幽灵与落地结果必须一致。 ");
        }

        [UnityTest]
        public IEnumerator NearbyCrossFacilityDockingSlots_AreProjectedAsSharedSpaceConflict()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(-1800, 1000));
            _context.ExecuteCommand(new RotateFacilityPreviewCommand(1));
            _context.ExecuteCommand(new RotateFacilityPreviewCommand(1));
            yield return null;

            FoundationPlacementPreviewState preview = _model.PlacementPreview.Value;
            bool candidateConflict = false;
            for (var i = 0; i < preview.InteractionSlotCount; i++)
                candidateConflict |= preview.IsInteractionSlotSharingSpace(i);
            Assert.That(candidateConflict, Is.True, "幽灵停靠位靠近既有设施时应显示共享空间冲突。 ");

            FoundationFacilityAccessState source = FindAccess(
                _context.ExecuteCommand(new GetFoundationFacilityAccessCommand()),
                "initial-vehicle-water-tank");
            bool existingConflict = false;
            for (var i = 0; i < source.InteractionSlotCount; i++)
                existingConflict |= source.IsDisplaySlotSharingSpace(i);
            Assert.That(existingConflict, Is.True, "冲突必须双向投影到既有设施，不只标记幽灵。 ");
        }

        [UnityTest]
        public IEnumerator BuildDrinkingStation_ResidentHaulsRealWaterAndDrinks()
        {
            // 屏蔽真实鼠标位置对候选姿态的扰动；响应式表现订阅仍保留。
            _worldView.enabled = false;
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(0, 0));
            yield return null; // 避开同帧 UI 点击穿透保护。
            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            yield return WaitForBuildTransaction();

            FoundationFacilityState[] facilities =
                _context.ExecuteCommand(new GetFoundationFacilitiesCommand());
            Assert.That(facilities.Length, Is.EqualTo(2), "初始水箱之外应新增一座饮水站。");

            bool observedCarriedWater = false;
            bool observedPhysicalCanInResidentHands = false;
            bool observedWaterInsideCan = false;
            bool observedStationWater = false;
            bool observedPlannedPath = false;
            bool observedOccupiedDockingSpace = false;
            const int frameLimit = 180;
            for (var i = 0; i < frameLimit && _model.CompletedDrinkCount.Value == 0; i++)
            {
                yield return null;
                observedCarriedWater |= _model.ResidentCarryingWater.Value;
                observedPhysicalCanInResidentHands |=
                    _model.WaterCanLocation.Value == FoundationWaterCanLocation.Resident;
                observedWaterInsideCan |=
                    _model.WaterCanWaterMilliliters.Value == 2_000;
                observedStationWater |=
                    _model.DrinkingStationWaterMilliliters.Value > 0;
                observedPlannedPath |= _model.RemainingPathMeters.Value > 0f &&
                                       _model.RemainingPathCorners.Value > 0 &&
                                       !string.IsNullOrEmpty(_model.ActivePathSummary.Value);
                FoundationFacilityAccessState[] liveAccess =
                    _context.ExecuteCommand(new GetFoundationFacilityAccessCommand());
                for (var accessIndex = 0;
                     accessIndex < liveAccess.Length && !observedOccupiedDockingSpace;
                     accessIndex++)
                {
                    for (var slotIndex = 0;
                         slotIndex < liveAccess[accessIndex].InteractionSlotCount;
                         slotIndex++)
                        observedOccupiedDockingSpace |=
                            liveAccess[accessIndex].IsDisplaySlotOccupied(slotIndex);
                }
            }

            FoundationReadModel readModel =
                _context.ExecuteCommand(new GetFoundationReadModelCommand());
            Assert.That(readModel.CompletedDrinkCount.CurrentValue, Is.EqualTo(1));
            Assert.That(observedCarriedWater, Is.True, "至少一帧应看见水真实位于居民携带库存。");
            Assert.That(
                observedPhysicalCanInResidentHands,
                Is.True,
                "居民必须先取得唯一实体水罐，不能把水抽象地挂在自身库存上。");
            Assert.That(observedWaterInsideCan, Is.True, "搬运途中 2 L 水必须真实位于水罐库存。");
            Assert.That(observedStationWater, Is.True, "水应先进入饮水站，再进入居民身体。");
            Assert.That(observedPlannedPath, Is.True, "居民移动前应先生成可观察的连续 NavMesh 路径。");
            Assert.That(
                observedOccupiedDockingSpace,
                Is.True,
                "居民前往和使用功能点期间，共享停靠空间应作为真实容量被占用并投影。 ");
            Assert.That(
                readModel.VehicleWaterMilliliters.CurrentValue,
                Is.EqualTo(58_000));
            Assert.That(
                readModel.BodyWaterMilliliters.CurrentValue +
                readModel.BladderWasteMilliliters.CurrentValue,
                Is.EqualTo(ResidentWaterCycle.DefaultDrinkServingMilliliters),
                "喝下的水随后只能位于体内水或代谢后的膀胱库存。");
            Assert.That(readModel.ResidentCarryingWater.CurrentValue, Is.False);
            Assert.That(readModel.WaterCanWaterMilliliters.CurrentValue, Is.Zero);
            Assert.That(
                readModel.WaterCanLocation.CurrentValue,
                Is.EqualTo(FoundationWaterCanLocation.DrinkingStation),
                "倒水后空水罐应留在饮水站旁，下一次补货必须先到这里取得它。");
            FoundationActionPlanProjection plan = readModel.LatestActionPlan.CurrentValue;
            Assert.That(plan.Evaluated, Is.True);
            Assert.That(plan.Feasible, Is.True);
            Assert.That(plan.Selected, Is.True);
            Assert.That(plan.SelectionProbability, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(plan.StepCount, Is.GreaterThanOrEqualTo(6));
            Assert.That(plan.TravelDistanceMeters, Is.GreaterThan(0f));
            Assert.That(plan.TotalDurationSeconds, Is.GreaterThan(plan.TravelSeconds));
            Assert.That(plan.PlanCost, Is.GreaterThan(0f));
            Assert.That(plan.TotalUtility, Is.GreaterThan(0f));
            Assert.That(readModel.RemainingPathMeters.CurrentValue, Is.Zero.Within(0.001f));
            Assert.That(readModel.RemainingPathCorners.CurrentValue, Is.Zero);
            Assert.That(readModel.ActivePathSummary.CurrentValue, Is.Empty);
            Assert.That(readModel.LastBlocker.CurrentValue, Is.Empty);
            AssertResidentDockedToAnySlot(facilities[1], _definitions[1], readModel);
            Assert.That(
                _worldView.transform.Find("Vehicle Deck Root/Facilities").childCount,
                Is.EqualTo(2),
                "View 应从设施记录派生两座 3D 设施，而非自己持有另一份摆放真值。");
            Transform stationVisual = FindFacilityVisual("facility-0001");
            Assert.That(stationVisual.Find("Clean Water Reservoir"), Is.Not.Null);
            Assert.That(stationVisual.Find("Tap Spout"), Is.Not.Null);
            Assert.That(stationVisual.Find("Drip Tray"), Is.Not.Null);
            Material tapMaterial = stationVisual.Find("Tap Spout")
                .GetComponent<Renderer>().sharedMaterial;
            Assert.That(tapMaterial.HasProperty("_Metallic"), Is.True);
            Assert.That(
                tapMaterial.GetFloat("_Metallic"),
                Is.GreaterThan(0.5f),
                "灰盒的金属、涂层和橡胶应使用可提前审查的 URP PBR 参数。 ");
        }

        [UnityTest]
        public IEnumerator BuildOnResidentPosition_EvacuatesResidentInsteadOfBlockingConstruction()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(0, 2400));
            yield return null;

            FoundationPlacementPreviewState preview = _model.PlacementPreview.Value;
            Assert.That(preview.RealtimeReachabilityEvaluated, Is.True);
            Assert.That(preview.CanConfirm, Is.True, "动态居民不能抢占玩家的建造位置。");
            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            yield return WaitForBuildTransaction();

            FoundationReadModel readModel =
                _context.ExecuteCommand(new GetFoundationReadModelCommand());
            FoundationFacilityState[] facilities =
                _context.ExecuteCommand(new GetFoundationFacilitiesCommand());
            Assert.That(facilities.Length, Is.EqualTo(2));
            Assert.That(
                readModel.BuildTransactionPhase.CurrentValue,
                Is.EqualTo(FoundationBuildTransactionPhase.Idle));
            Assert.That(readModel.PlacementPreview.CurrentValue.Active, Is.False);
            DeckPose residentPose = _layout.LocalToPose(
                new Vector3(
                    readModel.ResidentLocalPosition.CurrentValue.x,
                    0f,
                    readModel.ResidentLocalPosition.CurrentValue.z));
            int requiredClearanceMillimeters = Mathf.CeilToInt(
                (_context.GetUtility<DeckNavigationUtility>().NavigationAgentRadiusMeters +
                 _context.GetUtility<DeckNavigationUtility>().EffectiveVoxelSizeMeters * 0.55f) *
                1000f);
            Assert.That(
                _definitions[1].CreateFootprint().ContainsPoint(
                    facilities[1].Pose,
                    residentPose,
                    requiredClearanceMillimeters),
                Is.False,
                "NavMesh 更新后居民必须离开设施占地及 Agent 半径净空，不能只把中心挪到边缘。 ");
        }

        [UnityTest]
        public IEnumerator CandidateBlockingExistingFacility_IsWarningButCanStillCommit()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("access-wall"));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(-2000, 0));
            yield return null;

            FoundationPlacementPreviewState preview = _model.PlacementPreview.Value;
            Assert.That(
                preview.Failure,
                Is.EqualTo(FoundationPlacementFailure.RequiredInteractionUnreachable));
            Assert.That(preview.CanConfirm, Is.True, "可达性问题应是软警告，而不是几何禁建。");
            FoundationFacilityAccessState[] previewAccess =
                _context.ExecuteCommand(new GetFoundationFacilityAccessCommand());
            Assert.That(previewAccess.Length, Is.EqualTo(1));
            Assert.That(previewAccess[0].PreviewEvaluated, Is.True);
            Assert.That(
                previewAccess[0].PreviewAccess,
                Is.EqualTo(FoundationFacilityAccess.Unreachable),
                "横断甲板的候选应实时指出既有水箱会被切断。");
            Transform sourceVisual = FindFacilityVisual("initial-vehicle-water-tank");
            Transform sourceSlots = sourceVisual.Find(
                "Interaction Slots (build mode)");
            Assert.That(sourceSlots, Is.Not.Null);
            Assert.That(
                sourceSlots.gameObject.activeSelf,
                Is.True,
                "进入建造模式后全部 Slot 必须持续显示，不再依赖悬停。 ");
            Transform functionWarnings = sourceVisual.Find(
                "Inaccessible Function Warnings (build mode)");
            Assert.That(functionWarnings, Is.Not.Null);
            Assert.That(functionWarnings.childCount, Is.EqualTo(1));
            Assert.That(
                functionWarnings.GetChild(0).gameObject.activeSelf,
                Is.True,
                "没有可达 Slot 的功能点应在建造视图持续标红，不依赖悬停。");
            Renderer sourceBody = sourceVisual.GetComponentInChildren<Renderer>();
            var properties = new MaterialPropertyBlock();
            sourceBody.GetPropertyBlock(properties);
            Assert.That(properties.isEmpty, Is.False, "不可达设施应在建造视图持续着色。");

            Assert.That(sourceSlots.childCount, Is.EqualTo(3));
            for (var slotIndex = 0; slotIndex < sourceSlots.childCount; slotIndex++)
            {
                Renderer[] slotRenderers =
                    sourceSlots.GetChild(slotIndex).GetComponentsInChildren<Renderer>();
                Assert.That(slotRenderers.Length, Is.GreaterThan(0));
                for (var rendererIndex = 0;
                     rendererIndex < slotRenderers.Length;
                     rendererIndex++)
                    Assert.That(
                        slotRenderers[rendererIndex].sharedMaterial.name,
                        Is.EqualTo("M_InteractionSlotBlocked"));
            }

            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            yield return WaitForBuildTransaction();

            FoundationFacilityState[] facilities =
                _context.ExecuteCommand(new GetFoundationFacilitiesCommand());
            Assert.That(facilities.Length, Is.EqualTo(2));
            FoundationFacilityAccessState[] committedAccess =
                _context.ExecuteCommand(new GetFoundationFacilityAccessCommand());
            FoundationFacilityAccessState sourceAccess = FindAccess(
                committedAccess,
                "initial-vehicle-water-tank");
            Assert.That(
                sourceAccess.CommittedAccess,
                Is.EqualTo(FoundationFacilityAccess.Unreachable));
            FoundationFacilityAccessState wallAccess = FindAccess(
                committedAccess,
                "facility-0001");
            Assert.That(
                wallAccess.CommittedAccess,
                Is.EqualTo(FoundationFacilityAccess.PartiallyReachable),
                "同一设施的东侧功能点可用、西侧功能点不可用，应保留部分可达状态。");
            Assert.That(wallAccess.DisplayReachableSlotCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator SnapCanBeDisabled_AndExactContinuousPoseBecomesFacilityTruth()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPositionSnapCommand(0));
            _context.ExecuteCommand(new SetFoundationRotationSnapCommand(0));
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(375, -125));
            _context.ExecuteCommand(new RotateFacilityPreviewCommand(1));
            yield return null;

            FoundationPlacementPreviewState preview = _model.PlacementPreview.Value;
            Assert.That(preview.Pose, Is.EqualTo(new DeckPose(375, -125, 50)));
            Assert.That(preview.RealtimeReachabilityEvaluated, Is.True);
            Assert.That(preview.InteractionSlotCount, Is.EqualTo(3));
            Assert.That(preview.ReachableInteractionSlotCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(
                _model.ShowPlacementGrid.Value,
                Is.True,
                "自由模式应只临时隐藏网格，不覆盖玩家对其他吸附档位的网格偏好。 ");
            Assert.That(
                _worldView.transform.Find("Vehicle Deck Root/Optional Placement Grid")
                    .gameObject.activeSelf,
                Is.False);
            Transform interactionSlots = _worldView.transform.Find(
                "Vehicle Deck Root/Placement Interaction Slots");
            Assert.That(interactionSlots, Is.Not.Null);
            Assert.That(interactionSlots.gameObject.activeSelf, Is.True);
            Assert.That(interactionSlots.childCount, Is.EqualTo(3));

            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            yield return WaitForBuildTransaction();

            FoundationFacilityState[] facilities =
                _context.ExecuteCommand(new GetFoundationFacilitiesCommand());
            Assert.That(facilities.Length, Is.EqualTo(2));
            Assert.That(facilities[1].Pose, Is.EqualTo(new DeckPose(375, -125, 50)));
        }

        [UnityTest]
        public IEnumerator CancelDuringNavigationUpdate_RollsBackAndAllowsRetry()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(0, 0));
            yield return null;

            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            Assert.That(
                _model.BuildTransactionPhase.Value,
                Is.EqualTo(FoundationBuildTransactionPhase.UpdatingCandidateNavigation));
            _context.ExecuteCommand(new CancelFacilityPlacementCommand());
            yield return WaitForBuildTransaction();

            Assert.That(
                _context.ExecuteCommand(new GetFoundationFacilitiesCommand()).Length,
                Is.EqualTo(1));
            Assert.That(
                _model.InteractionMode.Value,
                Is.EqualTo(FoundationInteractionMode.Build),
                "取消当前幽灵应留在建造模式，方便继续选设施。 ");
            Assert.That(_model.PlacementPreview.Value.Active, Is.False);

            _context.ExecuteCommand(new ExitFoundationBuildModeCommand());
            Assert.That(_model.InteractionMode.Value, Is.EqualTo(FoundationInteractionMode.Observe));

            _context.ExecuteCommand(new EnterFoundationBuildModeCommand());
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(0, 0));
            yield return null;
            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            yield return WaitForBuildTransaction();

            Assert.That(
                _context.ExecuteCommand(new GetFoundationFacilitiesCommand()).Length,
                Is.EqualTo(2),
                "取消回滚必须同时释放连续占地与 NavMesh 候选障碍。");
        }

        [UnityTest]
        public IEnumerator IdleResident_RestocksDrinkingStationWithoutUnneededDrink()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(0, 0));
            yield return null;
            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            yield return WaitForBuildTransaction();

            const int drinkFrameLimit = 180;
            for (var i = 0;
                 i < drinkFrameLimit && _model.CompletedDrinkCount.Value == 0;
                 i++)
                yield return null;
            Assert.That(_model.CompletedDrinkCount.Value, Is.EqualTo(1));

            const int restockFrameLimit = 180;
            for (var i = 0;
                 i < restockFrameLimit &&
                 _model.DrinkingStationWaterMilliliters.Value < 4_000;
                 i++)
                yield return null;

            Assert.That(_model.DrinkingStationWaterMilliliters.Value, Is.EqualTo(4_000));
            Assert.That(_model.VehicleWaterMilliliters.Value, Is.EqualTo(55_700));
            Assert.That(
                _model.CompletedDrinkCount.Value,
                Is.EqualTo(1),
                "例行补货只补充站内库存，不应让不渴的居民连续喝水。");
            Assert.That(_model.ResidentCarryingWater.Value, Is.False);
            Assert.That(_model.WaterCanWaterMilliliters.Value, Is.Zero);
            Assert.That(
                _model.WaterCanLocation.Value,
                Is.EqualTo(FoundationWaterCanLocation.DrinkingStation));
            Assert.That(_model.LastBlocker.Value, Is.Empty);
        }

        [UnityTest]
        public IEnumerator NoNecessaryTask_ResidentChoosesWeightedLeisureCandidate()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(0, 0));
            yield return null;
            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            yield return WaitForBuildTransaction();

            const int targetLeisureCount = 12;
            const int frameLimit = 720;
            float entertainmentBeforeLeisure = _model.ResidentEntertainment.Value;
            float fatigueBeforeLeisure = _model.ResidentFatigue.Value;
            float stressBeforeLeisure = _model.ResidentStress.Value;
            for (var i = 0;
                 i < frameLimit && _model.CompletedLeisureCount.Value < targetLeisureCount;
                 i++)
                yield return null;

            Assert.That(
                _model.CompletedLeisureCount.Value,
                Is.GreaterThanOrEqualTo(targetLeisureCount),
                "必要饮水与低库存补货完成后，居民应能持续产生自主休闲。 ");
            Assert.That(
                _model.CompletedDaydreamCount.Value,
                Is.GreaterThan(0),
                "发呆应作为疲劳与压力休整候选保留，而不是依赖娱乐缺口。 ");
            Assert.That(
                _model.CompletedWanderCount.Value,
                Is.GreaterThan(0),
                "散步不能因路径成本与相对短名单的双重筛选而永久消失。 ");
            FoundationActionPlanProjection plan = _model.LatestActionPlan.Value;
            Assert.That(
                plan.CandidateId,
                Is.EqualTo(ResidentLeisurePlanFactory.WanderCandidateId).Or.EqualTo(
                    ResidentLeisurePlanFactory.DaydreamCandidateId));
            Assert.That(plan.Selected, Is.True);
            Assert.That(plan.SelectionProbability, Is.GreaterThan(0f));
            Assert.That(
                _model.ResidentEntertainment.Value,
                Is.LessThan(entertainmentBeforeLeisure),
                "发呆和闲逛不能把正向娱乐满足度补满；没有爱好设施时它应继续缓慢下降。 ");
            Assert.That(
                _model.ResidentFatigue.Value,
                Is.LessThanOrEqualTo(fatigueBeforeLeisure),
                "自主休整应连续缓解疲劳。 ");
            Assert.That(
                _model.ResidentStress.Value,
                Is.LessThanOrEqualTo(stressBeforeLeisure),
                "自主休整应连续缓解压力。 ");
            Assert.That(_model.ResidentMood.Value, Is.InRange(0f, 1f));
        }

        [UnityTest]
        public IEnumerator BuiltHobbyPoint_ResidentDocksAndRestoresEntertainment()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            _system.ConfigurePhysiologyForTests(
                configuredDrinkMetabolismSeconds: 8f,
                configuredInitialThirst: 0.08f,
                configuredThirstIncreasePerSecond: 0f);
            _system.ConfigureWellbeingForTests(
                entertainment: 0.12f,
                mood: 0.62f,
                fatigue: 0.24f,
                stress: 0.2f,
                paintingAffinity: 0.95f);
            _system.ResetScenario();
            yield return null;
            yield return BuildFacility("observation-easel", 1200, 0);

            FoundationFacilityState[] facilities = _context.ExecuteCommand(
                new GetFoundationFacilitiesCommand());
            FoundationFacilityState easel = facilities[facilities.Length - 1];
            Assert.That(easel.DefinitionId, Is.EqualTo("observation-easel"));

            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            var observedHobby = false;
            float entertainmentAtActivityStart = 0f;
            const int approachFrameLimit = 360;
            for (var i = 0; i < approachFrameLimit; i++)
            {
                yield return null;
                if (_model.ResidentPhase.Value != FoundationResidentPhase.EnjoyingHobby)
                    continue;
                observedHobby = true;
                entertainmentAtActivityStart = _model.ResidentEntertainment.Value;
                FoundationReadModel readModel = _context.ExecuteCommand(
                    new GetFoundationReadModelCommand());
                AssertResidentDockedToAnySlot(
                    easel,
                    _definitions[_definitions.Length - 1],
                    readModel);
                Assert.That(
                    _model.LatestActionPlan.Value.CandidateId,
                    Is.EqualTo("hobby:facility-0001"));
                break;
            }
            Assert.That(observedHobby, Is.True, "低娱乐且偏好作画时，应实际前往已建造画架。 ");

            const int completionFrameLimit = 180;
            for (var i = 0;
                 i < completionFrameLimit && _model.CompletedHobbyCount.Value == 0;
                 i++)
                yield return null;

            Assert.That(_model.CompletedHobbyCount.Value, Is.GreaterThanOrEqualTo(1));
            Assert.That(_model.CompletedLeisureCount.Value, Is.GreaterThanOrEqualTo(1));
            Assert.That(
                _model.ResidentEntertainment.Value,
                Is.GreaterThan(entertainmentAtActivityStart),
                "只有到达设施后的真实爱好阶段才应恢复正向娱乐满足。 ");
            Assert.That(_model.LastBlocker.Value, Is.Empty);
        }

        [UnityTest]
        public IEnumerator PlacementBlocksActiveHobby_CancelsSoftIntentAndKeepsNeedsResponsive()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            _system.ConfigureTimingsForTests(0.25f, 2f);
            _system.ConfigurePhysiologyForTests(
                configuredDrinkMetabolismSeconds: 8f,
                configuredInitialThirst: 0.08f,
                configuredThirstIncreasePerSecond: 0f);
            _system.ConfigureWellbeingForTests(
                entertainment: 0.12f,
                mood: 0.62f,
                fatigue: 0.24f,
                stress: 0.2f,
                paintingAffinity: 0.95f);
            _system.ResetScenario();
            yield return null;
            yield return BuildFacility("observation-easel", 1200, 0);
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));

            const int approachFrameLimit = 720;
            var observedTravel = false;
            for (var i = 0; i < approachFrameLimit; i++)
            {
                yield return null;
                if (_model.ResidentPhase.Value != FoundationResidentPhase.MovingToHobby)
                    continue;
                observedTravel = true;
                break;
            }
            Assert.That(observedTravel, Is.True, "应先观察到居民正前往画架，才能验证施工中断。 ");

            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return BuildFacility("slot-blocker", 1200, -800);
            Assert.That(
                _model.ResidentPhase.Value,
                Is.Not.EqualTo(FoundationResidentPhase.WaitingForRoute),
                "无物资所有权的爱好不应在目标永久失效后占住路线重试状态。 ");
            Assert.That(_model.LastBlocker.Value, Is.Empty);

            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            const int fallbackFrameLimit = 720;
            for (var i = 0;
                 i < fallbackFrameLimit && _model.CompletedLeisureCount.Value == 0;
                 i++)
                yield return null;

            Assert.That(_model.CompletedHobbyCount.Value, Is.Zero);
            Assert.That(
                _model.CompletedDaydreamCount.Value + _model.CompletedWanderCount.Value,
                Is.GreaterThan(0),
                "爱好失效后仍应回到统一决策并执行可行休整，而不是冻结新需求评估。 ");
        }

        [UnityTest]
        public IEnumerator PlacementBlockingActiveStation_RetargetsAnotherStationAndCompletesDrink()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            _system.ConfigureTimingsForTests(0.25f, 5f);
            yield return BuildFacility("drinking-station", 0, 0);
            yield return BuildFacility("drinking-station", 2400, 0);
            Assert.That(
                _model.DrinkingStationCapacityMilliliters.Value,
                Is.EqualTo(12_000),
                "两座设施必须各自贡献 6 L 容量，而不是继续共享一个 6 L 库存。 ");
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));

            const int approachFrameLimit = 720;
            var observedPreferredStationTravel = false;
            for (var i = 0; i < approachFrameLimit; i++)
            {
                yield return null;
                observedPreferredStationTravel =
                    _model.ResidentPhase.Value == FoundationResidentPhase.MovingToDrinkingStation &&
                    _model.CurrentTask.Value.Contains("facility-0001") &&
                    _model.ResidentCarryingWater.Value;
                if (observedPreferredStationTravel) break;
            }
            Assert.That(observedPreferredStationTravel, Is.True, "应先观察到居民正在前往第一座饮水站。 ");

            yield return BuildFacility("slot-blocker", 0, -800);
            Assert.That(
                _model.ResidentPhase.Value,
                Is.Not.EqualTo(FoundationResidentPhase.Blocked),
                "施工切断旧目标后不应把临时路径失败写成永久 AI 终态。 ");

            var observedRetarget = _model.CurrentTask.Value.Contains("facility-0002");
            const int completionFrameLimit = 900;
            for (var i = 0; i < completionFrameLimit && _model.CompletedDrinkCount.Value == 0; i++)
            {
                yield return null;
                observedRetarget |= _model.CurrentTask.Value.Contains("facility-0002");
                Assert.That(_model.ResidentPhase.Value, Is.Not.EqualTo(FoundationResidentPhase.Blocked));
            }

            Assert.That(observedRetarget, Is.True, "旧设施不可用后应重新解析到同功能的第二座设施。 ");
            Assert.That(_model.CompletedDrinkCount.Value, Is.EqualTo(1));
            FoundationFacilityInventoryState[] inventories = _context.ExecuteCommand(
                new GetFoundationFacilityInventoriesCommand());
            Assert.That(inventories, Has.Length.EqualTo(2));
            FoundationFacilityInventoryState firstStation = default;
            FoundationFacilityInventoryState secondStation = default;
            for (var i = 0; i < inventories.Length; i++)
            {
                if (inventories[i].FacilityInstanceId == "facility-0001")
                    firstStation = inventories[i];
                else if (inventories[i].FacilityInstanceId == "facility-0002")
                    secondStation = inventories[i];
            }
            Assert.That(firstStation.InventoryId, Is.Not.Empty);
            Assert.That(secondStation.InventoryId, Is.Not.Empty);
            Assert.That(firstStation.Amount, Is.Zero, "旧路径目标不应收到改道后的水。 ");
            Assert.That(
                secondStation.Amount,
                Is.EqualTo(1_700),
                "第二座站应收到 2 L 搬运水，并从自己的库存扣除 300 mL 饮用量。 ");
            Assert.That(
                _model.WaterCanAnchorFacilityInstanceId.Value,
                Is.EqualTo("facility-0002"),
                "水罐表现锚点必须与最终交付库存属于同一设施实例。 ");
            Assert.That(_model.LastBlocker.Value, Is.Empty);
        }

        [UnityTest]
        public IEnumerator SingleServingBodyCapacity_RecoversThroughRealToiletWithoutPermanentBlock()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            _system.ConfigurePhysiologyForTests(
                3f,
                0.96f,
                1f,
                configuredToiletHoldingCapacityMilliliters: 1_200,
                configuredBodyWaterCapacityMilliliters: 300,
                configuredBladderCapacityMilliliters: 600);
            _context.ExecuteCommand(new ResetFoundationSliceCommand());
            yield return null;
            yield return BuildFacility("drinking-station", 0, 0);
            yield return BuildFacility("toilet", 2000, 0);
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));

            const int frameLimit = 720;
            for (var i = 0;
                 i < frameLimit &&
                 (_model.CompletedDrinkCount.Value < 2 || _model.CompletedToiletUseCount.Value < 1);
                 i++)
            {
                yield return null;
                Assert.That(
                    _model.ResidentPhase.Value,
                    Is.Not.EqualTo(FoundationResidentPhase.Blocked),
                    "身体库存暂满只能形成等待/如厕压力，不能令居民永久停机。 " +
                    $"Task={_model.CurrentTask.Value}; Blocker={_model.LastBlocker.Value}");
            }

            Assert.That(
                _model.CompletedDrinkCount.Value,
                Is.GreaterThanOrEqualTo(2),
                $"Task={_model.CurrentTask.Value}; Blocker={_model.LastBlocker.Value}; " +
                $"Station={_model.DrinkingStationWaterMilliliters.Value}mL; " +
                $"Body={_model.BodyWaterMilliliters.Value}mL; " +
                $"Bladder={_model.BladderWasteMilliliters.Value}mL; " +
                $"Toilet={_model.CompletedToiletUseCount.Value}");
            Assert.That(_model.CompletedToiletUseCount.Value, Is.GreaterThanOrEqualTo(1));
            Assert.That(
                _model.ToiletHoldingWasteMilliliters.Value,
                Is.GreaterThanOrEqualTo(ResidentWaterCycle.DefaultDrinkServingMilliliters));
            StringAssert.DoesNotContain(
                "DestinationFull",
                _model.LastBlocker.Value,
                "有限身体容量可以产生短暂背压，但饮水—代谢—如厕闭环完成后不应留下永久阻塞。");
        }

        [UnityTest]
        public IEnumerator WaterCanWithoutLiquidTightCapability_RejectsCandidateWithoutFreezingResident()
        {
            _worldView.enabled = false;
            _system.ConfigureWaterCanForTests(CargoContainerCapability.Sealable, 1f);
            _context.ExecuteCommand(new ResetFoundationSliceCommand());
            yield return null;

            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(0, 0));
            yield return null;
            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            yield return WaitForBuildTransaction();
            const int decisionFrameLimit = 60;
            for (var i = 0;
                 i < decisionFrameLimit &&
                 !_model.LastBlocker.Value.Contains("防漏");
                 i++)
                yield return null;

            FoundationActionPlanProjection plan = _model.LatestActionPlan.Value;
            Assert.That(_model.ResidentPhase.Value, Is.Not.EqualTo(FoundationResidentPhase.Blocked));
            Assert.That(plan.Evaluated, Is.True);
            Assert.That(plan.Feasible, Is.True, "不可执行的补水候选应被过滤，并允许居民选择安全退路。 ");
            Assert.That(plan.Selected, Is.True);
            StringAssert.Contains("防漏", _model.LastBlocker.Value);
            Assert.That(_model.VehicleWaterMilliliters.Value, Is.EqualTo(60_000));
            Assert.That(_model.WaterCanWaterMilliliters.Value, Is.Zero);
            Assert.That(
                _model.WaterCanLocation.Value,
                Is.EqualTo(FoundationWaterCanLocation.VehicleWaterTank));
            Assert.That(_model.DrinkingStationWaterMilliliters.Value, Is.Zero);
            Assert.That(_model.ResidentCarryingWater.Value, Is.False);
        }

        private IEnumerator WaitForBuildTransaction()
        {
            const int frameLimit = 240;
            for (var i = 0;
                 i < frameLimit &&
                 _model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle;
                 i++)
                yield return null;

            Assert.That(
                _model.BuildTransactionPhase.Value,
                Is.EqualTo(FoundationBuildTransactionPhase.Idle),
                "NavMesh 建造事务应在限定帧内提交或完整回滚。");
        }

        private static NomadFacilityDefinition[] CreateDefinitions()
        {
            var source = ScriptableObject.CreateInstance<NomadFacilityDefinition>();
            source.ConfigureForTests(
                "vehicle-water-tank",
                "车辆水箱",
                NomadFacilityFunction.VehicleWaterTank,
                false,
                new[]
                {
                    new NomadFacilityFootprintPartDefinition(
                        Vector2.zero,
                        new Vector2(1.8f, 1f)),
                },
                RequiredGroup(
                    "water-pickup",
                    new NomadFacilityInteractionSlotDefinition(
                        "left",
                        new Vector2(-0.48f, -0.9f)),
                    new NomadFacilityInteractionSlotDefinition(
                        "center",
                        new Vector2(0f, -0.94f)),
                    new NomadFacilityInteractionSlotDefinition(
                        "right",
                        new Vector2(0.48f, -0.9f))),
                true,
                new Vector2(-3.4f, 2.25f),
                0f,
                new Vector3(1.65f, 1.4f, 0.86f),
                Color.blue);

            var station = ScriptableObject.CreateInstance<NomadFacilityDefinition>();
            station.ConfigureForTests(
                "drinking-station",
                "饮水站",
                NomadFacilityFunction.DrinkingStation,
                true,
                new[]
                {
                    new NomadFacilityFootprintPartDefinition(
                        Vector2.zero,
                        new Vector2(0.85f, 0.72f)),
                },
                RequiredGroup(
                    "drink-and-deliver",
                    new NomadFacilityInteractionSlotDefinition(
                        "left",
                        new Vector2(-0.24f, -0.78f)),
                    new NomadFacilityInteractionSlotDefinition(
                        "center",
                        new Vector2(0f, -0.82f)),
                    new NomadFacilityInteractionSlotDefinition(
                        "right",
                        new Vector2(0.24f, -0.78f))),
                false,
                Vector2.zero,
                0f,
                new Vector3(0.78f, 0.9f, 0.65f),
                Color.cyan);

            var accessWall = ScriptableObject.CreateInstance<NomadFacilityDefinition>();
            accessWall.ConfigureForTests(
                "access-wall",
                "通路测试墙",
                NomadFacilityFunction.FieldKitchen,
                true,
                new[]
                {
                    new NomadFacilityFootprintPartDefinition(
                        Vector2.zero,
                        new Vector2(0.3f, 8.4f)),
                },
                new[]
                {
                    new NomadFacilityInteractionGroupDefinition(
                        "east-function",
                        true,
                        new[]
                        {
                            new NomadFacilityInteractionSlotDefinition(
                                "east-a",
                                new Vector2(0.7f, -0.35f)),
                            new NomadFacilityInteractionSlotDefinition(
                                "east-b",
                                new Vector2(0.7f, 0.35f)),
                        }),
                    new NomadFacilityInteractionGroupDefinition(
                        "west-function",
                        true,
                        new[]
                        {
                            new NomadFacilityInteractionSlotDefinition(
                                "west",
                                new Vector2(-0.7f, 0f)),
                        }),
                },
                false,
                Vector2.zero,
                0f,
                new Vector3(0.3f, 1f, 8.4f),
                Color.gray);
            var toilet = ScriptableObject.CreateInstance<NomadFacilityDefinition>();
            toilet.ConfigureForTests(
                "toilet",
                "旱厕",
                NomadFacilityFunction.Toilet,
                true,
                new[]
                {
                    new NomadFacilityFootprintPartDefinition(Vector2.zero, new Vector2(0.8f, 0.9f)),
                },
                RequiredGroup(
                    "use-toilet",
                    new NomadFacilityInteractionSlotDefinition("left", new Vector2(-0.2f, -0.88f)),
                    new NomadFacilityInteractionSlotDefinition("center", new Vector2(0f, -0.92f)),
                    new NomadFacilityInteractionSlotDefinition("right", new Vector2(0.2f, -0.88f))),
                false,
                Vector2.zero,
                0f,
                new Vector3(0.72f, 0.75f, 0.82f),
                Color.yellow);

            var slotBlocker = ScriptableObject.CreateInstance<NomadFacilityDefinition>();
            slotBlocker.ConfigureForTests(
                "slot-blocker",
                "停靠位阻断测试件",
                NomadFacilityFunction.Storage,
                true,
                new[]
                {
                    new NomadFacilityFootprintPartDefinition(Vector2.zero, new Vector2(1.3f, 0.4f)),
                },
                RequiredGroup(
                    "inspect",
                    new NomadFacilityInteractionSlotDefinition("front", new Vector2(0f, -0.72f))),
                false,
                Vector2.zero,
                0f,
                new Vector3(1.3f, 0.7f, 0.4f),
                Color.magenta);

            var observationEasel = ScriptableObject.CreateInstance<NomadFacilityDefinition>();
            observationEasel.ConfigureForTests(
                "observation-easel",
                "观景画架",
                NomadFacilityFunction.HobbyPoint,
                true,
                new[]
                {
                    new NomadFacilityFootprintPartDefinition(
                        Vector2.zero,
                        new Vector2(0.9f, 0.68f)),
                },
                RequiredGroup(
                    "paint-and-observe",
                    new NomadFacilityInteractionSlotDefinition("left", new Vector2(-0.24f, -0.82f)),
                    new NomadFacilityInteractionSlotDefinition("center", new Vector2(0f, -0.86f)),
                    new NomadFacilityInteractionSlotDefinition("right", new Vector2(0.24f, -0.82f))),
                false,
                Vector2.zero,
                0f,
                new Vector3(0.86f, 1.38f, 0.62f),
                new Color(0.58f, 0.28f, 0.13f));
            return new[] { source, station, accessWall, toilet, slotBlocker, observationEasel };
        }

        private IEnumerator BuildFacility(
            string definitionId,
            int xMillimeters,
            int zMillimeters,
            int counterClockwiseRotationSteps = 0)
        {
            _context.ExecuteCommand(new BeginFacilityPlacementCommand(definitionId));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(xMillimeters, zMillimeters));
            for (var i = 0; i < counterClockwiseRotationSteps; i++)
                _context.ExecuteCommand(new RotateFacilityPreviewCommand(1));
            yield return null;
            Assert.That(_model.PlacementPreview.Value.CanConfirm, Is.True, $"测试设施 {definitionId} 应允许确认。 ");
            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            yield return WaitForBuildTransaction();
        }

        private static void AssertResidentDockedToAnySlot(
            in FoundationFacilityState facility,
            NomadFacilityDefinition definition,
            in FoundationReadModel readModel)
        {
            Vector3 resident = readModel.ResidentLocalPosition.CurrentValue;
            float residentYaw = readModel.ResidentLocalYawDegrees.CurrentValue;
            float bestDistance = float.PositiveInfinity;
            DeckPose bestPose = default;
            foreach (NomadFacilityInteractionGroupDefinition group in definition.InteractionGroups)
            {
                foreach (NomadFacilityInteractionSlotDefinition slot in group.AlternativeSlots)
                {
                    DeckPose pose = slot.Resolve(facility.Pose);
                    float distance = Vector2.Distance(
                        new Vector2(resident.x, resident.z),
                        new Vector2((float)pose.XMeters, (float)pose.ZMeters));
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    bestPose = pose;
                }
            }

            Assert.That(bestDistance, Is.LessThanOrEqualTo(0.002f), "动作开始前居民应精确走到量化停靠位。 ");
            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(residentYaw, (float)bestPose.YawDegrees)),
                Is.LessThanOrEqualTo(0.01f),
                "动作开始前居民朝向应精确对齐设施，供后续交互动画复用。 ");
        }

        private static FoundationFacilityAccessState FindAccess(
            FoundationFacilityAccessState[] states,
            string instanceId)
        {
            for (var i = 0; i < states.Length; i++)
            {
                if (states[i].InstanceId == instanceId) return states[i];
            }
            Assert.Fail($"没有找到设施访问状态：{instanceId}");
            return default;
        }

        private Transform FindFacilityVisual(string instanceId)
        {
            Transform facilities = _worldView.transform.Find("Vehicle Deck Root/Facilities");
            Assert.That(facilities, Is.Not.Null);
            for (var i = 0; i < facilities.childCount; i++)
            {
                Transform candidate = facilities.GetChild(i);
                if (candidate.name.Contains($"[{instanceId}]")) return candidate;
            }
            Assert.Fail($"没有找到设施表现：{instanceId}");
            return null;
        }

        private static NomadFacilityInteractionGroupDefinition[] RequiredGroup(
            string groupId,
            params NomadFacilityInteractionSlotDefinition[] slots) =>
            new[]
            {
                new NomadFacilityInteractionGroupDefinition(groupId, true, slots),
            };

        private static void CreateNavigationInfrastructure(
            Transform parent,
            DeckLayoutDefinition layout)
        {
            GameObject navigation = CreateChild(parent, "Navigation");
            NavMeshSurface surface = navigation.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.09f;
            surface.overrideTileSize = true;
            surface.tileSize = 128;
            surface.minRegionArea = 0.05f;

            GameObject walkableDeck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            walkableDeck.name = "Nav Source · Walkable Deck";
            walkableDeck.transform.SetParent(navigation.transform, false);
            walkableDeck.transform.localPosition =
                layout.DeckCenterLocal + new Vector3(0f, -0.15f, 0f);
            walkableDeck.transform.localScale = new Vector3(
                layout.DeckSize.x,
                0.3f,
                layout.DeckSize.z);
            walkableDeck.GetComponent<Renderer>().enabled = false;

            GameObject obstacles = CreateChild(navigation.transform, "Runtime Obstacles");
            DeckNavigationUtility utility = navigation.AddComponent<DeckNavigationUtility>();
            utility.ConfigureRuntime(
                surface,
                System.Array.Empty<NavigationAgentBinding>(),
                0.42f,
                navigation.transform,
                obstacles.transform,
                2.4f);
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }
    }
}
