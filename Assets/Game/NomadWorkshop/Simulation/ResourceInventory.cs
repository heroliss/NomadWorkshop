using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 数据驱动资源的稳定身份。模拟层只比较 id，不把 Unity 资产或显示名称带进经济真值。
    /// </summary>
    public readonly struct ResourceId : IEquatable<ResourceId>, IComparable<ResourceId>
    {
        public ResourceId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("资源 id 不能为空。", nameof(value));
            Value = value.Trim();
        }

        public string Value { get; }

        public bool IsValid => !string.IsNullOrEmpty(Value);

        public bool Equals(ResourceId other)
            => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj)
            => obj is ResourceId other && Equals(other);

        public override int GetHashCode()
            => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public int CompareTo(ResourceId other)
            => string.Compare(Value, other.Value, StringComparison.Ordinal);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(ResourceId left, ResourceId right) => left.Equals(right);

        public static bool operator !=(ResourceId left, ResourceId right) => !left.Equals(right);
    }

    /// <summary>Foundation Prototype 当前真正进入物质链的资源 id；新增内容不需要扩充枚举。</summary>
    public static class NomadResourceIds
    {
        public static readonly ResourceId Water = new("water");
        public static readonly ResourceId FoodIngredient = new("food-ingredient");
        public static readonly ResourceId PreparedMeal = new("prepared-meal");
        public static readonly ResourceId WasteWater = new("waste-water");
        public static readonly ResourceId HumanWaste = new("human-waste");
    }

    /// <summary>一种资源及其正整数数量。</summary>
    public readonly struct ResourceQuantity
    {
        public ResourceQuantity(ResourceId resource, int amount)
        {
            if (!resource.IsValid) throw new ArgumentException("资源 id 无效。", nameof(resource));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "资源数量必须大于零。");
            Resource = resource;
            Amount = amount;
        }

        public ResourceId Resource { get; }
        public int Amount { get; }
    }

    /// <summary>
    /// 有稳定身份和总容量的真实库存节点。只有 <see cref="ResourceFlowLedger"/> 能在运行期改写内容，
    /// 从而让数量预留、搬运中持有与设施加工共享同一组守恒边界。
    /// </summary>
    public sealed class ResourceInventory
    {
        private readonly Dictionary<ResourceId, int> _amounts = new();

        public ResourceInventory(string id, int capacity, params ResourceQuantity[] initialContents)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("库存 id 不能为空。", nameof(id));
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "库存容量不能为负数。");
            if (initialContents == null)
                throw new ArgumentNullException(nameof(initialContents));

            Id = id.Trim();
            Capacity = capacity;

            for (int i = 0; i < initialContents.Length; i++)
            {
                ResourceQuantity quantity = initialContents[i];
                if (!quantity.Resource.IsValid || quantity.Amount <= 0)
                    throw new ArgumentException($"初始库存第 {i} 项无效。", nameof(initialContents));

                _amounts.TryGetValue(quantity.Resource, out int current);
                _amounts[quantity.Resource] = checked(current + quantity.Amount);
            }

            if (TotalAmount > Capacity)
                throw new ArgumentException(
                    $"库存 {Id} 的初始数量 {TotalAmount} 超过容量 {Capacity}。",
                    nameof(initialContents));
        }

        public string Id { get; }
        public int Capacity { get; }

        public int TotalAmount
        {
            get
            {
                int total = 0;
                foreach (int amount in _amounts.Values) total = checked(total + amount);
                return total;
            }
        }

        public int FreeCapacity => Capacity - TotalAmount;

        public int GetAmount(ResourceId resource)
        {
            if (!resource.IsValid) throw new ArgumentException("资源 id 无效。", nameof(resource));
            return _amounts.TryGetValue(resource, out int amount) ? amount : 0;
        }

        /// <summary>返回按资源 id 排序的副本，供存档、诊断和 UI 读取而不泄露可变字典。</summary>
        public IReadOnlyList<ResourceQuantity> GetContentsSnapshot()
        {
            var result = new List<ResourceQuantity>(_amounts.Count);
            foreach (KeyValuePair<ResourceId, int> pair in _amounts)
                result.Add(new ResourceQuantity(pair.Key, pair.Value));
            result.Sort((left, right) => left.Resource.CompareTo(right.Resource));
            return result;
        }

        internal void Remove(ResourceId resource, int amount)
        {
            int current = GetAmount(resource);
            if (amount <= 0 || current < amount)
                throw new InvalidOperationException(
                    $"库存 {Id} 无法移出 {amount} 单位 {resource}；当前只有 {current}。 ");

            int remaining = current - amount;
            if (remaining == 0) _amounts.Remove(resource);
            else _amounts[resource] = remaining;
        }

        internal void Add(ResourceId resource, int amount)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "加入数量必须大于零。");
            if (FreeCapacity < amount)
                throw new InvalidOperationException(
                    $"库存 {Id} 无法加入 {amount} 单位 {resource}；剩余容量只有 {FreeCapacity}。 ");

            _amounts.TryGetValue(resource, out int current);
            _amounts[resource] = checked(current + amount);
        }
    }
}
