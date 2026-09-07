using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEngine;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class FoundationHudLayoutTests
    {
        private const float ScreenWidth = 1097f;
        private const float ScreenHeight = 713f;

        [TestCase(1225f, 713f)]
        [TestCase(960f, 540f)]
        [TestCase(800f, 450f)]
        [TestCase(540f, 960f)]
        public void ScaledPanels_DoNotOverlap_AndMatchPhysicalInput(float width, float height)
        {
            float scale = FoundationHudLayout.GetCanvasScale(width, height);
            Vector2 canvas = FoundationHudLayout.GetCanvasSize(width, height);
            var rectangles = new[]
            {
                FoundationHudLayout.GetCompactResidentCardRect(canvas.x),
                FoundationHudLayout.GetCornerToolbarRect(canvas.x),
                FoundationHudLayout.GetSupplyRect(canvas.x),
                FoundationHudLayout.GetInformationPanelRect(canvas.x, canvas.y),
                FoundationHudLayout.GetTimeControlsRect(canvas.x, canvas.y),
                FoundationHudLayout.GetRoofControlRect(canvas.x, canvas.y),
            };
            for (int i = 0; i < rectangles.Length; i++)
            {
                Rect rect = rectangles[i];
                Assert.That(rect.xMin >= 0 && rect.yMin >= 0 && rect.xMax <= canvas.x && rect.yMax <= canvas.y, Is.True);
                for (int j = i + 1; j < rectangles.Length; j++)
                    Assert.That(rect.Overlaps(rectangles[j]), Is.False, $"面板 {i} 与 {j} 相交。");
                Vector2 physical = new(rect.center.x * scale, height - rect.center.y * scale);
                Assert.That(FoundationHudLayout.IsScreenPointBlocked(physical, width, height, true, true), Is.True);
                if (i == 3 || i == 5)
                    Assert.That(FoundationHudLayout.IsScreenPointBlocked(physical, width, height, false, false), Is.False);
            }
            // 卡片下方与右侧面板之间仍可操作世界，缩放不应扩大透明拦截区。
            Vector2 free = new(200f * scale, height - 260f * scale);
            Assert.That(FoundationHudLayout.IsScreenPointBlocked(free, width, height, true, true), Is.False);
        }

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
