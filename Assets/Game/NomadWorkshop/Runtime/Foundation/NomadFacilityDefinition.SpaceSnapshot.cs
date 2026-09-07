using System;
using System.Linq;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFacilityDefinition
    {
        [SerializeField, HideInInspector] private string generatedSpaceSignature = string.Empty;
        [SerializeField, HideInInspector] private string legacySpaceSignature = string.Empty;

        /// <summary>编辑期生成的空间内容摘要；空值表示沿用尚未接入模型标记的旧定义。</summary>
        public string GeneratedSpaceSignature => generatedSpaceSignature ?? string.Empty;

        /// <summary>复制当前玩法空间，按与模型输入相同的量化规则生成摘要。</summary>
        public FoundationFacilitySpaceSnapshot CaptureSpaceSnapshot()
        {
            ValidateOrThrow();
            return new FoundationFacilitySpaceSnapshot(footprintParts, interactionGroups, placementRegions);
        }

        /// <summary>
        /// 在初始化时核对模型标记、生成记录与玩法定义，拒绝过期或未生成的数据。
        /// 这是静态资源校验，不用于动画中的模型实例或每帧查询。
        /// </summary>
        public void ValidateModelSpaceSnapshot()
        {
            var source = prefab != null ? prefab.GetComponent<FoundationFacilitySpaceAuthoring>() : null;
            if (GeneratedSpaceSignature.Length == 0)
            {
                if (source != null) throw new InvalidOperationException($"设施 {id} 已有模型空间标记，请先从模型生成空间定义。");
                return;
            }
            if (source == null || source.CreateSnapshot().Signature != GeneratedSpaceSignature ||
                CaptureSpaceSnapshot().Signature != GeneratedSpaceSignature)
                throw new InvalidOperationException($"设施 {id} 的模型空间已过期或来源缺失，请重新生成并检查存档兼容性。");
        }

        /// <summary>
        /// 恢复世界前核对已保存的空间身份。无摘要的旧档只兼容首次生成前的空间基线；
        /// 空间变化需要显式迁移，不能静默改变已有物品的支撑高度、容量或居民站位。
        /// </summary>
        public void ValidateSavedSpaceSignature(string savedSignature)
        {
            bool compatible = string.IsNullOrEmpty(savedSignature)
                ? GeneratedSpaceSignature.Length == 0 || GeneratedSpaceSignature == legacySpaceSignature
                : savedSignature == GeneratedSpaceSignature;
            if (!compatible)
                throw new NotSupportedException($"检查点设施定义 {id} 的空间版本与当前模型不兼容，需要明确迁移后才能恢复。");
        }

#if UNITY_EDITOR
        /// <summary>供生成器的临时候选使用；调用者须完成全部校验后再提交持久资产。</summary>
        public void ApplyGeneratedSpaceForEditor(FoundationFacilitySpaceSnapshot snapshot, GameObject sourcePrefab)
        {
            if (Application.isPlaying) throw new InvalidOperationException("不能在 Play 中生成设施空间。");
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (sourcePrefab == null) throw new ArgumentNullException(nameof(sourcePrefab));
            string baseline = legacySpaceSignature;
            if (GeneratedSpaceSignature.Length == 0)
            {
                // 新建定义可能尚无任何空间，不能要求先手填一份虚假旧数据才能生成。
                // 没有有效旧空间就没有旧档兼容基线；后续重复生成也不得补认基线。
                try { baseline = CaptureSpaceSnapshot().Signature; }
                catch (InvalidOperationException) { baseline = string.Empty; }
                catch (ArgumentException) { baseline = string.Empty; }
            }
            footprintParts = snapshot.Footprints.ToArray();
            interactionGroups = snapshot.Groups.ToArray();
            placementRegions = snapshot.Regions.ToArray();
            prefab = sourcePrefab;
            generatedSpaceSignature = snapshot.Signature;
            legacySpaceSignature = baseline;
        }
#endif
    }
}
