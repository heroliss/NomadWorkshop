using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>车辆甲板上的整数格坐标。X/Z 只表达逻辑位置，不承载 Unity 世界坐标。</summary>
    public readonly struct DeckCell : IEquatable<DeckCell>
    {
        public readonly int X;
        public readonly int Z;

        public DeckCell(int x, int z)
        {
            X = x;
            Z = z;
        }

        public DeckCell Offset(DeckCell other) => new(X + other.X, Z + other.Z);

        public bool Equals(DeckCell other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is DeckCell other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Z);
        public override string ToString() => $"({X}, {Z})";

        public static bool operator ==(DeckCell left, DeckCell right) => left.Equals(right);
        public static bool operator !=(DeckCell left, DeckCell right) => !left.Equals(right);
    }

    /// <summary>
    /// 可建造设施的离散朝向。第一版只允许四分之一圈，避免任意角度把占格、寻路与存档变成浮点问题。
    /// </summary>
    public enum FacilityQuarterTurn
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3,
    }

    /// <summary>一次设施摆放失败的首要原因；成功时为 <see cref="None"/>。</summary>
    public enum FacilityPlacementFailure
    {
        None,
        InvalidRequest,
        DuplicateInstanceId,
        FootprintOutOfBounds,
        InteractionOutOfBounds,
        FootprintOccupied,
        BlocksExistingInteraction,
        InteractionObstructed,
        NavigationDisconnected,
    }

    /// <summary>
    /// 一个设施类型的逻辑占格与交互格。坐标都相对设施枢轴，实际摆放时随朝向旋转。
    /// 交互格不是实体占格，但会被保护，保证后放的设施不能堵死已有工作位。
    /// </summary>
    public sealed class FacilityFootprint
    {
        private readonly DeckCell[] _occupiedCells;
        private readonly DeckCell[] _interactionCells;

        public FacilityFootprint(
            IReadOnlyList<DeckCell> occupiedCells,
            IReadOnlyList<DeckCell> interactionCells)
        {
            _occupiedCells = CopyRequiredDistinct(occupiedCells, nameof(occupiedCells));
            _interactionCells = CopyRequiredDistinct(interactionCells, nameof(interactionCells));

            var occupied = new HashSet<DeckCell>(_occupiedCells);
            for (var i = 0; i < _interactionCells.Length; i++)
            {
                if (occupied.Contains(_interactionCells[i]))
                    throw new ArgumentException(
                        $"交互格 {_interactionCells[i]} 不能与设施占格重叠。",
                        nameof(interactionCells));
            }
        }

        public IReadOnlyList<DeckCell> OccupiedCells => _occupiedCells;
        public IReadOnlyList<DeckCell> InteractionCells => _interactionCells;

        private static DeckCell[] CopyRequiredDistinct(
            IReadOnlyList<DeckCell> source,
            string parameterName)
        {
            if (source == null) throw new ArgumentNullException(parameterName);
            if (source.Count == 0)
                throw new ArgumentException("至少需要一个格子。", parameterName);

            var result = new DeckCell[source.Count];
            var unique = new HashSet<DeckCell>();
            for (var i = 0; i < source.Count; i++)
            {
                result[i] = source[i];
                if (!unique.Add(result[i]))
                    throw new ArgumentException($"格子 {result[i]} 重复。", parameterName);
            }

            return result;
        }
    }

    /// <summary>设施的一次摆放意图。InstanceId 是存档稳定身份，DefinitionId 指向设施定义。</summary>
    public readonly struct FacilityPlacementRequest
    {
        public readonly string InstanceId;
        public readonly string DefinitionId;
        public readonly DeckCell Pivot;
        public readonly FacilityQuarterTurn Rotation;
        public readonly FacilityFootprint Footprint;

        public FacilityPlacementRequest(
            string instanceId,
            string definitionId,
            DeckCell pivot,
            FacilityQuarterTurn rotation,
            FacilityFootprint footprint)
        {
            InstanceId = instanceId;
            DefinitionId = definitionId;
            Pivot = pivot;
            Rotation = rotation;
            Footprint = footprint;
        }
    }

    /// <summary>已提交的设施记录。世界坐标应由 Pivot、Rotation 与甲板布局参数派生。</summary>
    public sealed class PlacedFacility
    {
        private readonly DeckCell[] _occupiedCells;
        private readonly DeckCell[] _interactionCells;

        internal PlacedFacility(
            in FacilityPlacementRequest request,
            DeckCell[] occupiedCells,
            DeckCell[] interactionCells)
        {
            InstanceId = request.InstanceId;
            DefinitionId = request.DefinitionId;
            Pivot = request.Pivot;
            Rotation = request.Rotation;
            Footprint = request.Footprint;
            _occupiedCells = occupiedCells;
            _interactionCells = interactionCells;
        }

        public string InstanceId { get; }
        public string DefinitionId { get; }
        public DeckCell Pivot { get; }
        public FacilityQuarterTurn Rotation { get; }
        public FacilityFootprint Footprint { get; }
        public IReadOnlyList<DeckCell> OccupiedCells => _occupiedCells;
        public IReadOnlyList<DeckCell> InteractionCells => _interactionCells;
    }

    /// <summary>
    /// 车辆甲板设施摆放的纯 C# 权威规则。所有检查先完成再提交，失败不会留下半占用状态。
    /// </summary>
    public sealed class FacilityPlacementGrid
    {
        private readonly int _minX;
        private readonly int _minZ;
        private readonly int _maxXExclusive;
        private readonly int _maxZExclusive;
        private readonly Dictionary<string, PlacedFacility> _placements =
            new(StringComparer.Ordinal);
        private readonly Dictionary<DeckCell, string> _occupiedByInstance = new();
        private readonly Dictionary<DeckCell, int> _interactionUseCount = new();

        public FacilityPlacementGrid(int minX, int minZ, int width, int depth)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (depth <= 0) throw new ArgumentOutOfRangeException(nameof(depth));

            _minX = minX;
            _minZ = minZ;
            _maxXExclusive = checked(minX + width);
            _maxZExclusive = checked(minZ + depth);
        }

        public int Count => _placements.Count;

        public FacilityPlacementFailure Evaluate(in FacilityPlacementRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.InstanceId) ||
                string.IsNullOrWhiteSpace(request.DefinitionId) ||
                request.Footprint == null ||
                !IsValidRotation(request.Rotation))
                return FacilityPlacementFailure.InvalidRequest;

            if (_placements.ContainsKey(request.InstanceId))
                return FacilityPlacementFailure.DuplicateInstanceId;

            DeckCell[] occupiedCells = TransformCells(
                request.Footprint.OccupiedCells,
                request.Pivot,
                request.Rotation);
            DeckCell[] interactionCells = TransformCells(
                request.Footprint.InteractionCells,
                request.Pivot,
                request.Rotation);

            for (var i = 0; i < occupiedCells.Length; i++)
            {
                DeckCell cell = occupiedCells[i];
                if (!Contains(cell)) return FacilityPlacementFailure.FootprintOutOfBounds;
                if (_occupiedByInstance.ContainsKey(cell))
                    return FacilityPlacementFailure.FootprintOccupied;
                if (_interactionUseCount.ContainsKey(cell))
                    return FacilityPlacementFailure.BlocksExistingInteraction;
            }

            for (var i = 0; i < interactionCells.Length; i++)
            {
                DeckCell cell = interactionCells[i];
                if (!Contains(cell)) return FacilityPlacementFailure.InteractionOutOfBounds;
                if (_occupiedByInstance.ContainsKey(cell))
                    return FacilityPlacementFailure.InteractionObstructed;
            }

            return FacilityPlacementFailure.None;
        }

        public bool TryPlace(
            in FacilityPlacementRequest request,
            out PlacedFacility placement,
            out FacilityPlacementFailure failure)
        {
            failure = Evaluate(request);
            if (failure != FacilityPlacementFailure.None)
            {
                placement = null;
                return false;
            }

            DeckCell[] occupiedCells = TransformCells(
                request.Footprint.OccupiedCells,
                request.Pivot,
                request.Rotation);
            DeckCell[] interactionCells = TransformCells(
                request.Footprint.InteractionCells,
                request.Pivot,
                request.Rotation);
            placement = new PlacedFacility(request, occupiedCells, interactionCells);

            _placements.Add(placement.InstanceId, placement);
            for (var i = 0; i < occupiedCells.Length; i++)
                _occupiedByInstance.Add(occupiedCells[i], placement.InstanceId);
            for (var i = 0; i < interactionCells.Length; i++)
            {
                DeckCell cell = interactionCells[i];
                _interactionUseCount.TryGetValue(cell, out int useCount);
                _interactionUseCount[cell] = useCount + 1;
            }

            return true;
        }

        public bool Remove(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId) ||
                !_placements.TryGetValue(instanceId, out PlacedFacility placement))
                return false;

            _placements.Remove(instanceId);
            for (var i = 0; i < placement.OccupiedCells.Count; i++)
                _occupiedByInstance.Remove(placement.OccupiedCells[i]);
            for (var i = 0; i < placement.InteractionCells.Count; i++)
            {
                DeckCell cell = placement.InteractionCells[i];
                int remaining = _interactionUseCount[cell] - 1;
                if (remaining == 0) _interactionUseCount.Remove(cell);
                else _interactionUseCount[cell] = remaining;
            }

            return true;
        }

        public bool TryGetPlacement(string instanceId, out PlacedFacility placement) =>
            _placements.TryGetValue(instanceId, out placement);

        public bool IsOccupied(DeckCell cell) => _occupiedByInstance.ContainsKey(cell);
        public bool IsInteractionProtected(DeckCell cell) => _interactionUseCount.ContainsKey(cell);

        public bool Contains(DeckCell cell) =>
            cell.X >= _minX && cell.X < _maxXExclusive &&
            cell.Z >= _minZ && cell.Z < _maxZExclusive;

        public static DeckCell Rotate(DeckCell cell, FacilityQuarterTurn rotation) => rotation switch
        {
            FacilityQuarterTurn.North => cell,
            FacilityQuarterTurn.East => new DeckCell(cell.Z, -cell.X),
            FacilityQuarterTurn.South => new DeckCell(-cell.X, -cell.Z),
            FacilityQuarterTurn.West => new DeckCell(-cell.Z, cell.X),
            _ => throw new ArgumentOutOfRangeException(nameof(rotation), rotation, null),
        };

        /// <summary>把定义中的相对格按设施朝向转换为甲板绝对格。</summary>
        public static DeckCell TransformRelativeCell(
            DeckCell relativeCell,
            DeckCell pivot,
            FacilityQuarterTurn rotation) =>
            pivot.Offset(Rotate(relativeCell, rotation));

        private static DeckCell[] TransformCells(
            IReadOnlyList<DeckCell> source,
            DeckCell pivot,
            FacilityQuarterTurn rotation)
        {
            var result = new DeckCell[source.Count];
            for (var i = 0; i < source.Count; i++)
                result[i] = TransformRelativeCell(source[i], pivot, rotation);
            return result;
        }

        private static bool IsValidRotation(FacilityQuarterTurn rotation) =>
            rotation >= FacilityQuarterTurn.North && rotation <= FacilityQuarterTurn.West;
    }
}
