using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>
    /// 把少量语义几何体烘焙为普通 Unity Mesh。ProBuilder 只存在于作者阶段，
    /// 返回网格不保留 ProBuilder Component 或运行时 Package 依赖。
    /// </summary>
    internal static class NomadParametricMeshBuilder
    {
        internal enum PrimitiveKind
        {
            Box,
            Cylinder,
        }

        internal readonly struct Part
        {
            private Part(
                PrimitiveKind kind,
                Vector3 size,
                Vector3 position,
                Quaternion rotation,
                int materialSlot,
                float bevelWidth,
                int radialSegments)
            {
                Kind = kind;
                Size = size;
                Position = position;
                Rotation = rotation;
                MaterialSlot = materialSlot;
                BevelWidth = bevelWidth;
                RadialSegments = radialSegments;
            }

            internal PrimitiveKind Kind { get; }
            internal Vector3 Size { get; }
            internal Vector3 Position { get; }
            internal Quaternion Rotation { get; }
            internal int MaterialSlot { get; }
            internal float BevelWidth { get; }
            internal int RadialSegments { get; }

            internal static Part Box(
                Vector3 size,
                Vector3 position,
                int materialSlot,
                float bevelWidth,
                Quaternion? rotation = null)
            {
                return new Part(
                    PrimitiveKind.Box,
                    size,
                    position,
                    rotation ?? Quaternion.identity,
                    materialSlot,
                    bevelWidth,
                    0);
            }

            internal static Part Cylinder(
                float diameter,
                float height,
                Vector3 position,
                Quaternion rotation,
                int materialSlot,
                int radialSegments = 12)
            {
                return new Part(
                    PrimitiveKind.Cylinder,
                    new Vector3(diameter, height, diameter),
                    position,
                    rotation,
                    materialSlot,
                    0f,
                    radialSegments);
            }
        }

        internal readonly struct BuildResult
        {
            internal BuildResult(Mesh mesh, int[] materialSlots)
            {
                Mesh = mesh;
                MaterialSlots = materialSlots;
            }

            internal Mesh Mesh { get; }
            internal int[] MaterialSlots { get; }
        }

        internal static BuildResult Build(string meshName, IReadOnlyList<Part> parts)
        {
            if (string.IsNullOrWhiteSpace(meshName))
                throw new ArgumentException("生成网格必须提供名称。", nameof(meshName));
            if (parts == null || parts.Count == 0)
                throw new ArgumentException("生成网格至少需要一个几何部件。", nameof(parts));

            var sourceMeshes = new List<Mesh>(parts.Count);
            var groupMeshes = new List<Mesh>();
            try
            {
                var materialGroups = new SortedDictionary<int, List<CombineInstance>>();
                for (int i = 0; i < parts.Count; i++)
                {
                    Part part = parts[i];
                    ValidatePart(part, i);
                    Mesh source = CreateSourceMesh(part, $"{meshName}_Part_{i:00}");
                    sourceMeshes.Add(source);

                    if (!materialGroups.TryGetValue(part.MaterialSlot, out List<CombineInstance> group))
                    {
                        group = new List<CombineInstance>();
                        materialGroups.Add(part.MaterialSlot, group);
                    }

                    group.Add(new CombineInstance
                    {
                        mesh = source,
                        subMeshIndex = 0,
                        transform = Matrix4x4.TRS(part.Position, part.Rotation, Vector3.one),
                    });
                }

                int[] materialSlots = materialGroups.Keys.ToArray();
                var finalCombines = new CombineInstance[materialSlots.Length];
                for (int i = 0; i < materialSlots.Length; i++)
                {
                    int materialSlot = materialSlots[i];
                    Mesh groupMesh = new()
                    {
                        name = $"{meshName}_Material_{materialSlot}",
                        indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                    };
                    groupMesh.CombineMeshes(
                        materialGroups[materialSlot].ToArray(),
                        true,
                        true,
                        false);
                    groupMesh.RecalculateBounds();
                    groupMeshes.Add(groupMesh);
                    finalCombines[i] = new CombineInstance
                    {
                        mesh = groupMesh,
                        subMeshIndex = 0,
                        transform = Matrix4x4.identity,
                    };
                }

                var result = new Mesh
                {
                    name = meshName,
                    indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                };
                result.CombineMeshes(finalCombines, false, false, false);
                result.RecalculateBounds();
                return new BuildResult(result, materialSlots);
            }
            finally
            {
                for (int i = 0; i < sourceMeshes.Count; i++)
                    UnityEngine.Object.DestroyImmediate(sourceMeshes[i]);
                for (int i = 0; i < groupMeshes.Count; i++)
                    UnityEngine.Object.DestroyImmediate(groupMeshes[i]);
            }
        }

        private static Mesh CreateSourceMesh(Part part, string meshName)
        {
            ProBuilderMesh proBuilderMesh = part.Kind switch
            {
                PrimitiveKind.Box => ShapeGenerator.GenerateCube(PivotLocation.Center, part.Size),
                PrimitiveKind.Cylinder => ShapeGenerator.GenerateCylinder(
                    PivotLocation.Center,
                    part.RadialSegments,
                    part.Size.x * 0.5f,
                    part.Size.y,
                    0,
                    1),
                _ => throw new ArgumentOutOfRangeException(nameof(part.Kind), part.Kind, null),
            };

            try
            {
                if (part.Kind == PrimitiveKind.Box && part.BevelWidth > 0.0001f)
                {
                    Edge[] edges = proBuilderMesh.faces
                        .SelectMany(face => face.edges)
                        .ToArray();
                    if (Bevel.BevelEdges(proBuilderMesh, edges, part.BevelWidth) == null)
                        throw new InvalidOperationException(
                            $"{meshName} 的倒角宽度超出可用表面：{part.BevelWidth:F4}m。");
                }

                proBuilderMesh.ToMesh();
                proBuilderMesh.Refresh();
                Mesh generated = proBuilderMesh.GetComponent<MeshFilter>().sharedMesh;
                if (generated == null || generated.vertexCount == 0)
                    throw new InvalidOperationException($"ProBuilder 没有生成有效网格：{meshName}");

                Mesh copy = UnityEngine.Object.Instantiate(generated);
                copy.name = meshName;
                copy.RecalculateBounds();
                return copy;
            }
            finally
            {
                // ProBuilder 的临时 Face 可能让包内默认材质被 Editor 标脏；它从未是生成产物，
                // 必须在任何 SaveAssets 前清除该瞬态标记，避免尝试写 immutable PackageCache。
                Material defaultMaterial = BuiltinMaterials.defaultMaterial;
                if (defaultMaterial != null) EditorUtility.ClearDirty(defaultMaterial);
                UnityEngine.Object.DestroyImmediate(proBuilderMesh.gameObject);
            }
        }

        private static void ValidatePart(Part part, int index)
        {
            if (part.MaterialSlot < 0)
                throw new InvalidOperationException($"部件 {index} 的材质槽不能是负数。");
            if (!IsFinitePositive(part.Size.x) ||
                !IsFinitePositive(part.Size.y) ||
                !IsFinitePositive(part.Size.z))
                throw new InvalidOperationException($"部件 {index} 的三维尺寸必须是有限正数。");
            if (!IsFinite(part.BevelWidth) || part.BevelWidth < 0f)
                throw new InvalidOperationException($"部件 {index} 的倒角宽度无效。");
            if (part.Kind == PrimitiveKind.Box &&
                part.BevelWidth >= Mathf.Min(part.Size.x, part.Size.y, part.Size.z) * 0.45f)
                throw new InvalidOperationException($"部件 {index} 的倒角相对最薄边过大。");
            if (part.Kind == PrimitiveKind.Cylinder &&
                (part.RadialSegments < 6 || part.RadialSegments > 64))
                throw new InvalidOperationException($"部件 {index} 的圆周分段必须位于 6–64。");
        }

        private static bool IsFinitePositive(float value)
        {
            return IsFinite(value) && value > 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
