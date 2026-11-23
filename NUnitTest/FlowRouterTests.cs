using System.Collections.Generic;
using System.Linq;
using System.Windows;
using NUnit.Framework;
using ShaoLu.Views;

namespace NUnitTest
{
    [TestFixture]
    public class FlowRouterTests
    {
        private static bool SegmentCrossesRectInterior(Point a, Point b, Rect rect, double eps)
        {
            if (System.Math.Abs(a.Y - b.Y) < 0.001)
            {
                double left = System.Math.Min(a.X, b.X);
                double right = System.Math.Max(a.X, b.X);
                return a.Y > rect.Top + eps && a.Y < rect.Bottom - eps && right > rect.Left + eps && left < rect.Right - eps;
            }

            if (System.Math.Abs(a.X - b.X) < 0.001)
            {
                double top = System.Math.Min(a.Y, b.Y);
                double bottom = System.Math.Max(a.Y, b.Y);
                return a.X > rect.Left + eps && a.X < rect.Right - eps && bottom > rect.Top + eps && top < rect.Bottom - eps;
            }

            return false;
        }

        [Test]
        public void StraightLine_NoObstacle_KeepsTwoPoints()
        {
            var grid = FlowRouteGrid.Build(new List<Rect>(), 12, new[] { new Point(0, 100), new Point(400, 100) });
            var path = grid.Solve(new Point(0, 100), new Point(400, 100));

            Assert.That(path, Is.Not.Null);
            Assert.That(path.Count, Is.EqualTo(2));
            Assert.That(path[0].Y, Is.EqualTo(100).Within(0.001));
            Assert.That(path[1].X, Is.EqualTo(400).Within(0.001));
        }

        [Test]
        public void ObstacleOnTheWay_IsRoutedAround()
        {
            var obstacle = new Rect(150, 60, 120, 80);
            var grid = FlowRouteGrid.Build(new[] { obstacle }, 12, new[] { new Point(0, 100), new Point(400, 100) });
            var path = grid.Solve(new Point(0, 100), new Point(400, 100));

            Assert.That(path, Is.Not.Null);
            Assert.That(path.Count, Is.GreaterThan(2));

            var inflated = new Rect(obstacle.Left - 12, obstacle.Top - 12, obstacle.Width + 24, obstacle.Height + 24);
            for (int i = 1; i < path.Count; i++)
            {
                Assert.That(SegmentCrossesRectInterior(path[i - 1], path[i], inflated, 0.5), Is.False,
                    $"第 {i} 段穿过了节点（{path[i - 1]} -> {path[i]}）");
            }
        }

        [Test]
        public void MovementIsOrthogonalOnly()
        {
            var obstacle = new Rect(150, 60, 120, 80);
            var grid = FlowRouteGrid.Build(new[] { obstacle }, 12, new[] { new Point(0, 100), new Point(400, 100) });
            var path = grid.Solve(new Point(0, 100), new Point(400, 100));

            Assert.That(path, Is.Not.Null);
            for (int i = 1; i < path.Count; i++)
            {
                bool horizontal = System.Math.Abs(path[i].Y - path[i - 1].Y) < 0.001;
                bool vertical = System.Math.Abs(path[i].X - path[i - 1].X) < 0.001;
                Assert.That(horizontal || vertical, Is.True, $"第 {i} 段不是正交线段");
            }
        }

        [Test]
        public void StartAndEndPointsArePreserved()
        {
            var obstacle = new Rect(150, 60, 120, 80);
            var source = new Point(0, 100);
            var target = new Point(400, 100);
            var grid = FlowRouteGrid.Build(new[] { obstacle }, 12, new[] { source, target });
            var path = grid.Solve(source, target);

            Assert.That(path, Is.Not.Null);
            Assert.That(path[0].X, Is.EqualTo(source.X).Within(0.001));
            Assert.That(path[0].Y, Is.EqualTo(source.Y).Within(0.001));
            Assert.That(path[path.Count - 1], Is.EqualTo(target));
        }

        [Test]
        public void TwoObstaclesWithGap_RoutesThroughTheGap()
        {
            var first = new Rect(120, 40, 100, 60);
            var second = new Rect(120, 140, 100, 60);
            var grid = FlowRouteGrid.Build(new[] { first, second }, 12, new[] { new Point(0, 120), new Point(400, 120) });
            var path = grid.Solve(new Point(0, 120), new Point(400, 120));

            Assert.That(path, Is.Not.Null);
            var inflatedFirst = new Rect(first.Left - 12, first.Top - 12, first.Width + 24, first.Height + 24);
            var inflatedSecond = new Rect(second.Left - 12, second.Top - 12, second.Width + 24, second.Height + 24);
            for (int i = 1; i < path.Count; i++)
            {
                Assert.That(SegmentCrossesRectInterior(path[i - 1], path[i], inflatedFirst, 0.5), Is.False);
                Assert.That(SegmentCrossesRectInterior(path[i - 1], path[i], inflatedSecond, 0.5), Is.False);
            }
        }

        [Test]
        public void ApproachToTarget_IsHorizontal()
        {
            var obstacle = new Rect(230, 30, 120, 140);
            var source = new Point(0, 100);
            var target = new Point(400, 100);
            var grid = FlowRouteGrid.Build(new[] { obstacle }, 12, new[] { source, target });
            var path = grid.Solve(source, target);

            Assert.That(path, Is.Not.Null);
            var before = path[path.Count - 2];
            Assert.That(before.Y, Is.EqualTo(target.Y).Within(0.001),
                "最后一小段必须水平进入目标，不能竖直扎进接头");
        }

        [Test]
        public void NoPath_ReturnsNull()
        {
            // 起点被完整包在障碍里，没有任何出口
            var blocked = new Rect(-50, -50, 200, 300);
            var grid = FlowRouteGrid.Build(new[] { blocked }, 12, new[] { new Point(0, 100), new Point(400, 100) });
            var path = grid.Solve(new Point(0, 100), new Point(400, 100));

            Assert.That(path, Is.Null);
        }
    }
}
