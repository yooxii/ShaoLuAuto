using ShaoLu.Models;
using System;
using System.Globalization;
using System.Windows.Data;

namespace ShaoLu.Converters
{
    /// <summary>
    /// 将条件相关枚举转换为中文显示文本
    /// </summary>
    public class ConditionEnumToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ConditionMode mode)
                return ConvertConditionMode(mode);
            if (value is ConditionVariable variable)
                return ConvertConditionVariable(variable);
            if (value is ConditionOperator op)
                return ConvertConditionOperator(op);
            if (value is LogicConnector connector)
                return ConvertLogicConnector(connector);
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public static string ConvertConditionMode(ConditionMode mode) => mode switch
        {
            ConditionMode.Default => "默认（使用步骤自身结果）",
            ConditionMode.Custom => "自定义（使用下方规则判断）",
            _ => mode.ToString(),
        };

        public static string ConvertConditionVariable(ConditionVariable v) => v switch
        {
            ConditionVariable.ConstantTrue => "常量：真",
            ConditionVariable.ConstantFalse => "常量：假",
            ConditionVariable.Self_IsTrue => "本步骤 → 执行结果",
            ConditionVariable.Self_Similarity => "本步骤 → 相似度",
            ConditionVariable.Self_ExecutionTimeMs => "本步骤 → 执行耗时(ms)",
            ConditionVariable.Self_ClickX => "本步骤 → 点击X坐标",
            ConditionVariable.Self_ClickY => "本步骤 → 点击Y坐标",
            ConditionVariable.Self_OCRText => "本步骤 → OCR识别文本",
            ConditionVariable.Step_IsTrue => "引用步骤 → 执行结果",
            ConditionVariable.Step_Similarity => "引用步骤 → 相似度",
            ConditionVariable.Step_ExecutionTimeMs => "引用步骤 → 执行耗时(ms)",
            ConditionVariable.Step_ClickX => "引用步骤 → 点击X坐标",
            ConditionVariable.Step_ClickY => "引用步骤 → 点击Y坐标",
            ConditionVariable.Step_OCRText => "引用步骤 → OCR识别文本",
            _ => v.ToString(),
        };

        public static string ConvertConditionOperator(ConditionOperator op) => op switch
        {
            ConditionOperator.Equal => "等于 (==)",
            ConditionOperator.NotEqual => "不等于 (!=)",
            ConditionOperator.GreaterThan => "大于 (>)",
            ConditionOperator.LessThan => "小于 (<)",
            ConditionOperator.GreaterOrEqual => "大于等于 (>=)",
            ConditionOperator.LessOrEqual => "小于等于 (<=)",
            ConditionOperator.Contains => "包含",
            ConditionOperator.NotContains => "不包含",
            ConditionOperator.IsEmpty => "为空",
            ConditionOperator.IsNotEmpty => "不为空",
            _ => op.ToString(),
        };

        public static string ConvertLogicConnector(LogicConnector c) => c switch
        {
            LogicConnector.And => "并且 (AND)",
            LogicConnector.Or => "或者 (OR)",
            _ => c.ToString(),
        };

        /// <summary>
        /// 获取条件变量的说明文本（用于ToolTip）
        /// </summary>
        public static string GetVariableTooltip(ConditionVariable v) => v switch
        {
            ConditionVariable.ConstantTrue => "始终为真的常量值",
            ConditionVariable.ConstantFalse => "始终为假的常量值",
            ConditionVariable.Self_IsTrue => "当前步骤执行后是否成功（true/false）",
            ConditionVariable.Self_Similarity => "当前步骤图像匹配的实际相似度（0~1）",
            ConditionVariable.Self_ExecutionTimeMs => "当前步骤执行所花费的时间（毫秒）",
            ConditionVariable.Self_ClickX => "当前步骤实际点击的屏幕X坐标",
            ConditionVariable.Self_ClickY => "当前步骤实际点击的屏幕Y坐标",
            ConditionVariable.Self_OCRText => "当前步骤OCR识别出的文本内容",
            ConditionVariable.Step_IsTrue => "指定行号步骤的执行结果（需在左侧填写行号）",
            ConditionVariable.Step_Similarity => "指定行号步骤的图像匹配相似度",
            ConditionVariable.Step_ExecutionTimeMs => "指定行号步骤的执行耗时",
            ConditionVariable.Step_ClickX => "指定行号步骤的点击X坐标",
            ConditionVariable.Step_ClickY => "指定行号步骤的点击Y坐标",
            ConditionVariable.Step_OCRText => "指定行号步骤的OCR识别文本",
            _ => string.Empty,
        };
    }
}
