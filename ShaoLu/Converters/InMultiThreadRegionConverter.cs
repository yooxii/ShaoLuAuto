using ShaoLu.Services;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace ShaoLu.Converters
{
    /// <summary>
    /// 判断某个步骤是否属于「多线程区域」（用于列表行淡蓝背景，与普通步骤区分）。
    /// values[0] = 当前行步骤；values[1] = 步骤总表；values[2] = 折叠状态版本号（仅用于触发重算）。
    /// 判定口径：遍历步骤总表里每个「多线程开始」步骤（不要求折叠），
    /// 用 MultiThreadRange.TryResolve 解析它圈定的区域（开始行之后、结束标记之前），
    /// 区域内各行、以及「多线程开始」行与配对的「多线程区域结束」标记行本身都返回 true，
    /// 这样从开始到结束的整段区域颜色一致；未配对的孤立标记行不算。
    /// </summary>
    public class InMultiThreadRegionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var row = values != null && values.Length > 0 ? values[0] as AutomationStepBase : null;
            if (row == null || values.Length < 2) return false;

            var steps = ToStepList(values[1]);
            if (steps == null) return false;

            int rowIndex = IndexOf(steps, row);
            if (rowIndex < 0) return false;

            for (int i = 0; i < steps.Count; i++)
            {
                var start = steps[i] as MultiThreadStartStep;
                if (start == null) continue;

                // 区域解析失败时按「不在区域内」处理，避免误标
                if (!MultiThreadRange.TryResolve(steps, i, start.RangeMode, start.RangeEndUid, out int endIndex, out _)) continue;

                // 开始行本身
                if (rowIndex == i) return true;

                // 区域内的步骤
                if (rowIndex > i && rowIndex <= endIndex) return true;

                // 与区域配对的「多线程区域结束」标记行（它本身不在区域执行范围内，但属于整段区域）
                if (rowIndex == endIndex + 1 && rowIndex < steps.Count && steps[rowIndex] is MultiThreadEndStep) return true;
            }

            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static IReadOnlyList<AutomationStepBase> ToStepList(object value)
        {
            if (value is IReadOnlyList<AutomationStepBase> readOnly) return readOnly;
            if (value is IEnumerable<AutomationStepBase> sequence) return new List<AutomationStepBase>(sequence);
            return null;
        }

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
