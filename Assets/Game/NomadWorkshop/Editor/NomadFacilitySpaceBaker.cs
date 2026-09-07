using System;
using Game.NomadWorkshop.Foundation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>显式模型标记到设施定义的编辑期入口。失败只销毁候选，不写入原定义。</summary>
    public static class NomadFacilitySpaceBaker
    {
        public static void Bake(NomadFacilityDefinition definition, GameObject sourcePrefab)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出 Play 后生成设施空间。");
            if (definition == null || sourcePrefab == null) throw new ArgumentException("需要设施定义和来源 Prefab。");
            if (EditorUtility.IsPersistent(definition) && !EditorUtility.IsPersistent(sourcePrefab))
                throw new InvalidOperationException("持久设施定义只能引用已保存的模型 Prefab。");
            var source = sourcePrefab.GetComponent<FoundationFacilitySpaceAuthoring>();
            if (source == null) throw new InvalidOperationException("来源根缺少设施空间标记组件。");
            FoundationFacilitySpaceSnapshot snapshot = source.CreateSnapshot();
            var candidate = Object.Instantiate(definition);
            try
            {
                candidate.name = definition.name;
                candidate.ApplyGeneratedSpaceForEditor(snapshot, sourcePrefab);
                candidate.ValidateModelSpaceSnapshot();
                foreach (var rig in sourcePrefab.GetComponentsInChildren<FoundationFacilityArtRig>(true))
                    rig.ValidateAgainst(candidate);
                Undo.RecordObject(definition, "从模型生成设施空间");
                EditorUtility.CopySerialized(candidate, definition);
                EditorUtility.SetDirty(definition);
                if (EditorUtility.IsPersistent(definition)) AssetDatabase.SaveAssetIfDirty(definition);
            }
            finally { Object.DestroyImmediate(candidate); }
        }

        [MenuItem("Assets/SSFramework/游牧工坊/从模型生成选中设施的空间")]
        private static void BakeSelected()
        {
            var definition = Selection.activeObject as NomadFacilityDefinition;
            if (definition == null) throw new InvalidOperationException("请先选中需要生成空间的设施定义。");
            Bake(definition, definition.Prefab);
            Debug.Log($"设施 {definition.DisplayName} 的空间已从模型生成：{definition.GeneratedSpaceSignature}");
        }
    }
}
