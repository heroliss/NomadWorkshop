using Game.NomadWorkshop.Simulation;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 车辆主甲板的共享布局参数。整数索引只定义可视网格范围；设施、居民与存档均使用连续 DeckPose。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NW_DeckLayout",
        menuName = "SSFramework/游牧工坊/甲板布局定义")]
    public sealed class DeckLayoutDefinition : ScriptableObject
    {
        [Header("甲板尺寸与可视网格")]
        [SerializeField, Tooltip("可视网格左下角的 X 索引；它只定义甲板边界与辅助线，不限制设施必须落在格点。")]
        private int minX = -4;
        [SerializeField, Tooltip("可视网格左下角的 Z 索引；它只定义甲板边界与辅助线，不参与寻路。")]
        private int minZ = -3;
        [SerializeField, Min(1), Tooltip("甲板沿 X 方向的可视单元数。")]
        private int width = 9;
        [SerializeField, Min(1), Tooltip("甲板沿 Z 方向的可视单元数。")]
        private int depth = 7;
        [SerializeField, Min(0.1f), Tooltip("每个可视单元的世界尺寸（米）；甲板总尺寸由单元数乘以此值。")]
        private float cellSize = 1.2f;

        [Header("默认建造辅助")]
        [SerializeField, Min(0), Tooltip("进入场景时的位置吸附步长（毫米）。正式 UI 当前提供 200/300/400/500 四档。")]
        private int positionSnapMillimeters = 200;
        [SerializeField, Range(0, 3600), Tooltip("进入场景时的旋转吸附步长（0.1 度）；450 表示 45°。")]
        private int rotationSnapDeciDegrees = 450;

        public int MinX => minX;
        public int MinZ => minZ;
        public int Width => width;
        public int Depth => depth;
        public float CellSize => cellSize;
        public DeckPlacementSnapSettings DefaultSnapSettings =>
            new(positionSnapMillimeters, rotationSnapDeciDegrees);

        public DeckBounds CreateBounds()
        {
            DeckPose minimum = DeckPose.FromMeters(
                (minX - 0.5d) * cellSize,
                (minZ - 0.5d) * cellSize,
                0d);
            DeckPose maximum = DeckPose.FromMeters(
                (minX + width - 0.5d) * cellSize,
                (minZ + depth - 0.5d) * cellSize,
                0d);
            return new DeckBounds(
                minimum.XMillimeters,
                minimum.ZMillimeters,
                maximum.XMillimeters,
                maximum.ZMillimeters);
        }

        public ContinuousFacilityPlacementLedger CreatePlacementLedger() =>
            new(CreateBounds());

        /// <summary>把量化业务姿态投影为甲板根节点的局部坐标。</summary>
        public Vector3 PoseToLocal(in DeckPose pose, float localY = 0f) =>
            new(pose.XMillimeters / 1000f, localY, pose.ZMillimeters / 1000f);

        /// <summary>把甲板局部点量化为毫米姿态；玩家吸附由 System 随后独立应用。</summary>
        public DeckPose LocalToPose(Vector3 localPoint, float yawDegrees = 0f) =>
            DeckPose.FromMeters(localPoint.x, localPoint.z, yawDegrees);

        public Vector3 DeckCenterLocal => new(
            (minX + (width - 1) * 0.5f) * cellSize,
            0f,
            (minZ + (depth - 1) * 0.5f) * cellSize);

        public Vector3 DeckSize => new(width * cellSize, 0.35f, depth * cellSize);

        private void OnValidate()
        {
            width = Mathf.Max(1, width);
            depth = Mathf.Max(1, depth);
            cellSize = Mathf.Max(0.1f, cellSize);
            positionSnapMillimeters = Mathf.Max(0, positionSnapMillimeters);
            rotationSnapDeciDegrees = Mathf.Clamp(rotationSnapDeciDegrees, 0, 3600);
        }

#if UNITY_EDITOR
        /// <summary>只供隔离测试在资产落盘前建立布局；正式场景使用生成的 ScriptableObject 资产。</summary>
        public void ConfigureForTests(
            int configuredMinX,
            int configuredMinZ,
            int configuredWidth,
            int configuredDepth,
            float configuredCellSize,
            int configuredPositionSnapMillimeters = 200,
            int configuredRotationSnapDeciDegrees = 450)
        {
            minX = configuredMinX;
            minZ = configuredMinZ;
            width = configuredWidth;
            depth = configuredDepth;
            cellSize = configuredCellSize;
            positionSnapMillimeters = configuredPositionSnapMillimeters;
            rotationSnapDeciDegrees = configuredRotationSnapDeciDegrees;
            OnValidate();
        }
#endif
    }
}
