using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Viewmodels.AutomationStep;
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
            if (value is PopupCloseMode closeMode)
                return ConvertPopupCloseMode(closeMode);
            if (value is PopupWindowStyle winStyle)
                return ConvertPopupWindowStyle(winStyle);
            if (value is StepScope scope)
                return ConvertStepScope(scope);
            if (value is PositionMode posMode)
                return ConvertPositionMode(posMode);
            if (value is GetInputMode inputMode)
                return ConvertGetInputMode(inputMode);
            if (value is TextExtractMode extractMode)
                return ConvertTextExtractMode(extractMode);
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
            ConditionVariable.Self_OCRText => LanguageService.GetLocalizedString("ConditionVariable_Self_Text"),
            ConditionVariable.Self_PopupResult => "本步骤 → 弹窗选择结果",
            ConditionVariable.Step_IsTrue => "引用步骤 → 执行结果",
            ConditionVariable.Step_Similarity => "引用步骤 → 相似度",
            ConditionVariable.Step_ExecutionTimeMs => "引用步骤 → 执行耗时(ms)",
            ConditionVariable.Step_ClickX => "引用步骤 → 点击X坐标",
            ConditionVariable.Step_ClickY => "引用步骤 → 点击Y坐标",
            ConditionVariable.Step_OCRText => LanguageService.GetLocalizedString("ConditionVariable_Step_Text"),
            ConditionVariable.Step_PopupResult => "引用步骤 → 弹窗选择结果",
            ConditionVariable.Step_Triggered => Services.LanguageService.GetLocalizedString("ConditionVariable_Step_Triggered"),
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
            ConditionOperator.RegexMatch => Services.LanguageService.GetLocalizedString("ConditionOperator_RegexMatch"),
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

        public static string ConvertPopupWindowStyle(PopupWindowStyle s) => s switch
        {
            PopupWindowStyle.Normal => Services.LanguageService.GetLocalizedString("PopupWindowStyle_Normal"),
            PopupWindowStyle.Compact => Services.LanguageService.GetLocalizedString("PopupWindowStyle_Compact"),
            _ => s.ToString(),
        };

        public static string ConvertPopupCloseMode(PopupCloseMode m) => m switch
        {
            PopupCloseMode.ButtonClick => Services.LanguageService.GetLocalizedString("CloseMode_ButtonClick"),
            PopupCloseMode.Timeout => Services.LanguageService.GetLocalizedString("CloseMode_Timeout"),
            PopupCloseMode.StepReached => Services.LanguageService.GetLocalizedString("CloseMode_StepReached"),
            _ => m.ToString(),
        };

        public static string ConvertStepScope(StepScope s) => s switch
        {
            StepScope.All => Services.LanguageService.GetLocalizedString("StatsScope_All"),
            StepScope.LoggedOnly => Services.LanguageService.GetLocalizedString("StatsScope_LoggedOnly"),
            StepScope.Custom => Services.LanguageService.GetLocalizedString("StatsScope_Custom"),
            _ => s.ToString(),
        };

        public static string ConvertPositionMode(PositionMode m) => m switch
        {
            PositionMode.Absolute => Services.LanguageService.GetLocalizedString("PositionMode_Absolute"),
            PositionMode.CurrentMouse => Services.LanguageService.GetLocalizedString("PositionMode_CurrentMouse"),
            _ => m.ToString(),
        };

        public static string ConvertGetInputMode(GetInputMode m) => m switch
        {
            GetInputMode.OCR => Services.LanguageService.GetLocalizedString("InputMode_OCR"),
            GetInputMode.ScreenText => Services.LanguageService.GetLocalizedString("InputMode_ScreenText"),
            _ => m.ToString(),
        };

        public static string ConvertTextExtractMode(TextExtractMode m) => m switch
        {
            TextExtractMode.Whole => Services.LanguageService.GetLocalizedString("TextExtract_Whole"),
            TextExtractMode.Lines => Services.LanguageService.GetLocalizedString("TextExtract_Lines"),
            TextExtractMode.FromSubstring => Services.LanguageService.GetLocalizedString("TextExtract_FromSubstring"),
            TextExtractMode.FromStart => Services.LanguageService.GetLocalizedString("TextExtract_FromStart"),
            TextExtractMode.FromEnd => Services.LanguageService.GetLocalizedString("TextExtract_FromEnd"),
            _ => m.ToString(),
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
            ConditionVariable.Self_OCRText => Services.LanguageService.GetLocalizedString("ConditionVariable_Self_Text_Tooltip"),
            ConditionVariable.Self_PopupResult => "当前步骤弹出框用户点击的按钮值",
            ConditionVariable.Step_IsTrue => "指定行号步骤的执行结果（需在左侧填写行号）",
            ConditionVariable.Step_Similarity => "指定行号步骤的图像匹配相似度",
            ConditionVariable.Step_ExecutionTimeMs => "指定行号步骤的执行耗时",
            ConditionVariable.Step_ClickX => "指定行号步骤的点击X坐标",
            ConditionVariable.Step_ClickY => "指定行号步骤的点击Y坐标",
            ConditionVariable.Step_OCRText => Services.LanguageService.GetLocalizedString("ConditionVariable_Step_Text_Tooltip"),
            ConditionVariable.Step_PopupResult => "指定行号步骤的弹出框选择结果",
            ConditionVariable.Step_Triggered => Services.LanguageService.GetLocalizedString("ConditionVariable_Step_Triggered_Tooltip"),
            _ => string.Empty,
        };
    }
}
