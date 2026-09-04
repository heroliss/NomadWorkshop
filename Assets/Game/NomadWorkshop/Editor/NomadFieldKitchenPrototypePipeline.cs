using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>
    /// 用可审查参数生成野战厨房的普通 Mesh、材质、语义层级、Prefab 和预览场景。
    /// 这是 Foundation Prototype 的作者 Harness；最终视觉可以替换 Visual_Final，
    /// 但不得改变根尺寸、Collider、交互点和铰链语义。
    /// </summary>
    public static class NomadFieldKitchenPrototypePipeline
    {
        public const string AssetId = "NW_FieldKitchen_Prototype_01";
        public const string AssetRoot =
            "Assets/Game/NomadWorkshop/Prototype/ParametricProps/" + AssetId;
        public const string ProfilePath = AssetRoot + "/" + AssetId + "_Profile.asset";
        public const string MeshFolder = AssetRoot + "/Meshes";
        public const string MaterialFolder = AssetRoot + "/Materials";
        public const string PrefabFolder = AssetRoot + "/Prefabs";
        public const string PrefabPath = PrefabFolder + "/" + AssetId + ".prefab";
        public const string PreviewFolder = AssetRoot + "/Preview";
        public const string PreviewScenePath = PreviewFolder + "/" + AssetId + "_Preview.unity";

        public const string PrototypeVisualName = "Visual_Prototype";
        public const string FinalVisualName = "Visual_Final";
        public const string StaticBodyName = "StaticBody";
        public const string LeftLowerHingeName = "Hinge_LeftLower";
        public const string CenterHingeName = "Hinge_Center";
        public const string RightLowerHingeName = "Hinge_RightLower";

        // 预览场景只在契约升级或结构损坏时重建。若调整相机、灯光或根层级，递增名称后缀；
        // 否则重复生成会因 GameObject local fileID 重分配而产生无意义的场景 diff。
        public const string PreviewMarkerName = "__NW_FieldKitchenPreview_v1";

        private const string StaticMeshPath = MeshFolder + "/NW_FieldKitchen_StaticBody.asset";
        private const string LeftDoorMeshPath = MeshFolder + "/NW_FieldKitchen_Door_LeftLower.asset";
        private const string CenterDoorMeshPath = MeshFolder + "/NW_FieldKitchen_Door_Center.asset";
        private const string RightDoorMeshPath = MeshFolder + "/NW_FieldKitchen_Door_RightLower.asset";

        private const int WornTealSlot = 0;
        private const int DarkMetalSlot = 1;
        private const int SafetyOrangeSlot = 2;
        private const int StainlessSteelSlot = 3;
        private const int RubberSlot = 4;

        private static readonly string[] MaterialNames =
        {
            "M_NW_Prototype_WornTeal",
            "M_NW_Prototype_DarkMetal",
            "M_NW_Prototype_SafetyOrange",
            "M_NW_Prototype_StainlessSteel",
            "M_NW_Prototype_Rubber",
        };

        /// <summary>创建默认配置或定位已有配置；不会生成 Mesh、Prefab 或场景。</summary>
        [MenuItem("Assets/SSFramework/游牧工坊/参数化道具/创建或定位野战厨房配置")]
        public static void CreateOrSelectProfile()
        {
            NomadFieldKitchenPrototypeProfile profile = EnsureProfile();
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        /// <summary>按当前 Profile 幂等生成全部资产，并输出机器可读审计报告。</summary>
        [MenuItem("Assets/SSFramework/游牧工坊/参数化道具/生成并审计野战厨房")]
        public static void GenerateAndReport()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode，再生成参数化道具。");

            Scene loadedPreview = SceneManager.GetSceneByPath(PreviewScenePath);
            if (loadedPreview.IsValid() && loadedPreview.isLoaded)
                throw new InvalidOperationException(
                    "参数化道具预览场景正在打开；请先切换到其他场景，避免覆盖正在查看的实例。");

            NomadFieldKitchenPrototypeProfile profile = EnsureProfile();
            profile.ValidateOrThrow();
            EnsureFolder(MeshFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder(PreviewFolder);
            EnsurePreviewSceneAsset();

            Material[] materials = CreateOrUpdateMaterials(profile);
            GeneratedMeshes meshes = CreateOrUpdateMeshes(profile);
            CreateOrUpdatePrefab(profile, meshes, materials);
            CreateOrUpdatePreviewScene();
            AssetDatabase.SaveAssets();

            NomadFieldKitchenPrototypeAudit audit = Audit();
            WriteReport(audit);
            if (!audit.Passed) throw new InvalidOperationException(audit.ToMultilineString());
            Debug.Log(audit.ToMultilineString());
        }

        /// <summary>只读取落盘产物并验证参数、普通 Mesh、语义层级、材质和预览契约。</summary>
        public static NomadFieldKitchenPrototypeAudit Audit()
        {
            var issues = new List<string>();
            NomadFieldKitchenPrototypeProfile profile =
                AssetDatabase.LoadAssetAtPath<NomadFieldKitchenPrototypeProfile>(ProfilePath);
            bool profileValid = AuditProfile(profile, issues);
            string proBuilderVersion = AuditProBuilderPackage(issues);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            bool hierarchyMatches = AuditHierarchy(prefab, issues);
            bool meshContractMatches = AuditMeshes(prefab, issues, out int meshFilterCount,
                out int vertexCount, out int triangleCount);
            bool materialContractMatches = AuditMaterials(prefab, issues);
            bool colliderContractMatches =
                AuditCollider(prefab, profile, issues, out Vector3 colliderSize);
            bool interactionContractMatches = AuditInteraction(prefab, issues);
            bool previewSceneExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(PreviewScenePath) != null;
            if (!previewSceneExists) issues.Add($"缺少预览场景：{PreviewScenePath}");

            return new NomadFieldKitchenPrototypeAudit(
                profileValid,
                !string.IsNullOrEmpty(proBuilderVersion),
                hierarchyMatches,
                meshContractMatches,
                materialContractMatches,
                colliderContractMatches,
                interactionContractMatches,
                previewSceneExists,
                proBuilderVersion,
                meshFilterCount,
                vertexCount,
                triangleCount,
                colliderSize,
                issues);
        }

        private static NomadFieldKitchenPrototypeProfile EnsureProfile()
        {
            EnsureFolder(AssetRoot);
            NomadFieldKitchenPrototypeProfile profile =
                AssetDatabase.LoadAssetAtPath<NomadFieldKitchenPrototypeProfile>(ProfilePath);
            if (profile != null) return profile;

            profile = ScriptableObject.CreateInstance<NomadFieldKitchenPrototypeProfile>();
            profile.name = AssetId + "_Profile";
            AssetDatabase.CreateAsset(profile, ProfilePath);
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }

        private static Material[] CreateOrUpdateMaterials(NomadFieldKitchenPrototypeProfile profile)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("当前项目找不到 Universal Render Pipeline/Lit Shader。");

            Color[] colors =
            {
                profile.WornTeal,
                profile.DarkMetal,
                profile.SafetyOrange,
                profile.StainlessSteel,
                profile.Rubber,
            };
            float[] metallic = { 0.62f, 0.72f, 0.48f, 0.94f, 0f };
            float[] smoothness = { 0.27f, 0.22f, 0.31f, 0.64f, 0.16f };
            var result = new Material[MaterialNames.Length];

            for (int i = 0; i < MaterialNames.Length; i++)
            {
                string path = GetMaterialPath(i);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = MaterialNames[i] };
                    AssetDatabase.CreateAsset(material, path);
                }
                else
                {
                    material.shader = shader;
                }

                material.SetColor("_BaseColor", colors[i]);
                material.SetFloat("_Metallic", metallic[i]);
                material.SetFloat("_Smoothness", smoothness[i]);
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                result[i] = material;
            }

            return result;
        }

        private static GeneratedMeshes CreateOrUpdateMeshes(
            NomadFieldKitchenPrototypeProfile profile)
        {
            Scene authorScene = SceneManager.GetActiveScene();
            Scene scratchScene = EditorSceneManager.OpenScene(
                PreviewScenePath,
                OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scratchScene);
                NomadParametricMeshBuilder.BuildResult staticBody =
                    NomadParametricMeshBuilder.Build(
                        "NW_FieldKitchen_StaticBody",
                        CreateStaticBodyParts(profile));
                DoorGeometry left = CalculateDoorGeometry(profile, DoorKind.LeftLower);
                DoorGeometry center = CalculateDoorGeometry(profile, DoorKind.Center);
                DoorGeometry right = CalculateDoorGeometry(profile, DoorKind.RightLower);
                NomadParametricMeshBuilder.BuildResult leftDoor = BuildDoor(
                    "NW_FieldKitchen_Door_LeftLower", left, profile, true);
                NomadParametricMeshBuilder.BuildResult centerDoor = BuildDoor(
                    "NW_FieldKitchen_Door_Center", center, profile, true);
                NomadParametricMeshBuilder.BuildResult rightDoor = BuildDoor(
                    "NW_FieldKitchen_Door_RightLower", right, profile, false);

                return new GeneratedMeshes(
                    SaveMeshAsset(staticBody, StaticMeshPath),
                    SaveMeshAsset(leftDoor, LeftDoorMeshPath),
                    SaveMeshAsset(centerDoor, CenterDoorMeshPath),
                    SaveMeshAsset(rightDoor, RightDoorMeshPath));
            }
            finally
            {
                // ProBuilder API 会先在 Active Scene 创建临时对象。把它们限制在可丢弃的
                // Additive Scratch Scene，关闭时不保存，当前用户场景的 dirty 状态保持原样。
                if (authorScene.IsValid() && authorScene.isLoaded)
                    SceneManager.SetActiveScene(authorScene);
                EditorSceneManager.CloseScene(scratchScene, true);
            }
        }

        private static IReadOnlyList<NomadParametricMeshBuilder.Part> CreateStaticBodyParts(
            NomadFieldKitchenPrototypeProfile p)
        {
            var parts = new List<NomadParametricMeshBuilder.Part>();
            float halfWidth = p.Width * 0.5f;
            float halfDepth = p.Depth * 0.5f;
            float baseTop = p.CounterHeight - p.CounterThickness * 0.5f;
            float bodyHeight = baseTop - p.FootHeight;
            float bodyCenterY = p.FootHeight + bodyHeight * 0.5f;
            float frontZ = -halfDepth - p.PanelThickness * 0.52f;
            float rearZ = halfDepth - p.PanelThickness * 0.5f;
            float bevel = p.BevelWidth;
            float fineBevel = Mathf.Min(bevel * 0.5f, p.PanelThickness * 0.25f);
            float rightRegionWidth = p.Width - p.LeftTowerWidth;
            float middleDividerX = -halfWidth + p.LeftTowerWidth + rightRegionWidth * 0.5f;

            // 承重壳体：背板、底板、侧柱、分区立柱、台面和可见底脚。
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(p.Width, bodyHeight, p.PanelThickness),
                new Vector3(0f, bodyCenterY, rearZ), WornTealSlot, fineBevel));
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(p.Width - p.FrameThickness * 2f, p.PanelThickness, p.Depth),
                new Vector3(0f, p.FootHeight + p.PanelThickness * 0.5f, 0f),
                DarkMetalSlot, fineBevel));
            AddVerticalPost(parts, -halfWidth + p.FrameThickness * 0.5f, bodyCenterY,
                bodyHeight, p, DarkMetalSlot);
            AddVerticalPost(parts, halfWidth - p.FrameThickness * 0.5f, bodyCenterY,
                bodyHeight, p, DarkMetalSlot);
            AddVerticalPost(parts, -halfWidth + p.LeftTowerWidth, bodyCenterY,
                bodyHeight, p, DarkMetalSlot);
            AddVerticalPost(parts, middleDividerX, bodyCenterY,
                bodyHeight, p, DarkMetalSlot);
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(p.Width + 0.06f, p.CounterThickness, p.Depth + 0.08f),
                new Vector3(0f, p.CounterHeight, -0.015f),
                StainlessSteelSlot, bevel));

            float footSize = p.FrameThickness * 1.45f;
            float footY = p.FootHeight * 0.5f;
            float footX = halfWidth - p.FrameThickness * 1.3f;
            float footZ = halfDepth - p.FrameThickness * 1.3f;
            foreach (float x in new[] { -footX, footX })
            foreach (float z in new[] { -footZ, footZ })
                parts.Add(NomadParametricMeshBuilder.Part.Box(
                    new Vector3(footSize, p.FootHeight, footSize),
                    new Vector3(x, footY, z), RubberSlot, fineBevel));

            // 左侧上半部分是静态通风和控制区，明确不属于左下门。
            float towerCenterX = -halfWidth + p.LeftTowerWidth * 0.5f;
            float upperBottom = Mathf.Max(p.CounterHeight + p.DoorGap, 1.08f);
            float upperHeight = p.Height - upperBottom;
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(p.LeftTowerWidth, upperHeight, p.PanelThickness),
                new Vector3(towerCenterX, upperBottom + upperHeight * 0.5f, rearZ),
                WornTealSlot, fineBevel));
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(p.LeftTowerWidth, p.PanelThickness, p.Depth),
                new Vector3(towerCenterX, p.Height - p.PanelThickness * 0.5f, 0f),
                DarkMetalSlot, fineBevel));
            float upperCenterY = upperBottom + upperHeight * 0.5f;
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(p.PanelThickness, upperHeight, p.Depth),
                new Vector3(-halfWidth + p.PanelThickness * 0.5f, upperCenterY, 0f),
                DarkMetalSlot, fineBevel));
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(p.PanelThickness, upperHeight, p.Depth),
                new Vector3(-halfWidth + p.LeftTowerWidth - p.PanelThickness * 0.5f,
                    upperCenterY, 0f),
                DarkMetalSlot, fineBevel));
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(p.LeftTowerWidth, p.PanelThickness, p.Depth),
                new Vector3(towerCenterX, upperBottom + p.PanelThickness * 0.5f, 0f),
                DarkMetalSlot, fineBevel));
            float controlHeight = Mathf.Min(0.58f, upperHeight - p.FrameThickness * 2f);
            float controlY = upperBottom + upperHeight * 0.48f;
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(p.LeftTowerWidth - p.FrameThickness * 0.7f,
                    upperHeight - p.FrameThickness, p.PanelThickness),
                new Vector3(towerCenterX, upperCenterY, frontZ),
                WornTealSlot, bevel));
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(p.LeftTowerWidth - p.FrameThickness,
                    controlHeight + 0.055f, p.PanelThickness),
                new Vector3(towerCenterX, controlY, frontZ - p.PanelThickness * 0.55f),
                DarkMetalSlot, bevel));
            float controlFrontZ = frontZ - p.PanelThickness * 1.12f;
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(p.LeftTowerWidth - p.FrameThickness * 1.5f,
                    controlHeight, p.PanelThickness),
                new Vector3(towerCenterX, controlY, controlFrontZ),
                WornTealSlot, bevel));

            for (int i = 0; i < 5; i++)
            {
                parts.Add(NomadParametricMeshBuilder.Part.Box(
                    new Vector3(p.LeftTowerWidth * 0.56f, 0.025f, 0.018f),
                    new Vector3(towerCenterX - 0.04f,
                        controlY + 0.16f - i * 0.065f,
                        controlFrontZ - p.PanelThickness * 0.65f),
                    DarkMetalSlot, 0.004f));
            }

            for (int i = 0; i < 3; i++)
            {
                parts.Add(NomadParametricMeshBuilder.Part.Cylinder(
                    0.055f,
                    0.026f,
                    new Vector3(towerCenterX + p.LeftTowerWidth * 0.27f,
                        controlY + 0.15f - i * 0.13f,
                        controlFrontZ - p.PanelThickness * 0.85f),
                    Quaternion.Euler(90f, 0f, 0f),
                    i == 0 ? SafetyOrangeSlot : DarkMetalSlot,
                    12));
            }

            // 右侧抽屉保持静态，下面才是可动门。
            DoorGeometry rightDoor = CalculateDoorGeometry(p, DoorKind.RightLower);
            float drawerHeight = Mathf.Max(0.12f, baseTop - rightDoor.Top - p.DoorGap * 2f);
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(rightDoor.Width, drawerHeight, p.PanelThickness),
                new Vector3(rightDoor.CenterX,
                    rightDoor.Top + p.DoorGap + drawerHeight * 0.5f,
                    frontZ), WornTealSlot, bevel));
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(rightDoor.Width * 0.42f, 0.025f, 0.035f),
                new Vector3(rightDoor.CenterX,
                    rightDoor.Top + p.DoorGap + drawerHeight * 0.5f,
                    frontZ - p.PanelThickness * 0.8f), DarkMetalSlot, 0.005f));

            // 台面功能轮廓：双炉盘、浅水槽和低成本可读的水龙头。
            float workStart = -halfWidth + p.LeftTowerWidth;
            float workWidth = p.Width - p.LeftTowerWidth;
            float burnerX = workStart + workWidth * 0.27f;
            float topY = p.CounterHeight + p.CounterThickness * 0.56f;
            foreach (float xOffset in new[] { -0.14f, 0.14f })
                parts.Add(NomadParametricMeshBuilder.Part.Cylinder(
                    0.24f, 0.022f,
                    new Vector3(burnerX + xOffset, topY, -0.08f),
                    Quaternion.identity, DarkMetalSlot, 16));

            float sinkX = workStart + workWidth * 0.76f;
            float sinkWidth = workWidth * 0.31f;
            float sinkDepth = p.Depth * 0.47f;
            float sinkRim = 0.032f;
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(sinkWidth - sinkRim * 2f, 0.012f,
                    sinkDepth - sinkRim * 2f),
                new Vector3(sinkX, topY + 0.007f, 0.04f),
                DarkMetalSlot, 0.004f));
            foreach (float x in new[] { sinkX - sinkWidth * 0.5f, sinkX + sinkWidth * 0.5f })
                parts.Add(NomadParametricMeshBuilder.Part.Box(
                    new Vector3(sinkRim, 0.026f, sinkDepth),
                    new Vector3(x, topY + 0.017f, 0.04f),
                    StainlessSteelSlot, 0.006f));
            foreach (float z in new[] { 0.04f - sinkDepth * 0.5f, 0.04f + sinkDepth * 0.5f })
                parts.Add(NomadParametricMeshBuilder.Part.Box(
                    new Vector3(sinkWidth, 0.026f, sinkRim),
                    new Vector3(sinkX, topY + 0.017f, z),
                    StainlessSteelSlot, 0.006f));
            parts.Add(NomadParametricMeshBuilder.Part.Cylinder(
                0.035f, 0.33f,
                new Vector3(sinkX + workWidth * 0.13f,
                    topY + 0.165f, p.Depth * 0.22f),
                Quaternion.identity, StainlessSteelSlot, 12));
            parts.Add(NomadParametricMeshBuilder.Part.Cylinder(
                0.032f, 0.22f,
                new Vector3(sinkX + workWidth * 0.065f,
                    topY + 0.31f, p.Depth * 0.22f),
                Quaternion.Euler(0f, 0f, 90f), StainlessSteelSlot, 12));

            // 前沿安全标记帮助俯视镜头识别设施朝向。
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(0.24f, 0.045f, 0.035f),
                new Vector3(halfWidth - 0.18f,
                    p.CounterHeight - p.CounterThickness * 0.1f,
                    -halfDepth - 0.035f), SafetyOrangeSlot, 0.007f));
            return parts;
        }

        private static void AddVerticalPost(
            ICollection<NomadParametricMeshBuilder.Part> parts,
            float x,
            float centerY,
            float height,
            NomadFieldKitchenPrototypeProfile profile,
            int materialSlot)
        {
            parts.Add(NomadParametricMeshBuilder.Part.Box(
                new Vector3(profile.FrameThickness, height, profile.Depth),
                new Vector3(x, centerY, 0f),
                materialSlot,
                Mathf.Min(profile.BevelWidth, profile.FrameThickness * 0.3f)));
        }

        private static NomadParametricMeshBuilder.BuildResult BuildDoor(
            string meshName,
            DoorGeometry geometry,
            NomadFieldKitchenPrototypeProfile profile,
            bool extendsRightFromHinge)
        {
            float direction = extendsRightFromHinge ? 1f : -1f;
            float panelCenterX = direction * geometry.Width * 0.5f;
            float handleX = direction * geometry.Width * 0.82f;
            float handleY = geometry.Height * 0.15f;
            var parts = new List<NomadParametricMeshBuilder.Part>
            {
                NomadParametricMeshBuilder.Part.Box(
                    new Vector3(geometry.Width, geometry.Height, profile.PanelThickness),
                    new Vector3(panelCenterX, 0f, 0f),
                    WornTealSlot,
                    profile.BevelWidth),
                NomadParametricMeshBuilder.Part.Box(
                    new Vector3(0.035f, geometry.Height * 0.34f, 0.045f),
                    new Vector3(handleX, handleY, -profile.PanelThickness * 0.92f),
                    DarkMetalSlot,
                    0.006f),
                NomadParametricMeshBuilder.Part.Box(
                    new Vector3(0.045f, geometry.Height * 0.11f, 0.055f),
                    new Vector3(direction * geometry.Width * 0.03f,
                        geometry.Height * 0.31f, profile.PanelThickness * 0.05f),
                    SafetyOrangeSlot,
                    0.006f),
            };
            return NomadParametricMeshBuilder.Build(meshName, parts);
        }

        private static DoorGeometry CalculateDoorGeometry(
            NomadFieldKitchenPrototypeProfile p,
            DoorKind kind)
        {
            float leftEdge = -p.Width * 0.5f;
            float workStart = leftEdge + p.LeftTowerWidth;
            float workWidth = p.Width - p.LeftTowerWidth;
            float bottom = p.FootHeight + p.DoorGap;
            float fullTop = p.CounterHeight - p.CounterThickness * 0.65f - p.DoorGap;

            return kind switch
            {
                DoorKind.LeftLower => DoorGeometry.FromEdges(
                    leftEdge + p.DoorGap,
                    workStart - p.DoorGap,
                    bottom,
                    Mathf.Min(fullTop, 0.74f)),
                DoorKind.Center => DoorGeometry.FromEdges(
                    workStart + p.DoorGap,
                    workStart + workWidth * 0.5f - p.DoorGap,
                    bottom,
                    fullTop),
                DoorKind.RightLower => DoorGeometry.FromEdges(
                    workStart + workWidth * 0.5f + p.DoorGap,
                    p.Width * 0.5f - p.DoorGap,
                    bottom,
                    Mathf.Min(fullTop, 0.76f)),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
        }

        private static MeshAsset SaveMeshAsset(
            NomadParametricMeshBuilder.BuildResult build,
            string assetPath)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (mesh == null)
            {
                mesh = build.Mesh;
                AssetDatabase.CreateAsset(mesh, assetPath);
            }
            else
            {
                CopyMeshData(build.Mesh, mesh);
                mesh.name = build.Mesh.name;
                UnityEngine.Object.DestroyImmediate(build.Mesh);
                EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssetIfDirty(mesh);
            }

            return new MeshAsset(mesh, build.MaterialSlots);
        }

        private static void CopyMeshData(Mesh source, Mesh destination)
        {
            destination.Clear(false);
            destination.indexFormat = source.indexFormat;
            destination.vertices = source.vertices;

            Vector3[] normals = source.normals;
            if (normals.Length == source.vertexCount) destination.normals = normals;
            Vector4[] tangents = source.tangents;
            if (tangents.Length == source.vertexCount) destination.tangents = tangents;
            Color32[] colors = source.colors32;
            if (colors.Length == source.vertexCount) destination.colors32 = colors;

            CopyUvChannel(source.uv, values => destination.uv = values, source.vertexCount);
            CopyUvChannel(source.uv2, values => destination.uv2 = values, source.vertexCount);
            CopyUvChannel(source.uv3, values => destination.uv3 = values, source.vertexCount);
            CopyUvChannel(source.uv4, values => destination.uv4 = values, source.vertexCount);
            CopyUvChannel(source.uv5, values => destination.uv5 = values, source.vertexCount);
            CopyUvChannel(source.uv6, values => destination.uv6 = values, source.vertexCount);
            CopyUvChannel(source.uv7, values => destination.uv7 = values, source.vertexCount);
            CopyUvChannel(source.uv8, values => destination.uv8 = values, source.vertexCount);

            destination.subMeshCount = source.subMeshCount;
            for (int i = 0; i < source.subMeshCount; i++)
                destination.SetIndices(
                    source.GetIndices(i, true),
                    source.GetTopology(i),
                    i,
                    false,
                    0);
            destination.bounds = source.bounds;
            destination.UploadMeshData(false);
        }

        private static void CopyUvChannel(
            Vector2[] values,
            Action<Vector2[]> assign,
            int vertexCount)
        {
            if (values.Length == vertexCount) assign(values);
        }

        private static void CreateOrUpdatePrefab(
            NomadFieldKitchenPrototypeProfile profile,
            GeneratedMeshes meshes,
            Material[] materials)
        {
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(AssetId);
                SceneManager.MoveGameObjectToScene(root, previewScene);

                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, profile.Height * 0.5f, 0f);
                collider.size = new Vector3(profile.Width, profile.Height, profile.Depth);

                Transform anchors = CreateChild(root.transform, "Anchors");
                Transform workPosition = CreateAnchor(
                    anchors, "WorkPosition",
                    new Vector3(0.15f, 0f, -profile.Depth * 0.5f - 0.68f),
                    Quaternion.identity);
                Transform handTarget = CreateAnchor(
                    anchors, "PrimaryHandTarget",
                    new Vector3(0.15f, profile.CounterHeight + 0.04f,
                        -profile.Depth * 0.5f - 0.08f),
                    Quaternion.identity);
                CreateAnchor(anchors, "StorageAccess",
                    new Vector3(0f, 0.62f, -profile.Depth * 0.5f - 0.08f),
                    Quaternion.identity);
                CreateAnchor(anchors, "WaterInput",
                    new Vector3(profile.Width * 0.5f, 0.42f, profile.Depth * 0.28f),
                    Quaternion.Euler(0f, 90f, 0f));
                CreateAnchor(anchors, "WasteOutput",
                    new Vector3(profile.Width * 0.5f, 0.18f, profile.Depth * 0.2f),
                    Quaternion.Euler(0f, 90f, 0f));

                FacilityInteractionAnchor interaction =
                    root.AddComponent<FacilityInteractionAnchor>();
                interaction.ConfigureRuntime(
                    "PrepareMeal",
                    ResidentAnimationSemantic.Work,
                    workPosition,
                    handTarget);

                Transform prototypeVisual = CreateChild(root.transform, PrototypeVisualName);
                CreateMeshObject(prototypeVisual, StaticBodyName, meshes.StaticBody, materials);

                float frontZ = -profile.Depth * 0.5f - profile.PanelThickness * 0.52f;
                DoorGeometry left = CalculateDoorGeometry(profile, DoorKind.LeftLower);
                DoorGeometry center = CalculateDoorGeometry(profile, DoorKind.Center);
                DoorGeometry right = CalculateDoorGeometry(profile, DoorKind.RightLower);
                CreateDoorObject(prototypeVisual, LeftLowerHingeName, left.Left,
                    left.CenterY, frontZ, meshes.LeftDoor, materials);
                CreateDoorObject(prototypeVisual, CenterHingeName, center.Left,
                    center.CenterY, frontZ, meshes.CenterDoor, materials);
                CreateDoorObject(prototypeVisual, RightLowerHingeName, right.Right,
                    right.CenterY, frontZ, meshes.RightDoor, materials);

                Transform finalVisual = CreateChild(root.transform, FinalVisualName);
                finalVisual.gameObject.SetActive(false);

                ResetLocalTransform(root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (!success) throw new InvalidOperationException($"保存 Prefab 失败：{PrefabPath}");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static void CreateMeshObject(
            Transform parent,
            string name,
            MeshAsset meshAsset,
            IReadOnlyList<Material> materials)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            MeshFilter filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = meshAsset.Mesh;
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = meshAsset.MaterialSlots
                .Select(slot => materials[slot])
                .ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private static void CreateDoorObject(
            Transform parent,
            string hingeName,
            float hingeX,
            float hingeY,
            float hingeZ,
            MeshAsset mesh,
            IReadOnlyList<Material> materials)
        {
            Transform hinge = CreateChild(parent, hingeName);
            hinge.localPosition = new Vector3(hingeX, hingeY, hingeZ);
            CreateMeshObject(hinge, hingeName.Replace("Hinge_", "Door_") + "_Visual",
                mesh, materials);
        }

        private static void CreateOrUpdatePreviewScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new InvalidOperationException($"无法加载 Prefab：{PrefabPath}");

            int rendererIndex = NomadRenderingSpikePipeline.GetSecondaryRendererIndexOrThrow();
            Material groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                NomadRenderingSpikePipeline.GroundMaterialPath);
            if (groundMaterial == null)
                throw new InvalidOperationException(
                    "缺少 3D Rendering Spike 地面材质，请先重放对应配置菜单。");

            EnsurePreviewSceneAsset();

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                if (CanReusePreviewScene(scene, prefab, groundMaterial)) return;

                foreach (GameObject root in scene.GetRootGameObjects())
                    UnityEngine.Object.DestroyImmediate(root);

                var marker = new GameObject(PreviewMarkerName);
                SceneManager.MoveGameObjectToScene(marker, scene);

                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (instance == null) throw new InvalidOperationException("无法实例化参数化道具 Prefab。");
                instance.name = AssetId;

                // 预览实例故意展开三扇门，Prefab 本身仍保持闭合零姿态。
                SetPreviewHinge(instance.transform, LeftLowerHingeName, -34f);
                SetPreviewHinge(instance.transform, CenterHingeName, -48f);
                SetPreviewHinge(instance.transform, RightLowerHingeName, 40f);

                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "PreviewGround";
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.position = new Vector3(0f, -0.055f, 0f);
                ground.transform.localScale = new Vector3(5.2f, 0.1f, 4.6f);
                ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
                UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());

                var cameraObject = new GameObject("Main Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.025f, 0.032f, 0.038f, 1f);
                camera.fieldOfView = 37f;
                camera.depth = 10f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 50f;
                camera.allowHDR = true;
                UniversalAdditionalCameraData cameraData =
                    cameraObject.AddComponent<UniversalAdditionalCameraData>();
                cameraData.SetRenderer(rendererIndex);
                cameraObject.transform.position = new Vector3(3.35f, 2.45f, -4.15f);
                cameraObject.transform.LookAt(new Vector3(0f, 0.95f, 0f));

                CreateDirectionalLight(scene, "Key Light",
                    new Color(1f, 0.83f, 0.67f, 1f), 1.55f,
                    Quaternion.Euler(43f, -37f, 0f), true);
                CreatePointLight(scene, "Cool Fill",
                    new Color(0.38f, 0.62f, 1f, 1f), 2.4f, 7f,
                    new Vector3(-2.6f, 2.15f, -1.8f));
                CreatePointLight(scene, "Rim Light",
                    new Color(0.62f, 0.72f, 0.86f, 1f), 1.15f, 5.5f,
                    new Vector3(2.3f, 1.65f, 2.1f));

                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.19f, 0.21f, 0.23f, 1f);
                RenderSettings.skybox = null;

                if (!EditorSceneManager.SaveScene(scene, PreviewScenePath))
                    throw new InvalidOperationException($"保存预览场景失败：{PreviewScenePath}");
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static bool CanReusePreviewScene(
            Scene scene,
            GameObject prefab,
            Material groundMaterial)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            string[] expectedRootNames =
            {
                PreviewMarkerName,
                AssetId,
                "PreviewGround",
                "Main Camera",
                "Key Light",
                "Cool Fill",
                "Rim Light",
            };
            if (roots.Length != expectedRootNames.Length ||
                expectedRootNames.Any(name => roots.Count(root => root.name == name) != 1))
                return false;

            GameObject instance = roots.Single(root => root.name == AssetId);
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance) != PrefabPath ||
                PrefabUtility.GetCorrespondingObjectFromSource(instance) != prefab)
                return false;

            Transform prototype = instance.transform.Find(PrototypeVisualName);
            if (prototype == null ||
                !HasPreviewHingeRotation(prototype, LeftLowerHingeName, -34f) ||
                !HasPreviewHingeRotation(prototype, CenterHingeName, -48f) ||
                !HasPreviewHingeRotation(prototype, RightLowerHingeName, 40f))
                return false;

            GameObject ground = roots.Single(root => root.name == "PreviewGround");
            MeshRenderer groundRenderer = ground.GetComponent<MeshRenderer>();
            if (groundRenderer == null || groundRenderer.sharedMaterial != groundMaterial ||
                ground.GetComponent<Collider>() != null)
                return false;

            GameObject cameraObject = roots.Single(root => root.name == "Main Camera");
            if (cameraObject.GetComponent<Camera>() == null ||
                cameraObject.GetComponent<UniversalAdditionalCameraData>() == null)
                return false;

            return HasLight(roots, "Key Light", LightType.Directional) &&
                   HasLight(roots, "Cool Fill", LightType.Point) &&
                   HasLight(roots, "Rim Light", LightType.Point);
        }

        private static bool HasPreviewHingeRotation(
            Transform prototype,
            string hingeName,
            float angle)
        {
            Transform hinge = prototype.Find(hingeName);
            return hinge != null &&
                   Quaternion.Angle(hinge.localRotation, Quaternion.Euler(0f, angle, 0f)) <= 0.01f;
        }

        private static bool HasLight(
            IEnumerable<GameObject> roots,
            string name,
            LightType type)
        {
            Light light = roots.Single(root => root.name == name).GetComponent<Light>();
            return light != null && light.type == type;
        }

        private static void EnsurePreviewSceneAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PreviewScenePath) != null) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    NomadRenderingSpikePipeline.PreviewScenePath) == null)
                throw new InvalidOperationException(
                    "缺少可复制的 3D Rendering Spike 预览场景，请先重放对应配置菜单。");
            if (!AssetDatabase.CopyAsset(
                    NomadRenderingSpikePipeline.PreviewScenePath,
                    PreviewScenePath))
                throw new InvalidOperationException($"无法创建预览场景资产：{PreviewScenePath}");
            AssetDatabase.ImportAsset(PreviewScenePath,
                ImportAssetOptions.ForceSynchronousImport);
        }

        private static void SetPreviewHinge(Transform root, string hingeName, float angle)
        {
            Transform hinge = root.Find(PrototypeVisualName + "/" + hingeName);
            if (hinge == null) throw new InvalidOperationException($"预览缺少铰链：{hingeName}");
            hinge.localRotation = Quaternion.Euler(0f, angle, 0f);
        }

        private static void CreateDirectionalLight(
            Scene scene,
            string name,
            Color color,
            float intensity,
            Quaternion rotation,
            bool shadows)
        {
            var lightObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            lightObject.transform.rotation = rotation;
        }

        private static void CreatePointLight(
            Scene scene,
            string name,
            Color color,
            float intensity,
            float range,
            Vector3 position)
        {
            var lightObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            lightObject.transform.position = position;
        }

        private static bool AuditProfile(
            NomadFieldKitchenPrototypeProfile profile,
            ICollection<string> issues)
        {
            if (profile == null)
            {
                issues.Add($"缺少参数配置：{ProfilePath}");
                return false;
            }

            try
            {
                profile.ValidateOrThrow();
                return true;
            }
            catch (Exception exception)
            {
                issues.Add("参数配置无效：" + exception.Message);
                return false;
            }
        }

        private static string AuditProBuilderPackage(ICollection<string> issues)
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(ProBuilderMesh).Assembly);
            if (package == null || package.name != "com.unity.probuilder")
            {
                issues.Add("无法确认生成器引用的是 Unity ProBuilder Package。");
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(package.version))
            {
                issues.Add("ProBuilder Package 没有可审计的版本号。");
                return string.Empty;
            }

            return package.version;
        }

        private static bool AuditHierarchy(GameObject prefab, ICollection<string> issues)
        {
            if (prefab == null)
            {
                issues.Add($"缺少生成 Prefab：{PrefabPath}");
                return false;
            }

            bool rootIdentity = Approximately(prefab.transform.localPosition, Vector3.zero) &&
                                Quaternion.Angle(prefab.transform.localRotation,
                                    Quaternion.identity) <= 0.01f &&
                                Approximately(prefab.transform.localScale, Vector3.one);
            Transform prototype = prefab.transform.Find(PrototypeVisualName);
            Transform final = prefab.transform.Find(FinalVisualName);
            Transform staticBody = prefab.transform.Find(PrototypeVisualName + "/" + StaticBodyName);
            Transform left = prefab.transform.Find(PrototypeVisualName + "/" + LeftLowerHingeName);
            Transform center = prefab.transform.Find(PrototypeVisualName + "/" + CenterHingeName);
            Transform right = prefab.transform.Find(PrototypeVisualName + "/" + RightLowerHingeName);
            bool matches = rootIdentity && prototype != null && prototype.gameObject.activeSelf &&
                           final != null && !final.gameObject.activeSelf && staticBody != null &&
                           left != null && center != null && right != null &&
                           Quaternion.Angle(left.localRotation, Quaternion.identity) <= 0.01f &&
                           Quaternion.Angle(center.localRotation, Quaternion.identity) <= 0.01f &&
                           Quaternion.Angle(right.localRotation, Quaternion.identity) <= 0.01f;
            if (!matches)
                issues.Add("Prefab 的 Prototype / Final、静态主体、三铰链或闭合零姿态不符合契约。");
            return matches;
        }

        private static bool AuditMeshes(
            GameObject prefab,
            ICollection<string> issues,
            out int meshFilterCount,
            out int vertexCount,
            out int triangleCount)
        {
            meshFilterCount = 0;
            vertexCount = 0;
            triangleCount = 0;
            if (prefab == null) return false;

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            meshFilterCount = filters.Length;
            bool matches = filters.Length == 4 &&
                           prefab.GetComponentsInChildren<ProBuilderMesh>(true).Length == 0;
            foreach (MeshFilter filter in filters)
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    matches = false;
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(mesh);
                if (!path.StartsWith(MeshFolder + "/", StringComparison.Ordinal) ||
                    mesh.vertexCount == 0 || mesh.subMeshCount == 0)
                    matches = false;
                vertexCount += mesh.vertexCount;
                for (int i = 0; i < mesh.subMeshCount; i++)
                    triangleCount += checked((int)mesh.GetIndexCount(i) / 3);
            }

            if (triangleCount < 250 || triangleCount > 12000) matches = false;
            if (!matches)
                issues.Add(
                    "生成 Prefab 应只有 1 个静态 Mesh + 3 个门 Mesh、不得保留 ProBuilder Component，且三角面预算应位于 250–12000。");
            return matches;
        }

        private static bool AuditMaterials(GameObject prefab, ICollection<string> issues)
        {
            if (prefab == null) return false;
            Shader expectedShader = Shader.Find("Universal Render Pipeline/Lit");
            bool matches = expectedShader != null;
            var usedPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader != expectedShader ||
                        !material.enableInstancing)
                    {
                        matches = false;
                        continue;
                    }

                    usedPaths.Add(AssetDatabase.GetAssetPath(material));
                }
            }

            for (int i = 0; i < MaterialNames.Length; i++)
                matches &= usedPaths.Contains(GetMaterialPath(i));
            if (!matches)
                issues.Add("五个原型材质必须使用 URP/Lit、开启 GPU Instancing，并全部被 Prefab 消费。");
            return matches;
        }

        private static bool AuditCollider(
            GameObject prefab,
            NomadFieldKitchenPrototypeProfile profile,
            ICollection<string> issues,
            out Vector3 colliderSize)
        {
            colliderSize = Vector3.zero;
            if (prefab == null || profile == null) return false;
            BoxCollider[] colliders = prefab.GetComponentsInChildren<BoxCollider>(true);
            bool matches = colliders.Length == 1;
            if (matches)
            {
                colliderSize = colliders[0].size;
                matches = Approximately(
                              colliderSize,
                              new Vector3(profile.Width, profile.Height, profile.Depth),
                              0.0001f) &&
                          Approximately(
                              colliders[0].center,
                              new Vector3(0f, profile.Height * 0.5f, 0f),
                              0.0001f);
            }

            if (!matches)
                issues.Add("根级玩法 BoxCollider 必须精确等于 Profile 的宽、高、深且从地面起算。");
            return matches;
        }

        private static bool AuditInteraction(GameObject prefab, ICollection<string> issues)
        {
            if (prefab == null) return false;
            FacilityInteractionAnchor[] interactions =
                prefab.GetComponentsInChildren<FacilityInteractionAnchor>(true);
            Transform anchors = prefab.transform.Find("Anchors");
            bool matches = interactions.Length == 1 && interactions[0].IsConfigured &&
                           interactions[0].ActionId == "PrepareMeal" &&
                           interactions[0].AnimationSemantic == ResidentAnimationSemantic.Work &&
                           anchors != null &&
                           anchors.Find("WorkPosition") != null &&
                           anchors.Find("PrimaryHandTarget") != null &&
                           anchors.Find("StorageAccess") != null &&
                           anchors.Find("WaterInput") != null &&
                           anchors.Find("WasteOutput") != null;
            if (!matches)
                issues.Add("野战厨房缺少工作、手部、储物、进水或排污交互接缝。");
            return matches;
        }

        private static string GetMaterialPath(int slot)
        {
            if (slot < 0 || slot >= MaterialNames.Length)
                throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
            return MaterialFolder + "/" + MaterialNames[slot] + ".mat";
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static Transform CreateAnchor(
            Transform parent,
            string name,
            Vector3 localPosition,
            Quaternion localRotation)
        {
            Transform anchor = CreateChild(parent, name);
            anchor.localPosition = localPosition;
            anchor.localRotation = localRotation;
            return anchor;
        }

        private static void ResetLocalTransform(Transform transform)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        private static bool Approximately(Vector3 left, Vector3 right, float tolerance = 0.0001f)
        {
            return Mathf.Abs(left.x - right.x) <= tolerance &&
                   Mathf.Abs(left.y - right.y) <= tolerance &&
                   Mathf.Abs(left.z - right.z) <= tolerance;
        }

        private static void EnsureFolder(string folderPath)
        {
            string[] segments = folderPath.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }

        private static void WriteReport(NomadFieldKitchenPrototypeAudit audit)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 throw new InvalidOperationException("无法定位项目根目录。");
            string outputFolder = Path.Combine(
                projectRoot,
                "ArtPipelineOutput",
                "ParametricProps",
                AssetId);
            Directory.CreateDirectory(outputFolder);
            var report = new PrototypeReport
            {
                schemaVersion = 1,
                status = audit.Passed ? "passed" : "failed",
                assetId = AssetId,
                unityVersion = Application.unityVersion,
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                profilePath = ProfilePath,
                prefabPath = PrefabPath,
                previewScenePath = PreviewScenePath,
                proBuilderVersion = audit.ProBuilderVersion,
                meshFilterCount = audit.MeshFilterCount,
                vertexCount = audit.VertexCount,
                triangleCount = audit.TriangleCount,
                colliderSize = audit.ColliderSize,
                issues = audit.Issues.ToArray(),
                manualArtReviewStillRequired = true,
            };
            File.WriteAllText(
                Path.Combine(outputFolder, "report.json"),
                JsonUtility.ToJson(report, true) + Environment.NewLine);
        }

        private enum DoorKind
        {
            LeftLower,
            Center,
            RightLower,
        }

        private readonly struct DoorGeometry
        {
            private DoorGeometry(float left, float right, float bottom, float top)
            {
                Left = left;
                Right = right;
                Bottom = bottom;
                Top = top;
            }

            internal float Left { get; }
            internal float Right { get; }
            internal float Bottom { get; }
            internal float Top { get; }
            internal float Width => Right - Left;
            internal float Height => Top - Bottom;
            internal float CenterX => (Left + Right) * 0.5f;
            internal float CenterY => (Bottom + Top) * 0.5f;

            internal static DoorGeometry FromEdges(
                float left,
                float right,
                float bottom,
                float top)
            {
                if (right <= left || top <= bottom)
                    throw new InvalidOperationException("门板尺寸无效，请检查 Profile 的分区与门缝参数。");
                return new DoorGeometry(left, right, bottom, top);
            }
        }

        private readonly struct MeshAsset
        {
            internal MeshAsset(Mesh mesh, int[] materialSlots)
            {
                Mesh = mesh;
                MaterialSlots = materialSlots;
            }

            internal Mesh Mesh { get; }
            internal int[] MaterialSlots { get; }
        }

        private readonly struct GeneratedMeshes
        {
            internal GeneratedMeshes(
                MeshAsset staticBody,
                MeshAsset leftDoor,
                MeshAsset centerDoor,
                MeshAsset rightDoor)
            {
                StaticBody = staticBody;
                LeftDoor = leftDoor;
                CenterDoor = centerDoor;
                RightDoor = rightDoor;
            }

            internal MeshAsset StaticBody { get; }
            internal MeshAsset LeftDoor { get; }
            internal MeshAsset CenterDoor { get; }
            internal MeshAsset RightDoor { get; }
        }

        [Serializable]
        private sealed class PrototypeReport
        {
            public int schemaVersion;
            public string status = string.Empty;
            public string assetId = string.Empty;
            public string unityVersion = string.Empty;
            public string generatedAtUtc = string.Empty;
            public string profilePath = string.Empty;
            public string prefabPath = string.Empty;
            public string previewScenePath = string.Empty;
            public string proBuilderVersion = string.Empty;
            public int meshFilterCount;
            public int vertexCount;
            public int triangleCount;
            public Vector3 colliderSize;
            public string[] issues = Array.Empty<string>();
            public bool manualArtReviewStillRequired;
        }
    }

    /// <summary>参数化野战厨房从 Profile 到普通 Unity Prefab 的只读证据。</summary>
    public sealed class NomadFieldKitchenPrototypeAudit
    {
        public NomadFieldKitchenPrototypeAudit(
            bool profileValid,
            bool proBuilderPackageResolved,
            bool hierarchyMatches,
            bool meshContractMatches,
            bool materialContractMatches,
            bool colliderContractMatches,
            bool interactionContractMatches,
            bool previewSceneExists,
            string proBuilderVersion,
            int meshFilterCount,
            int vertexCount,
            int triangleCount,
            Vector3 colliderSize,
            IReadOnlyList<string> issues)
        {
            ProfileValid = profileValid;
            ProBuilderPackageResolved = proBuilderPackageResolved;
            HierarchyMatches = hierarchyMatches;
            MeshContractMatches = meshContractMatches;
            MaterialContractMatches = materialContractMatches;
            ColliderContractMatches = colliderContractMatches;
            InteractionContractMatches = interactionContractMatches;
            PreviewSceneExists = previewSceneExists;
            ProBuilderVersion = proBuilderVersion;
            MeshFilterCount = meshFilterCount;
            VertexCount = vertexCount;
            TriangleCount = triangleCount;
            ColliderSize = colliderSize;
            Issues = issues;
        }

        public bool ProfileValid { get; }
        public bool ProBuilderPackageResolved { get; }
        public bool HierarchyMatches { get; }
        public bool MeshContractMatches { get; }
        public bool MaterialContractMatches { get; }
        public bool ColliderContractMatches { get; }
        public bool InteractionContractMatches { get; }
        public bool PreviewSceneExists { get; }
        public string ProBuilderVersion { get; }
        public int MeshFilterCount { get; }
        public int VertexCount { get; }
        public int TriangleCount { get; }
        public Vector3 ColliderSize { get; }
        public IReadOnlyList<string> Issues { get; }

        public bool Passed =>
            ProfileValid &&
            ProBuilderPackageResolved &&
            HierarchyMatches &&
            MeshContractMatches &&
            MaterialContractMatches &&
            ColliderContractMatches &&
            InteractionContractMatches &&
            PreviewSceneExists &&
            Issues.Count == 0;

        public string ToMultilineString()
        {
            string issues = Issues.Count == 0 ? "（无）" : string.Join("\n  - ", Issues);
            return $"[Nomad Parametric Prop Audit] {(Passed ? "PASS" : "FAIL")}\n" +
                   $"ProBuilder：{ProBuilderVersion}\n" +
                   $"普通 Mesh：{MeshFilterCount} 个 / {VertexCount} Vertex / {TriangleCount} Triangle\n" +
                   $"玩法 Collider：{ColliderSize}\n" +
                   $"视觉结论：manual_review_required；自动化只证明层级、预算和材质契约。\n" +
                   $"问题：\n  - {issues}";
        }
    }
}
