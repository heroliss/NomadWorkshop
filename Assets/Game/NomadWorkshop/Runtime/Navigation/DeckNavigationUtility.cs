using System;
using System.Collections.Generic;
using Game.Framework.Utility;
using Game.NomadWorkshop.Simulation;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Game.NomadWorkshop.Navigation
{
    [Serializable]
    public struct NavigationAgentBinding
    {
        [SerializeField, Tooltip("业务层引用居民时使用的稳定 id。")]
        private string agentId;
        [SerializeField, Tooltip("执行 Unity NavMesh 查询与局部避障的居民 Agent。")]
        private NavMeshAgent agent;

        public NavigationAgentBinding(string agentId, NavMeshAgent agent)
        {
            this.agentId = agentId;
            this.agent = agent;
        }

        public string AgentId => agentId;
        public NavMeshAgent Agent => agent;
    }

    public readonly struct DeckNavPathProbe
    {
        public DeckNavPathProbe(
            Vector3 sampledStart,
            Vector3 sampledEnd,
            NavMeshPathStatus status,
            Vector3[] corners,
            float pathLength)
        {
            SampledStart = sampledStart;
            SampledEnd = sampledEnd;
            Status = status;
            Corners = corners ?? Array.Empty<Vector3>();
            PathLength = pathLength;
            DirectDistance = Vector3.Distance(sampledStart, sampledEnd);
        }

        public Vector3 SampledStart { get; }
        public Vector3 SampledEnd { get; }
        public NavMeshPathStatus Status { get; }
        public IReadOnlyList<Vector3> Corners { get; }
        public float PathLength { get; }
        public float DirectDistance { get; }
        public float LengthRatio => DirectDistance <= 0.0001f ? 1f : PathLength / DirectDistance;
        public bool IsComplete => Status == NavMeshPathStatus.PathComplete;
    }

    public readonly struct NavigationAgentSnapshot
    {
        public NavigationAgentSnapshot(
            Vector3 position,
            Vector3 velocity,
            float remainingDistance,
            bool pathPending,
            bool hasPath,
            bool isOnNavMesh,
            NavMeshPathStatus pathStatus)
        {
            Position = position;
            Velocity = velocity;
            RemainingDistance = remainingDistance;
            PathPending = pathPending;
            HasPath = hasPath;
            IsOnNavMesh = isOnNavMesh;
            PathStatus = pathStatus;
        }

        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public float RemainingDistance { get; }
        public bool PathPending { get; }
        public bool HasPath { get; }
        public bool IsOnNavMesh { get; }
        public NavMeshPathStatus PathStatus { get; }
    }

    /// <summary>
    /// 一份由导航 Utility 持有的运行期设施障碍代理。业务 System 决定何时提交或回滚，
    /// Handle 只提供立即停用与延迟销毁所需的窄所有权边界。
    /// </summary>
    public sealed class DeckNavigationObstacleHandle
    {
        internal DeckNavigationObstacleHandle(string instanceId, GameObject root)
        {
            InstanceId = instanceId;
            Root = root;
        }

        internal GameObject Root { get; }
        public string InstanceId { get; }
        public bool IsActive => Root != null && Root.activeSelf;
    }

    /// <summary>
    /// 车辆甲板连续导航的 Unity Adapter。它封装 NavMesh 构建、路径查询与 Agent 状态，
    /// 不决定居民任务、设施容量、库存转移或交互位归属。
    /// </summary>
    public sealed class DeckNavigationUtility : MonoUtilityBase
    {
        [SerializeField, Tooltip("甲板连续可行走区域的 NavMeshSurface；设施提交时会重建它。")]
        private NavMeshSurface surface;
        [SerializeField, Tooltip("业务居民 id 到 NavMeshAgent 的显式绑定；避免 System 直接查找场景对象。")]
        private NavigationAgentBinding[] agentBindings =
            Array.Empty<NavigationAgentBinding>();
        [SerializeField, Min(0.05f), Tooltip("通用移动请求允许在目标附近搜索 NavMesh 的最大距离（米）；设施精确停靠另用更小容差。")]
        private float sampleDistance = 1f;
        [SerializeField, Tooltip("DeckPose 与世界坐标转换所依附的甲板 Transform，通常就是 NavMeshSurface 根节点。")]
        private Transform navigationSpace;
        [SerializeField, Tooltip("运行时设施导航障碍的统一父节点，便于提交、回滚和调试查看。")]
        private Transform runtimeObstacleRoot;
        [SerializeField, Min(0.5f), Tooltip("运行时设施障碍盒的高度（米）；只需足以切断甲板 NavMesh。")]
        private float runtimeObstacleHeight = 2.4f;

        private readonly Dictionary<string, NavMeshAgent> _agents =
            new(StringComparer.Ordinal);

        public bool IsBuilt => surface != null && surface.navMeshData != null;
        /// <summary>当前 NavMeshSurface 所用 Agent 类型的烘焙半径；实时预览必须复用它而非另写近似值。</summary>
        public float NavigationAgentRadiusMeters
        {
            get
            {
                ValidateConfiguration();
                NavMeshBuildSettings settings = NavMesh.GetSettingsByID(surface.agentTypeID);
                if (settings.agentRadius <= 0f)
                    throw new InvalidOperationException("NavMesh Agent 类型没有有效半径。 ");
                return settings.agentRadius;
            }
        }

        /// <summary>当前烘焙的有效体素尺寸；未覆写时采用 Unity 推荐的 Agent 半径三分之一。</summary>
        public float EffectiveVoxelSizeMeters => surface != null && surface.overrideVoxelSize
            ? Mathf.Max(0.001f, surface.voxelSize)
            : NavigationAgentRadiusMeters / 3f;

        protected override void Awake()
        {
            base.Awake();
            RebuildAgentCatalog();
        }

        public void ConfigureRuntime(
            NavMeshSurface configuredSurface,
            NavigationAgentBinding[] configuredAgents,
            float configuredSampleDistance = 1f,
            Transform configuredNavigationSpace = null,
            Transform configuredRuntimeObstacleRoot = null,
            float configuredObstacleHeight = 2.4f)
        {
            surface = configuredSurface != null
                ? configuredSurface
                : throw new ArgumentNullException(nameof(configuredSurface));
            agentBindings = configuredAgents != null
                ? (NavigationAgentBinding[])configuredAgents.Clone()
                : throw new ArgumentNullException(nameof(configuredAgents));
            sampleDistance = Mathf.Max(0.05f, configuredSampleDistance);
            navigationSpace = configuredNavigationSpace != null
                ? configuredNavigationSpace
                : configuredSurface.transform;
            runtimeObstacleRoot = configuredRuntimeObstacleRoot;
            runtimeObstacleHeight = Mathf.Max(0.5f, configuredObstacleHeight);
            RebuildAgentCatalog();
        }

        /// <summary>同步建立初始小型甲板 NavMesh；设施建造确认使用 <see cref="BeginUpdate"/>。</summary>
        public void BuildNow()
        {
            ValidateConfiguration();
            surface.BuildNavMesh();
            if (surface.navMeshData == null)
                throw new InvalidOperationException("NavMeshSurface 没有生成可用的 NavMeshData。");
        }

        /// <summary>
        /// 按当前启用的 Collider / Modifier 启动一次增量更新。调用方必须保留返回的操作，
        /// 在完成后验证业务可达性；失败时先停用候选障碍，再启动第二次更新完成回滚。
        /// </summary>
        public AsyncOperation BeginUpdate()
        {
            ValidateConfiguration();
            if (!IsBuilt)
                throw new InvalidOperationException("初始 NavMesh 尚未建立，不能执行增量更新。");
            return surface.UpdateNavMesh(surface.navMeshData);
        }

        /// <summary>
        /// 在车辆局部坐标中查询完整路径；返回 Probe 的端点与拐角也全部位于同一局部空间。
        /// </summary>
        public bool TryCalculateCompleteLocalPath(
            Vector3 localStart,
            Vector3 localEnd,
            out DeckNavPathProbe probe)
        {
            Transform space = NavigationSpace;
            if (!TryCalculateCompletePath(
                    space.TransformPoint(localStart),
                    space.TransformPoint(localEnd),
                    out DeckNavPathProbe worldProbe))
            {
                probe = default;
                return false;
            }

            var localCorners = new Vector3[worldProbe.Corners.Count];
            for (var i = 0; i < localCorners.Length; i++)
                localCorners[i] = space.InverseTransformPoint(worldProbe.Corners[i]);
            probe = new DeckNavPathProbe(
                space.InverseTransformPoint(worldProbe.SampledStart),
                space.InverseTransformPoint(worldProbe.SampledEnd),
                worldProbe.Status,
                localCorners,
                CalculateLength(localCorners));
            return true;
        }

        /// <summary>
        /// 把导航空间中的局部点解析到最近 NavMesh 点。用于设施落地后让动态居民从新障碍中避让；
        /// 调用方负责决定允许的最大位移，Utility 不擅自改写业务位置。
        /// </summary>
        public bool TrySampleLocalPosition(
            Vector3 localPosition,
            float maximumDistance,
            out Vector3 sampledLocalPosition)
        {
            if (maximumDistance <= 0f)
                throw new ArgumentOutOfRangeException(nameof(maximumDistance));
            Transform space = NavigationSpace;
            if (!IsBuilt || !NavMesh.SamplePosition(
                    space.TransformPoint(localPosition),
                    out NavMeshHit hit,
                    maximumDistance,
                    NavMesh.AllAreas))
            {
                sampledLocalPosition = default;
                return false;
            }

            sampledLocalPosition = space.InverseTransformPoint(hit.position);
            return true;
        }

        /// <summary>
        /// 从量化设施姿态与简化 Footprint 创建不可见的 NavMesh 构建输入；它在提交前也可以作为候选障碍。
        /// </summary>
        public DeckNavigationObstacleHandle CreateFacilityObstacle(
            string instanceId,
            in DeckPose pose,
            ContinuousFacilityFootprint footprint)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("导航障碍必须提供稳定设施 id。", nameof(instanceId));
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            if (pose.DeckLevel != 0)
                throw new NotSupportedException("当前 Foundation 导航 Adapter 只支持主甲板层 0。");
            if (runtimeObstacleRoot == null)
                throw new InvalidOperationException("DeckNavigationUtility 缺少 Runtime Obstacle Root。");
            int notWalkableArea = NavMesh.GetAreaFromName("Not Walkable");
            if (notWalkableArea < 0)
                throw new InvalidOperationException("项目缺少 Unity 内置的 Not Walkable NavMesh Area。");

            var root = new GameObject($"Nav Obstacle · {instanceId}");
            root.transform.SetParent(runtimeObstacleRoot, false);
            root.transform.localPosition = new Vector3(
                pose.XMillimeters / 1000f,
                0f,
                pose.ZMillimeters / 1000f);
            root.transform.localRotation = Quaternion.Euler(0f, (float)pose.YawDegrees, 0f);

            var modifier = root.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = notWalkableArea;
            modifier.applyToChildren = true;

            for (var i = 0; i < footprint.Parts.Count; i++)
            {
                DeckFootprintPart part = footprint.Parts[i];
                var partObject = new GameObject($"Part {i + 1:D2}");
                partObject.transform.SetParent(root.transform, false);
                partObject.transform.localPosition = new Vector3(
                    part.LocalCenterXMillimeters / 1000f,
                    runtimeObstacleHeight * 0.5f,
                    part.LocalCenterZMillimeters / 1000f);
                partObject.transform.localRotation = Quaternion.Euler(
                    0f,
                    part.LocalYawDeciDegrees / 10f,
                    0f);
                BoxCollider collider = partObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(
                    part.WidthMillimeters / 1000f,
                    runtimeObstacleHeight,
                    part.DepthMillimeters / 1000f);
            }

            return new DeckNavigationObstacleHandle(instanceId.Trim(), root);
        }

        /// <summary>立即从下一次 NavMesh 收集中排除障碍；GameObject 在帧末销毁。</summary>
        public void DeactivateAndDestroyObstacle(DeckNavigationObstacleHandle handle)
        {
            if (handle?.Root == null) return;
            handle.Root.SetActive(false);
            if (Application.isPlaying) Destroy(handle.Root);
            else DestroyImmediate(handle.Root);
        }

        public bool TryCalculateCompletePath(
            Vector3 worldStart,
            Vector3 worldEnd,
            out DeckNavPathProbe probe)
        {
            if (!TrySample(worldStart, out NavMeshHit startHit) ||
                !TrySample(worldEnd, out NavMeshHit endHit))
            {
                probe = default;
                return false;
            }

            var path = new NavMeshPath();
            bool calculated = NavMesh.CalculatePath(
                startHit.position,
                endHit.position,
                NavMesh.AllAreas,
                path);
            Vector3[] corners = path.corners ?? Array.Empty<Vector3>();
            float length = 0f;
            for (var i = 1; i < corners.Length; i++)
                length += Vector3.Distance(corners[i - 1], corners[i]);

            probe = new DeckNavPathProbe(
                startHit.position,
                endHit.position,
                path.status,
                corners,
                length);
            return calculated && probe.IsComplete;
        }

        public bool TryWarp(string agentId, Vector3 worldPosition)
        {
            if (!TryGetAgent(agentId, out NavMeshAgent agent) ||
                !TrySample(worldPosition, out NavMeshHit hit))
                return false;

            if (!agent.enabled) agent.enabled = true;
            agent.isStopped = true;
            agent.updateRotation = true;
            bool warped = agent.Warp(hit.position);
            if (warped) agent.ResetPath();
            return warped;
        }

        public bool TrySetDestination(string agentId, Vector3 worldDestination)
        {
            if (!TryGetAgent(agentId, out NavMeshAgent agent) ||
                !TrySample(worldDestination, out NavMeshHit hit))
                return false;
            if (!agent.isOnNavMesh) return false;

            agent.updateRotation = true;
            agent.isStopped = false;
            return agent.SetDestination(hit.position);
        }

        public bool TryGetSnapshot(string agentId, out NavigationAgentSnapshot snapshot)
        {
            if (!TryGetAgent(agentId, out NavMeshAgent agent))
            {
                snapshot = default;
                return false;
            }

            snapshot = new NavigationAgentSnapshot(
                agent.transform.position,
                agent.velocity,
                agent.isOnNavMesh ? agent.remainingDistance : float.PositiveInfinity,
                agent.isOnNavMesh && agent.pathPending,
                agent.isOnNavMesh && agent.hasPath,
                agent.isOnNavMesh,
                agent.isOnNavMesh ? agent.pathStatus : NavMeshPathStatus.PathInvalid);
            return true;
        }

        public bool IsArrived(string agentId, float tolerance)
        {
            if (!TryGetAgent(agentId, out NavMeshAgent agent) || !agent.isOnNavMesh || agent.pathPending)
                return false;
            if (agent.pathStatus != NavMeshPathStatus.PathComplete)
                return false;

            float allowed = Mathf.Max(agent.stoppingDistance, Mathf.Max(0.01f, tolerance));
            return agent.remainingDistance <= allowed &&
                   (!agent.hasPath || agent.velocity.sqrMagnitude <= 0.04f);
        }

        public void Stop(string agentId)
        {
            if (!TryGetAgent(agentId, out NavMeshAgent agent) || !agent.isOnNavMesh) return;
            agent.isStopped = true;
            agent.ResetPath();
        }

        /// <summary>在 NavMesh 接近完成后做很短的精确贴靠；业务 System 决定何时进入此阶段。</summary>
        public bool AdvanceDocking(
            string agentId,
            Vector3 targetPosition,
            Quaternion targetRotation,
            float positionSpeed,
            float angularSpeed,
            float deltaTime,
            float positionTolerance,
            float angleTolerance)
        {
            if (!TryGetAgent(agentId, out NavMeshAgent agent) || !agent.isOnNavMesh)
                return false;

            agent.isStopped = true;
            agent.ResetPath();
            agent.updateRotation = false;

            Vector3 current = agent.transform.position;
            Vector3 target = new(targetPosition.x, current.y, targetPosition.z);
            Vector3 next = Vector3.MoveTowards(
                current,
                target,
                Mathf.Max(0.01f, positionSpeed) * deltaTime);
            if (!agent.Warp(next)) return false;

            agent.transform.rotation = Quaternion.RotateTowards(
                agent.transform.rotation,
                targetRotation,
                Mathf.Max(1f, angularSpeed) * deltaTime);

            return Vector3.Distance(next, target) <= Mathf.Max(0.005f, positionTolerance) &&
                   Quaternion.Angle(agent.transform.rotation, targetRotation) <=
                   Mathf.Max(0.1f, angleTolerance);
        }

        public void ResumeAutomaticRotation(string agentId)
        {
            if (TryGetAgent(agentId, out NavMeshAgent agent)) agent.updateRotation = true;
        }

        public Vector3[] GetCurrentPathCorners(string agentId)
        {
            if (!TryGetAgent(agentId, out NavMeshAgent agent) ||
                !agent.isOnNavMesh || !agent.hasPath)
                return Array.Empty<Vector3>();
            return agent.path.corners ?? Array.Empty<Vector3>();
        }

        private bool TrySample(Vector3 worldPosition, out NavMeshHit hit)
        {
            if (!IsBuilt)
            {
                hit = default;
                return false;
            }

            return NavMesh.SamplePosition(
                worldPosition,
                out hit,
                sampleDistance,
                NavMesh.AllAreas);
        }

        private bool TryGetAgent(string agentId, out NavMeshAgent agent)
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                agent = null;
                return false;
            }
            return _agents.TryGetValue(agentId, out agent) && agent != null;
        }

        private void RebuildAgentCatalog()
        {
            _agents.Clear();
            if (agentBindings == null) return;
            for (var i = 0; i < agentBindings.Length; i++)
            {
                NavigationAgentBinding binding = agentBindings[i];
                if (string.IsNullOrWhiteSpace(binding.AgentId) || binding.Agent == null) continue;
                _agents[binding.AgentId.Trim()] = binding.Agent;
            }
        }

        private void ValidateConfiguration()
        {
            if (surface == null) throw new InvalidOperationException("DeckNavigationUtility 缺少 NavMeshSurface。");
            if (navigationSpace == null) navigationSpace = surface.transform;
            RebuildAgentCatalog();
            if (agentBindings != null && _agents.Count != agentBindings.Length)
                throw new InvalidOperationException("居民 Agent id 为空、重复，或存在缺失引用。");
        }

        private Transform NavigationSpace => navigationSpace != null
            ? navigationSpace
            : surface != null
                ? surface.transform
                : throw new InvalidOperationException("DeckNavigationUtility 缺少 Navigation Space。");

        private static float CalculateLength(IReadOnlyList<Vector3> corners)
        {
            float length = 0f;
            for (var i = 1; i < corners.Count; i++)
                length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }
    }
}
