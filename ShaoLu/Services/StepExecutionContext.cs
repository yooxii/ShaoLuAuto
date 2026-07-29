using ShaoLu.Models;
using System.Collections.Generic;

namespace ShaoLu.Services
{
    /// <summary>
    /// 步骤执行上下文，存储所有步骤的执行结果，供条件判断引用
    /// </summary>
    public class StepExecutionContext
    {
        private readonly Dictionary<int, StepExecutionResult> _results = new();

        /// <summary>
        /// 设置指定行号步骤的执行结果
        /// </summary>
        public void SetResult(int lineNo, StepExecutionResult result)
        {
            _results[lineNo] = result;
        }

        /// <summary>
        /// 获取指定行号步骤的执行结果
        /// </summary>
        public StepExecutionResult GetResult(int lineNo)
        {
            return _results.TryGetValue(lineNo, out var result) ? result : null;
        }

        /// <summary>
        /// 清空所有结果（每次运行前调用）
        /// </summary>
        public void Clear()
        {
            _results.Clear();
        }

        /// <summary>
        /// 解析条件变量的实际值
        /// </summary>
        /// <param name="variable">条件变量类型</param>
        /// <param name="stepLineNo">引用的步骤行号（当变量为 Step_* 时使用）</param>
        /// <param name="currentResult">当前步骤的执行结果</param>
        /// <returns>变量的实际值</returns>
        public object ResolveVariable(ConditionVariable variable, int stepLineNo, StepExecutionResult currentResult)
        {
            switch (variable)
            {
                // 常量
                case ConditionVariable.ConstantTrue:
                    return true;
                case ConditionVariable.ConstantFalse:
                    return false;

                // 当前步骤自身
                case ConditionVariable.Self_IsTrue:
                    return currentResult?.IsTrue ?? false;
                case ConditionVariable.Self_Similarity:
                    return currentResult?.Similarity ?? -1;
                case ConditionVariable.Self_ExecutionTimeMs:
                    return currentResult?.ExecutionTimeMs ?? 0;
                case ConditionVariable.Self_ClickX:
                    return currentResult?.ClickPosition?.X ?? 0;
                case ConditionVariable.Self_ClickY:
                    return currentResult?.ClickPosition?.Y ?? 0;
                case ConditionVariable.Self_OCRText:
                    return currentResult?.OCRText ?? string.Empty;

                // 引用其他步骤
                case ConditionVariable.Step_IsTrue:
                    return GetResult(stepLineNo)?.IsTrue ?? false;
                case ConditionVariable.Step_Similarity:
                    return GetResult(stepLineNo)?.Similarity ?? -1;
                case ConditionVariable.Step_ExecutionTimeMs:
                    return GetResult(stepLineNo)?.ExecutionTimeMs ?? 0;
                case ConditionVariable.Step_ClickX:
                    return GetResult(stepLineNo)?.ClickPosition?.X ?? 0;
                case ConditionVariable.Step_ClickY:
                    return GetResult(stepLineNo)?.ClickPosition?.Y ?? 0;
                case ConditionVariable.Step_OCRText:
                    return GetResult(stepLineNo)?.OCRText ?? string.Empty;

                default:
                    return null;
            }
        }
    }
}
