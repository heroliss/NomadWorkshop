using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 一个设施交互位的稳定地址。地址属于存档稳定的设施实例与定义内稳定的 Group / Slot，
    /// 不使用运行时 Transform 或数组下标作为身份。
    /// </summary>
    public readonly struct InteractionSlotAddress : IEquatable<InteractionSlotAddress>
    {
        public InteractionSlotAddress(string facilityInstanceId, string groupId, string slotId)
        {
            FacilityInstanceId = Normalize(facilityInstanceId, nameof(facilityInstanceId));
            GroupId = Normalize(groupId, nameof(groupId));
            SlotId = Normalize(slotId, nameof(slotId));
        }

        public string FacilityInstanceId { get; }
        public string GroupId { get; }
        public string SlotId { get; }

        /// <summary>供原子预留表使用的稳定键；控制字符分隔避免普通策划 id 产生歧义。</summary>
        public string ReservationKey =>
            $"dock-slot:\u001f{FacilityInstanceId}\u001f{GroupId}\u001f{SlotId}";

        internal string GroupKey => $"{FacilityInstanceId}\u001f{GroupId}";

        public bool Equals(InteractionSlotAddress other) =>
            string.Equals(FacilityInstanceId, other.FacilityInstanceId, StringComparison.Ordinal) &&
            string.Equals(GroupId, other.GroupId, StringComparison.Ordinal) &&
            string.Equals(SlotId, other.SlotId, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is InteractionSlotAddress other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(FacilityInstanceId, GroupId, SlotId);

        public override string ToString() => $"{FacilityInstanceId}/{GroupId}/{SlotId}";

        private static string Normalize(string value, string parameterName)
        {
            value = value?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException("交互位稳定地址不能包含空 id。", parameterName);
        }
    }

    /// <summary>参与空间聚类的交互位；Pose 是甲板连续坐标中的真实站姿。</summary>
    public readonly struct InteractionSpaceSlot
    {
        public InteractionSpaceSlot(InteractionSlotAddress address, in DeckPose pose)
        {
            Address = address;
            Pose = pose;
        }

        public InteractionSlotAddress Address { get; }
        public DeckPose Pose { get; }
    }

    /// <summary>
    /// 一组不能同时容纳多名居民的停靠位。组内每个 Slot 键都会被一次性预留，因而从任意成员进入
    /// 都会占满同一空间；跨 Group 聚类同时意味着建造视图应显示空间冲突提示。
    /// </summary>
    public sealed class InteractionSpace
    {
        internal InteractionSpace(
            string stableId,
            InteractionSpaceSlot[] members,
            string[] reservationKeys,
            bool spansMultipleGroups)
        {
            StableId = stableId;
            Members = members;
            ReservationKeys = reservationKeys;
            SpansMultipleGroups = spansMultipleGroups;
        }

        public string StableId { get; }
        public IReadOnlyList<InteractionSpaceSlot> Members { get; }
        public IReadOnlyList<string> ReservationKeys { get; }
        public bool IsShared => Members.Count > 1;
        public bool SpansMultipleGroups { get; }
    }

    /// <summary>
    /// 根据连续甲板距离重建停靠空间容量。它不持有居民、不修改设施，也不执行预留；调用方把
    /// <see cref="InteractionSpace.ReservationKeys"/> 交给 <see cref="ReservationLedger"/> 即可获得
    /// 单线程模拟中的原子互斥语义。
    /// </summary>
    public sealed class InteractionSpaceTopology
    {
        private readonly int _mergeDistanceMillimeters;
        private readonly Dictionary<InteractionSlotAddress, InteractionSpace> _spaceByAddress = new();
        private InteractionSpace[] _spaces = Array.Empty<InteractionSpace>();

        public InteractionSpaceTopology(int mergeDistanceMillimeters)
        {
            if (mergeDistanceMillimeters < 0)
                throw new ArgumentOutOfRangeException(nameof(mergeDistanceMillimeters));
            _mergeDistanceMillimeters = mergeDistanceMillimeters;
        }

        public int MergeDistanceMillimeters => _mergeDistanceMillimeters;
        public IReadOnlyList<InteractionSpace> Spaces => _spaces;

        public void Rebuild(IReadOnlyList<InteractionSpaceSlot> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var slots = new InteractionSpaceSlot[source.Count];
            for (var i = 0; i < source.Count; i++) slots[i] = source[i];
            Array.Sort(slots, CompareSlots);

            for (var i = 1; i < slots.Length; i++)
            {
                if (slots[i - 1].Address.Equals(slots[i].Address))
                    throw new ArgumentException(
                        $"交互位地址重复：{slots[i].Address}。",
                        nameof(source));
            }

            var parents = new int[slots.Length];
            for (var i = 0; i < parents.Length; i++) parents[i] = i;
            long mergeDistanceSquared =
                (long)_mergeDistanceMillimeters * _mergeDistanceMillimeters;
            for (var left = 0; left < slots.Length; left++)
            {
                for (var right = left + 1; right < slots.Length; right++)
                {
                    if (slots[left].Pose.DeckLevel != slots[right].Pose.DeckLevel)
                        continue;
                    long deltaX = slots[left].Pose.XMillimeters -
                                  (long)slots[right].Pose.XMillimeters;
                    long deltaZ = slots[left].Pose.ZMillimeters -
                                  (long)slots[right].Pose.ZMillimeters;
                    if (deltaX * deltaX + deltaZ * deltaZ <= mergeDistanceSquared)
                        Union(parents, left, right);
                }
            }

            var membersByRoot = new Dictionary<int, List<InteractionSpaceSlot>>();
            for (var i = 0; i < slots.Length; i++)
            {
                int root = FindRoot(parents, i);
                if (!membersByRoot.TryGetValue(root, out List<InteractionSpaceSlot> members))
                {
                    members = new List<InteractionSpaceSlot>();
                    membersByRoot.Add(root, members);
                }
                members.Add(slots[i]);
            }

            var spaces = new List<InteractionSpace>(membersByRoot.Count);
            foreach (List<InteractionSpaceSlot> members in membersByRoot.Values)
            {
                InteractionSpaceSlot[] memberArray = members.ToArray();
                var reservationKeys = new string[memberArray.Length];
                string firstGroup = memberArray[0].Address.GroupKey;
                bool spansMultipleGroups = false;
                for (var i = 0; i < memberArray.Length; i++)
                {
                    reservationKeys[i] = memberArray[i].Address.ReservationKey;
                    spansMultipleGroups |= !string.Equals(
                        firstGroup,
                        memberArray[i].Address.GroupKey,
                        StringComparison.Ordinal);
                }
                spaces.Add(new InteractionSpace(
                    $"dock-space:{memberArray[0].Address.ReservationKey}",
                    memberArray,
                    reservationKeys,
                    spansMultipleGroups));
            }
            spaces.Sort((left, right) => string.CompareOrdinal(left.StableId, right.StableId));

            _spaces = spaces.ToArray();
            _spaceByAddress.Clear();
            for (var i = 0; i < _spaces.Length; i++)
            {
                InteractionSpace space = _spaces[i];
                for (var memberIndex = 0; memberIndex < space.Members.Count; memberIndex++)
                    _spaceByAddress.Add(space.Members[memberIndex].Address, space);
            }
        }

        public bool TryGetSpace(
            in InteractionSlotAddress address,
            out InteractionSpace space) =>
            _spaceByAddress.TryGetValue(address, out space);

        public bool HasCrossGroupConflict(in InteractionSlotAddress address) =>
            _spaceByAddress.TryGetValue(address, out InteractionSpace space) &&
            space.SpansMultipleGroups;

        private static int CompareSlots(InteractionSpaceSlot left, InteractionSpaceSlot right) =>
            string.CompareOrdinal(left.Address.ReservationKey, right.Address.ReservationKey);

        private static int FindRoot(int[] parents, int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }

        private static void Union(int[] parents, int left, int right)
        {
            int leftRoot = FindRoot(parents, left);
            int rightRoot = FindRoot(parents, right);
            if (leftRoot == rightRoot) return;
            if (leftRoot < rightRoot) parents[rightRoot] = leftRoot;
            else parents[leftRoot] = rightRoot;
        }
    }
}
