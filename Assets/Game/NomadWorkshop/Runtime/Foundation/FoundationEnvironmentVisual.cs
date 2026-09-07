using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 环境模型的距离表现配置。地表和地景通过显式引用接线，替换模型不依赖网格/材质名称。
    /// 米数均为环境局部空间单位；地表 UV 的 V 方向须沿车辆局部 +Z。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoundationEnvironmentVisual : MonoBehaviour
    {
        /// <summary>一个由环境表现独占 _BaseMap_ST 的单材质表面；不改变共享材质。</summary>
        [Serializable]
        public struct ScrollingSurface
        {
            [SerializeField] private Renderer renderer;
            [SerializeField, Min(.001f)] private float metersPerUvUnit;
            public Renderer Renderer => renderer;
            /// <summary>网格 UV 的 V 每增加 1 对应的米数；材质自身 Tiling 在投影时另外计入。</summary>
            public float MetersPerUvUnit => metersPerUvUnit;
        }

        [SerializeField] private ScrollingSurface[] scrollingSurfaces = Array.Empty<ScrollingSurface>();
        [SerializeField] private Transform[] sceneryGroups = Array.Empty<Transform>();
        [SerializeField, Min(1f)] private float sceneryLoopMeters = 60f;

        public IReadOnlyList<ScrollingSurface> ScrollingSurfaces => Array.AsReadOnly(scrollingSurfaces);
        public IReadOnlyList<Transform> SceneryGroups => Array.AsReadOnly(sceneryGroups);
        public float SceneryLoopMeters => sceneryLoopMeters;
    }
}
