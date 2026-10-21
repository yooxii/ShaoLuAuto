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

        private static string DbPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoShaoLu", "execution_log.db");

        /// <summary>
        /// 记录一条执行日志。
        /// 返回 true 表示已成功落库；false 表示落库失败（已写 Warn 日志，不抛给调用方打断流程）。
        /// </summary>
        public static bool Log(Guid stepUid, string stepFileName, string stepName, string inputText, string ocrText = null,
            double? executionTimeMs = null, bool? isTrue = null, double? similarity = null,
            string threadName = null, string textSource = null, string errorMessage = null)
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
                    ExecutedAt = DateTime.Now
                };
                return Fsql.Insert(log).ExecuteAffrows() > 0;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to write execution log");
                return false;
            }
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
            var query = Fsql.Select<StepExecutionLog>();

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
            var query = Fsql.Select<StepExecutionLog>();

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
            return Fsql.Select<StepExecutionLog>()
                .Distinct()
                .ToList(x => x.StepFileName)
                .Where(x => !string.IsNullOrEmpty(x))
                .OrderBy(x => x)
                .ToList();
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
                Fsql.Delete<StepExecutionLog>()
                    .Where(x => x.ExecutedAt < cutoff)
                    .ExecuteAffrows();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to cleanup old execution logs");
            }
        }
    }
}
