using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 将同一绝对旅程距离投影为地表 UV 与路旁物件的位置。没有独立时间积分，暂停/回退/恢复不会累积误差。
    /// JourneyPresentation 独占并释放本实例；借用模型和材质，释放时还原绑定时的位姿及属性块。
    /// </summary>
    public sealed class FoundationEnvironmentPresentation : IDisposable
    {
        private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
        private readonly Transform _root;
        private readonly List<Surface> _surfaces = new();
        private readonly List<Scenery> _scenery = new();
        private readonly double _loopMeters = 60;
        private bool _disposed;

        private sealed class Surface
        {
            internal readonly Renderer Renderer;
            internal readonly double MetersPerUvUnit;
            internal readonly Vector4 InitialST;
            internal readonly MaterialPropertyBlock Initial = new();
            internal readonly MaterialPropertyBlock Current = new();
            internal Surface(Renderer renderer, double metersPerUvUnit)
            {
                Renderer = renderer; MetersPerUvUnit = metersPerUvUnit;
                renderer.GetPropertyBlock(Initial);
                InitialST = Initial.HasVector(BaseMapST) ? Initial.GetVector(BaseMapST) : renderer.sharedMaterial.GetVector(BaseMapST);
                if (!Finite(InitialST.x) || !Finite(InitialST.y) || !Finite(InitialST.z) || !Finite(InitialST.w))
                    throw new ArgumentException("地表材质的 UV 变换必须为有限值。");
            }
        }

        private readonly struct Scenery
        {
            internal readonly Transform Part;
            internal readonly Vector3 Position;
            internal Scenery(Transform part, Transform root) { Part = part; Position = root.InverseTransformPoint(part.position); }
        }

        /// <summary>先完整校验引用，再允许 Render 修改。未配置组件的 NW1 历史环境保留原有发现规则。</summary>
        public FoundationEnvironmentPresentation(Transform root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            _root = root;
            FoundationEnvironmentVisual binding = root.GetComponent<FoundationEnvironmentVisual>();
            if (binding == null)
            {
                foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                    if (part.GetComponent<Renderer>() == null && part.name.StartsWith("Desert outcrop_", StringComparison.Ordinal))
                        _scenery.Add(new Scenery(part, root));
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (renderer.sharedMaterial != null && renderer.sharedMaterial.name == "NW1_Ground")
                        _surfaces.Add(new Surface(renderer, 4));
                return;
            }
            if (!Finite(binding.SceneryLoopMeters) || binding.SceneryLoopMeters < 1)
                throw new ArgumentException("地景循环长度必须是至少 1 米的有限值。");
            _loopMeters = binding.SceneryLoopMeters;
            var parts = new HashSet<Transform>();
            foreach (Transform part in binding.SceneryGroups)
            {
                if (part == null || part == root || !part.IsChildOf(root) || !parts.Add(part))
                    throw new ArgumentException("地景引用不能为空、重复或超出环境模型。");
                foreach (Transform other in parts)
                    if (other != part && (part.IsChildOf(other) || other.IsChildOf(part)))
                        throw new ArgumentException("地景组不能相互嵌套，否则会重复移动。");
                var scenery = new Scenery(part, root);
                if (Math.Abs(scenery.Position.z) >= _loopMeters/2)
                    throw new ArgumentException("地景初始位置须位于循环区间内。");
                _scenery.Add(scenery);
            }
            var renderers = new HashSet<Renderer>();
            foreach (var surface in binding.ScrollingSurfaces)
            {
                Renderer renderer = surface.Renderer;
                if (renderer == null || !renderer.transform.IsChildOf(root) || !renderers.Add(renderer) ||
                    renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial == null || !renderer.sharedMaterial.HasProperty(BaseMapST) ||
                    !Finite(surface.MetersPerUvUnit) || surface.MetersPerUvUnit <= 0)
                    throw new ArgumentException("地表需要模型内不重复的单材质 Renderer、有效 UV 变换和正数米制间距。");
                foreach (Transform part in parts)
                    if (renderer.transform.IsChildOf(part)) throw new ArgumentException("滚动地表不能同时随地景组移动。");
                _surfaces.Add(new Surface(renderer, surface.MetersPerUvUnit));
            }
        }

        /// <summary>绝对距离以微米传入；每次都从初始值计算，可直接投影反向行驶或存档位置。</summary>
        public void Render(long positionMicrometers)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FoundationEnvironmentPresentation));
            double distance = positionMicrometers/1000000d;
            foreach (Scenery scenery in _scenery)
            {
                Vector3 position = scenery.Position;
                position.z = (float)(Modulo(position.z-distance+_loopMeters/2, _loopMeters)-_loopMeters/2);
                scenery.Part.position = _root.TransformPoint(position);
            }
            foreach (Surface surface in _surfaces)
            {
                Vector4 st = surface.InitialST;
                st.w = (float)(st.w + Modulo(distance*st.y/surface.MetersPerUvUnit, 1));
                surface.Renderer.GetPropertyBlock(surface.Current);
                surface.Current.SetVector(BaseMapST, st);
                surface.Renderer.SetPropertyBlock(surface.Current);
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static double Modulo(double value, double period) => (value % period + period) % period;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Surface surface in _surfaces)
                if (surface.Renderer != null) surface.Renderer.SetPropertyBlock(surface.Initial);
            if (_root != null)
                foreach (Scenery scenery in _scenery)
                    if (scenery.Part != null) scenery.Part.position = _root.TransformPoint(scenery.Position);
        }
    }
}
