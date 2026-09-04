using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 资源数量采用的基础计量维度。库存容量与内容必须使用同一维度，避免把“一件食物”
    /// 和“一毫升水”相加后得到没有物理意义的总量；复合设施通过多个库存隔间表达。
    /// </summary>
    public enum ResourceMeasure
    {
        Item = 0,
        Milliliter = 1,
    }

    /// <summary>
    /// 数据驱动资源的稳定身份。模拟层比较规范 id 与计量维度，不把 Unity 资产或显示名称带进经济真值。
    /// </summary>
    public readonly struct ResourceId : IEquatable<ResourceId>, IComparable<ResourceId>
    {
        public ResourceId(string value, ResourceMeasure measure = ResourceMeasure.Item)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("资源 id 不能为空。", nameof(value));
            if (!Enum.IsDefined(typeof(ResourceMeasure), measure))
                throw new ArgumentOutOfRangeException(nameof(measure), "资源计量维度无效。");
            Value = value.Trim();
            Measure = measure;
        }

        public string Value { get; }
        public ResourceMeasure Measure { get; }

        public bool IsValid => !string.IsNullOrEmpty(Value);

        public bool Equals(ResourceId other)
            => Measure == other.Measure &&
               string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj)
            => obj is ResourceId other && Equals(other);

        public override int GetHashCode()
            => Value == null ? 0 : HashCode.Combine(StringComparer.Ordinal.GetHashCode(Value), Measure);

        public int CompareTo(ResourceId other)
        {
            int idOrder = string.Compare(Value, other.Value, StringComparison.Ordinal);
            return idOrder != 0 ? idOrder : Measure.CompareTo(other.Measure);
        }

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(ResourceId left, ResourceId right) => left.Equals(right);

        public static bool operator !=(ResourceId left, ResourceId right) => !left.Equals(right);
    }

    /// <summary>Foundation Prototype 当前真正进入物质链的资源 id；新增内容不需要扩充枚举。</summary>
    public static class NomadResourceIds
    {
        public static readonly ResourceId Water = new("water", ResourceMeasure.Milliliter);
        public static readonly ResourceId FoodIngredient = new("food-ingredient");
        public static readonly ResourceId PreparedMeal = new("prepared-meal");
        public static readonly ResourceId WasteWater =
            new("waste-water", ResourceMeasure.Milliliter);
        public static readonly ResourceId HumanWaste =
            new("human-waste", ResourceMeasure.Milliliter);
    }

    /// <summary>
    /// 一种资源及其正整数基础量。单位由 <see cref="ResourceId.Measure"/> 决定：液体为 mL，
    /// 离散物品为件。基础量始终使用整数，避免存档与资源守恒受浮点误差影响。
    /// </summary>
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

        public ResourceInventory(
            string id,
            ResourceMeasure measure,
            int capacity,
            params ResourceQuantity[] initialContents)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("库存 id 不能为空。", nameof(id));
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "库存容量不能为负数。");
            if (!Enum.IsDefined(typeof(ResourceMeasure), measure))
                throw new ArgumentOutOfRangeException(nameof(measure), "库存计量维度无效。");
            if (initialContents == null)
                throw new ArgumentNullException(nameof(initialContents));

            Id = id.Trim();
            Measure = measure;
            Capacity = capacity;

            for (int i = 0; i < initialContents.Length; i++)
            {
                ResourceQuantity quantity = initialContents[i];
                if (!quantity.Resource.IsValid || quantity.Amount <= 0)
                    throw new ArgumentException($"初始库存第 {i} 项无效。", nameof(initialContents));
                EnsureCompatible(quantity.Resource);

                _amounts.TryGetValue(quantity.Resource, out int current);
                _amounts[quantity.Resource] = checked(current + quantity.Amount);
            }

            if (TotalAmount > Capacity)
                throw new ArgumentException(
                    $"库存 {Id} 的初始数量 {TotalAmount} 超过容量 {Capacity}。",
                    nameof(initialContents));
        }

        public string Id { get; }

        /// <summary>该库存所有内容及容量共同使用的基础计量维度。</summary>
        public ResourceMeasure Measure { get; }

        /// <summary>以 <see cref="Measure"/> 为单位的容量。</summary>
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
            EnsureCompatible(resource);
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
            EnsureCompatible(resource);
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "加入数量必须大于零。");
            if (FreeCapacity < amount)
                throw new InvalidOperationException(
                    $"库存 {Id} 无法加入 {amount} 单位 {resource}；剩余容量只有 {FreeCapacity}。 ");

            _amounts.TryGetValue(resource, out int current);
            _amounts[resource] = checked(current + amount);
        }

        internal void EnsureCompatible(ResourceId resource)
        {
            if (!resource.IsValid)
                throw new ArgumentException("资源 id 无效。", nameof(resource));
            if (resource.Measure != Measure)
                throw new InvalidOperationException(
                    $"库存 {Id} 使用 {Measure}，不能存放使用 {resource.Measure} 的资源 {resource}。");
        }
    }

    /// <summary>把基础量转为紧凑且带单位的玩家可读文本；不改变模拟层保存的整数真值。</summary>
    public static class ResourceAmountFormatting
    {
        public static string Format(int amount, ResourceMeasure measure)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "资源显示量不能为负数。");

            return measure switch
            {
                ResourceMeasure.Item => $"{amount} 件",
                ResourceMeasure.Milliliter when amount < 1000 => $"{amount} mL",
                ResourceMeasure.Milliliter =>
                    $"{(amount / 1000d).ToString("0.##", CultureInfo.InvariantCulture)} L",
                _ => throw new ArgumentOutOfRangeException(nameof(measure), "资源计量维度无效。"),
            };
        }
    }
}
