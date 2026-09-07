using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Game.NomadWorkshop.Simulation;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 转换到设施根的空间快照。摘要只含毫米/0.1 度量化的玩法空间与稳定身份，
    /// 不含网格、材质、对象名或世界变换；只换美术且保留空间时可复用存档。
    /// </summary>
    public sealed class FoundationFacilitySpaceSnapshot
    {
        private readonly NomadFacilityFootprintPartDefinition[] _footprints;
        private readonly NomadFacilityInteractionGroupDefinition[] _groups;
        private readonly NomadPlacementRegionDefinition[] _regions;
        public IReadOnlyList<NomadFacilityFootprintPartDefinition> Footprints => Array.AsReadOnly(_footprints);
        public IReadOnlyList<NomadFacilityInteractionGroupDefinition> Groups => Array.AsReadOnly(_groups);
        public IReadOnlyList<NomadPlacementRegionDefinition> Regions => Array.AsReadOnly(_regions);
        public string Signature { get; }

        public FoundationFacilitySpaceSnapshot(IEnumerable<NomadFacilityFootprintPartDefinition> footprints,
            IEnumerable<NomadFacilityInteractionGroupDefinition> groups, IEnumerable<NomadPlacementRegionDefinition> regions)
        {
            _footprints = footprints.ToArray();
            _groups = groups.Select(group => group == null ? null :
                new NomadFacilityInteractionGroupDefinition(group.GroupId, group.RequiredForOperation,
                    group.AlternativeSlots.ToArray())).ToArray();
            _regions = regions.Select(region => new NomadPlacementRegionDefinition(region.RegionId,
                region.LocalCenterMeters, region.SizeMeters, region.LocalYawDegrees, region.SupportHeightMeters,
                region.EdgeInsetMeters, region.AcceptedCategories.ToArray())).ToArray();
            if (_footprints.Length == 0 || _groups.Length == 0) throw new ArgumentException("设施空间需要占地和至少一个交互组。");
            // 顺序影响候选优先级，摘要保留配置顺序，不为排序稳定改变实际选择语义。
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write("nomad-facility-space-v1"); writer.Write(_footprints.Length);
                foreach (var part in _footprints)
                {
                    WritePose(writer, DeckPose.FromMeters(part.LocalCenterMeters.x, part.LocalCenterMeters.y, part.LocalYawDegrees));
                    WriteSize(writer, part.SizeMeters.x, part.SizeMeters.y);
                }
                writer.Write(_groups.Length);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var group in _groups)
                {
                    if (group == null || !ids.Add(group.GroupId)) throw new ArgumentException("设施空间包含空或重复交互组。");
                    group.ValidateOrThrow("模型空间快照"); writer.Write(group.GroupId); writer.Write(group.RequiredForOperation);
                    writer.Write(group.AlternativeSlots.Count);
                    foreach (var slot in group.AlternativeSlots)
                    {
                        writer.Write(slot.SlotId); WritePose(writer, DeckPose.FromMeters(slot.LocalPositionMeters.x, slot.LocalPositionMeters.y, slot.LocalYawDegrees));
                    }
                }
                writer.Write(_regions.Length); ids.Clear();
                foreach (var region in _regions)
                {
                    region.ValidateOrThrow("模型空间快照");
                    if (!ids.Add(region.RegionId)) throw new ArgumentException("设施空间包含重复放置区域。");
                    writer.Write(region.RegionId); WritePose(writer, DeckPose.FromMeters(region.LocalCenterMeters.x, region.LocalCenterMeters.y, region.LocalYawDegrees));
                    WriteSize(writer, region.SizeMeters.x, region.SizeMeters.y);
                    writer.Write(UnityEngine.Mathf.RoundToInt(region.SupportHeightMeters*1000));
                    writer.Write(UnityEngine.Mathf.RoundToInt(region.EdgeInsetMeters*1000));
                    writer.Write(region.AcceptedCategories.Count);
                    foreach (string category in region.AcceptedCategories) writer.Write(category);
                }
            }
            using var hash = SHA256.Create();
            Signature = BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
        }

        private static void WritePose(BinaryWriter writer, DeckPose pose)
        { writer.Write(pose.XMillimeters); writer.Write(pose.ZMillimeters); writer.Write(pose.YawDeciDegrees); }
        private static void WriteSize(BinaryWriter writer, float x, float z)
        {
            DeckPose size = DeckPose.FromMeters(x, z, 0);
            writer.Write(size.XMillimeters); writer.Write(size.ZMillimeters);
        }
    }
}
