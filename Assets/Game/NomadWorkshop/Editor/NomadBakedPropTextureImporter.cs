using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>
    /// 小道具共享图集的 Editor Adapter。调用者先验证来源哈希；这里只写指定 stem 的三个纹理与材质，
    /// 统一颜色空间、法线、金属/光滑度 Alpha 和旧 Sprite 元数据清理，不拥有场景、Prefab 或模型锚点。
    /// </summary>
    internal static class NomadBakedPropTextureImporter
    {
        internal static Material Import(string root, string source, string stem)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时导入道具图集。");
            string[] channels = { "Color", "Normal", "Surface" };
            foreach (string channel in channels)
                if (!File.Exists(source + "/" + stem + "_" + channel + ".png"))
                    throw new InvalidOperationException("道具图集不完整：" + stem + "/" + channel);
            var textures = new Texture2D[3];
            for (int i = 0; i < channels.Length; i++)
            {
                string file = stem + "_" + channels[i] + ".png", path = root + "/Textures/" + file;
                File.Copy(source + "/" + file, path, true);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = i == 1 ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.spriteImportMode = SpriteImportMode.None; importer.sRGBTexture = i == 0;
                importer.isReadable = false; importer.alphaIsTransparency = false; importer.maxTextureSize = 2048;
                importer.alphaSource = i == 0 ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
                importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                var data = new SerializedObject(importer);
                foreach (string field in new[] { "m_SpriteSheet.m_Sprites", "m_SpriteSheet.m_NameFileIdTable", "m_InternalIDToNameTable" })
                {
                    var property = data.FindProperty(field);
                    if (property == null || !property.isArray) throw new InvalidOperationException("Unity 纹理字段已改变：" + field);
                    property.ClearArray();
                }
                data.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport();
                textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            string materialPath = root + "/Materials/" + stem + "Atlas.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("缺少 URP Lit。");
                material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath);
            }
            material.name = stem + "Atlas"; material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", textures[0]); material.SetTexture("_BumpMap", textures[1]);
            material.SetTexture("_MetallicGlossMap", textures[2]); material.SetFloat("_BumpScale", 1);
            material.SetFloat("_Metallic", 1); material.SetFloat("_Smoothness", 1); material.SetFloat("_SmoothnessTextureChannel", 0);
            material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material); return material;
        }
    }
}
