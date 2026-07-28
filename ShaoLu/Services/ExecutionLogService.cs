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
        /// 记录一条执行日志
        /// </summary>
        public static void Log(Guid stepUid, string stepFileName, string stepName, string inputText)
        {
            try
            {
                var log = new StepExecutionLog
                {
                    StepUid = stepUid,
                    StepFileName = stepFileName ?? "unsaved",
                    StepName = stepName ?? string.Empty,
                    InputText = inputText ?? string.Empty,
                    ExecutedAt = DateTime.Now
                };
                Fsql.Insert(log).ExecuteAffrows();
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Error(ex, "Failed to write execution log");
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

            // 时间范围
            if (from.HasValue)
            {
                query = query.Where(x => x.ExecutedAt >= from.Value);
            }
            if (to.HasValue)
            {
                query = query.Where(x => x.ExecutedAt <= to.Value);
            }

            // 排序
            string sortExpression = descending ? $"{orderBy} DESC" : $"{orderBy} ASC";
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
            if (to.HasValue)
            {
                query = query.Where(x => x.ExecutedAt <= to.Value);
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
    }
}
