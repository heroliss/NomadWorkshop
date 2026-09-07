using System;
using System.IO;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using R3;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>从真实移动中捕获楼梯中段姿态；超时或退出 Play 立即撤销观察，不手设角色位置。</summary>
    [InitializeOnLoad]
    public static class NomadStairTraversalReview
    {
        private static NomadFoundationContext _context;
        private static ReadOnlyReactiveProperty<StairTraversalState> _read;
        private static double _deadline;
        private static int _settle;
        private static bool _waiting;

        static NomadStairTraversalReview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Detach;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) Detach();
            };
        }

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/观察楼梯持桶近景")]
        public static void Observe()
        {
            ValidateScene();
            if (_waiting) throw new InvalidOperationException("已经在等待楼梯中段。");
            _context = Find<NomadFoundationContext>();
            _read = _context.ExecuteCommand(new GetStairTraversalStateCommand());
            _context.ExecuteCommand(new PauseStairTraversalCommand(false));
            if (!_context.ExecuteCommand(new MoveStairTraversalCommand(1))) throw new InvalidOperationException("上楼路线无效。");
            _waiting = true; _settle = 0; _deadline = EditorApplication.timeSinceStartup + 45;
            EditorApplication.update += Tick;
        }

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/记录楼梯运行状态")]
        public static void Record()
        {
            ValidateScene();
            NomadStairTraversalView view = Find<NomadStairTraversalView>();
            StairTraversalState state = Find<NomadFoundationContext>().ExecuteCommand(new GetStairTraversalStateCommand()).CurrentValue;
            NavMeshAgent agent = Find<NavMeshAgent>();
            Transform handle = view.Container.CarryPivot;
            Animator animator = view.Resident.Animator;
            var contact = animator.GetComponent<FoundationResidentCarryIK>();
            Transform palmTarget = view.Container.RightPalm;
            Vector3 shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
            Vector3 elbow = animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position;
            var report = new Report
            {
                scenePath = SceneManager.GetActiveScene().path, capturedUtc = DateTime.UtcNow.ToString("O"),
                phase = state.Phase.ToString(), paused = state.Paused, body = state.Position,
                targetFloor = state.TargetFloor, waterMilliliters = state.WaterMilliliters,
                canInstanceId = view.WaterCan.GetInstanceID(), rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand).position,
                handle = handle.position, leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position,
                rightPalm = contact.RightPalmContactPosition, palmTarget = palmTarget.position,
                palmAngleDegrees = Quaternion.Angle(contact.RightPalmRotation, palmTarget.rotation),
                rightShoulder = shoulder, rightElbow = elbow, rightShoulderAtSolve = contact.RightShoulderAtSolve,
                shoulderSolveErrorMeters = Vector3.Distance(shoulder, contact.RightShoulderAtSolve),
                elbowDropMeters = Vector3.Dot(shoulder - elbow, view.Resident.transform.up),
                pelvisOffsetMeters = view.FootIK.PelvisOffset, rightArmReachRatio = contact.RightArmReachRatio,
                rightElbowClearanceResolved = contact.RightElbowClearanceResolved,
                rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot).position,
                leftTarget = view.FootIK.LeftTarget, rightTarget = view.FootIK.RightTarget,
                leftWeight = view.FootIK.LeftContactWeight, rightWeight = view.FootIK.RightContactWeight,
                pathPending = agent.pathPending, remainingDistance = agent.remainingDistance,
                nextPosition = agent.nextPosition, desiredVelocity = agent.desiredVelocity,
                bodyStepHeight = agent.GetComponent<CharacterController>().stepOffset,
                navigationStepHeight = NavMesh.GetSettingsByID(agent.agentTypeID).agentClimb,
            };
            Directory.CreateDirectory("Logs/AIValidation/nomad-warm-art");
            File.WriteAllText("Logs/AIValidation/nomad-warm-art/stair-review.json", JsonUtility.ToJson(report, true));
            Debug.Log($"[NomadStairReview] {report.phase} y={state.Position.y:F3} m，握点误差=" +
                $"{Vector3.Distance(report.rightPalm, report.palmTarget):F4} m，肩部求解误差={report.shoulderSolveErrorMeters:F4} m，" +
                $"肘部低于肩膀={report.elbowDropMeters:F3} m，身体/导航跨高={report.bodyStepHeight:F2}/{report.navigationStepHeight:F2} m。");
        }

        private static void Tick()
        {
            if (!_waiting || !EditorApplication.isPlaying) { Detach(); return; }
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline)
                {
                    Record();
                    throw new TimeoutException("未能从真实移动到达楼梯中段。");
                }
                StairTraversalState state = _read.CurrentValue;
                if (_settle == 0)
                {
                    if (state.Position.y < 1.4f) return;
                    _context.ExecuteCommand(new PauseStairTraversalCommand(true));
                    _settle = 1;
                    return;
                }
                if (++_settle < 4) return;
                NomadStairTraversalView view = Find<NomadStairTraversalView>();
                Camera camera = Find<Camera>();
                Vector3 target = view.Resident.transform.position + Vector3.up * .65f;
                camera.transform.position = target + new Vector3(4f,2.6f,-3.5f);
                camera.transform.LookAt(target);
                camera.orthographicSize = 2.8f;
                Record();
                Detach();
            }
            catch (Exception ex) { Detach(); Debug.LogException(ex); }
        }

        private static void ValidateScene()
        {
            string path = SceneManager.GetActiveScene().path;
            if (!EditorApplication.isPlaying || (path != NomadStairTraversalPipeline.ScenePath &&
                path != NomadResidentSamplePipeline.StairScenePath && !NomadResidentCrewPipeline.IsStairScene(path) &&
                !NomadWorkwearPipeline.IsStairScene(path)))
                throw new InvalidOperationException("请先运行持桶楼梯实验场景。");
        }

        private static void Detach()
        {
            EditorApplication.update -= Tick; _waiting = false; _context = null; _read = null;
        }

        private static T Find<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<T>()).Single();

        [Serializable]
        private sealed class Report
        {
            public string phase, scenePath, capturedUtc;
            public bool paused, pathPending;
            public Vector3 body, rightHand, handle, leftFoot, rightFoot, leftTarget, rightTarget, nextPosition, desiredVelocity;
            public Vector3 rightPalm, palmTarget;
            public Vector3 rightShoulder, rightElbow, rightShoulderAtSolve;
            public float shoulderSolveErrorMeters, elbowDropMeters, pelvisOffsetMeters, rightArmReachRatio;
            public bool rightElbowClearanceResolved;
            public float palmAngleDegrees;
            public int targetFloor, waterMilliliters, canInstanceId;
            public float leftWeight, rightWeight, remainingDistance, bodyStepHeight, navigationStepHeight;
        }
    }
}
