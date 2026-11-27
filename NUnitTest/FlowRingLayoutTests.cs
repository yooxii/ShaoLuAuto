using System.Collections.Generic;
using System.Linq;
using System.Windows;
using NUnit.Framework;
using ShaoLu.Views;

namespace NUnitTest
{
    /// <summary>
    /// 自动排列的「回字形」位置表：左上 → 右上 → 右下 → 左下 绕一圈，
    /// 绕回起点旁边就整体往外扩一圈，不往中间填。
    /// </summary>
    [TestFixture]
    public class FlowRingLayoutTests
    {
        private static List<Point> Build(int count) => FlowRingLayout.Build(count);

        /// <summary>真正的重叠（共边不算）</summary>
        private static bool Overlaps(Rect first, Rect second)
            => first.Left < second.Right - 0.5 && first.Right > second.Left + 0.5
               && first.Top < second.Bottom - 0.5 && first.Bottom > second.Top + 0.5;

        [Test]
        public void FourNodes_FormOneRectangle()
        {
            var cells = Build(4);

            Assert.That(cells.Count, Is.EqualTo(4));
            Assert.That(cells[0], Is.EqualTo(new Point(0, 0)), "第一个节点在左上角");
            Assert.That(cells[1], Is.EqualTo(new Point(FlowRingLayout.CellWidth, 0)), "第二个在右上角");
            Assert.That(cells[2], Is.EqualTo(new Point(FlowRingLayout.CellWidth, FlowRingLayout.CellHeight)), "第三个在右下角");
            Assert.That(cells[3], Is.EqualTo(new Point(0, FlowRingLayout.CellHeight)), "第四个在左下角");
        }

        [Test]
        public void OneRing_GoesClockwiseAroundTheBorder()
        {
            var cells = Build(12);

            // 5 列 × 3 行的四条边正好 12 个：上边 5 个、右边 2 个、下边 4 个、左边 1 个
            Assert.That(cells.Count, Is.EqualTo(12));
            Assert.That(FlowRingLayout.RingCapacity(5, 3), Is.EqualTo(12));
            Assert.That(cells.Distinct().Count(), Is.EqualTo(12), "一圈内位置不能重复");

            for (int i = 0; i <= 4; i++)
                Assert.That(cells[i].Y, Is.EqualTo(0), $"上边第 {i + 1} 个应该和第一个同一行");
            Assert.That(cells[5].X, Is.EqualTo(4 * FlowRingLayout.CellWidth), "右边从右上角开始往下");
            Assert.That(cells[11].X, Is.EqualTo(0), "最后一个回到左边");
            Assert.That(cells[11].Y, Is.EqualTo(FlowRingLayout.CellHeight), "左下角上方一格");
        }

        [Test]
        public void Overflow_ExpandsOutward_InsteadOfFillingTheMiddle()
        {
            var few = Build(15);     // 第一圈 14 个 + 1 个
            var many = Build(30);    // 第一圈 14 个 + 外圈 16 个

            var firstRing = many.Take(14).ToList();
            double left = firstRing.Min(p => p.X);
            double top = firstRing.Min(p => p.Y);
            double right = firstRing.Max(p => p.X);
            double bottom = firstRing.Max(p => p.Y);
            Assert.That(right, Is.GreaterThan(left), "第一圈应该有宽度");
            Assert.That(bottom, Is.GreaterThan(top), "第一圈应该有高度");

            foreach (var cell in many.Skip(14))
            {
                bool strictlyInside = cell.X > left && cell.X < right
                                      && cell.Y > top && cell.Y < bottom;
                Assert.That(strictlyInside, Is.False, "多出来的节点要往外扩圈，不能填进第一圈中间");
            }

            // 同一套形状下，少几个节点只是排法的一部分（两个数量都只用到外圈的第一段）
            Assert.That(few.All(p => many.Contains(p)), Is.True, "扩圈不能改动第一圈的位置");
            Assert.That(many.Max(p => p.X), Is.GreaterThan(right), "外圈应该比第一圈更靠右");
            Assert.That(many.Max(p => p.Y), Is.GreaterThan(bottom), "外圈应该比第一圈更靠下");
        }

        [Test]
        public void EveryNode_SitsOnItsOwnCell_NoOverlap()
        {
            foreach (int count in new[] { 1, 2, 5, 12, 13, 14, 20, 30, 60, 120 })
            {
                var cells = Build(count);
                Assert.That(cells.Count, Is.EqualTo(count), $"{count} 个节点应该给 {count} 个位置");
                Assert.That(cells.Distinct().Count(), Is.EqualTo(count), $"{count} 个节点里有重复位置");

                var rects = cells
                    .Select(p => new Rect(p, new Size(FlowRingLayout.CellWidth, FlowRingLayout.CellHeight)))
                    .ToList();
                for (int i = 0; i < rects.Count; i++)
                {
                    for (int j = i + 1; j < rects.Count; j++)
                    {
                        Assert.That(Overlaps(rects[i], rects[j]), Is.False,
                            $"{count} 个节点时第 {i + 1} 与第 {j + 1} 个格子重叠了");
                    }
                }
            }
        }

        [Test]
        public void AllCells_AlignToTheGridFromOrigin()
        {
            var cells = Build(17);

            Assert.That(cells.Min(p => p.X), Is.EqualTo(0).Within(0.001), "最左边从 0 开始");
            Assert.That(cells.Min(p => p.Y), Is.EqualTo(0).Within(0.001), "最上边从 0 开始");
            foreach (var cell in cells)
            {
                double column = cell.X / FlowRingLayout.CellWidth;
                double row = cell.Y / FlowRingLayout.CellHeight;
                Assert.That(column, Is.EqualTo(System.Math.Round(column)).Within(0.001), "横坐标必须落在格子上");
                Assert.That(row, Is.EqualTo(System.Math.Round(row)).Within(0.001), "纵坐标必须落在格子上");
            }
        }

        [Test]
        public void EmptyOrSingle_IsSafe()
        {
            Assert.That(Build(0), Is.Empty);
            Assert.That(Build(-3), Is.Empty);

            var single = Build(1);
            Assert.That(single.Count, Is.EqualTo(1));
            Assert.That(single[0], Is.EqualTo(new Point(0, 0)));
        }
    }
}
