using ShaoLu.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ShaoLu.Services
{
    /// <summary>
    /// 执行记录展示层：把数据库里的原始记录加工成界面直接绑定的展示文本
    /// （结果、执行时间、线程），并把 10 秒内连续出现的相同结果归并成一条 ×N 记录。
    /// 纯逻辑、无 WPF 依赖，便于单元测试。
    /// </summary>
    public static class ExecutionLogDisplay
    {
        /// <summary>归并的时间窗口（秒）：相邻两条记录间隔不超过该值才算重复</summary>
        private const double MergeWindowSeconds = 10.0;

        private static readonly string MainThreadText = LanguageService.GetLocalizedString("Msg_MainThread", "主线程");
        private static readonly string SubThreadText = LanguageService.GetLocalizedString("Msg_SubThreadPrefix", "子线程");
        private const string SubThreadMark = "[SubThread]";
        private const string TextSourceDirect = "Direct";

        /// <summary>兼容旧数据：从 InputText 文本中提取耗时</summary>
        private static readonly Regex TimePattern = new Regex(@"\[Time:(\d+)ms\]", RegexOptions.Compiled);

        /// <summary>为每条记录填充展示属性，并将相邻重复记录归并</summary>
        public static void Build(IReadOnlyList<StepExecutionLog> logs)
        {
            if (logs == null)
                return;

            foreach (var log in logs)
            {
                if (log == null)
                    continue;
                log.RepeatCount = 1;
                log.IsMergedAway = false;
                log.ThreadDisplay = ResolveThread(log);
                log.ExecutionTimeDisplay = ResolveExecutionTime(log);
                log.ResultDisplay = BuildResult(log);
            }

            MergeRepeats(logs);
        }

        /// <summary>
        /// 归并：按传入顺序扫描，当前行与上一行步骤相同、结果文本相同、且执行时间间隔不超过 10 秒时并入同一组。
        /// 组内保留第一条记录，重复次数累加到该组首行，并在其结果显示末尾追加 ×N（N ≥ 2）；被并入的后续记录标记为 IsMergedAway，由列表过滤掉。
        /// </summary>
        private static void MergeRepeats(IReadOnlyList<StepExecutionLog> logs)
        {
            StepExecutionLog groupHead = null;
            StepExecutionLog prev = null;

            foreach (var log in logs)
            {
                if (log == null)
                    continue;

                if (groupHead != null && CanMerge(prev, log))
                {
                    groupHead.RepeatCount++;
                    // 被归并进上一行的重复记录不再显示，重复次数由组首的「 ×N」体现
                    log.IsMergedAway = true;
                }
                else
                {
                    if (groupHead != null && groupHead.RepeatCount >= 2)
                        AppendRepeatSuffix(groupHead);
                    groupHead = log;
                }

                prev = log;
            }

            if (groupHead != null && groupHead.RepeatCount >= 2)
                AppendRepeatSuffix(groupHead);
        }

        private static bool CanMerge(StepExecutionLog prev, StepExecutionLog current)
        {
            if (prev == null || current == null || prev == current)
                return false;
            if (prev.StepUid != current.StepUid)
                return false;
            if (!string.Equals(prev.ResultDisplay, current.ResultDisplay, StringComparison.Ordinal))
                return false;
            return Math.Abs((current.ExecutedAt - prev.ExecutedAt).TotalSeconds) <= MergeWindowSeconds;
        }

        /// <summary>在结果显示末尾追加「 ×N」（乘法符号，前面有一个空格）</summary>
        private static void AppendRepeatSuffix(StepExecutionLog head)
        {
            head.ResultDisplay = (head.ResultDisplay ?? string.Empty) + " ×" + head.RepeatCount.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>线程标签：优先取结构化字段，旧数据按 [SubThread] 前缀判断</summary>
        private static string ResolveThread(StepExecutionLog log)
        {
            if (!string.IsNullOrWhiteSpace(log.ThreadName))
                return log.ThreadName;
            if (log.InputText != null && log.InputText.Contains(SubThreadMark))
                return SubThreadText;
            return MainThreadText;
        }

        /// <summary>执行时间：优先取结构化字段，旧数据从 InputText 中正则回退提取</summary>
        private static string ResolveExecutionTime(StepExecutionLog log)
        {
            if (log.ExecutionTimeMs.HasValue)
                return FormatMs(log.ExecutionTimeMs.Value);

            if (!string.IsNullOrEmpty(log.InputText))
            {
                var match = TimePattern.Match(log.InputText);
                if (match.Success)
                    return FormatMs(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            }

            return "-";
        }

        /// <summary>统一「数值 ms」格式</summary>
        private static string FormatMs(double value)
        {
            return value.ToString("F0", CultureInfo.InvariantCulture) + " ms";
        }

        /// <summary>
        /// 结果显示：结构化字段可用时按 [Result:..] [Similarity:..] [Error:..] 渲染，
        /// 全部缺失（旧数据）时沿用 InputText 原文；有识别文本时再接上 [OCR:..] 或 [直接获取:..]。
        /// </summary>
        private static string BuildResult(StepExecutionLog log)
        {
            string resultPart;

            if (!log.IsTrue.HasValue && !log.Similarity.HasValue && string.IsNullOrEmpty(log.ErrorMessage))
            {
                resultPart = log.InputText ?? string.Empty;
            }
            else
            {
                var builder = new List<string>();
                if (log.IsTrue.HasValue)
                    builder.Add("[Result:" + (log.IsTrue.Value ? "True" : "False") + "]");
                if (log.Similarity.HasValue && log.Similarity.Value >= 0)
                    builder.Add("[Similarity:" + log.Similarity.Value.ToString("F3", CultureInfo.InvariantCulture) + "]");
                if (!string.IsNullOrEmpty(log.ErrorMessage))
                    builder.Add("[Error:" + log.ErrorMessage + "]");
                resultPart = string.Join(" ", builder);
            }

            var textPart = BuildTextPart(log);
            if (string.IsNullOrEmpty(textPart))
                return resultPart;
            if (string.IsNullOrEmpty(resultPart))
                return textPart;
            return resultPart + " " + textPart;
        }

        /// <summary>识别文本部分：有 OCR 识别就写 [OCR:xxxx]，有直接读取屏幕就写 [直接获取:xxxx]</summary>
        private static string BuildTextPart(StepExecutionLog log)
        {
            if (string.IsNullOrEmpty(log.OCRText))
                return string.Empty;

            bool direct = string.Equals(log.TextSource, TextSourceDirect, StringComparison.OrdinalIgnoreCase);
            return direct
                ? $"[{LanguageService.GetLocalizedString("Msg_DirectRead", "直接获取")}:{log.OCRText}]"
                : $"[OCR:{log.OCRText}]";
        }
    }
}
