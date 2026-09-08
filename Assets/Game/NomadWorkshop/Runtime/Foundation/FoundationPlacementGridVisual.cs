using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 从真实位置吸附步长生成一张轻量网格 Mesh。网格不再复用旧版布局索引 CellSize，因而玩家看到的
    /// 每条线都能实际吸附；关闭位置吸附时网格也随之隐藏。
    /// </summary>
    public sealed class FoundationPlacementGridVisual : IDisposable
    {
        private readonly DeckSupportRegion _support;
        private readonly Mesh _mesh;
        private int _stepMillimeters = -1;

        public FoundationPlacementGridVisual(
            Transform parent,
            DeckBounds bounds,
            Material material)
            : this(parent, new DeckSupportRegion(bounds), material)
        {
        }

        /// <summary>按规范化支撑面裁切辅助线，孔洞与未铺板区域不显示可吸附地板。</summary>
        public FoundationPlacementGridVisual(Transform parent, DeckSupportRegion support, Material material)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (material == null) throw new ArgumentNullException(nameof(material));
            _support = support ?? throw new ArgumentNullException(nameof(support));

            Root = new GameObject("Optional Placement Grid").transform;
            Root.SetParent(parent, false);
            var meshObject = new GameObject("Synchronized Snap Grid Mesh");
            meshObject.transform.SetParent(Root, false);
            meshObject.transform.localPosition = new Vector3(0f, 0.018f, 0f);
            var filter = meshObject.AddComponent<MeshFilter>();
            var renderer = meshObject.AddComponent<MeshRenderer>();
            _mesh = new Mesh { name = "NW_RuntimeSynchronizedPlacementGrid" };
            _mesh.MarkDynamic();
            filter.sharedMesh = _mesh;
            renderer.sharedMaterial = material;
        }

        public Transform Root { get; }
        public int StepMillimeters => _stepMillimeters;

        public void Rebuild(int stepMillimeters)
        {
            stepMillimeters = Math.Max(0, stepMillimeters);
            if (_stepMillimeters == stepMillimeters) return;
            _stepMillimeters = stepMillimeters;
            _mesh.Clear();
            if (stepMillimeters == 0) return;

            float thickness = Mathf.Min(0.018f, stepMillimeters / 1000f * 0.075f);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            foreach (DeckBounds bounds in _support.Surfaces)
            {
                float minimumX = bounds.MinXMillimeters / 1000f;
                float maximumX = bounds.MaxXMillimeters / 1000f;
                float minimumZ = bounds.MinZMillimeters / 1000f;
                float maximumZ = bounds.MaxZMillimeters / 1000f;
                int firstX = CeilToStep(bounds.MinXMillimeters, stepMillimeters);
                for (int x = firstX; x <= bounds.MaxXMillimeters; x += stepMillimeters)
                    AddQuad(vertices, triangles,
                        Mathf.Max(minimumX, x / 1000f - thickness * 0.5f), minimumZ,
                        Mathf.Min(maximumX, x / 1000f + thickness * 0.5f), maximumZ);

                int firstZ = CeilToStep(bounds.MinZMillimeters, stepMillimeters);
                for (int z = firstZ; z <= bounds.MaxZMillimeters; z += stepMillimeters)
                    AddQuad(vertices, triangles, minimumX,
                        Mathf.Max(minimumZ, z / 1000f - thickness * 0.5f), maximumX,
                        Mathf.Min(maximumZ, z / 1000f + thickness * 0.5f));
            }

            _mesh.SetVertices(vertices);
            _mesh.SetTriangles(triangles, 0);
            _mesh.RecalculateBounds();
        }

        public void SetVisible(bool visible) =>
            Root.gameObject.SetActive(visible && _stepMillimeters > 0);

        public void Dispose()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_mesh);
                else UnityEngine.Object.DestroyImmediate(_mesh);
            }
        }

        private static int CeilToStep(int value, int step)
        {
            int quotient = value / step;
            int remainder = value % step;
            if (remainder != 0 && value > 0) quotient++;
            return quotient * step;
        }

        private static void AddQuad(
            ICollection<Vector3> vertices,
            ICollection<int> triangles,
            float minimumX,
            float minimumZ,
            float maximumX,
            float maximumZ)
        {
            int first = vertices.Count;
            vertices.Add(new Vector3(minimumX, 0f, minimumZ));
            vertices.Add(new Vector3(maximumX, 0f, minimumZ));
            vertices.Add(new Vector3(maximumX, 0f, maximumZ));
            vertices.Add(new Vector3(minimumX, 0f, maximumZ));
            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 1);
            triangles.Add(first);
            triangles.Add(first + 3);
            triangles.Add(first + 2);
        }
    }
}
