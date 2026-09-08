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

        [Header("局部楼板样板（毫米）")]
        [SerializeField, Tooltip("空数组沿用起步矩形；非空时使用这些轴对齐板片的并集。它是静态布局样板，尚非玩家施工存档。")]
        private RectInt[] supportPlates = System.Array.Empty<RectInt>();
        [SerializeField, Tooltip("从板片并集中扣除的开口；尺寸和坐标均为毫米。")]
        private RectInt[] supportOpenings = System.Array.Empty<RectInt>();

        [Header("默认建造辅助")]
        [SerializeField, Min(0), Tooltip("进入场景时的位置吸附步长（毫米）。正式 UI 提供自由、200/300/400/600；非零档位共用 100 mm 基础格与甲板原点。")]
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

        /// <summary>创建静态支撑快照；主车架原点、取水驿站和旧起步尺寸不因局部外扩而重新居中。</summary>
        public DeckSupportRegion CreateSupportRegion()
        {
            DeckBounds[] plates = supportPlates == null || supportPlates.Length == 0
                ? new[] { CreateBounds() } : ConvertParts(supportPlates);
            return new DeckSupportRegion(plates, ConvertParts(supportOpenings));
        }

        private static DeckBounds[] ConvertParts(RectInt[] parts)
        {
            var result = new DeckBounds[parts?.Length ?? 0];
            for (var i = 0; i < result.Length; i++)
            {
                RectInt p = parts[i];
                if (p.width <= 0 || p.height <= 0) throw new System.ArgumentException("甲板板片/开口尺寸必须为正毫米数。");
                result[i] = new DeckBounds(p.x, p.y, checked(p.x + p.width), checked(p.y + p.height));
            }
            return result;
        }

        public ContinuousFacilityPlacementLedger CreatePlacementLedger() =>
            new(new[] { CreateSupportRegion() });

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
        /// <summary>在独立样板资产落盘前配置局部板，复制数组；运行中不能借此热换结构。</summary>
        public void ConfigureSupportForTests(RectInt[] plates, RectInt[] openings)
        {
            supportPlates = plates == null ? System.Array.Empty<RectInt>() : (RectInt[])plates.Clone();
            supportOpenings = openings == null ? System.Array.Empty<RectInt>() : (RectInt[])openings.Clone();
            CreateSupportRegion();
        }

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
