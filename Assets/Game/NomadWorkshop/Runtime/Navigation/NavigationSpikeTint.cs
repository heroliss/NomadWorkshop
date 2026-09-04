using UnityEngine;

namespace Game.NomadWorkshop.Navigation
{
    /// <summary>保存 Harness 灰盒的期望颜色；运行时材质由 View 按颜色族集中创建与释放。</summary>
    public sealed class NavigationSpikeTint : MonoBehaviour
    {
        [SerializeField] private Color color = Color.white;

        public Color Color => color;

        public void ConfigureRuntime(Color configuredColor) => color = configuredColor;
    }
}
