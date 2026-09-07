using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// World View 拥有的屋顶显示会话。建造期间强制剖开，退出后恢复观看偏好；
    /// 不修改对象、灯、Collider 或业务状态。构造时先验证全部引用再修改 Renderer，
    /// Dispose 恢复原启用/投影状态；HUD 只能借用，不能独立释放它。
    /// </summary>
    public sealed class FoundationRoofPresentation : IDisposable
    {
        private readonly Snapshot[] _original;

        public bool ExteriorPreferred { get; private set; }
        public bool BuildCutaway { get; private set; }
        public bool ExteriorVisible => ExteriorPreferred && !BuildCutaway;
        public bool IsDisposed { get; private set; }

        public FoundationRoofPresentation(params FoundationRoofVisual[] bindings)
        {
            if (bindings == null || bindings.Length == 0)
                throw new ArgumentException("屋顶表现需要至少一组显式绑定。", nameof(bindings));
            var seen = new HashSet<Renderer>();
            var snapshots = new List<Snapshot>();
            foreach (FoundationRoofVisual binding in bindings)
            {
                if (binding == null || binding.RoofRenderers.Count == 0)
                    throw new ArgumentException("屋顶绑定缺失或未指定 Renderer。", nameof(bindings));
                foreach (Renderer renderer in binding.RoofRenderers)
                {
                    if (renderer == null || !renderer.transform.IsChildOf(binding.transform) || !seen.Add(renderer))
                        throw new ArgumentException("屋顶 Renderer 缺失、重复或不属于绑定节点。", nameof(bindings));
                    snapshots.Add(new Snapshot(renderer));
                }
            }
            _original = snapshots.ToArray();
            Apply();
        }

        /// <summary>建造强制剖开时忽略观看切换，避免按下隐藏的切换键改变退出后的偏好。</summary>
        public void ToggleExteriorPreference()
        {
            ThrowIfDisposed();
            if (BuildCutaway) return;
            ExteriorPreferred = !ExteriorPreferred;
            Apply();
        }

        /// <summary>由真实交互模式驱动；与暂停、检查点和居民工作生命周期无关。</summary>
        public void SetBuildCutaway(bool enabled)
        {
            ThrowIfDisposed();
            BuildCutaway = enabled;
            Apply();
        }

        private void Apply()
        {
            foreach (Snapshot original in _original)
            {
                Renderer renderer = original.Renderer;
                if (renderer == null) continue;
                bool keepShadow = original.Shadows != ShadowCastingMode.Off;
                renderer.enabled = original.Enabled && (ExteriorVisible || keepShadow);
                renderer.shadowCastingMode = ExteriorVisible || !keepShadow
                    ? original.Shadows : ShadowCastingMode.ShadowsOnly;
            }
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            foreach (Snapshot original in _original)
            {
                if (original.Renderer == null) continue;
                original.Renderer.enabled = original.Enabled;
                original.Renderer.shadowCastingMode = original.Shadows;
            }
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(FoundationRoofPresentation));
        }

        private readonly struct Snapshot
        {
            public readonly Renderer Renderer;
            public readonly bool Enabled;
            public readonly ShadowCastingMode Shadows;

            public Snapshot(Renderer renderer)
            {
                Renderer = renderer;
                Enabled = renderer.enabled;
                Shadows = renderer.shadowCastingMode;
            }
        }
    }
}
