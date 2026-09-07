using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 模型包装层显式声明参与剖切的屋顶 Renderer；棚架、灯和碰撞不放入此绑定。
    /// 替换模型后重绑引用即可，运行时不依赖节点名称或 Renderer 包围盒猜测屋顶。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoundationRoofVisual : MonoBehaviour
    {
        [SerializeField] private Renderer[] roofRenderers = Array.Empty<Renderer>();

        public IReadOnlyList<Renderer> RoofRenderers => Array.AsReadOnly(roofRenderers);
    }
}
