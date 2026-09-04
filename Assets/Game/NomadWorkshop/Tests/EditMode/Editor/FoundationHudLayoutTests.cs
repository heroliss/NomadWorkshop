using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEngine;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class FoundationHudLayoutTests
    {
        private const float ScreenWidth = 1097f;
        private const float ScreenHeight = 713f;

        [Test]
        public void LeftThirdOutsideVisibleCard_DoesNotBlockWorldInput()
        {
            Assert.That(
                FoundationHudLayout.IsScreenPointBlocked(
                    new Vector2(100f, ScreenHeight * 0.5f),
                    ScreenWidth,
                    ScreenHeight,
                    informationPanelVisible: false),
                Is.False,
                "左侧中部没有 UI，不应因旧的 410×730 固定区域吞掉幽灵移动。 ");
        }

        [Test]
        public void VisibleCardToolbarAndOpenPanel_BlockWorldInput()
        {
            Assert.That(
                FoundationHudLayout.IsScreenPointBlocked(
                    new Vector2(50f, ScreenHeight - 50f),
                    ScreenWidth,
                    ScreenHeight,
                    informationPanelVisible: false),
                Is.True);
            Assert.That(
                FoundationHudLayout.IsScreenPointBlocked(
                    new Vector2(ScreenWidth - 80f, ScreenHeight - 30f),
                    ScreenWidth,
                    ScreenHeight,
                    informationPanelVisible: false),
                Is.True);
            Assert.That(
                FoundationHudLayout.IsScreenPointBlocked(
                    new Vector2(ScreenWidth - 80f, ScreenHeight * 0.5f),
                    ScreenWidth,
                    ScreenHeight,
                    informationPanelVisible: true),
                Is.True);
        }

        [Test]
        public void RequestingAnotherPanel_ReplacesPreviousSelection()
        {
            FoundationHudPanel current = FoundationHudPanel.Resident;
            current = FoundationHudLayout.Toggle(current, FoundationHudPanel.Build);
            Assert.That(current, Is.EqualTo(FoundationHudPanel.Build));

            current = FoundationHudLayout.Toggle(current, FoundationHudPanel.Developer);
            Assert.That(current, Is.EqualTo(FoundationHudPanel.Developer));

            current = FoundationHudLayout.Toggle(current, FoundationHudPanel.Developer);
            Assert.That(current, Is.EqualTo(FoundationHudPanel.None));
        }
    }
}
