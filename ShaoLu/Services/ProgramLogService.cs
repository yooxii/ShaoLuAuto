using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ShaoLu.Services
{
    /// <summary>
    /// 一条程序日志（来自 NLog 的文本日志文件）。
    /// 文件里的行格式为：<c>时间 | 等级 | 记录器 | 内容</c>（见 NLog.config）。
    /// </summary>
    public sealed class ProgramLogEntry
    {
        /// <summary>日志时间</summary>
        public DateTime Time { get; set; }

        /// <summary>等级（DEBUG / INFO / WARN / ERROR / FATAL）</summary>
        public string Level { get; set; }

        /// <summary>记录器名称（一般是类名）</summary>
        public string Logger { get; set; }

        /// <summary>日志内容（含异常信息；多行内容会合并成一行存储）</summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// 程序日志读取：直接解析 NLog 写出的 <c>logs\{日期}.log</c>，
    /// 不需要额外的内存缓冲，所以程序重启前的日志也能查到。
    /// </summary>
    public static class ProgramLogService
    {
        /// <summary>单次最多读取的日志条数（只保留最近的这些条，避免大文件拖慢界面）</summary>
        public const int DefaultMaxEntries = 3000;

        private static readonly string[] TimeFormats =
        {
            "yyyy-MM-dd HH:mm:ss.ffff",
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss.ff",
            "yyyy-MM-dd HH:mm:ss",
        };

        private static readonly string[] LevelNames = { "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL" };

        /// <summary>
        /// 日志目录（与 NLog.config 的 ${basedir}/logs 一致；${basedir} 即程序目录）
        /// </summary>
        public static string LogDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        /// <summary>日志等级名（从低到高），供界面下拉绑定</summary>
        public static IReadOnlyList<string> Levels => LevelNames;

        /// <summary>日志里的等级顺序，值越大越严重；未知等级按 INFO 处理</summary>
        public static int LevelRank(string level)
        {
            if (string.IsNullOrWhiteSpace(level)) return 2;

            string normalized = level.Trim().ToUpperInvariant();
            for (int i = 0; i < LevelNames.Length; i++)
            {
                if (string.Equals(LevelNames[i], normalized, StringComparison.Ordinal))
                    return i;
            }
            return 2;
        }

        /// <summary>
        /// 列出日志文件（完整路径，按日期新的在前）；目录不存在时返回空列表。
        /// </summary>
        public static List<string> GetLogFiles()
        {
            var files = new List<string>();
            try
            {
                string dir = LogDirectory;
                if (!Directory.Exists(dir)) return files;

                files.AddRange(Directory.GetFiles(dir, "*.log")
                    .OrderByDescending(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
                // 目录不可读：当作没有日志
            }
            return files;
        }

        /// <summary>
        /// 读取并解析一个日志文件，返回按时间从新到旧排序的条目（最多 <paramref name="maxEntries"/> 条）。
        /// 文件被 NLog 占用时用共享读打开，不会影响程序继续写日志。
        /// </summary>
        public static List<ProgramLogEntry> Read(string filePath, int maxEntries = DefaultMaxEntries)
        {
            var entries = new List<ProgramLogEntry>();
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return entries;
            if (maxEntries <= 0)
                maxEntries = DefaultMaxEntries;

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    ProgramLogEntry current = null;
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (TryParseLine(line, out var parsed))
                        {
                            current = parsed;
                            entries.Add(parsed);
                            // 超过上限时成批裁掉最早的部分，避免堆积
                            if (entries.Count > maxEntries * 2)
                                entries.RemoveRange(0, entries.Count - maxEntries);
                        }
                        else if (current != null && !string.IsNullOrWhiteSpace(line))
                        {
                            // 多行日志（异常堆栈等）：接在上一条后面，仍然一条一行显示
                            current.Message = (current.Message ?? string.Empty) + " " + line.Trim();
                        }
                    }
                }
            }
            catch
            {
                // 文件被独占锁住或读取失败：返回已经读到的部分
            }

            if (entries.Count > maxEntries)
                entries.RemoveRange(0, entries.Count - maxEntries);

            // 文件里是从旧到新，展示时反过来：最新的在最上面
            entries.Reverse();
            return entries;
        }

        /// <summary>把一条日志渲染成一行文本：时间 | 等级 | 记录器 | 内容</summary>
        public static string FormatLine(ProgramLogEntry entry)
        {
            if (entry == null) return string.Empty;

            string level = string.IsNullOrEmpty(entry.Level) ? "INFO" : entry.Level;
            string logger = string.IsNullOrEmpty(entry.Logger) ? "-" : entry.Logger;
            string message = string.IsNullOrEmpty(entry.Message) ? string.Empty : entry.Message;

            return string.Format(CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss.fff} | {1,-5} | {2} | {3}",
                entry.Time, level, logger, message);
        }

        /// <summary>解析一行日志；不符合「时间 | 等级 | 记录器 | 内容」格式时返回 false</summary>
        private static bool TryParseLine(string line, out ProgramLogEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(line))
                return false;

            int p1 = line.IndexOf(" | ", StringComparison.Ordinal);
            if (p1 <= 0) return false;
            int p2 = line.IndexOf(" | ", p1 + 3, StringComparison.Ordinal);
            if (p2 < 0) return false;
            int p3 = line.IndexOf(" | ", p2 + 3, StringComparison.Ordinal);
            if (p3 < 0) return false;

            string timeText = line.Substring(0, p1).Trim();
            if (!DateTime.TryParseExact(timeText, TimeFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime time))
                return false;

            entry = new ProgramLogEntry
            {
                Time = time,
                Level = line.Substring(p1 + 3, p2 - p1 - 3).Trim(),
                Logger = line.Substring(p2 + 3, p3 - p2 - 3).Trim(),
                Message = line.Substring(p3 + 3).Trim(),
            };
            return true;
        }
    }
}
