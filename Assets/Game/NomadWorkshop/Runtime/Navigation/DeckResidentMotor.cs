using System;
using UnityEngine;
using UnityEngine.AI;

namespace Game.NomadWorkshop.Navigation
{
    /// <summary>
    /// 一个居民拥有的原生移动实例。仅封装 Unity 物理移动，不持有任务、库存或 Model。
    /// 主线程创建/释放；Stop 撤销目的地，Suspend 保留意图并立即冻结，Dispose 先停用再销毁。
    /// 位置使用甲板局部坐标，路径走廊和避让由 PlayerLoop 推进，不能同步调用“步进”。
    /// </summary>
    internal sealed class DeckResidentMotor : IDisposable
    {
        // 路径接纳留出约 15 mm 的导航停车误差；实际身体末段仍受更外层的 110 mm 上限约束。
        internal const float MaximumDockingSampleDistance = .09f;
        private const float MaximumDockingMovement = .11f;
        private readonly Transform _space;
        private readonly GameObject _root;
        private readonly NavMeshAgent _agent;
        private readonly CharacterController _body;
        private readonly float _stepHeight;
        private Vector3 _worldDestination;
        private bool _hasDestination;
        private bool _running;

        internal DeckResidentMotor(Transform space, string id, int agentTypeId,
            float radius, float height, Vector3 localPosition, float yaw, float stepHeight = 0f,
            float bodyRadius = 0f)
        {
            _space = space;
            _stepHeight = Mathf.Clamp(stepHeight, 0f, height * .25f);
            _root = new GameObject($"Resident Motor · {id}");
            _root.SetActive(false);
            _root.transform.SetParent(space, false);
            _root.transform.localPosition = localPosition;
            _root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            _agent = _root.AddComponent<NavMeshAgent>();
            _agent.agentTypeID = agentTypeId;
            _agent.radius = radius;
            _agent.height = height;
            _agent.baseOffset = 0f;
            _agent.stoppingDistance = 0.015f;
            _agent.autoBraking = true;
            _agent.autoRepath = true;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            _agent.avoidancePriority = 50;
            // 局部避让不保证绝不重叠。由 CharacterController 独占最终 Transform 位移，
            // Agent 仍负责路径/避让；同物体的 Agent 使动态身体被 NavMeshSurface.ignoreNavMeshAgent 排除。
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _body = _root.AddComponent<CharacterController>();
            // 导航净空可包含手提物；胶囊体仍按躯干宽度跨踏面，过宽的球形底部会提前顶高身体。
            _body.radius = bodyRadius > 0f ? Mathf.Min(bodyRadius, radius) : radius;
            _body.height = height;
            _body.center = Vector3.up * (height * 0.5f);
            _body.skinWidth = Mathf.Max(0.01f, _body.radius * 0.1f);
            _body.minMoveDistance = 0f;
            _body.stepOffset = _stepHeight;
            _body.slopeLimit = _stepHeight > 0f ? 55f : 90f;
            _root.SetActive(true);
            if (!Warp(localPosition, yaw))
            {
                Dispose();
                throw new InvalidOperationException($"居民 {id} 的原生 Agent 无法绑定到甲板 NavMesh。");
            }
        }

        internal Vector3 LocalPosition => _root != null ? _space.InverseTransformPoint(_root.transform.position) : default;
        internal float LocalYaw => _root != null ? _root.transform.localEulerAngles.y : 0f;
        internal bool IsOnNavMesh => _agent != null && _agent.enabled && _agent.isOnNavMesh;
        internal bool PathPending => IsOnNavMesh && _agent.pathPending;
        internal bool PathComplete => IsOnNavMesh && !PathPending && _agent.pathStatus == NavMeshPathStatus.PathComplete;
        internal float RemainingDistance => IsOnNavMesh && !PathPending ? _agent.remainingDistance : float.PositiveInfinity;

        /// <summary>用于初始化、恢复/建造迁移、显式快进后同步；普通移动和末段停靠均经身体约束。</summary>
        internal bool Warp(Vector3 localPosition, float yaw)
        {
            if (_agent == null || _root == null || _space == null) return false;
            _body.enabled = false;
            _root.transform.position = _space.TransformPoint(localPosition);
            if (!_agent.enabled) _agent.enabled = true;
            bool warped = _agent.Warp(_root.transform.position);
            _body.enabled = true;
            if (!warped) return false;
            Stop();
            _root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return true;
        }

        internal bool SetDestination(Vector3 sampledLocalGoal)
        {
            if (!IsOnNavMesh) return false;
            _worldDestination = _space.TransformPoint(sampledLocalGoal);
            _hasDestination = _agent.SetDestination(_worldDestination);
            return _hasDestination;
        }

        /// <summary>业务倍率映射到速度与加速度；暂停不删路径，恢复后仍沿同一个意图继续。</summary>
        internal void ConfigureMotion(float baseSpeed, float multiplier, bool run)
        {
            if (!IsOnNavMesh) return;
            // 业务时钟使用 unscaledDeltaTime，补偿外层 Unity timeScale；0 时原生 PlayerLoop 本就不推进。
            float scale = multiplier / Mathf.Max(0.0001f, Time.timeScale);
            _agent.speed = Mathf.Max(0.01f, baseSpeed * scale);
            _agent.acceleration = 18f * scale * scale;
            _agent.angularSpeed = 720f * scale;
            bool moving = run && _hasDestination;
            if (moving && !_running)
            {
                ReanchorSimulation();
                moving = _hasDestination;
            }
            _running = moving;
            // 行走者必须互相看见：高优先级会忽略低优先级，不能用居民编号分配路权。
            _agent.avoidancePriority = moving ? 50 : 0;
            _agent.isStopped = !moving;
            if (!moving) HoldPosition();
        }

        /// <summary>
        /// 每个真实帧消费一次 Agent 模拟位移，通过身体碰撞提交脚底，再把受约束位置反馈给寻路。
        /// System 在业务步之前调用；未提交的模拟位置不能作为到达、库存交接或驾驶到岗证据。
        /// </summary>
        internal void CommitPhysicalMovement()
        {
            if (!IsOnNavMesh || !_running || !_body.enabled || _agent.pathPending) return;
            // Agent 到达模拟终点会清掉 hasPath，但受速度/碰撞约束的身体可能仍在后方；
            // 仍需消费最后的 nextPosition，不能把“没有剩余路径”当成“不再提交身体位移”。
            Vector3 wanted = _agent.nextPosition;
            Vector3 delta = wanted - _root.transform.position;
            if (_stepHeight > 0f) delta.y = 0f;
            Vector3 motion = Vector3.ClampMagnitude(delta, _agent.speed * Time.deltaTime);
            // 台阶模式由胶囊体跨真实踏面，NavMesh 的平滑高度只用于路线，不能把身体悬在踏面上方。
            // 小幅持续下压让下楼时及时接地；普通单层模式保留既有移动契约。
            if (_stepHeight > 0f) motion.y = -2f * Time.deltaTime;
            _body.Move(motion);
            Vector3 direction = _agent.velocity;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                _root.transform.rotation = Quaternion.RotateTowards(_root.transform.rotation,
                    Quaternion.LookRotation(direction), _agent.angularSpeed * Time.deltaTime);
            // nextPosition 赋值不能确保模拟点立即贴回脚底；碰撞限制移动时显式重新锚定，
            // 只重算同一个目的地，不移动身体、不重领业务资源。
            Vector3 discrepancy = _root.transform.position - wanted;
            discrepancy.y = 0f;
            if (discrepancy.sqrMagnitude > 0.000004f) ReanchorSimulation();
        }

        private void ReanchorSimulation()
        {
            bool hadDestination = _hasDestination;
            if (!_agent.Warp(_root.transform.position)) return;
            if (hadDestination) _hasDestination = _agent.SetDestination(_worldDestination);
        }

        internal void Suspend()
        {
            if (!IsOnNavMesh) return;
            _agent.isStopped = true;
            _running = false;
            HoldPosition();
        }

        internal void Stop()
        {
            _hasDestination = false;
            _running = false;
            if (!IsOnNavMesh) return;
            _agent.isStopped = true;
            _agent.updateRotation = false;
            HoldPosition();
            _agent.ResetPath();
        }

        private void HoldPosition()
        {
            _agent.avoidancePriority = 0;
            _agent.nextPosition = _root.transform.position;
            _agent.velocity = Vector3.zero;
        }

        /// <summary>
        /// 只有走廊已经抵达的最后几厘米允许精确停靠。业务层先确认目标工作位与居民净空，
        /// 成功前不能转移库存或完成动作；大距离 Warp 会被拒绝。
        /// </summary>
        internal bool TryFinishDocking(Vector3 exactLocalGoal, bool hasYaw, float yaw)
        {
            Vector3 difference = exactLocalGoal - LocalPosition;
            if (!PathComplete || RemainingDistance > 0.055f || difference.magnitude > MaximumDockingMovement) return false;
            float finalYaw = hasYaw ? yaw : LocalYaw;
            _body.Move(_space.TransformPoint(exactLocalGoal) - _root.transform.position);
            difference = exactLocalGoal - LocalPosition;
            float tolerance = _stepHeight > 0f ? .05f : .02f;
            if (difference.sqrMagnitude > tolerance * tolerance) return false;
            _root.transform.localRotation = Quaternion.Euler(0f, finalYaw, 0f);
            Stop();
            return true;
        }

        public void Dispose()
        {
            if (_root == null) return;
            Stop();
            _root.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
            else UnityEngine.Object.DestroyImmediate(_root);
        }
    }
}
