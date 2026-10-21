using NUnit.Framework;
using ShaoLu.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NUnitTest
{
    /// <summary>
    /// 覆盖 StepsOrderMove：步骤列表多选统一上移 / 下移的移动计划（纯函数，可脱离 UI 测试）。
    /// 选中索引一律按 AutomationStepBases 中的位置计，LineNo 仅用于显示。
    /// </summary>
    [TestFixture]
    public class StepsOrderMoveTests
    {
        private static List<string> MakeList(int count)
            => Enumerable.Range(0, count).Select(i => ((char)('A' + i)).ToString()).ToList();

        private static List<string> Apply(List<string> steps, IReadOnlyList<StepsOrderMove.MoveStep> moves)
        {
            foreach (var move in moves)
            {
                StepsOrderMove.ApplyMove(steps, move);
            }
            return steps;
        }

        private static string Plan(IReadOnlyList<StepsOrderMove.MoveStep> moves)
            => string.Join(",", moves.Select(m => m.ToString()));

        [Test]
        public void Up_ContiguousBlockTouchingTop_TopPartStaysPut_RestMovesUp()
        {
            var steps = MakeList(5);
            var moves = StepsOrderMove.BuildUp(new[] { 0, 2, 3 }, 5);

            Assert.That(Plan(moves), Is.EqualTo("2->1,3->2"));
            Assert.That(Apply(steps, moves), Is.EqualTo(new[] { "A", "C", "D", "B", "E" }));
        }

        [Test]
        public void Up_TopRowsSelectedEntirely_IsNoOp()
        {
            var moves = StepsOrderMove.BuildUp(new[] { 0, 1 }, 4);

            Assert.That(moves, Is.Empty);
            Assert.That(Apply(MakeList(4), moves), Is.EqualTo(MakeList(4)));
        }

        [Test]
        public void Up_FarApartSelection_MovesEachUpOne_KeepsOrder()
        {
            var steps = MakeList(6);
            var moves = StepsOrderMove.BuildUp(new[] { 1, 4 }, 6);

            Assert.That(Plan(moves), Is.EqualTo("1->0,4->3"));
            Assert.That(Apply(steps, moves), Is.EqualTo(new[] { "B", "A", "C", "E", "D", "F" }));
        }

        [Test]
        public void Down_FarApartSelection_MovesEachDownOne_KeepsOrder()
        {
            var steps = MakeList(6);
            var moves = StepsOrderMove.BuildDown(new[] { 1, 4 }, 6);

            Assert.That(Plan(moves), Is.EqualTo("4->5,1->2"));
            Assert.That(Apply(steps, moves), Is.EqualTo(new[] { "A", "C", "B", "D", "F", "E" }));
        }

        [Test]
        public void Up_RepeatedClicks_StopAtSelectedLeader_WithoutScramblingOrder()
        {
            var steps = MakeList(6);

            Assert.That(Plan(StepsOrderMove.BuildUp(new[] { 0, 3 }, 6)), Is.EqualTo("3->2"));
            Apply(steps, StepsOrderMove.BuildUp(new[] { 0, 3 }, 6));
            Assert.That(steps, Is.EqualTo(new[] { "A", "B", "D", "C", "E", "F" }));

            Assert.That(Plan(StepsOrderMove.BuildUp(new[] { 0, 2 }, 6)), Is.EqualTo("2->1"));
            Apply(steps, StepsOrderMove.BuildUp(new[] { 0, 2 }, 6));
            Assert.That(steps, Is.EqualTo(new[] { "A", "D", "B", "C", "E", "F" }));

            Assert.That(StepsOrderMove.BuildUp(new[] { 0, 1 }, 6), Is.Empty);
            Assert.That(steps.IndexOf("A"), Is.LessThan(steps.IndexOf("D")));
        }

        [Test]
        public void Down_RepeatedClicks_StopAtSelectedRear_WithoutScramblingOrder()
        {
            var steps = MakeList(4);

            Assert.That(Plan(StepsOrderMove.BuildDown(new[] { 1, 3 }, 4)), Is.EqualTo("1->2"));
            Apply(steps, StepsOrderMove.BuildDown(new[] { 1, 3 }, 4));
            Assert.That(steps, Is.EqualTo(new[] { "A", "C", "B", "D" }));

            Assert.That(StepsOrderMove.BuildDown(new[] { 2, 3 }, 4), Is.Empty);
            Assert.That(steps.IndexOf("B"), Is.LessThan(steps.IndexOf("D")));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        public void Up_AllSelected_IsNoOp(int count)
        {
            var selected = Enumerable.Range(0, count).ToArray();

            Assert.That(StepsOrderMove.BuildUp(selected, count), Is.Empty);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        public void Down_AllSelected_IsNoOp(int count)
        {
            var selected = Enumerable.Range(0, count).ToArray();

            Assert.That(StepsOrderMove.BuildDown(selected, count), Is.Empty);
        }

        [Test]
        public void SingleSelection_MatchesLegacySingleStepBehaviour()
        {
            Assert.That(Plan(StepsOrderMove.BuildUp(new[] { 2 }, 4)), Is.EqualTo("2->1"));
            Assert.That(StepsOrderMove.BuildUp(new[] { 0 }, 4), Is.Empty);
            Assert.That(Plan(StepsOrderMove.BuildUp(new[] { 3 }, 4)), Is.EqualTo("3->2"));

            Assert.That(Plan(StepsOrderMove.BuildDown(new[] { 1 }, 4)), Is.EqualTo("1->2"));
            Assert.That(Plan(StepsOrderMove.BuildDown(new[] { 0 }, 4)), Is.EqualTo("0->1"));
            Assert.That(StepsOrderMove.BuildDown(new[] { 3 }, 4), Is.Empty);

            Assert.That(Apply(MakeList(4), StepsOrderMove.BuildUp(new[] { 2 }, 4)),
                Is.EqualTo(new[] { "A", "C", "B", "D" }));
            Assert.That(Apply(MakeList(4), StepsOrderMove.BuildDown(new[] { 1 }, 4)),
                Is.EqualTo(new[] { "A", "C", "B", "D" }));
        }

        [Test]
        public void Build_IgnoresEmptyDuplicatedAndOutOfRangeIndices()
        {
            Assert.That(StepsOrderMove.BuildUp(Array.Empty<int>(), 3), Is.Empty);
            Assert.That(StepsOrderMove.BuildDown(Array.Empty<int>(), 3), Is.Empty);
            Assert.That(StepsOrderMove.BuildUp(new[] { 5, -1, 3 }, 3), Is.Empty);
            Assert.That(StepsOrderMove.BuildDown(new[] { 5, -1, 3 }, 3), Is.Empty);
            Assert.That(StepsOrderMove.BuildUp(new[] { 1, 1, 1 }, 3).Count, Is.EqualTo(1));
            Assert.That(StepsOrderMove.BuildUp(new[] { 1 }, 0), Is.Empty);
        }

        [Test]
        public void Up_AllSubsets_NeverJumpMoreThanOneUp_AndKeepSelectedOrder()
        {
            const int count = 5;

            for (int mask = 0; mask < 1 << count; mask++)
            {
                var selected = Enumerable.Range(0, count).Where(i => (mask & (1 << i)) != 0).ToArray();
                var steps = MakeList(count);
                Apply(steps, StepsOrderMove.BuildUp(selected, count));

                for (int i = 0; i < count; i++)
                {
                    Assert.That(steps.IndexOf(((char)('A' + i)).ToString()),
                        Is.GreaterThanOrEqualTo(i - 1), $"mask={mask} index={i}");
                }

                var expectedOrder = selected.Select(i => ((char)('A' + i)).ToString()).ToList();
                Assert.That(steps.Where(expectedOrder.Contains).ToList(), Is.EqualTo(expectedOrder), $"mask={mask}");
            }
        }

        [Test]
        public void Down_AllSubsets_NeverJumpMoreThanOneDown_AndKeepSelectedOrder()
        {
            const int count = 5;

            for (int mask = 0; mask < 1 << count; mask++)
            {
                var selected = Enumerable.Range(0, count).Where(i => (mask & (1 << i)) != 0).ToArray();
                var steps = MakeList(count);
                Apply(steps, StepsOrderMove.BuildDown(selected, count));

                for (int i = 0; i < count; i++)
                {
                    Assert.That(steps.IndexOf(((char)('A' + i)).ToString()),
                        Is.LessThanOrEqualTo(i + 1), $"mask={mask} index={i}");
                }

                var expectedOrder = selected.Select(i => ((char)('A' + i)).ToString()).ToList();
                Assert.That(steps.Where(expectedOrder.Contains).ToList(), Is.EqualTo(expectedOrder), $"mask={mask}");
            }
        }
    }
}
