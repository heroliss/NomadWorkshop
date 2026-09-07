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
        public void RoofControl_BlocksOnlyItsVisibleBottomLeftRectangle()
        {
            Rect rect = FoundationHudLayout.GetRoofControlRect(ScreenWidth, ScreenHeight);
            Vector2 point = new(rect.center.x, ScreenHeight - rect.center.y);
            Assert.That(FoundationHudLayout.IsScreenPointBlocked(point, ScreenWidth, ScreenHeight, false, true), Is.True);
            Assert.That(FoundationHudLayout.IsScreenPointBlocked(point, ScreenWidth, ScreenHeight, false, false), Is.False);
            point.x = rect.xMax + 2f;
            Assert.That(FoundationHudLayout.IsScreenPointBlocked(point, ScreenWidth, ScreenHeight, false, true), Is.False);
        }

        [TestCase(180f, 240f)]
        [TestCase(12f, 20f)]
        public void RoofControl_NarrowViewportKeepsRectangleInsideScreen(float width, float height)
        {
            Rect rect = FoundationHudLayout.GetRoofControlRect(width, height);
            Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(rect.xMax, Is.LessThanOrEqualTo(width));
            Assert.That(rect.yMax, Is.LessThanOrEqualTo(height));
        }

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

        [TestCase(FoundationHudPanel.None)]
        [TestCase(FoundationHudPanel.Resident)]
        [TestCase(FoundationHudPanel.Developer)]
        public void LeavingBuildPanel_RequiresBuildModeExit(FoundationHudPanel nextPanel)
        {
            Assert.That(
                FoundationHudLayout.ShouldExitBuildMode(
                    FoundationInteractionMode.Build,
                    nextPanel),
                Is.True);
        }

        [Test]
        public void StayingOnBuildPanel_DoesNotExitAndObserveModeMustEnter()
        {
            Assert.That(
                FoundationHudLayout.ShouldExitBuildMode(
                    FoundationInteractionMode.Build,
                    FoundationHudPanel.Build),
                Is.False);
            Assert.That(
                FoundationHudLayout.ShouldEnterBuildMode(
                    FoundationInteractionMode.Observe,
                    FoundationHudPanel.Build),
                Is.True);
        }
    }
}
