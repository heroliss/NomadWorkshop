using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 当前 HUD 的轻量皮肤与小尺寸九宫格纹理，由 View 的 Bag 拥有。
    /// 只在 OnGUI 中创建/借用 Skin，调用方负责在 finally 恢复全局 GUI.skin；不修改源皮肤。
    /// </summary>
    public sealed class FoundationHudTheme : IDisposable
    {
        public static readonly Color Text = new(.91f, .89f, .82f);
        public static readonly Color Muted = new(.66f, .70f, .65f);
        public static readonly Color Brass = new(.84f, .66f, .36f);
        public static readonly Color Teal = new(.36f, .64f, .59f);
        public static readonly Color Panel = new(.085f, .11f, .105f, .97f);
        public static readonly Color Border = new(.31f, .35f, .29f);
        private readonly List<Object> _owned = new();
        public GUISkin Skin { get; }
        public GUIStyle SelectedButton { get; }

        public FoundationHudTheme(GUISkin source)
        {
            Skin = Object.Instantiate(source);
            Skin.name = "Nomad HUD / Skin";
            Skin.hideFlags = HideFlags.HideAndDontSave;
            _owned.Add(Skin);
            Texture2D normal = Plate("Button", new Color(.16f, .20f, .18f), Border);
            Texture2D hover = Plate("Hover", new Color(.23f, .29f, .26f), Brass);
            Texture2D active = Plate("Pressed", new Color(.11f, .17f, .15f), Teal);
            Texture2D selected = Plate("Selected", new Color(.16f, .31f, .27f), Teal);
            Skin.button = new GUIStyle(source.button)
            {
                border = new RectOffset(3, 3, 3, 3),
                padding = new RectOffset(8, 8, 5, 5),
                margin = new RectOffset(2, 2, 2, 2),
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                normal = { background = normal, textColor = Text },
                hover = { background = hover, textColor = Text },
                focused = { background = hover, textColor = Text },
                active = { background = active, textColor = Brass },
            };
            SelectedButton = new GUIStyle(Skin.button);
            SelectedButton.normal.background = selected;
            SelectedButton.normal.textColor = Brass;
            Skin.box = new GUIStyle(source.box)
            {
                normal = { background = Plate("Panel", Panel, Border), textColor = Text },
                border = new RectOffset(3, 3, 3, 3),
                padding = new RectOffset(10, 10, 10, 10),
            };
            Skin.label = new GUIStyle(source.label) { fontSize = 12, normal = { textColor = Text } };
        }

        private Texture2D Plate(string name, Color fill, Color edge)
        {
            const int size = 12;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool border = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    bool corner = (x == 0 || x == size - 1) && (y == 0 || y == size - 1);
                    pixels[y * size + x] = corner ? Color.clear : border ? edge : fill;
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Nomad HUD / " + name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels(pixels); texture.Apply(false, true);
            _owned.Add(texture);
            return texture;
        }

        public void Dispose()
        {
            foreach (Object resource in _owned)
                if (resource != null)
                {
                    if (Application.isPlaying) Object.Destroy(resource);
                    else Object.DestroyImmediate(resource);
                }
            _owned.Clear();
        }
    }
}
