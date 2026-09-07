using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>
    /// 从保留的 CC0 人体生成游戏自有工作服与绑骨附件，复用原 Avatar、骨权重及五类动画。
    /// 不改第三方 FBX；几何分区和附件均落到 ArtFirstPass，重跑保持资产 GUID。
    /// </summary>
    public static class NomadWorkshopResidentArtPipeline
    {
        public const string PrefabPath = NomadWarmWorkshopArtPipeline.Root + "/Prefabs/NW1_Resident.prefab";

        public static GameObject Generate()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(NomadHumanoidAssetPipeline.CharacterModelPath);
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = "NW1_Resident";
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null || !animator.isHuman) throw new InvalidOperationException("居民源缺少 Humanoid。");
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                float neckHeight = instance.transform.InverseTransformPoint(head.position).y;
                float waistHeight = instance.transform.InverseTransformPoint(hips.position).y - .04f;
                float gloveStart = Mathf.Abs(instance.transform.InverseTransformPoint(hand.position).x) - .035f;
                Material shirt = Material("NW1_Workshirt", new Color(.38f, .25f, .13f));
                Material trousers = Material("NW1_Trousers", new Color(.095f, .15f, .145f));
                Material leather = Material("NW1_Leather", new Color(.095f, .066f, .044f));
                Material trim = Material("NW1_Stitch", new Color(.32f, .24f, .13f));
                Material scarf = Material("NW1_Scarf", new Color(.49f, .16f, .065f));

                foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Material[] originalMaterials = renderer.sharedMaterials;
                    if (!originalMaterials.Any(m => m != null && m.name == "ResidentBody")) continue;
                    Mesh mesh = UnityEngine.Object.Instantiate(renderer.sharedMesh);
                    mesh.name = "NW1_Workwear_" + renderer.name;
                    Vector3[] vertices = mesh.vertices;
                    var groups = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
                    int[] triangles = mesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        Vector3 center = (vertices[triangles[i]] + vertices[triangles[i+1]] + vertices[triangles[i+2]]) / 3f;
                        Vector3 p = instance.transform.InverseTransformPoint(renderer.transform.TransformPoint(center));
                        int group = p.y >= neckHeight - .015f && Mathf.Abs(p.x) < .18f ? 0 :
                            p.y < .23f || (Mathf.Abs(p.x) > gloveStart && p.y > waistHeight) ? 3 :
                            p.y < waistHeight ? 2 : 1;
                        groups[group].Add(triangles[i]);
                        groups[group].Add(triangles[i+1]);
                        groups[group].Add(triangles[i+2]);
                    }
                    mesh.subMeshCount = groups.Length;
                    for (int i = 0; i < groups.Length; i++) mesh.SetTriangles(groups[i], i);
                    string meshPath = NomadWarmWorkshopArtPipeline.Root + "/Models/" + mesh.name + ".asset";
                    Mesh retained = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (retained == null) { AssetDatabase.CreateAsset(mesh, meshPath); retained = mesh; }
                    else { EditorUtility.CopySerialized(mesh, retained); UnityEngine.Object.DestroyImmediate(mesh); }
                    renderer.sharedMesh = retained;
                    renderer.sharedMaterials = new[] { originalMaterials[0], shirt, trousers, leather };
                }
                Attach(PrimitiveType.Sphere, "帆布便帽", head, head.position + Vector3.up*.17f,
                    new Vector3(.38f, .16f, .37f), shirt, preview);
                AttachBrim(head, leather, preview);
                Attach(PrimitiveType.Cube, "围巾", chest, head.position + new Vector3(0, -.09f, .025f),
                    new Vector3(.28f, .10f, .24f), scarf, preview);
                Attach(PrimitiveType.Cube, "工作背心", chest, chest.position + new Vector3(0, .06f, .015f),
                    new Vector3(.38f, .37f, .22f), shirt, preview);
                for (int side = -1; side <= 1; side += 2)
                {
                    Attach(PrimitiveType.Cube, "胸前工具袋", chest, chest.position + new Vector3(side*.115f, .05f, -.14f),
                        new Vector3(.11f, .125f, .035f), trim, preview);
                    Attach(PrimitiveType.Cube, "皮带工具包", hips, hips.position + new Vector3(side*.19f, -.045f, -.045f),
                        new Vector3(.11f, .19f, .17f), leather, preview);
                }
                Attach(PrimitiveType.Cube, "背包", chest, chest.position + new Vector3(0, -.03f, .18f),
                    new Vector3(.31f, .36f, .17f), trousers, preview);
                foreach (HumanBodyBones footBone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                {
                    Transform foot = animator.GetBoneTransform(footBone);
                    Attach(PrimitiveType.Sphere, "耐磨工靴", foot, foot.position + new Vector3(0, .025f, -.055f),
                        new Vector3(.17f, .20f, .32f), leather, preview);
                }
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);
                // 源人体面朝 -Z；单位外层根对齐玩法 +Z，避免居民反着走、附件前后颠倒。
                var root = new GameObject("NW1_Resident");
                SceneManager.MoveGameObjectToScene(root, preview);
                instance.name = "WorkshopRig";
                instance.transform.SetParent(root.transform, false);
                instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f,180f,0f));
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("工作服 Prefab 保存失败。");
                return prefab;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static Material Material(string name, Color color)
        {
            string path = NomadWarmWorkshopArtPipeline.Root + "/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .15f);
            NomadWorkshopSurfacePipeline.Apply(material, name);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void Attach(PrimitiveType type, string name, Transform bone, Vector3 position,
            Vector3 scale, Material material, Scene scene)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            SceneManager.MoveGameObjectToScene(part, scene);
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
            part.name = name;
            part.transform.position = position;
            part.transform.rotation = Quaternion.identity;
            part.transform.localScale = scale;
            part.transform.SetParent(bone, true);
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void AttachBrim(Transform head, Material material, Scene scene)
        {
            const int sides = 12;
            var vertices = new Vector3[sides+2];
            var triangles = new int[sides*3];
            vertices[0] = new Vector3(0,0,-.095f);
            for (int i=0; i<=sides; i++)
            {
                float a = Mathf.PI*i/sides;
                vertices[i+1] = new Vector3(Mathf.Cos(a)*.19f, -.025f*Mathf.Sin(a), -.10f-Mathf.Sin(a)*.20f);
                if (i==sides) continue;
                triangles[i*3]=0; triangles[i*3+1]=i+1; triangles[i*3+2]=i+2;
            }
            var mesh = new Mesh { name = "NW1_CapBrim" };
            mesh.vertices=vertices; mesh.triangles=triangles;
            mesh.uv=vertices.Select(v => new Vector2((v.x+.19f)/.38f,(-v.z-.095f)/.205f)).ToArray();
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            string path = NomadWarmWorkshopArtPipeline.Root + "/Models/NW1_CapBrim.asset";
            Mesh retained = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (retained == null) { AssetDatabase.CreateAsset(mesh,path); retained=mesh; }
            else { EditorUtility.CopySerialized(mesh,retained); UnityEngine.Object.DestroyImmediate(mesh); }
            var part = new GameObject("软檐便帽 · 帽檐");
            SceneManager.MoveGameObjectToScene(part,scene);
            part.AddComponent<MeshFilter>().sharedMesh=retained;
            part.AddComponent<MeshRenderer>().sharedMaterial=material;
            part.transform.position=head.position+Vector3.up*.12f;
            part.transform.SetParent(head,true);
        }
    }
}
