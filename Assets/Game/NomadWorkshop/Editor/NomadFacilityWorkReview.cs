using System;
using System.IO;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>等待真实设施工作进入中段后暂停并设置近景；只观察同一读模型，失败/退出/重载撤销监听。</summary>
    [InitializeOnLoad]
    public static class NomadFacilityWorkReview
    {
        private static NomadFoundationContext _context;
        private static FoundationReadModel _read;
        private static FoundationResidentPhase _phase;
        private static float _previousSpeed;
        private static bool _previousPaused;
        private static double _deadline;
        private static int _settleFrame;
        private static string _residentId;
        private static bool _contactReview;

        static NomadFacilityWorkReview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) Cancel();
            };
        }

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/观察水箱装水近景")]
        public static void ObserveFill() => Begin(FoundationResidentPhase.PickingUpWater);

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/观察饮水站倒水近景")]
        public static void ObservePour() => Begin(FoundationResidentPhase.DeliveringWater);

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/观察装水掌心近景")]
        public static void ObserveFillContact() => Begin(FoundationResidentPhase.PickingUpWater, true);

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/观察倒水掌心近景")]
        public static void ObservePourContact() => Begin(FoundationResidentPhase.DeliveringWater, true);

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/观察地面握罐近景")]
        public static void ObserveGroundPickup() => Begin(FoundationResidentPhase.PickingUpWaterCan);

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/观察地面下放近景")]
        public static void ObserveGroundPlacement() => Begin(FoundationResidentPhase.PlacingWaterCan);

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/触发水箱故障并观察实体维修")]
        public static void ObserveRepair() => Begin(FoundationResidentPhase.RepairingFacility);

        private static void Begin(FoundationResidentPhase phase, bool contactReview = false)
        {
            if (_context != null) throw new InvalidOperationException("当前仍有设施观察在等待。");
            Scene scene = SceneManager.GetActiveScene();
            if (!EditorApplication.isPlaying || (scene.path != NomadWarmWorkshopArtPipeline.ScenePath &&
                scene.path != NomadResidentSamplePipeline.ScenePath && scene.path != NomadResidentCrewPipeline.ScenePath &&
                scene.path != NomadFacilityBindingPipeline.ScenePath && scene.path != NomadReferenceVehiclePipeline.ScenePath))
                throw new InvalidOperationException("请先运行温暖工坊或现成人物对比样板。");
            _context = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadFoundationContext>()).Single();
            _read = _context.ExecuteCommand(new GetFoundationReadModelCommand());
            if (!_read.IsReady.CurrentValue) { _context = null; throw new InvalidOperationException("样板尚未就绪。"); }
            _phase = phase;
            _contactReview = contactReview;
            _previousSpeed = _read.SimulationSpeed.CurrentValue;
            _previousPaused = _read.IsPaused.CurrentValue;
            _deadline = EditorApplication.timeSinceStartup + 60;
            _settleFrame = -1;
            if (phase == FoundationResidentPhase.RepairingFacility)
                _context.ExecuteCommand(new ForcePrimaryWaterTankFaultCommand());
            _context.ExecuteCommand(new SetFoundationSpeedCommand(1f));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            EditorApplication.update += Tick;
            Write(new Report { status = "waiting-for-real-work", phase = phase.ToString() });
            Debug.Log("[NomadFacilityReview] WAITING：等待真实 " + phase + "，不跳过移动或提前提交资源。");
        }

        private static void Tick()
        {
            try
            {
                if (_context == null) { Cancel(); return; }
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > _deadline)
                {
                    Write(new Report { status = "timeout-or-exit", phase = _phase.ToString() });
                    Cancel();
                    return;
                }
                if (_settleFrame < 0)
                {
                    foreach (FoundationResidentReadModel resident in _read.Residents)
                    {
                        FoundationFacilityWorkState work = resident.FacilityWork.CurrentValue;
                        float minimum = _phase == FoundationResidentPhase.PickingUpWaterCan ? .82f : .4f;
                        float maximum = _phase == FoundationResidentPhase.PickingUpWaterCan ? .98f : .7f;
                        if (!work.Active || work.Phase != _phase || work.Progress < minimum || work.Progress > maximum) continue;
                        _residentId = resident.StableId;
                        _context.ExecuteCommand(new SetFoundationPausedCommand(true));
                        _context.ExecuteCommand(new SetFoundationSpeedCommand(_previousSpeed));
                        _settleFrame = Time.frameCount + 3;
                        return;
                    }
                    return;
                }
                if (Time.frameCount < _settleFrame) return;
                Capture();
                EditorApplication.update -= Tick;
                _context = null;
            }
            catch (Exception error)
            {
                Write(new Report { status = "failed", phase = _phase.ToString(), error = error.ToString() });
                Cancel();
                Debug.LogException(error);
            }
        }

        private static void Capture()
        {
            var roots = _context.gameObject.scene.GetRootGameObjects();
            var view = roots.SelectMany(r => r.GetComponentsInChildren<NomadFoundationWorldView>()).Single();
            Transform deck = view.transform.Find("Vehicle Deck Root");
            FoundationResidentReadModel resident = _read.Residents.Single(r => r.StableId == _residentId);
            FoundationFacilityWorkState work = resident.FacilityWork.CurrentValue;
            var rig = deck.GetComponentsInChildren<FoundationFacilityArtRig>().Single(r => r.GetComponentsInParent<Transform>()
                .Any(t => t.name.EndsWith("[" + work.FacilityInstanceId + "]", StringComparison.Ordinal)));
            var humanoid = deck.Find("Resident " + _residentId.Substring(_residentId.LastIndexOf('-') + 1))
                .GetComponent<ResidentHumanoidPresentation>();
            Transform can = deck.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Water Can 01 [physical carrier]");
            Camera camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>()).Single();
            Vector3 focus = (rig.transform.position + humanoid.transform.position) * .5f + Vector3.up * .72f;
            camera.orthographic = true;
            camera.orthographicSize = 1.8f;
            camera.transform.position = focus + rig.transform.TransformDirection(_phase == FoundationResidentPhase.RepairingFacility
                ? new Vector3(2.2f, 1.8f, 3.2f) : new Vector3(3.2f, 2.6f, -3.0f));
            camera.transform.LookAt(focus);
            var contact = humanoid.Animator.GetComponent<FoundationResidentCarryIK>();
            Transform palmTarget = can.GetComponent<FoundationCarriedContainerRig>().RightPalm;
            if (work.ItemContactPlacement.Active)
            {
                focus = (humanoid.transform.position + can.position) * .5f + Vector3.up * .65f;
                camera.orthographicSize = 1.35f;
                camera.transform.position = focus + humanoid.transform.TransformDirection(new Vector3(2.6f, 2.2f, 3.5f));
                camera.transform.LookAt(focus);
            }
            if (_contactReview)
            {
                focus = palmTarget.position;
                camera.orthographicSize = .68f;
                camera.transform.position = focus + humanoid.transform.TransformDirection(new Vector3(3f, 2.7f, -1.4f));
                camera.transform.LookAt(focus);
            }
            bool holdsCan = _read.WaterCanCarrierId.CurrentValue == resident.StableId &&
                _read.WaterCanLocation.CurrentValue == FoundationWaterCanLocation.Resident;
            bool touchesCan = holdsCan || work.ItemContactPlacement.Active;
            Write(new Report
            {
                status = "paused-at-real-work", phase = _phase.ToString(), residentId = resident.StableId,
                facilityId = work.FacilityInstanceId, groupId = work.GroupId, progress = work.Progress,
                palmGap = touchesCan ? Vector3.Distance(contact.RightPalmContactPosition, palmTarget.position) : -1f,
                palmAngleDegrees = touchesCan ? Quaternion.Angle(contact.RightPalmRotation, palmTarget.rotation) : -1f,
                handWeight = contact.RightHandContactWeight, footWeight = contact.GroundFootContactWeight,
                leftFootError = Vector3.Distance(humanoid.Animator.GetBoneTransform(HumanBodyBones.LeftFoot).position, contact.LeftGroundFootTarget),
                rightFootError = Vector3.Distance(humanoid.Animator.GetBoneTransform(HumanBodyBones.RightFoot).position, contact.RightGroundFootTarget),
                contactPlacement = work.ItemContactPlacement,
                canLocation = _read.WaterCanLocation.CurrentValue.ToString(),
                elbowLocal = humanoid.transform.InverseTransformPoint(humanoid.Animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position),
                simulationTick = _read.SimulationTick.CurrentValue, waterMilliliters = _read.WaterCanWaterMilliliters.CurrentValue,
                handGap = holdsCan ? Vector3.Distance(humanoid.Animator.GetBoneTransform(HumanBodyBones.RightHand).position,
                    can.GetComponent<FoundationCarriedContainerRig>().CarryPivot.position) : -1f,
                waterVisible = rig.Water.enabled, hoseVisible = rig.Hose.enabled,
                waterFrom = rig.Water.enabled ? rig.Water.GetPosition(0) : default,
                waterTo = rig.Water.enabled ? rig.Water.GetPosition(rig.Water.positionCount - 1) : default,
                residentPosition = humanoid.transform.position, canPosition = can.position
            });
            Debug.Log("[NomadFacilityReview] READY：实际设施工作已暂停，近景和状态报告已就绪。");
        }

        private static void Cancel()
        {
            EditorApplication.update -= Tick;
            try
            {
                if (_context != null && EditorApplication.isPlaying)
                {
                    _context.ExecuteCommand(new SetFoundationSpeedCommand(_previousSpeed));
                    _context.ExecuteCommand(new SetFoundationPausedCommand(_previousPaused));
                }
            }
            finally { _context = null; }
        }

        private static void Write(Report report)
        {
            report.capturedUtc = DateTime.UtcNow.ToString("O");
            report.scenePath = SceneManager.GetActiveScene().path;
            Directory.CreateDirectory("Logs/AIValidation/nomad-warm-art");
            string prefix = _contactReview ? "contact-review-" : "facility-review-";
            File.WriteAllText("Logs/AIValidation/nomad-warm-art/" + prefix + _phase + ".json", JsonUtility.ToJson(report, true));
        }

        [Serializable] private sealed class Report
        {
            public string status, phase, residentId, facilityId, groupId, capturedUtc, error, canLocation, scenePath;
            public long simulationTick;
            public float progress, handGap, palmGap, palmAngleDegrees;
            public float handWeight, footWeight, leftFootError, rightFootError;
            public FoundationItemPlacementState contactPlacement;
            public int waterMilliliters;
            public bool waterVisible, hoseVisible;
            public Vector3 waterFrom, waterTo, residentPosition, canPosition, elbowLocal;
        }
    }
}
