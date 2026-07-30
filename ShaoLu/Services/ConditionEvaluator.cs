using ShaoLu.Models;
using System;
using System.Collections.Generic;

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
