using System;
using System.Collections.Generic;
using System.Linq;

namespace ShaoLu.Services
{
    /// <summary>
    /// 「步骤记录区」多选统一上移 / 下移的移动计划计算。纯函数、无 WPF 依赖，可脱离 UI 测试。
    /// 索引一律是步骤在当前列表中的位置（AutomationStepBases 的下标），LineNo 只用于显示。
    /// 语义：一次点击中每个被选中的步骤最多移动一位；碰到顶 / 底边界，或上方 / 下方紧邻的选中步骤
    /// 自己停住时，该步骤也停住且不越过它，其余选中步骤继续移动；选中块内部相对顺序始终不变。
    /// </summary>
    public static class StepsOrderMove
    {
        /// <summary>一次移动：把 from 位置的步骤移到 to 位置，等价于 ObservableCollection.Move(from, to)。</summary>
        public readonly struct MoveStep
        {
            public MoveStep(int from, int to)
            {
                From = from;
                To = to;
            }

            public int From { get; }

            public int To { get; }

            public override string ToString() => From + "->" + To;
        }

        /// <summary>计算一次「上移」要执行的移动序列；返回空表示本次点击不动。</summary>
        public static IReadOnlyList<MoveStep> BuildUp(IEnumerable<int> selectedIndices, int count) => Build(selectedIndices, count, true);

        /// <summary>计算一次「下移」要执行的移动序列；返回空表示本次点击不动。</summary>
        public static IReadOnlyList<MoveStep> BuildDown(IEnumerable<int> selectedIndices, int count) => Build(selectedIndices, count, false);

        private static IReadOnlyList<MoveStep> Build(IEnumerable<int> selectedIndices, int count, bool up)
        {
            var moves = new List<MoveStep>();
            if (count <= 0 || selectedIndices == null) return moves;

            var selected = new HashSet<int>();
            foreach (var index in selectedIndices)
            {
                if (index >= 0 && index < count) selected.Add(index);
            }
            if (selected.Count == 0) return moves;

            // 上移按索引升序、下移按索引降序：这样处理到第 i 项时，该项仍在原索引 i 上，原索引全程有效。
            var ordered = selected.OrderBy(index => index).ToList();
            if (!up) ordered.Reverse();

            var moved = new HashSet<int>();
            foreach (var index in ordered)
            {
                int target = up ? index - 1 : index + 1;
                if (target < 0 || target >= count) continue;                                    // 到顶 / 到底：本条停住
                if (selected.Contains(target) && !moved.Contains(target)) continue;             // 紧邻的选中步骤没动，不能越过
                moves.Add(new MoveStep(index, target));
                moved.Add(index);
            }
            return moves;
        }

        /// <summary>按 ObservableCollection.Move 的语义在列表上执行一次移动（测试与调用方复用）。</summary>
        public static void ApplyMove<T>(IList<T> list, MoveStep move)
        {
            var item = list[move.From];
            list.RemoveAt(move.From);
            list.Insert(move.To, item);
        }
    }
}
