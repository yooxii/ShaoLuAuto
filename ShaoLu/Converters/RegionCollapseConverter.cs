using ShaoLu.Services;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShaoLu.Converters
{
    /// <summary>
    /// 步骤列表行的可见性：落在「已折叠的多线程区域」内的行返回 Collapsed，其余返回 Visible。
    /// values[0] = 当前行步骤；values[1] = 步骤总表；values[2] = 折叠状态版本号（仅用于触发重算，不参与判定）。
    /// 判定口径：遍历步骤总表里每个 IsRegionCollapsed = true 的「多线程开始」步骤，
    /// 用 MultiThreadRange.TryResolve 解析它圈定的区域（区域含开始行之后、结束标记之前的步骤），
    /// 行索引落在区域内则隐藏；「多线程开始」行与「多线程区域结束」标记行始终显示。
    /// 区域解析失败（结束步骤被删除、缺少配对结束标记等）时视为未折叠，不隐藏任何行。
    /// 折叠只影响列表显示，不改变任何运行语义。
    /// </summary>
    public class RegionCollapseConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var row = values != null && values.Length > 0 ? values[0] as AutomationStepBase : null;
            if (row == null || values.Length < 2) return Visibility.Visible;

            // 区域边界行始终显示：多线程开始行承载折叠开关，区域结束标记行标示范围末尾
            if (row is MultiThreadStartStep || row is MultiThreadEndStep) return Visibility.Visible;

            var steps = ToStepList(values[1]);
            if (steps == null) return Visibility.Visible;

            int rowIndex = IndexOf(steps, row);
            if (rowIndex < 0) return Visibility.Visible;

            for (int i = 0; i < steps.Count; i++)
            {
                var start = steps[i] as MultiThreadStartStep;
                if (start == null || !start.IsRegionCollapsed) continue;

                // 区域解析失败时保持可见，避免误藏行
                if (!MultiThreadRange.TryResolve(steps, i, start.RangeMode, start.RangeEndUid, out int endIndex, out _)) continue;

                if (rowIndex > i && rowIndex <= endIndex) return Visibility.Collapsed;
            }

            return Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        /// <summary>把绑定到的步骤总表转成区域解析器需要的只读列表</summary>
        private static IReadOnlyList<AutomationStepBase> ToStepList(object value)
        {
            if (value is IReadOnlyList<AutomationStepBase> readOnly) return readOnly;
            if (value is IEnumerable<AutomationStepBase> sequence) return new List<AutomationStepBase>(sequence);
            return null;
        }

        /// <summary>按引用查找行在总表中的索引（Uid 可能重复，不能用值比较）</summary>
        private static int IndexOf(IReadOnlyList<AutomationStepBase> steps, AutomationStepBase row)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (ReferenceEquals(steps[i], row)) return i;
            }
            return -1;
        }
    }
}
