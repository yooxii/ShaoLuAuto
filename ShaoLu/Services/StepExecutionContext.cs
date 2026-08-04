using ShaoLu.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ShaoLu.Services
{
    /// <summary>
    /// 步骤执行上下文，存储所有步骤的执行结果，供条件判断引用
    /// </summary>
    public class StepExecutionContext
    {
        private static StepExecutionContext _instance;
        public static StepExecutionContext Instance => _instance ??= new StepExecutionContext();

        private readonly Dictionary<int, StepExecutionResult> _results = new();
        private readonly Dictionary<Guid, StepExecutionResult> _resultsByUid = new();
        private readonly Dictionary<Guid, int> _executionCounts = new();
        private readonly Stopwatch _totalStopwatch = new();

        /// <summary>总执行时间(ms)</summary>
        public double TotalElapsedMs => _totalStopwatch.ElapsedMilliseconds;

        /// <summary>刚刚执行完成的步骤 Uid（供 Step_Triggered 变量使用）</summary>
        public Guid? CurrentStepUid { get; set; }

        /// <summary>当前正在执行的步骤 Uid（步骤开始前设置，供统计窗口展示）</summary>
        public Guid? RunningStepUid { get; set; }

        /// <summary>开始计时</summary>
        public void StartTimer() => _totalStopwatch.Restart();

        /// <summary>停止计时</summary>
        public void StopTimer() => _totalStopwatch.Stop();

        /// <summary>
        /// 设置指定行号步骤的执行结果
        /// </summary>
        public void SetResult(int lineNo, StepExecutionResult result)
        {
            _results[lineNo] = result;
        }

        /// <summary>
        /// 设置指定 Uid 步骤的执行结果
        /// </summary>
        public void SetResultByUid(Guid uid, StepExecutionResult result)
        {
            _resultsByUid[uid] = result;
            _executionCounts.TryGetValue(uid, out int count);
            _executionCounts[uid] = count + 1;
        }

        /// <summary>
        /// 获取指定行号步骤的执行结果
        /// </summary>
        public StepExecutionResult GetResult(int lineNo)
        {
            return _results.TryGetValue(lineNo, out var result) ? result : null;
        }

        /// <summary>
        /// 根据 Uid 获取步骤执行结果
        /// </summary>
        public StepExecutionResult GetResultByUid(Guid? uid)
        {
            if (!uid.HasValue) return null;
            return _resultsByUid.TryGetValue(uid.Value, out var result) ? result : null;
        }

        /// <summary>
        /// 清空所有结果（每次运行前调用）
        /// </summary>
        public void Clear()
        {
            _results.Clear();
            _resultsByUid.Clear();
            _executionCounts.Clear();
            _totalStopwatch.Reset();
            CurrentStepUid = null;
            RunningStepUid = null;
        }

        /// <summary>获取步骤执行时间</summary>
        public double GetStepTime(Guid uid)
        {
            return _resultsByUid.TryGetValue(uid, out var r) ? r.ExecutionTimeMs : -1;
        }

        /// <summary>获取步骤执行次数</summary>
        public int GetStepCount(Guid uid)
        {
            return _executionCounts.TryGetValue(uid, out int count) ? count : 0;
        }

        /// <summary>获取步骤执行结果描述</summary>
        public string GetStepResult(Guid uid)
        {
            if (!_resultsByUid.TryGetValue(uid, out var r)) return null;
            if (!string.IsNullOrEmpty(r.OCRText)) return r.OCRText;
            if (!string.IsNullOrEmpty(r.PopupResult)) return r.PopupResult;
            return r.IsTrue ? "True" : "False";
        }

        /// <summary>
        /// 解析条件变量的实际值
        /// </summary>
        /// <param name="variable">条件变量类型</param>
        /// <param name="stepUid">引用的步骤 Uid（当变量为 Step_* 时使用）</param>
        /// <param name="currentResult">当前步骤的执行结果</param>
        /// <returns>变量的实际值</returns>
        public object ResolveVariable(ConditionVariable variable, Guid? stepUid, StepExecutionResult currentResult)
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
                case ConditionVariable.Self_PopupResult:
                    return currentResult?.PopupResult ?? string.Empty;

                // 引用其他步骤（通过 Uid 查找）
                case ConditionVariable.Step_IsTrue:
                    return GetResultByUid(stepUid)?.IsTrue ?? false;
                case ConditionVariable.Step_Similarity:
                    return GetResultByUid(stepUid)?.Similarity ?? -1;
                case ConditionVariable.Step_ExecutionTimeMs:
                    return GetResultByUid(stepUid)?.ExecutionTimeMs ?? 0;
                case ConditionVariable.Step_ClickX:
                    return GetResultByUid(stepUid)?.ClickPosition?.X ?? 0;
                case ConditionVariable.Step_ClickY:
                    return GetResultByUid(stepUid)?.ClickPosition?.Y ?? 0;
                case ConditionVariable.Step_OCRText:
                    return GetResultByUid(stepUid)?.OCRText ?? string.Empty;
                case ConditionVariable.Step_PopupResult:
                    return GetResultByUid(stepUid)?.PopupResult ?? string.Empty;
                case ConditionVariable.Step_Triggered:
                    // 仅当引用步骤是刚刚执行完成的步骤时为 true（每次步骤执行后只成立一次）
                    return stepUid.HasValue && CurrentStepUid.HasValue && stepUid.Value == CurrentStepUid.Value;

                default:
                    return null;
            }
        }
    }
}
