using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ShaoLu.Models;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;

namespace ShaoLu.Services
{
    /// <summary>
    /// 调试日志：调试模式打开时，把每一步的执行细节单独写进 <c>logs\debug\debug_&lt;时间&gt;.log</c>。
    /// 与「执行记录」（SQLite 数据库）和程序日志（<c>logs\yyyy-MM-dd.log</c>）分开保存，
    /// 一个工程一次运行一个文件，现场排查时直接打开对应文件即可。
    /// </summary>
    public static class DebugLogService
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static readonly object Gate = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>测试用：改写日志目录（为 null 时用程序目录下的 logs\debug）</summary>
        internal static string DirectoryOverride { get; set; }

        /// <summary>调试日志目录</summary>
        public static string LogDirectory
            => DirectoryOverride ?? Path.Combine(AppContext.BaseDirectory, "logs", "debug");

        /// <summary>调试模式是否打开（读取用户设置；读不到按关闭处理）</summary>
        public static bool IsEnabled
        {
            get
            {
                try { return SingletonLocator.Settings?.Step?.DebugMode == true; }
                catch { return false; }
            }
        }

        /// <summary>本次运行的调试日志文件（还没开始记录时为空）</summary>
        public static string CurrentLogPath { get; private set; }

        #region 记录

        /// <summary>
        /// 开始记录一次运行：返回新建的日志文件路径；调试模式关着时返回 null，不建文件。
        /// </summary>
        public static string BeginSession(string projectName, int stepCount)
        {
            if (!IsEnabled) return null;

            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogDirectory);
                    string path = Path.Combine(LogDirectory, $"debug_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log");
                    CurrentLogPath = path;

                    var sb = new StringBuilder();
                    sb.AppendLine("==================================================");
                    sb.AppendLine("AutoShaoLu 调试记录");
                    sb.AppendLine($"开始时间 : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine($"工程     : {Describe(projectName)}");
                    sb.AppendLine($"步骤数   : {stepCount}");
                    sb.AppendLine("==================================================");
                    File.AppendAllText(path, sb.ToString(), Utf8NoBom);
                    return path;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "调试日志：创建记录文件失败");
                return null;
            }
        }

        /// <summary>步骤开始（配合暂停 / 单步排查看「哪一步卡住」）</summary>
        public static void StepStarted(int index, AutomationStepBase step, string threadName = null)
        {
            if (step == null) return;
            Append($"[{Stamp()}] ▶ 第 {index + 1} 步 {step.Name}（{step.Type}）{ThreadPart(threadName)}");
        }

        /// <summary>
        /// 步骤执行完成：结果、耗时、相似度、识别文本、参数细节与下一步走向。
        /// </summary>
        /// <param name="nextIndex">解析出的下一步索引；-1 表示顺序执行下一步</param>
        public static void StepFinished(int index, AutomationStepBase step, StepExecutionResult result, string details,
            int nextIndex, string nextStepName, string threadName = null)
        {
            if (step == null) return;

            var sb = new StringBuilder();
            sb.Append($"[{Stamp()}] ● 第 {index + 1} 步 {step.Name}（{step.Type}）{ThreadPart(threadName)}");
            sb.Append($"  结果={(step.IsTrue ? "真" : "假")}");
            if (result != null && result.ExecutionTimeMs > 0)
                sb.Append($"  耗时={result.ExecutionTimeMs:F0}ms");
            if (result != null && result.Similarity > 0)
                sb.Append($"  相似度={result.Similarity:F3}");
            if (result != null && !string.IsNullOrEmpty(result.OCRText))
                sb.Append($"  文本=\"{Shorten(result.OCRText, 120)}\"");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(details))
                sb.AppendLine($"      参数: {Shorten(details, 800)}");

            if (step.IsError && !string.IsNullOrWhiteSpace(step.ErrorMessage))
                sb.AppendLine($"      错误: {Shorten(step.ErrorMessage, 400)}");

            sb.AppendLine(nextIndex >= 0
                ? $"      跳转: 第 {nextIndex + 1} 步 {Describe(nextStepName)}"
                : "      跳转: 顺序执行下一步");

            Append(sb.ToString().TrimEnd());
        }

        /// <summary>步骤抛异常 / 被判定失败时的记录（异常路径没有正常结果）</summary>
        public static void StepFailed(int index, AutomationStepBase step, string error, double elapsedMs, string threadName = null)
        {
            if (step == null) return;

            var sb = new StringBuilder();
            sb.Append($"[{Stamp()}] ✖ 第 {index + 1} 步 {step.Name}（{step.Type}）{ThreadPart(threadName)}");
            if (elapsedMs > 0) sb.Append($"  耗时={elapsedMs:F0}ms");
            sb.AppendLine();
            sb.AppendLine($"      错误: {Shorten(error ?? step.ErrorMessage ?? "未知错误", 400)}");
            Append(sb.ToString().TrimEnd());
        }

        /// <summary>运行期间的其它事件：暂停 / 继续 / 停止 / 中断 / 跳转等</summary>
        public static void Note(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            Append($"[{Stamp()}] · {message}");
        }

        /// <summary>结束一次运行</summary>
        public static void EndSession(string reason)
        {
            Append($"[{Stamp()}] ■ 运行结束：{Describe(reason)}");
            Append("==================================================");
        }

        #endregion

        #region 文件管理

        /// <summary>最近一份调试日志（没有时为空）</summary>
        public static string LatestLogPath()
        {
            try
            {
                if (!Directory.Exists(LogDirectory)) return null;
                return new DirectoryInfo(LogDirectory)
                    .GetFiles("debug_*.log")
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault()?.FullName;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "调试日志：查找最近日志失败");
                return null;
            }
        }

        /// <summary>删除所有调试日志，返回删除的文件数</summary>
        public static int ClearLogs()
        {
            int count = 0;
            try
            {
                if (!Directory.Exists(LogDirectory)) return 0;
                foreach (var file in new DirectoryInfo(LogDirectory).GetFiles("debug_*.log"))
                {
                    try
                    {
                        file.Delete();
                        count++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "调试日志：删除 {0} 失败", file.FullName);
                    }
                }

                // 正在记录的这份也一并作废（下次运行会新建）
                lock (Gate) { CurrentLogPath = null; }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "调试日志：清理失败");
            }

            return count;
        }

        /// <summary>把当前整个桌面截一张图存进调试目录，返回图片路径（失败返回 null）</summary>
        public static string SaveScreenshot()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
                if (bounds.Width <= 0 || bounds.Height <= 0) return null;

                using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);

                string path = Path.Combine(LogDirectory, $"screen_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                return path;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "调试日志：截图失败");
                return null;
            }
        }

        #endregion

        #region 内部

        private static void Append(string text)
        {
            if (!IsEnabled || string.IsNullOrEmpty(text)) return;

            string path = CurrentLogPath;
            if (string.IsNullOrEmpty(path))
            {
                // 运行中途才打开调试模式：补建一份文件，别把后面的记录丢掉
                path = BeginSession(null, 0);
                if (string.IsNullOrEmpty(path)) return;
            }

            try
            {
                lock (Gate)
                {
                    File.AppendAllText(path, text + Environment.NewLine, Utf8NoBom);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "调试日志：写入失败");
            }
        }

        private static string Stamp() => DateTime.Now.ToString("HH:mm:ss.fff");

        private static string ThreadPart(string threadName)
            => string.IsNullOrWhiteSpace(threadName) ? "  [主线程]" : $"  [{threadName}]";

        private static string Describe(string text) => string.IsNullOrWhiteSpace(text) ? "（未命名）" : text;

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
            return text.Substring(0, max) + "…";
        }

        #endregion
    }
}
