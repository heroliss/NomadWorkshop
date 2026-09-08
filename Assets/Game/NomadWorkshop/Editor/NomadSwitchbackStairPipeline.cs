using System;
using System.Collections.Generic;
using System.Linq;
using Game.NomadWorkshop.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>把折返尺寸样件接入同一携物实验；只重建独立候选，不覆盖原直梯。</summary>
    public static class NomadSwitchbackStairPipeline
    {
        public const string ScenePath = "Assets/Game/NomadWorkshop/Scenes/SwitchbackCarrySpike.unity";
        private const string Root = "Assets/Game/NomadWorkshop/ArtFirstPass";
        private const float Width = 1.65f, Rise = 3.2f / 18f, Tread = .30f;

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/创建或打开持罐折返梯实验")]
        public static void CreateOrOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时创建折返梯。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先保存脏场景。");
            Scene scene = EditorSceneManager.OpenScene("Assets/Game/NomadWorkshop/Scenes/WorkwearMechanicStairs.unity");
            var view = UnityEngine.Object.FindFirstObjectByType<NomadStairTraversalView>();
            var data = new SerializedObject(view);
            Transform nav = (Transform)data.FindProperty("navigationSpace").objectReferenceValue;
            if (data.FindProperty("containerPrefab").objectReferenceValue == null)
                throw new InvalidOperationException("折返梯需要已接入 NW11 的工装直梯基线。");
            foreach (Transform child in nav.Cast<Transform>().ToArray())
                if (child.name.StartsWith("Step ", StringComparison.Ordinal) || child.name.StartsWith("Stair Guard", StringComparison.Ordinal) ||
                    child.name == "Warm Wood Handrail" || child.name == "Upper Back Rail" ||
                    child.name == "Upper Left Rail" || child.name == "Upper Right Rail")
                    UnityEngine.Object.DestroyImmediate(child.gameObject);

            // 上层出口移到并排第二段；上下目标和下层平台保留原三维到达检查。
            Transform upper = nav.Find("Floor 2 · Partial Deck");
            upper.localPosition = new Vector3(.5f, 3.08f, 4.8f);
            upper.localScale = new Vector3(8f, .24f, 4.8f);
            Mesh tread = SaveMesh("NW12_FoldedTread", Extrude("折边踏板", -Width * .5f, Width * .5f, new[]
            {
                new Vector2(0,0), new Vector2(.30f,0), new Vector2(.30f,-.055f), new Vector2(.278f,-.055f),
                new Vector2(.278f,-.022f), new Vector2(.024f,-.022f), new Vector2(.024f,-.070f), new Vector2(0,-.070f)
            }));
            var profile = new List<Vector2>();
            for (int i = 0; i < 9; i++)
            {
                float height = (i + 1) * Rise - .022f;
                profile.Add(new Vector2(i * Tread, height));
                profile.Add(new Vector2((i + 1) * Tread, height));
            }
            profile.Add(new Vector2(2.7f, 1.27f)); profile.Add(new Vector2(0, -.13f));
            Mesh stringer = SaveMesh("NW12_NotchedStringer", Extrude("台阶支承梁", -.022f, .022f, profile.ToArray()));

            Material deck = Material("Deck"), frame = Material("Frame"), teal = Material("Teal"), wood = Material("Wood");
            for (int lane = 0; lane < 2; lane++)
            {
                float x = lane == 0 ? 1.2f : 3.07f, z = lane == 0 ? 2.4f : -.3f, baseY = lane * 1.6f;
                int direction = lane == 0 ? -1 : 1;
                Quaternion rotation = Quaternion.Euler(0, lane == 0 ? 180 : 0, 0);
                for (int i = 0; i < 9; i++)
                {
                    var step = MeshObject(nav, $"Step {lane * 9 + i + 1:00} · Actual Tread", tread, deck);
                    step.transform.localPosition = new Vector3(x, baseY + (i + 1) * Rise, z + direction * i * Tread);
                    step.transform.localRotation = rotation;
                }
                foreach (float side in new[] { -1f, 1f })
                {
                    float edgeX = x + side * .85f;
                    var beam = MeshObject(nav, "Notched Flight Stringer", stringer, teal);
                    beam.transform.localPosition = new Vector3(edgeX, baseY, z);
                    beam.transform.localRotation = rotation;
                    for (int i = 0; i <= 9; i += 3)
                        Bar(nav, "Flight Guard Post", new Vector3(edgeX, baseY + i * Rise, z + direction * i * Tread),
                            new Vector3(edgeX, baseY + i * Rise + 1.02f, z + direction * i * Tread), .036f, frame);
                    Bar(nav, "Flight Handrail", new Vector3(edgeX, baseY + 1.04f, z),
                        new Vector3(edgeX, baseY + 2.64f, z + direction * 2.7f), .048f, wood);
                }
            }
            Box(nav, "Carrier Turning Landing", new Vector3(2.135f,1.56f,-1.125f), new Vector3(3.52f,.08f,1.65f), deck);
            foreach (float x in new[] { .345f, 3.925f })
            {
                foreach (float z in new[] { -.37f, -1.89f })
                    Bar(nav, "Landing Support And Guard", new Vector3(x,0,z), new Vector3(x,2.62f,z), .062f, teal);
                Bar(nav, "Landing Side Rail", new Vector3(x,2.64f,-.3f), new Vector3(x,2.64f,-1.95f), .048f, wood);
            }
            Bar(nav, "Landing Back Rail", new Vector3(.345f,2.64f,-1.95f), new Vector3(3.925f,2.64f,-1.95f), .048f, wood);
            Bar(nav, "Upper Back Rail", new Vector3(-3.45f,4.2f,7.1f), new Vector3(4.35f,4.2f,7.1f), .07f, teal);
            Bar(nav, "Upper Left Rail", new Vector3(-3.45f,4.2f,2.5f), new Vector3(-3.45f,4.2f,7.1f), .07f, teal);
            Bar(nav, "Upper Right Rail", new Vector3(4.35f,4.2f,2.5f), new Vector3(4.35f,4.2f,7.1f), .07f, teal);
            foreach (float x in new[] { -3.45f, 4.35f })
            foreach (float z in new[] { 2.5f, 4.8f, 7.1f })
                Bar(nav, "Upper Guard Post", new Vector3(x,3.2f,z), new Vector3(x,4.2f,z), .062f, teal);
            Bar(nav, "Upper Guard Post", new Vector3(.45f,3.2f,7.1f), new Vector3(.45f,4.2f,7.1f), .062f, teal);
            var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            Vector3 focus = nav.TransformPoint(new Vector3(1,1.5f,1));
            camera.transform.position = focus + new Vector3(10,9,-12);
            camera.transform.LookAt(focus); camera.orthographicSize = 6.5f;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("折返梯保存失败。");
            Debug.Log("[NomadSwitchback] 已创建折返梯：18 级真实薄踏板、1.65 m 转身平台，等待真实导航与携物验收。");
        }

        private static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/NW1_" + name + ".mat")
            ?? throw new InvalidOperationException("缺少楼梯共享材质：" + name);

        private static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        private static Mesh SaveMesh(string name, Mesh source)
        {
            string path = Root + "/Models/" + name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { source.name = name; AssetDatabase.CreateAsset(source, path); return source; }
            EditorUtility.CopySerialized(source, mesh); UnityEngine.Object.DestroyImmediate(source);
            EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); return mesh;
        }

        private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material; return go;
        }

        private static void Bar(Transform parent, string name, Vector3 start, Vector3 end, float width, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = (start + end) * .5f;
            go.transform.localScale = new Vector3(width, Vector3.Distance(start, end) * .5f, width);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, end - start);
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>挤出 Z/Y 简单截面；耳切三角化保留凹折边，各面拆顶点保证硬表面法线。</summary>
        private static Mesh Extrude(string name, float left, float right, Vector2[] profile)
        {
            float area = 0;
            for (int i = 0; i < profile.Length; i++) area += Cross(profile[i], profile[(i + 1) % profile.Length]);
            if (area < 0) Array.Reverse(profile);
            var polygon = Enumerable.Range(0, profile.Length).ToList();
            var caps = new List<int>();
            while (polygon.Count > 3)
            {
                bool clipped = false;
                for (int j = 0; j < polygon.Count; j++)
                {
                    int a = polygon[(j + polygon.Count - 1) % polygon.Count], b = polygon[j], c = polygon[(j + 1) % polygon.Count];
                    if (Cross(profile[b] - profile[a], profile[c] - profile[b]) <= .0000001f) continue;
                    bool contains = polygon.Any(p => p != a && p != b && p != c &&
                        Cross(profile[b]-profile[a], profile[p]-profile[a]) >= -.0000001f &&
                        Cross(profile[c]-profile[b], profile[p]-profile[b]) >= -.0000001f &&
                        Cross(profile[a]-profile[c], profile[p]-profile[c]) >= -.0000001f);
                    if (contains) continue;
                    caps.AddRange(new[] { a,b,c }); polygon.RemoveAt(j); clipped = true; break;
                }
                if (!clipped) throw new InvalidOperationException("折边截面无法三角化。");
            }
            caps.AddRange(polygon);
            var vertices = new List<Vector3>(); var indices = new List<int>(); var uv = new List<Vector2>();
            Vector3 At(float x, int i) => new(x, profile[i].y, profile[i].x);
            void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int first = vertices.Count; vertices.AddRange(new[] { a,b,c });
                uv.AddRange(new[] { new Vector2(a.z,a.y+a.x), new Vector2(b.z,b.y+b.x), new Vector2(c.z,c.y+c.x) });
                indices.AddRange(new[] { first,first+1,first+2 });
            }
            for (int i = 0; i < caps.Count; i += 3)
            {
                Triangle(At(left,caps[i]),At(left,caps[i+1]),At(left,caps[i+2]));
                Triangle(At(right,caps[i+2]),At(right,caps[i+1]),At(right,caps[i]));
            }
            for (int i = 0; i < profile.Length; i++)
            {
                int j = (i + 1) % profile.Length;
                Triangle(At(left,i),At(right,i),At(right,j));
                Triangle(At(left,i),At(right,j),At(left,j));
            }
            var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetTriangles(indices,0);
            mesh.SetUVs(0,uv); mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
