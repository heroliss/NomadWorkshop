using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEngine;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class FoundationHudThemeTests
    {
        [Test]
        public void DisposingTheme_ReleasesOwnedSkinAndTextures_WithoutChangingBorrowedSkin()
        {
            var source = ScriptableObject.CreateInstance<GUISkin>();
            var borrowed = new Texture2D(2, 2);
            source.button.normal.background = borrowed;
            var theme = new FoundationHudTheme(source);
            GUISkin skin = theme.Skin;
            var owned = new[] { skin.button.normal.background, skin.button.hover.background,
                skin.button.active.background, skin.box.normal.background, theme.SelectedButton.normal.background };
            try
            {
                Assert.That(skin, Is.Not.SameAs(source));
                Assert.That(source.button.normal.background, Is.SameAs(borrowed));
                Assert.That(skin.button.normal.background, Is.Not.SameAs(borrowed));
                theme.Dispose();
                theme.Dispose();
                Assert.That(skin == null, Is.True);
                foreach (Texture2D texture in owned) Assert.That(texture == null, Is.True);
                Assert.That(source != null && borrowed != null, Is.True);
                Assert.That(source.button.normal.background, Is.SameAs(borrowed));
            }
            finally
            {
                theme.Dispose();
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(borrowed);
            }
        }
    }
}
