using ShaoLu.Models;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ShaoLu.Services
{
    /// <summary>
    /// 条件评估器，负责评估规则行列表并返回最终布尔结果
    /// </summary>
    public static class ConditionEvaluator
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// 评估条件列表，返回最终 bool 结果
        /// 多行条件按 AND/OR 连接器组合
        /// </summary>
        /// <param name="conditions">条件规则行列表</param>
        /// <param name="context">执行上下文</param>
        /// <param name="currentResult">当前步骤的执行结果</param>
        /// <returns>最终评估结果</returns>
        public static bool Evaluate(IList<StepCondition> conditions, StepExecutionContext context, StepExecutionResult currentResult)
        {
            if (conditions == null || conditions.Count == 0)
            {
                // 没有条件时，使用默认结果
                return currentResult?.IsTrue ?? false;
            }

            // 第一行的结果作为初始值
            bool result = EvaluateSingle(conditions[0], context, currentResult);

            // 从第二行开始，根据前一行的 Connector 进行组合
            for (int i = 1; i < conditions.Count; i++)
            {
                bool current = EvaluateSingle(conditions[i], context, currentResult);
                var connector = conditions[i - 1].Connector;

                if (connector == LogicConnector.And)
                {
                    result = result && current;
                }
                else // Or
                {
                    result = result || current;
                }
            }

            return result;
        }

        /// <summary>
        /// 评估单个条件行
        /// </summary>
        private static bool EvaluateSingle(StepCondition cond, StepExecutionContext context, StepExecutionResult currentResult)
        {
            try
            {
                // 常量类型直接返回
                if (cond.Variable == ConditionVariable.ConstantTrue)
                    return true;
                if (cond.Variable == ConditionVariable.ConstantFalse)
                    return false;

                // 解析变量值
                object leftValue = context.ResolveVariable(cond.Variable, cond.StepUid, currentResult);

                // 识别文本变量：按条件行的提取设置预处理文本
                if (cond.IsTextVariable && leftValue is string textValue)
                {
                    leftValue = ApplyTextExtraction(textValue, cond);
                }

                // 执行比较
                return Compare(leftValue, cond.Operator, cond.Value);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Condition evaluation failed for variable {0}", cond.Variable);
                return false;
            }
        }

        /// <summary>
        /// 比较逻辑（支持数字比较、字符串相等/包含、布尔比较）
        /// </summary>
        private static bool Compare(object left, ConditionOperator op, string right)
        {
            // 处理 IsEmpty / IsNotEmpty（不需要右值）
            if (op == ConditionOperator.IsEmpty)
            {
                return IsEmptyValue(left);
            }
            if (op == ConditionOperator.IsNotEmpty)
            {
                return !IsEmptyValue(left);
            }

            if (left == null)
            {
                return op == ConditionOperator.NotEqual || op == ConditionOperator.NotContains;
            }

            // 正则表达式匹配（右值为正则模式）
            if (op == ConditionOperator.RegexMatch)
            {
                string input = left.ToString() ?? string.Empty;
                try
                {
                    return Regex.IsMatch(input, right ?? string.Empty);
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Invalid regex pattern: {0}", right);
                    return false;
                }
            }

            // 布尔类型比较
            if (left is bool boolVal)
            {
                bool rightBool = ParseBool(right);
                return op switch
                {
                    ConditionOperator.Equal => boolVal == rightBool,
                    ConditionOperator.NotEqual => boolVal != rightBool,
                    _ => false
                };
            }

            // 数字类型比较
            if (IsNumeric(left, out double leftNum) && double.TryParse(right, out double rightNum))
            {
                return op switch
                {
                    ConditionOperator.Equal => Math.Abs(leftNum - rightNum) < 1e-9,
                    ConditionOperator.NotEqual => Math.Abs(leftNum - rightNum) >= 1e-9,
                    ConditionOperator.GreaterThan => leftNum > rightNum,
                    ConditionOperator.LessThan => leftNum < rightNum,
                    ConditionOperator.GreaterOrEqual => leftNum >= rightNum,
                    ConditionOperator.LessOrEqual => leftNum <= rightNum,
                    _ => false
                };
            }

            // 字符串比较
            string leftStr = left.ToString() ?? string.Empty;
            string rightStr = right ?? string.Empty;

            return op switch
            {
                ConditionOperator.Equal => string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase),
                ConditionOperator.NotEqual => !string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase),
                ConditionOperator.Contains => leftStr.IndexOf(rightStr, StringComparison.OrdinalIgnoreCase) >= 0,
                ConditionOperator.NotContains => leftStr.IndexOf(rightStr, StringComparison.OrdinalIgnoreCase) < 0,
                // 对于字符串，尝试数字比较
                ConditionOperator.GreaterThan when double.TryParse(leftStr, out double l) && double.TryParse(rightStr, out double r) => l > r,
                ConditionOperator.LessThan when double.TryParse(leftStr, out double l) && double.TryParse(rightStr, out double r) => l < r,
                ConditionOperator.GreaterOrEqual when double.TryParse(leftStr, out double l) && double.TryParse(rightStr, out double r) => l >= r,
                ConditionOperator.LessOrEqual when double.TryParse(leftStr, out double l) && double.TryParse(rightStr, out double r) => l <= r,
                _ => false
            };
        }

        private static bool IsEmptyValue(object value)
        {
            if (value == null) return true;
            if (value is string s) return string.IsNullOrWhiteSpace(s);
            return false;
        }

        /// <summary>
        /// 按条件行的提取设置对识别文本进行预处理
        /// 支持：指定行、从子字符串开始读取指定长度、从开头/末尾读取
        /// </summary>
        public static string ApplyTextExtraction(string text, StepCondition cond)
        {
            if (string.IsNullOrEmpty(text) || cond == null || cond.TextExtractMode == TextExtractMode.Whole)
                return text ?? string.Empty;

            try
            {
                switch (cond.TextExtractMode)
                {
                    case TextExtractMode.Lines:
                        return ExtractLines(text, cond.ExtractLines);
                    case TextExtractMode.FromSubstring:
                        return ExtractFromSubstring(text, cond.ExtractMarker, cond.ExtractLength);
                    case TextExtractMode.FromStart:
                        return cond.ExtractLength > 0 && text.Length > cond.ExtractLength
                            ? text.Substring(0, cond.ExtractLength)
                            : text;
                    case TextExtractMode.FromEnd:
                        return cond.ExtractLength > 0 && text.Length > cond.ExtractLength
                            ? text.Substring(text.Length - cond.ExtractLength)
                            : text;
                    default:
                        return text;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Text extraction failed for mode {0}", cond.TextExtractMode);
                return text;
            }
        }

        /// <summary>
        /// 提取指定行（1 基，支持 "1,3,5-8" 格式，多行结果按换行拼接）
        /// </summary>
        private static string ExtractLines(string text, string lineSpec)
        {
            if (string.IsNullOrWhiteSpace(lineSpec)) return text;

            string[] lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var selected = new List<string>();
            foreach (var part in lineSpec.Split(new[] { ',', '，', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var segment = part.Trim();
                if (segment.Contains("-"))
                {
                    var bounds = segment.Split('-');
                    if (bounds.Length == 2
                        && int.TryParse(bounds[0].Trim(), out int start)
                        && int.TryParse(bounds[1].Trim(), out int end))
                    {
                        for (int i = Math.Max(1, start); i <= Math.Min(lines.Length, end); i++)
                            selected.Add(lines[i - 1]);
                    }
                }
                else if (int.TryParse(segment, out int lineNo) && lineNo >= 1 && lineNo <= lines.Length)
                {
                    selected.Add(lines[lineNo - 1]);
                }
            }
            return string.Join("\n", selected);
        }

        /// <summary>
        /// 从子字符串首次出现位置之后读取指定长度（length&lt;=0 表示直到末尾）
        /// </summary>
        private static string ExtractFromSubstring(string text, string marker, int length)
        {
            if (string.IsNullOrEmpty(marker))
            {
                // 未指定子字符串时退化为从开头读取
                return length > 0 && text.Length > length ? text.Substring(0, length) : text;
            }

            int index = text.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) return string.Empty;

            string rest = text.Substring(index + marker.Length);
            return length > 0 && rest.Length > length ? rest.Substring(0, length) : rest;
        }

        private static bool IsNumeric(object value, out double result)
        {
            switch (value)
            {
                case int i:
                    result = i;
                    return true;
                case double d:
                    result = d;
                    return true;
                case float f:
                    result = f;
                    return true;
                case long l:
                    result = l;
                    return true;
                case decimal m:
                    result = (double)m;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }

        private static bool ParseBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (bool.TryParse(value, out bool result)) return result;
            // 支持 "1" / "0" 形式
            if (value == "1") return true;
            if (value == "0") return false;
            return false;
        }
    }
}
