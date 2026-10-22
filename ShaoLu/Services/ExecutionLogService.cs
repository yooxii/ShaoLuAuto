using ShaoLu.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShaoLu.Services
{
    /// <summary>
    /// 执行日志服务，使用 FreeSql + SQLite 记录文字步骤的执行历史
    /// </summary>
    public class ExecutionLogService
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// 允许作为排序依据的列名白名单（防止把外部字符串直接拼进 SQL 排序子句）
        /// </summary>
        private static readonly HashSet<string> AllowedSortColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            "Id",
            "StepUid",
            "StepFileName",
            "StepName",
            "InputText",
            "OCRText",
            "ExecutionTimeMs",
            "IsTrue",
            "Similarity",
            "ThreadName",
            "TextSource",
            "ErrorMessage",
            "Details",
            "ExecutedAt",
        };

        private const string DefaultSortColumn = "ExecutedAt";

        private static readonly Lazy<IFreeSql> _fsql = new(() =>
        {
            // 确保数据库目录存在
            string dbDir = Path.GetDirectoryName(DbPath);
            if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
                Directory.CreateDirectory(dbDir);

            return new FreeSql.FreeSqlBuilder()
                .UseConnectionString(FreeSql.DataType.Sqlite, $"Data Source={DbPath}")
                .UseAutoSyncStructure(true)
                .Build();
        });

        private static IFreeSql Fsql => _fsql.Value;

        private static readonly HashSet<string> SyncedTables = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object TableSyncGate = new();

        /// <summary>
        /// 按步骤文件名派生分表名：ASCII 字母数字保留，其余转下划线，再附 8 位哈希防碰撞。
        /// </summary>
        public static string TableNameFor(string stepFileName)
        {
            string name = string.IsNullOrWhiteSpace(stepFileName) ? "unsaved" : stepFileName;
            var sb = new System.Text.StringBuilder("StepLog_");
            foreach (char c in name)
                sb.Append(c < 128 && char.IsLetterOrDigit(c) ? c : '_');
            string safe = sb.ToString();
            if (safe.Length > 48) safe = safe.Substring(0, 48);
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(name));
                safe += "_" + BitConverter.ToString(hash).Replace("-", string.Empty).Substring(0, 8);
            }
            return safe;
        }

        /// <summary>取（必要时创建）该文件对应的分表名。</summary>
        private static string EnsureTable(string stepFileName)
        {
            string table = TableNameFor(stepFileName);
            lock (TableSyncGate)
            {
                if (!SyncedTables.Contains(table))
                {
                    Fsql.CodeFirst.SyncStructure(typeof(StepExecutionLog), table);
                    SyncedTables.Add(table);
                }
            }
            return table;
        }

        private static bool TableExists(string table)
        {
            try { return Fsql.DbFirst.ExistsTable(table); }
            catch { return false; }
        }

        /// <summary>所有日志表（主表 + 分表），供筛选/清理遍历使用。</summary>
        private static List<string> LogTables()
        {
            var tables = new List<string> { "StepExecutionLog" };
            try
            {
                foreach (var info in Fsql.DbFirst.GetTablesByDatabase())
                {
                    if (info?.Name != null && info.Name.StartsWith("StepLog_", StringComparison.OrdinalIgnoreCase))
                        tables.Add(info.Name);
                }
            }
            catch { }
            return tables;
        }

        private static string DbPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoShaoLu", "execution_log.db");

        /// <summary>
        /// 记录一条执行日志。
        /// 返回 true 表示已成功落库；false 表示落库失败（已写 Warn 日志，不抛给调用方打断流程）。
        /// </summary>
        public static bool Log(Guid stepUid, string stepFileName, string stepName, string inputText, string ocrText = null,
            double? executionTimeMs = null, bool? isTrue = null, double? similarity = null,
            string threadName = null, string textSource = null, string errorMessage = null, string details = null)
        {
            try
            {
                var log = new StepExecutionLog
                {
                    StepUid = stepUid,
                    StepFileName = stepFileName ?? "unsaved",
                    StepName = stepName ?? string.Empty,
                    InputText = inputText ?? string.Empty,
                    OCRText = ocrText,
                    ExecutionTimeMs = executionTimeMs,
                    IsTrue = isTrue,
                    Similarity = similarity,
                    ThreadName = threadName,
                    TextSource = textSource,
                    ErrorMessage = errorMessage,
                    Details = details,
                    ExecutedAt = DateTime.Now
                };
                // 按步骤文件分表保存，单个文件的历史再长也不会拖慢其他文件的查询
                string table = EnsureTable(log.StepFileName);
                bool ok = Fsql.Insert(log).AsTable(old => table).ExecuteAffrows() > 0;
                if (ok) NotifyLogWritten(log);
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to write execution log");
                NotifyWriteFailure(ex);
                return false;
            }
        }

        private static int _writeFailureNotified;

        /// <summary>写入成功后触发（执行记录窗口据此实时刷新）。</summary>
        public static event Action<StepExecutionLog> LogWritten;

        private static void NotifyLogWritten(StepExecutionLog log)
        {
            try { LogWritten?.Invoke(log); } catch { }
        }

        /// <summary>每次运行开始时重置「写入失败已提示」标记，保证每次运行最多提示一次。</summary>
        public static void ResetWriteFailureNotify()
        {
            System.Threading.Interlocked.Exchange(ref _writeFailureNotified, 0);
        }

        /// <summary>执行记录写不进数据库时弹一次警告（每次运行只弹一次，避免刷屏）。</summary>
        private static void NotifyWriteFailure(Exception ex)
        {
            if (System.Threading.Interlocked.Exchange(ref _writeFailureNotified, 1) == 1) return;
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) return;
                string title = LanguageService.GetLocalizedString("ExecLog_WriteFailedTitle", "执行记录写入失败");
                string body = string.Format(
                    LanguageService.GetLocalizedString("ExecLog_WriteFailedBody",
                        "执行记录无法写入数据库，本次运行的记录会丢失。\n\n原因：{0}"),
                    ex == null ? string.Empty : ex.Message);
                dispatcher.BeginInvoke(new Action(() => WPFDevelopers.Controls.MessageBox.Show(
                    body, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning)));
            }
            catch { }
        }

        /// <summary>
        /// 分页查询执行日志，支持关键字搜索、筛选和排序
        /// </summary>
        public static List<StepExecutionLog> Query(
            string keyword = null,
            string stepFileName = null,
            Guid? stepUid = null,
            DateTime? from = null,
            DateTime? to = null,
            string orderBy = "ExecutedAt",
            bool descending = true,
            int page = 1,
            int pageSize = 100)
        {
            // 按步骤文件分表：优先查该文件对应的分表，分表不存在时回退主表（历史数据）
            string logTable = null;
            if (!string.IsNullOrWhiteSpace(stepFileName))
            {
                string candidate = TableNameFor(stepFileName);
                if (TableExists(candidate)) logTable = candidate;
            }
            var query = logTable == null
                ? Fsql.Select<StepExecutionLog>()
                : Fsql.Select<StepExecutionLog>().AsTable((t, o) => logTable);

            // 关键字搜索（匹配 InputText 或 StepName）
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(x => x.InputText.Contains(keyword) || x.StepName.Contains(keyword));
            }

            // 按步骤文件名筛选
            if (!string.IsNullOrWhiteSpace(stepFileName))
            {
                query = query.Where(x => x.StepFileName == stepFileName);
            }

            // 按步骤 UID 筛选
            if (stepUid.HasValue)
            {
                query = query.Where(x => x.StepUid == stepUid.Value);
            }

            // 时间范围：半开区间 [from, to)，调用方传"结束日期 + 1 天"以包含结束日当天
            if (from.HasValue)
            {
                query = query.Where(x => x.ExecutedAt >= from.Value);
            }
            if (to.HasValue)
            {
                query = query.Where(x => x.ExecutedAt < to.Value);
            }

            // 排序：列名必须命中白名单，否则回退默认列（不信任外部传入的字符串）
            string sortColumn = AllowedSortColumns.Contains(orderBy ?? string.Empty) ? orderBy : DefaultSortColumn;
            string sortExpression = descending ? $"{sortColumn} DESC" : $"{sortColumn} ASC";
            query = query.OrderBy(sortExpression);

            // 分页
            return query.Page(page, pageSize).ToList();
        }

        /// <summary>
        /// 获取查询的总记录数（用于分页显示）
        /// </summary>
        public static long QueryCount(
            string keyword = null,
            string stepFileName = null,
            Guid? stepUid = null,
            DateTime? from = null,
            DateTime? to = null)
        {
            string logTable = null;
            if (!string.IsNullOrWhiteSpace(stepFileName))
            {
                string candidate = TableNameFor(stepFileName);
                if (TableExists(candidate)) logTable = candidate;
            }
            var query = logTable == null
                ? Fsql.Select<StepExecutionLog>()
                : Fsql.Select<StepExecutionLog>().AsTable((t, o) => logTable);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(x => x.InputText.Contains(keyword) || x.StepName.Contains(keyword));
            }
            if (!string.IsNullOrWhiteSpace(stepFileName))
            {
                query = query.Where(x => x.StepFileName == stepFileName);
            }
            if (stepUid.HasValue)
            {
                query = query.Where(x => x.StepUid == stepUid.Value);
            }
            if (from.HasValue)
            {
                query = query.Where(x => x.ExecutedAt >= from.Value);
            }
            // 与 Query 保持一致：半开区间 [from, to)
            if (to.HasValue)
            {
                query = query.Where(x => x.ExecutedAt < to.Value);
            }

            return query.Count();
        }

        /// <summary>
        /// 获取所有不重复的 StepFileName 列表（供筛选下拉框使用）
        /// </summary>
        public static List<string> GetDistinctFileNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in LogTables())
            {
                try
                {
                    var list = table == "StepExecutionLog"
                        ? Fsql.Select<StepExecutionLog>().Distinct().ToList(x => x.StepFileName)
                        : Fsql.Select<StepExecutionLog>().AsTable((t, o) => table).Distinct().ToList(x => x.StepFileName);
                    foreach (var n in list)
                        if (!string.IsNullOrEmpty(n)) names.Add(n);
                }
                catch { }
            }
            return names.OrderBy(x => x).ToList();
        }

        /// <summary>
        /// 清理超过保留天数的旧日志（retentionDays <= 0 时不执行）
        /// </summary>
        public static void CleanupOldLogs(int retentionDays)
        {
            if (retentionDays <= 0) return;

            try
            {
                var cutoff = DateTime.Now.AddDays(-retentionDays);
                // 主表 + 各步骤文件分表都要清理，否则分表会无限增长
                foreach (var table in LogTables())
                {
                    try
                    {
                        if (table == "StepExecutionLog")
                            Fsql.Delete<StepExecutionLog>().Where(x => x.ExecutedAt < cutoff).ExecuteAffrows();
                        else
                            Fsql.Delete<StepExecutionLog>().AsTable(old => table).Where(x => x.ExecutedAt < cutoff).ExecuteAffrows();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "Failed to cleanup table " + table);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to cleanup old execution logs");
            }
        }
    }
}
