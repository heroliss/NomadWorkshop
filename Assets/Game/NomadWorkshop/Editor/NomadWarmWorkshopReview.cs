using System;
using System.IO;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Simulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>
    /// 等待真实搬水工作到达携行阶段后暂停并布置检查镜头。只运行于指定 Play 场景；
    /// 超时、退出 Play 或域重载都会撤监听，不残留自动化更新。
    /// </summary>
    [InitializeOnLoad]
    public static class NomadWarmWorkshopReview
    {
        private static NomadFoundationContext _context;
        private static FoundationReadModel _read;
        private static float _previousSpeed;
        private static bool _previousPaused;
        private static double _deadline;
        private static bool _waiting;
        private static int _settleFrames;
        private static string _trackedCarrier;
        private static Vector3 _carryStartPosition;
        private static bool _journeyReview;
        private static long _journeyStart;

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/记录当前环境光")]
        public static void RecordAmbientLighting()
        {
            if (!EditorApplication.isPlaying || !IsSupportedScene())
                throw new InvalidOperationException("请先运行温暖工坊样板。");
            SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
            Debug.Log($"[NomadWarmReview] 当前环境光：mode={RenderSettings.ambientMode}, " +
                $"sky={RenderSettings.ambientSkyColor}, horizon={RenderSettings.ambientEquatorColor}, " +
                $"ground={RenderSettings.ambientGroundColor}, SH0=({probe[0,0]:F3},{probe[1,0]:F3},{probe[2,0]:F3})。");
        }

        static NomadWarmWorkshopReview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) Cancel();
            };
        }

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/观察真实持桶近景")]
        public static void ObserveCarry()
        {
            StartObservation(false);
        }

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/观察启程俯瞰")]
        public static void ObserveJourney()
        {
            StartObservation(true);
        }

        private static void StartObservation(bool journey)
        {
            if (!EditorApplication.isPlaying || !IsSupportedScene())
                throw new InvalidOperationException("请先运行温暖工坊样板。");
            if (_waiting) throw new InvalidOperationException("已经在等待本次持桶检查点。");
            Scene scene = SceneManager.GetActiveScene();
            _context = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadFoundationContext>()).Single();
            _read = _context.ExecuteCommand(new GetFoundationReadModelCommand());
            if (!_read.IsReady.CurrentValue) throw new InvalidOperationException("样板尚未就绪。");
            _previousSpeed = _read.SimulationSpeed.CurrentValue;
            _previousPaused = _read.IsPaused.CurrentValue;
            _deadline = EditorApplication.timeSinceStartup + 45;
            _settleFrames = -1;
            _trackedCarrier = null;
            _journeyReview = journey;
            _journeyStart = _read.JourneyPositionMicrometers.CurrentValue;
            _waiting = true;
            WriteReport(new Report { status = journey ? "waiting-for-real-journey" : "waiting-for-real-carry" });
            if (journey) _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(
                _journeyStart < 1_000_000_000L ? NomadJourneyEndpoint.Destination : NomadJourneyEndpoint.Origin));
            _context.ExecuteCommand(new SetFoundationSpeedCommand(4f));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            EditorApplication.update += Tick;
            Debug.Log(journey ? "[NomadWarmReview] WAITING：已设置旅程目标，等待居民实际到岗并行驶 15 米。" :
                "[NomadWarmReview] WAITING：等待真实持满罐行走，不改库存或任务进度。");
        }

        private static bool IsSupportedScene()
        {
            string path = SceneManager.GetActiveScene().path;
            return path == NomadWarmWorkshopArtPipeline.ScenePath || path == NomadResidentSamplePipeline.ScenePath ||
                path == NomadResidentCrewPipeline.ScenePath || path == NomadReferenceVehiclePipeline.ScenePath;
        }

        private static void Tick()
        {
            try { TickCore(); }
            catch (Exception error)
            {
                Cancel();
                WriteReport(new Report { status = "failed", error = error.ToString() });
                Debug.LogException(error);
            }
        }

        private static void TickCore()
        {
            if (!_waiting) return;
            if (!EditorApplication.isPlaying || _context == null || EditorApplication.timeSinceStartup >= _deadline)
            {
                Cancel();
                WriteReport(new Report { status = "cancelled-before-carry" });
                Debug.LogWarning("[NomadWarmReview] CANCELLED：未在时间内到达检查点，已撤销监听。");
                return;
            }
            if (_journeyReview)
            {
                TickJourney();
                return;
            }
            bool carrying = _read.WaterCanLocation.CurrentValue == FoundationWaterCanLocation.Resident &&
                _read.WaterCanWaterMilliliters.CurrentValue > 0 &&
                _read.Residents.Any(r => r.ResidentPhase.CurrentValue == FoundationResidentPhase.MovingToDrinkingStation);
            if (_settleFrames < 0)
            {
                if (!carrying) return;
                string carrierId = _read.WaterCanCarrierId.CurrentValue;
                Vector3 position = _read.Residents.Single(r => r.StableId == carrierId).ResidentLocalPosition.CurrentValue;
                if (_trackedCarrier != carrierId)
                {
                    _trackedCarrier = carrierId;
                    _carryStartPosition = position;
                    return;
                }
                if (Vector3.Distance(position,_carryStartPosition) < .4f) return;
                _context.ExecuteCommand(new SetFoundationPausedCommand(true));
                _context.ExecuteCommand(new SetFoundationSpeedCommand(_previousSpeed));
                _settleFrames = Time.frameCount + 3;
                return;
            }
            if (Time.frameCount < _settleFrames) return;
            Scene scene = _context.gameObject.scene;
            var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadFoundationWorldView>()).Single();
            Transform deck = view.transform.Find("Vehicle Deck Root");
            Transform can = deck.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Water Can 01 [physical carrier]");
            Transform resident = can.parent;
            Animator animator = resident.GetComponent<ResidentHumanoidPresentation>().Animator;
            Camera camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single();
            Vector3 focus = resident.position + Vector3.up*.98f;
            camera.orthographic = false;
            camera.transform.position = focus + resident.TransformDirection(new Vector3(2.65f,1.65f,3.15f));
            camera.transform.LookAt(focus);
            var report = new Report
            {
                status = "paused-at-real-carry",
                residentId = _read.WaterCanCarrierId.CurrentValue,
                waterMilliliters = _read.WaterCanWaterMilliliters.CurrentValue,
                residentPosition = resident.position,
                gripLocal = resident.Find("Right Hand Carry Anchor").localPosition,
                shoulderLocal = resident.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position),
                headLocal = resident.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Head).position),
                handGap = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.RightHand).position, can.GetComponent<FoundationCarriedContainerRig>().CarryPivot.position),
                palmGap = Vector3.Distance(animator.GetComponent<FoundationResidentCarryIK>().RightPalmContactPosition,
                    can.GetComponent<FoundationCarriedContainerRig>().RightPalm.position),
                palmAngleDegrees = Quaternion.Angle(animator.GetComponent<FoundationResidentCarryIK>().RightPalmRotation,
                    can.GetComponent<FoundationCarriedContainerRig>().RightPalm.rotation),
                renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>().Select(r =>
                    r.name + ": " + string.Join(",",r.sharedMaterials.Select(m => m.name)) + " center=" +
                    resident.InverseTransformPoint(r.bounds.center)).ToArray()
            };
            WriteReport(report);
            _waiting = false;
            EditorApplication.update -= Tick;
            _context = null;
            Debug.Log("[NomadWarmReview] READY：真实携行已暂停；可以捕获 Game View。");
        }

        private static void TickJourney()
        {
            if (_settleFrames < 0)
            {
                if (Math.Abs(_read.JourneyPositionMicrometers.CurrentValue-_journeyStart) < 15_000_000L) return;
                _context.ExecuteCommand(new SetFoundationPausedCommand(true));
                _context.ExecuteCommand(new SetFoundationSpeedCommand(_previousSpeed));
                _settleFrames = Time.frameCount+3;
                return;
            }
            if (Time.frameCount < _settleFrames) return;
            Scene scene = _context.gameObject.scene;
            var roots = scene.GetRootGameObjects();
            Camera camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>()).Single();
            var view = roots.SelectMany(r => r.GetComponentsInChildren<NomadFoundationWorldView>()).Single();
            Transform deck = view.transform.Find("Vehicle Deck Root");
            Vector3 focus = deck.position+Vector3.up*.15f;
            camera.orthographic = false;
            camera.transform.position = focus+new Vector3(9,15,11);
            camera.transform.LookAt(focus);
            WriteReport(new Report {
                status = "paused-after-real-departure",
                journeyMeters = _read.JourneyPositionMicrometers.CurrentValue/1_000_000d,
                residentId = string.Join(",", _read.Residents.Where(r => r.ResidentPhase.CurrentValue == FoundationResidentPhase.Driving).Select(r => r.StableId)),
                renderers = deck.GetComponentsInChildren<ParticleSystem>().Where(p => p.name == "履带扬尘")
                    .Select(p => p.name+": particles="+p.particleCount+", time="+p.time).ToArray()
            });
            _waiting = false;
            EditorApplication.update -= Tick;
            _context = null;
            Debug.Log("[NomadWarmReview] READY：真实启程已暂停，俯瞰镜头就绪。");
        }

        private static void Cancel()
        {
            EditorApplication.update -= Tick;
            try
            {
                if (_waiting && _context != null && EditorApplication.isPlaying)
                {
                    _context.ExecuteCommand(new SetFoundationSpeedCommand(_previousSpeed));
                    _context.ExecuteCommand(new SetFoundationPausedCommand(_previousPaused));
                }
            }
            finally { _waiting = false; _context = null; }
        }

        private static void WriteReport(Report report)
        {
            report.capturedUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory("Logs/AIValidation/nomad-warm-art");
            File.WriteAllText("Logs/AIValidation/nomad-warm-art/" + (_journeyReview ? "journey-review.json" : "carry-review.json"), JsonUtility.ToJson(report,true));
        }

        [Serializable] private sealed class Report
        {
            public string status;
            public string capturedUtc;
            public string error;
            public string residentId;
            public int waterMilliliters;
            public double journeyMeters;
            public Vector3 residentPosition;
            public Vector3 gripLocal;
            public Vector3 shoulderLocal;
            public Vector3 headLocal;
            public float handGap;
            public float palmGap, palmAngleDegrees;
            public string[] renderers;
        }
    }
}
