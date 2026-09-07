using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>生成可重复的微表面纹理；大色块由材质调色，细节只补漆面、织物和沙地的粗糙度。</summary>
    public static class NomadWorkshopSurfacePipeline
    {
        private const int Size = 512;
        private const string Folder = NomadWarmWorkshopArtPipeline.Root + "/Textures";
        private static readonly Dictionary<string, Color32[]> Surfaces = new();
        private static readonly HashSet<string> WrittenMasks = new();

        public static void GenerateTextures()
        {
            Surfaces.Clear();
            WrittenMasks.Clear();
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder(NomadWarmWorkshopArtPipeline.Root, "Textures");
            foreach (string kind in new[] { "Metal", "Fabric", "Earth" }) Generate(kind);
        }

        public static void Apply(Material material, string name)
        {
            string kind = name.Contains("Canvas") || name.Contains("Workshirt") || name.Contains("Trousers") ||
                name.Contains("Scarf") ? "Fabric" :
                name.Contains("Ground") || name.Contains("Rock") || name.Contains("Sand") ? "Earth" : "Metal";
            if (name.Contains("Lamp") || name.Contains("Glass") || name.Contains("Foliage")) return;
            material.SetTexture("_BaseMap", Load(kind, "Color"));
            material.SetTexture("_BumpMap", Load(kind, "Normal"));
            // URP 开启 Metallic Map 后不再乘材质的 _Metallic；每种金属度必须编码到 R 通道。
            byte metallic = (byte)Mathf.RoundToInt(Mathf.Clamp01(material.GetFloat("_Metallic"))*255f);
            string channel = "M" + metallic + "_Surface";
            if (WrittenMasks.Add(kind + channel))
            {
                Color32[] mask = (Color32[])Surfaces[kind].Clone();
                for (int i = 0; i < mask.Length; i++) mask[i].r = metallic;
                Write(kind, channel, mask, false);
            }
            material.SetTexture("_MetallicGlossMap", Load(kind, channel));
            material.SetFloat("_BumpScale", kind == "Earth" ? .28f : .16f);
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material);
        }

        private static Texture2D Load(string kind, string channel) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/NW1_{kind}_{channel}.png");

        private static void Generate(string kind)
        {
            var height = new float[Size * Size];
            var color = new Color32[height.Length];
            var surface = new Color32[height.Length];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int i = y * Size + x;
                float u = x / (float)Size;
                float v = y / (float)Size;
                float broad = PeriodicNoise(u, v, 8f, 13.2f);
                float fine = PeriodicNoise(u, v, 95f, 6.7f);
                float detail;
                if (kind == "Fabric")
                    detail = .5f + .24f * Mathf.Sin(u*Mathf.PI*128f) * Mathf.Sin(v*Mathf.PI*128f);
                else if (kind == "Earth")
                    detail = .5f + .035f * Mathf.Sin(u*Mathf.PI*64f + broad*3f) + (fine-.5f)*.18f;
                else
                    detail = .5f + (fine-.5f)*.2f - Mathf.Pow(Mathf.Max(0, Mathf.Sin(v*Mathf.PI*180f)), 20f)*
                        Mathf.Max(0, PeriodicNoise(u,v,18f,29.1f)-.62f);
                height[i] = broad*.25f + detail*.75f;
                float value = Mathf.Clamp01(.90f + broad*.08f + (detail-.5f)*.08f);
                byte c = (byte)Mathf.RoundToInt(value*255f);
                color[i] = new Color32(c,c,c,255);
                surface[i] = new Color32(255,0,0,(byte)Mathf.RoundToInt((.65f+fine*.3f)*255f));
            }
            var normal = new Color32[height.Length];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = height[y*Size+(x+1)%Size] - height[y*Size+(x+Size-1)%Size];
                float dy = height[((y+1)%Size)*Size+x] - height[((y+Size-1)%Size)*Size+x];
                Vector3 n = new Vector3(-dx*2.4f,-dy*2.4f,1f).normalized;
                normal[y*Size+x] = new Color32((byte)((n.x*.5f+.5f)*255f),
                    (byte)((n.y*.5f+.5f)*255f),(byte)((n.z*.5f+.5f)*255f),255);
            }
            Write(kind, "Color", color, false);
            Write(kind, "Normal", normal, true);
            Surfaces[kind] = surface;
        }

        private static float PeriodicNoise(float u, float v, float scale, float seed)
        {
            float a = Mathf.Lerp(Mathf.PerlinNoise(u*scale+seed,v*scale+seed),
                Mathf.PerlinNoise((u-1)*scale+seed,v*scale+seed),u);
            float b = Mathf.Lerp(Mathf.PerlinNoise(u*scale+seed,(v-1)*scale+seed),
                Mathf.PerlinNoise((u-1)*scale+seed,(v-1)*scale+seed),u);
            return Mathf.Lerp(a,b,v);
        }

        private static void Write(string kind, string channel, Color32[] pixels, bool normal)
        {
            string path = $"{Folder}/NW1_{kind}_{channel}.png";
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, channel != "Color");
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = channel == "Color";
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.isReadable = false;
            importer.maxTextureSize = Size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
    }
}
