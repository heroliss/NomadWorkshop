using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// Humanoid 手部接触的表现 Adapter。必须挂在 Animator 所在对象，目标由世界 View 提供；
    /// 它不移动物品、不结算库存，目标为空时释放当前手部约束。
    /// </summary>
    public sealed class FoundationResidentCarryIK : MonoBehaviour
    {
        public const string GripLayerName = "Carry Fingers";
        private Animator _animator;
        private Transform _bodyFrame;
        private Transform _hand;
        private Transform _shoulder;
        private Transform _rightGrip;
        private FoundationCarriedContainerRig _container;
        private int _gripLayer = -1;
        private Quaternion _palmFrameInHand;
        private Quaternion _handInIkGoal;
        private bool _ikBasisKnown;
        private Vector3 _palmContactInHand;
        private float _armLength;
        private float _upperArmLength;
        private float _forearmLength;
        private float _rightSide;
        private float _contactWeight;
        private bool _alignPalm;
        private float _groundReach;
        private bool _groundFeetCaptured;
        private Vector3 _bodyOffsetAtSolve;
        private System.Func<Vector3> _applyBodySupport;
        private Vector3 _leftStandingFoot, _rightStandingFoot;
        private Vector3 _leftGroundFoot, _rightGroundFoot;
        private Quaternion _leftGroundRotation, _rightGroundRotation;

        /// <summary>最近一次 Animator IK 回调实际施加的右手约束权重，供表现诊断读取。</summary>
        public float RightHandContactWeight { get; private set; }
        /// <summary>按 Avatar 标定后随腕骨变换的实际掌心表面位置，不是 IK 目标或把手位置。</summary>
        public Vector3 RightPalmContactPosition => _hand.TransformPoint(_palmContactInHand);
        /// <summary>实际掌心接触坐标；+Y 背离掌面，+Z 指向手指，供接触朝向验收。</summary>
        public Quaternion RightPalmRotation => _hand.rotation * _palmFrameInHand;
        /// <summary>最近一次 IK Pass 是否找到了同时满足臂长与罐体净空的肘部候选。</summary>
        public bool RightElbowClearanceResolved { get; private set; }
        /// <summary>最近一次 IK Pass 的腕部目标距离 / 已标定臂长；大于 1 表示物品姿态不可达。</summary>
        public float RightArmReachRatio { get; private set; }
        /// <summary>初始化姿态标定的双骨总长，供核对 Avatar 缩放或运行时拉伸。</summary>
        public float RightArmLength => _armLength;
        /// <summary>本次求解所读到的肩部位置，供与 Animator 输出后的实际肩部比较。</summary>
        public Vector3 RightShoulderAtSolve { get; private set; }
        /// <summary>最近一次地面拾放 IK Pass 的足部支撑权重；普通携行和楼梯保持零。</summary>
        public float GroundFootContactWeight { get; private set; }
        /// <summary>地面拾放固定的左脚 IK 世界目标，只用于诊断，不能作为实际足骨位置。</summary>
        public Vector3 LeftGroundFootTarget { get; private set; }
        /// <summary>地面拾放固定的右脚 IK 世界目标，只用于诊断，不能作为实际足骨位置。</summary>
        public Vector3 RightGroundFootTarget { get; private set; }

        /// <summary>
        /// 按已初始化 Avatar 的手指根标定掌心；bodyFrame 是面朝玩法 +Z 的居民根，不是 FBX 内层。
        /// 可选 applyBodySupport 由同一居民的表现宿主提供，在本组件 IK Pass 内先施加脚部/骨盆支撑，
        /// 返回本次施加的世界位移。支撑端不得另行运行 OnAnimatorIK；地面拾放期间改由蹲身支撑独占。
        /// 回调与居民共同存活，不移动逻辑根；未提供时保留普通甲板行为。
        /// </summary>
        public void Configure(Animator animator, Transform bodyFrame, System.Func<Vector3> applyBodySupport = null)
        {
            _animator = animator;
            _bodyFrame = bodyFrame;
            _applyBodySupport = applyBodySupport;
            _ikBasisKnown = false;
            _gripLayer = animator.GetLayerIndex(GripLayerName);
            _hand = RequireBone(HumanBodyBones.RightHand);
            _shoulder = RequireBone(HumanBodyBones.RightUpperArm);
            Transform elbow = RequireBone(HumanBodyBones.RightLowerArm);
            Transform middle = RequireBone(HumanBodyBones.RightMiddleProximal);
            Transform index = RequireBone(HumanBodyBones.RightIndexProximal);
            Transform little = RequireBone(HumanBodyBones.RightLittleProximal);
            Vector3 fingers = (middle.position - _hand.position).normalized;
            Vector3 width = index.position - little.position;
            Vector3 palmNormal = Vector3.Cross(fingers, width).normalized;
            _palmFrameInHand = Quaternion.Inverse(_hand.rotation) * Quaternion.LookRotation(fingers, -palmNormal);
            _palmContactInHand = _hand.InverseTransformPoint(
                Vector3.Lerp(_hand.position, middle.position, .5f) + palmNormal * (width.magnitude * .24f));
            _upperArmLength = Vector3.Distance(_shoulder.position, elbow.position);
            _forearmLength = Vector3.Distance(elbow.position, _hand.position);
            _armLength = _upperArmLength + _forearmLength;
            _rightSide = Mathf.Sign(bodyFrame.InverseTransformPoint(_shoulder.position).x);
            _leftStandingFoot = bodyFrame.InverseTransformPoint(RequireBone(HumanBodyBones.LeftFoot).position);
            _rightStandingFoot = bodyFrame.InverseTransformPoint(RequireBone(HumanBodyBones.RightFoot).position);
        }

        /// <summary>通用物品暂保留腕部位置接缝，不把其锚点冒充水罐的掌心表面。</summary>
        public void SetRightGrip(Transform grip, float weight = 1f)
        {
            _container = null;
            SetGrip(grip, weight, false);
        }

        /// <summary>
        /// 容器掌心目标 +Y 是把手上表面的外法线，+Z 是手指朝向；避让盒由同一绑定提供。
        /// 壳体边界用于选择无穿入的肘部朝向，不改变臂长或物品姿态；容器为空会释放手部约束。
        /// </summary>
        public void SetContainerGrip(FoundationCarriedContainerRig container, float weight = 1f)
        {
            _container = container;
            SetGrip(container != null ? container.RightPalm : null, weight, true);
        }

        /// <summary>地面拾放的绝对蹲身进度；只调整 Humanoid 骨盆和双脚，不移动导航/身体胶囊。</summary>
        public void SetGroundReach(float amount) => _groundReach = Mathf.Clamp01(amount);

        private void SetGrip(Transform grip, float weight, bool alignPalm)
        {
            _rightGrip = grip;
            _contactWeight = grip != null ? Mathf.Clamp01(weight) : 0f;
            _alignPalm = alignPalm;
            if (_animator != null && _gripLayer >= 0) _animator.SetLayerWeight(_gripLayer, _contactWeight);
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (_animator == null) return;
            if (!_ikBasisKnown)
            {
                // Humanoid 的 IK Goal 使用 Avatar 手部坐标，不一定等于 FBX 腕骨坐标。
                // 在首次 IK Pass、尚未施加约束时测量一次固定偏置，避免逐帧追逐自身修正。
                _handInIkGoal = Quaternion.Inverse(_animator.GetIKRotation(AvatarIKGoal.RightHand)) * _hand.rotation;
                _ikBasisKnown = true;
            }
            ApplyGroundReach();
            if (_groundReach <= 0f && _applyBodySupport != null)
                _bodyOffsetAtSolve = _applyBodySupport();
            float weight = _rightGrip != null ? _contactWeight : 0f;
            RightHandContactWeight = weight;
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, _alignPalm ? weight : 0f);
            _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, _alignPalm ? weight : 0f);
            if (_rightGrip == null) return;
            if (_alignPalm)
            {
                Quaternion handRotation = _rightGrip.rotation * Quaternion.Inverse(_palmFrameInHand);
                // 让掌心表面接触把手，腕骨应留在表面之后，不能继续直接塞进把手中心。
                Vector3 offset = Vector3.Scale(_palmContactInHand, _hand.lossyScale);
                Vector3 wrist = _rightGrip.position - handRotation * offset;
                _animator.SetIKPosition(AvatarIKGoal.RightHand, wrist);
                _animator.SetIKRotation(AvatarIKGoal.RightHand, handRotation * Quaternion.Inverse(_handInIkGoal));
                // 倾倒时肘部绕到罐体前方，避免抬起的壳体扫进上臂；平常下垂携行仍保留自然后弯。
                float tilt = Mathf.InverseLerp(.995f, .94f, Vector3.Dot(_rightGrip.up, _bodyFrame.up));
                Vector3 preferredElbow =
                    _shoulder.position + _bodyOffsetAtSolve + _bodyFrame.right * (_rightSide * _armLength * .45f) -
                    _bodyFrame.up * (_armLength * .30f) + _bodyFrame.forward * Mathf.Lerp(-.06f, .32f, tilt);
                _animator.SetIKHintPosition(AvatarIKHint.RightElbow, ResolveCanElbow(wrist, preferredElbow));
            }
            else
                _animator.SetIKPosition(AvatarIKGoal.RightHand, _rightGrip.position);
        }

        private Vector3 ResolveCanElbow(Vector3 wrist, Vector3 preferred)
        {
            RightElbowClearanceResolved = false;
            if (_container == null) return preferred;
            // bodyPosition 的 IK 修正在本次求解后才写回骨骼 Transform，不能直接读取未修正的肩膀。
            Vector3 shoulder = _shoulder.position + _bodyOffsetAtSolve;
            RightShoulderAtSolve = shoulder;
            Vector3 delta = wrist - shoulder;
            float distance = delta.magnitude;
            RightArmReachRatio = distance / _armLength;
            if (distance < .001f || distance >= _armLength) return preferred;
            Vector3 axis = delta / distance;
            float along = (_upperArmLength * _upperArmLength - _forearmLength * _forearmLength +
                distance * distance) / (2f * distance);
            float radius = Mathf.Sqrt(Mathf.Max(0f, _upperArmLength * _upperArmLength - along * along));
            Vector3 center = shoulder + along * axis;
            Vector3 radial = Vector3.ProjectOnPlane(preferred - center, axis).normalized * radius;
            if (radial.sqrMagnitude < .000001f) return preferred;
            Vector3 At(float angle) => center + Quaternion.AngleAxis(angle, axis) * radial;
            bool Clear(float angle)
            {
                Vector3 elbow = At(angle);
                return _bodyFrame.InverseTransformPoint(elbow).x * _rightSide > .17f &&
                    SegmentClear(shoulder, elbow) && SegmentClear(elbow, wrist);
            }
            bool SegmentClear(Vector3 from, Vector3 to)
            {
                foreach (var volume in _container.ClearanceVolumes)
                {
                    Vector3 scale = volume.frame.lossyScale;
                    // 在 3 cm 最小手臂净空外再留 5 mm，吸收 Avatar 求解误差。
                    Bounds bounds = volume.bounds;
                    bounds.Expand(new Vector3(.07f / scale.x, .07f / scale.y, .07f / scale.z));
                    Vector3 a = volume.frame.InverseTransformPoint(from), b = volume.frame.InverseTransformPoint(to);
                    if (bounds.Contains(a) || (bounds.IntersectRay(new Ray(a, b - a), out float hit) &&
                        hit <= Vector3.Distance(a, b))) return false;
                }
                return true;
            }
            if (Clear(0f)) { RightElbowClearanceResolved = true; return At(0f); }
            // 肩和腕固定时，肘部只在双骨长度确定的圆上移动。寻找离自然弯曲最近的可行方向，
            // 然后二分边界，避免离散候选导致可见跳变；没有解时保留目标，让验收暴露不可达姿态。
            for (int step = 1; step <= 18; step++)
            for (int sign = 1; sign >= -1; sign -= 2)
            {
                float high = step * 10f;
                if (!Clear(high * sign)) continue;
                float low = high - 10f;
                for (int iteration = 0; iteration < 6; iteration++)
                {
                    float middle = (low + high) * .5f;
                    if (Clear(middle * sign)) high = middle;
                    else low = middle;
                }
                RightElbowClearanceResolved = true;
                return At(high * sign);
            }
            return preferred;
        }

        private void ApplyGroundReach()
        {
            _bodyOffsetAtSolve = Vector3.zero;
            GroundFootContactWeight = 0f;
            if (_groundReach <= 0f)
            {
                // 先释放上一段蹲身支撑；可选踏面支撑随后在同一 IK Pass 中接管脚部。
                if (_groundFeetCaptured)
                {
                    ApplyGroundFoot(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, 0f, Vector3.zero, Quaternion.identity, -1f);
                    ApplyGroundFoot(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, 0f, Vector3.zero, Quaternion.identity, 1f);
                    _groundFeetCaptured = false;
                }
                return;
            }
            if (!_groundFeetCaptured)
            {
                // 用 Avatar 初始化时的双脚站姿收脚，不把到岗前行走动作的任意跨步锁成蹲身支撑位。
                _leftGroundFoot = _leftStandingFoot;
                _rightGroundFoot = _rightStandingFoot;
                _leftGroundFoot.y = _animator.leftFeetBottomHeight;
                _rightGroundFoot.y = _animator.rightFeetBottomHeight;
                _leftGroundRotation = Quaternion.Inverse(_bodyFrame.rotation) * _animator.GetIKRotation(AvatarIKGoal.LeftFoot);
                _rightGroundRotation = Quaternion.Inverse(_bodyFrame.rotation) * _animator.GetIKRotation(AvatarIKGoal.RightFoot);
                _groundFeetCaptured = true;
            }
            Vector3 groundOffset = _container != null ? _container.GroundReachOffset : new Vector3(0f, -.55f, .20f);
            _bodyOffsetAtSolve = _bodyFrame.TransformVector(groundOffset) * _groundReach;
            GroundFootContactWeight = Mathf.SmoothStep(0f, 1f, _groundReach / .15f);
            if (_alignPalm && _rightGrip != null && _contactWeight > 0f)
            {
                Quaternion handRotation = _rightGrip.rotation * Quaternion.Inverse(_palmFrameInHand);
                Vector3 wrist = _rightGrip.position - handRotation * Vector3.Scale(_palmContactInHand, _hand.lossyScale);
                Vector3 reach = wrist - (_shoulder.position + _bodyOffsetAtSolve);
                // 容器偏移只给蹲姿基准。不同 Avatar 与动作帧的肩膀高度会变化，不能靠拉长手臂接触把手。
                // 在双脚固定的支撑阶段，骨盆向目标补足够取距离，留下少量肘部弯曲余量；物品与胶囊不移动。
                float excess = Mathf.Max(0f, reach.magnitude - _armLength * .97f);
                _bodyOffsetAtSolve += reach.normalized * (excess * GroundFootContactWeight * _contactWeight);
            }
            _animator.bodyPosition += _bodyOffsetAtSolve;
            LeftGroundFootTarget = _bodyFrame.TransformPoint(_leftGroundFoot);
            RightGroundFootTarget = _bodyFrame.TransformPoint(_rightGroundFoot);
            ApplyGroundFoot(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, GroundFootContactWeight,
                LeftGroundFootTarget, _bodyFrame.rotation * _leftGroundRotation, -1f);
            ApplyGroundFoot(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, GroundFootContactWeight,
                RightGroundFootTarget, _bodyFrame.rotation * _rightGroundRotation, 1f);
        }

        private void ApplyGroundFoot(AvatarIKGoal goal, AvatarIKHint knee, float weight,
            Vector3 position, Quaternion rotation, float side)
        {
            _animator.SetIKPositionWeight(goal, weight);
            _animator.SetIKRotationWeight(goal, weight);
            _animator.SetIKHintPositionWeight(knee, weight);
            if (weight <= 0f) return;
            _animator.SetIKPosition(goal, position);
            _animator.SetIKRotation(goal, rotation);
            _animator.SetIKHintPosition(knee, _bodyFrame.TransformPoint(new Vector3(side * .26f, .40f, .30f)));
        }

        private Transform RequireBone(HumanBodyBones bone)
        {
            Transform result = _animator.GetBoneTransform(bone);
            if (result == null) throw new MissingReferenceException("水罐掌心接触缺少 Humanoid 骨骼：" + bone);
            return result;
        }
    }
}
