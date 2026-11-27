using System.Collections.Generic;
using System.Linq;
using System.Windows;
using NUnit.Framework;
using ShaoLu.Views;

namespace NUnitTest
{
    /// <summary>
    /// 流程图自动排列：「默认」是把节点只摆在矩形四条边上的口字形（左上 → 右上 → 右下 → 左下），
    /// 「垂直流」是顶部居中一路向下排成一列。
    /// </summary>
    [TestFixture]
    public class FlowAutoLayoutTests
    {
        /// <summary>真正的重叠（共边不算）</summary>
        private static bool Overlaps(Rect first, Rect second)
            => first.Left < second.Right - 0.5 && first.Right > second.Left + 0.5
               && first.Top < second.Bottom - 0.5 && first.Bottom > second.Top + 0.5;

        private static List<Rect> CellsToRects(IReadOnlyList<Point> cells)
            => cells.Select(p => new Rect(p, new Size(FlowAutoLayout.CellWidth, FlowAutoLayout.CellHeight))).ToList();

        [Test]
        public void FourNodes_FormOneRectangle()
        {
            var cells = FlowAutoLayout.BuildPerimeter(4);

            Assert.That(cells.Count, Is.EqualTo(4));
            Assert.That(cells[0], Is.EqualTo(new Point(0, 0)), "第一个节点在左上角");
            Assert.That(cells[1], Is.EqualTo(new Point(FlowAutoLayout.CellWidth, 0)), "第二个在右上角");
            Assert.That(cells[2], Is.EqualTo(new Point(FlowAutoLayout.CellWidth, FlowAutoLayout.CellHeight)), "第三个在右下角");
            Assert.That(cells[3], Is.EqualTo(new Point(0, FlowAutoLayout.CellHeight)), "第四个在左下角");
        }

        [Test]
        public void Perimeter_GoesClockwiseAndClosesTheLoop()
        {
            var cells = FlowAutoLayout.BuildPerimeter(12);
            var shape = FlowAutoLayout.PerimeterShape(12);

            Assert.That(cells.Count, Is.EqualTo(12));
            Assert.That(cells.Distinct().Count(), Is.EqualTo(12), "一圈内位置不能重复");
            Assert.That(FlowAutoLayout.PerimeterCapacity(shape.Columns, shape.Rows), Is.GreaterThanOrEqualTo(12));
            Assert.That(shape.Columns * shape.Rows, Is.GreaterThan(12), "节点只占四条边，不铺满整个矩形");

            // 上边：同一个 y，从左往右
            Assert.That(cells[0], Is.EqualTo(new Point(0, 0)));
            Assert.That(cells[1].X, Is.GreaterThan(cells[0].X));
            Assert.That(cells[1].Y, Is.EqualTo(0));

            // 右边：同一个 x，往下走
            int rightStart = cells.FindIndex(p => p.Y > 0 && p.X == (shape.Columns - 1) * FlowAutoLayout.CellWidth);
            Assert.That(rightStart, Is.GreaterThan(0));
            Assert.That(cells[rightStart + 1].Y, Is.GreaterThan(cells[rightStart].Y));

            // 下边：从右往左
            var bottom = cells.Where(p => p.Y == (shape.Rows - 1) * FlowAutoLayout.CellHeight).ToList();
            for (int i = 1; i < bottom.Count; i++)
                Assert.That(bottom[i].X, Is.LessThan(bottom[i - 1].X), "下边应该从右往左");

            // 最后一个回到左边，正好在左上角下面一格（首尾相连）
            Assert.That(cells[cells.Count - 1].X, Is.EqualTo(0));
            Assert.That(cells[cells.Count - 1].Y, Is.EqualTo(FlowAutoLayout.CellHeight));
        }

        [Test]
        public void Perimeter_NeverFillsTheMiddle()
        {
            var cells = FlowAutoLayout.BuildPerimeter(30);
            var shape = FlowAutoLayout.PerimeterShape(30);

            Assert.That(cells.Distinct().Count(), Is.EqualTo(30));

            foreach (var cell in cells)
            {
                int column = (int)System.Math.Round(cell.X / FlowAutoLayout.CellWidth);
                int row = (int)System.Math.Round(cell.Y / FlowAutoLayout.CellHeight);
                bool onBorder = column == 0 || column == shape.Columns - 1 || row == 0 || row == shape.Rows - 1;
                Assert.That(onBorder, Is.True, $"格子 ({column},{row}) 落在矩形中间了，节点只能摆四条边");
            }
        }

        [Test]
        public void Perimeter_GrowsTheRectangleWhenStepsAreAdded()
        {
            var small = FlowAutoLayout.BuildPerimeter(12);
            var large = FlowAutoLayout.BuildPerimeter(30);
            var smallShape = FlowAutoLayout.PerimeterShape(12);
            var largeShape = FlowAutoLayout.PerimeterShape(30);

            Assert.That(largeShape.Columns, Is.GreaterThan(smallShape.Columns), "步骤变多时矩形要撑宽");
            Assert.That(largeShape.Rows, Is.GreaterThan(smallShape.Rows), "步骤变多时矩形要撑高");
            Assert.That(large.Max(p => p.X), Is.GreaterThan(small.Max(p => p.X)));
            Assert.That(large.Max(p => p.Y), Is.GreaterThan(small.Max(p => p.Y)));
            Assert.That(FlowAutoLayout.PerimeterCapacity(smallShape.Columns, smallShape.Rows), Is.GreaterThanOrEqualTo(12));
            Assert.That(FlowAutoLayout.PerimeterCapacity(largeShape.Columns, largeShape.Rows), Is.GreaterThanOrEqualTo(30));
        }

        [Test]
        public void Vertical_IsOneCenteredColumnGoingDown()
        {
            var cells = FlowAutoLayout.BuildVertical(6);

            Assert.That(cells.Count, Is.EqualTo(6));
            foreach (var cell in cells)
                Assert.That(cell.X, Is.EqualTo(cells[0].X).Within(0.001), "垂直流的所有节点同一列");

            for (int i = 1; i < cells.Count; i++)
                Assert.That(cells[i].Y - cells[i - 1].Y, Is.EqualTo(FlowAutoLayout.VerticalGap).Within(0.001),
                    "相邻两个节点按固定行距往下");

            double center = cells[0].X + FlowAutoLayout.CellWidth / 2d;
            Assert.That(center, Is.EqualTo(FlowAutoLayout.VerticalCenterX).Within(0.001), "整列应该居中");
            Assert.That(cells[0].Y, Is.EqualTo(0), "第一个在最上面");
            Assert.That(FlowAutoLayout.VerticalGap, Is.GreaterThan(FlowAutoLayout.CellHeight),
                "行距要比卡片高，留出绕线的通道");
        }

        [Test]
        public void EveryNode_SitsOnItsOwnCell_NoOverlap()
        {
            foreach (int count in new[] { 1, 2, 5, 12, 13, 14, 20, 30, 60, 120 })
            {
                var perimeter = FlowAutoLayout.BuildPerimeter(count);
                var vertical = FlowAutoLayout.BuildVertical(count);

                Assert.That(perimeter.Count, Is.EqualTo(count), $"{count} 个节点应该给 {count} 个口字形位置");
                Assert.That(vertical.Count, Is.EqualTo(count), $"{count} 个节点应该给 {count} 个垂直流位置");
                Assert.That(perimeter.Distinct().Count(), Is.EqualTo(count), $"{count} 个节点里有重复位置");
                Assert.That(vertical.Distinct().Count(), Is.EqualTo(count), $"{count} 个节点里有重复位置");

                AssertNoOverlap(perimeter, count);
                AssertNoOverlap(vertical, count);
            }
        }

        private static void AssertNoOverlap(List<Point> cells, int count)
        {
            var rects = CellsToRects(cells);
            for (int i = 0; i < rects.Count; i++)
            {
                for (int j = i + 1; j < rects.Count; j++)
                {
                    Assert.That(Overlaps(rects[i], rects[j]), Is.False,
                        $"{count} 个节点时第 {i + 1} 与第 {j + 1} 个格子重叠了");
                }
            }
        }

        [Test]
        public void EmptyOrSingle_IsSafe()
        {
            Assert.That(FlowAutoLayout.BuildPerimeter(0), Is.Empty);
            Assert.That(FlowAutoLayout.BuildPerimeter(-3), Is.Empty);
            Assert.That(FlowAutoLayout.BuildVertical(0), Is.Empty);
            Assert.That(FlowAutoLayout.BuildVertical(-3), Is.Empty);

            var single = FlowAutoLayout.BuildPerimeter(1);
            Assert.That(single.Count, Is.EqualTo(1));
            Assert.That(single[0], Is.EqualTo(new Point(0, 0)));
        }
    }
}
