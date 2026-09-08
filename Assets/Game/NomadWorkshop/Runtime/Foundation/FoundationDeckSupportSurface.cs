using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 静态局部甲板样板的物理/可见地板 Adapter。Awake 从同一布局重建碰撞和外扩板面；
    /// 不从高模取碰撞，不拥有施工状态。动态结构事务将由 System 准备候选后提交，不通过本组件热改布局。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class FoundationDeckSupportSurface : MonoBehaviour
    {
        [SerializeField] private DeckLayoutDefinition layout;
        [SerializeField, Min(0.01f)] private float thickness = 0.3f;
        private Mesh _ownedMesh;
        private Mesh _ownedVisualMesh;

        private void Awake()
        {
            if (layout == null) throw new InvalidOperationException("局部甲板地板缺少布局定义。");
            _ownedMesh = CreateMesh(layout.CreateSupportRegion(), thickness);
            _ownedVisualMesh = CreateExtensionMesh(layout, thickness);
            GetComponent<MeshFilter>().sharedMesh = _ownedVisualMesh;
            GetComponent<MeshCollider>().sharedMesh = _ownedMesh;
        }

        private void OnDestroy()
        {
            if (_ownedMesh != null) Destroy(_ownedMesh);
            if (_ownedVisualMesh != null) Destroy(_ownedVisualMesh);
        }

        /// <summary>保留原车体精制甲板外观，只显示起步矩形外的板面；板孔仍从同一支撑区域扣除。</summary>
        public static Mesh CreateExtensionMesh(DeckLayoutDefinition layout, float thickness = 0.3f)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            DeckSupportRegion support = layout.CreateSupportRegion();
            return CreateMesh(new DeckSupportRegion(support.Surfaces, new[] { layout.CreateBounds() }), thickness);
        }

        /// <summary>
        /// 从互不重叠的支撑面生成等高板体，顶面 Y=0，底面=-thickness，米制 UV。
        /// 返回的新 Mesh 由调用方保存为资产或在销毁时释放；适用于单层轴对齐板片。
        /// </summary>
        public static Mesh CreateMesh(DeckSupportRegion support, float thickness = 0.3f)
        {
            if (support == null) throw new ArgumentNullException(nameof(support));
            if (float.IsNaN(thickness) || float.IsInfinity(thickness) || thickness <= 0)
                throw new ArgumentOutOfRangeException(nameof(thickness));
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            foreach (DeckBounds p in support.Surfaces)
            {
                float x0 = p.MinXMillimeters / 1000f, x1 = p.MaxXMillimeters / 1000f;
                float z0 = p.MinZMillimeters / 1000f, z1 = p.MaxZMillimeters / 1000f;
                var a = new Vector3(x0, 0, z0); var b = new Vector3(x0, 0, z1);
                var c = new Vector3(x1, 0, z1); var d = new Vector3(x1, 0, z0);
                Vector3 down = Vector3.down * thickness;
                Quad(a,b,c,d); Quad(d+down,c+down,b+down,a+down);
                Quad(a,a+down,b+down,b); Quad(b,b+down,c+down,c);
                Quad(c,c+down,d+down,d); Quad(d,d+down,a+down,a);
            }
            var mesh = new Mesh { name = "NW_RuntimeDeckSupport", indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int first = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                bool horizontal = Mathf.Abs(a.y-b.y) < .00001f && Mathf.Abs(a.y-c.y) < .00001f;
                foreach (Vector3 v in new[] { a,b,c,d })
                    uv.Add(horizontal ? new Vector2(v.x,v.z) :
                        Mathf.Abs(a.x-b.x) < .00001f && Mathf.Abs(a.x-c.x) < .00001f
                            ? new Vector2(v.z,v.y) : new Vector2(v.x,v.y));
                triangles.Add(first); triangles.Add(first+1); triangles.Add(first+2);
                triangles.Add(first); triangles.Add(first+2); triangles.Add(first+3);
            }
        }
    }
}
