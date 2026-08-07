using OfficeOpenXml;
using ShaoLu.Models;
using ShaoLu.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShaoLu.Services
{
    /// <summary>
    /// 零件烧录统计服务：
    ///  - 管理运行时会话（BeginSession/EndSession/CurrentSession）
    ///  - 持久化烧录记录到 SQLite（burn_in_record 表）
    ///  - 提供按工令聚合的统计查询
    ///  - 导出 CSV / Excel
    ///  - 读写用户配置（burn_in_config.json）
    /// </summary>
    public static class BurnInService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        #region 数据库

        private static readonly Lazy<IFreeSql> _fsql = new(() =>
        {
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
            "AutoShaoLu", "burn_in.db");

        private static string DefaultScreenshotDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoShaoLu", "screenshots");

        /// <summary>实际生效的截图保存目录（指定配置为空时使用默认位置）</summary>
        public static string GetEffectiveScreenshotDir(BurnInConfig config) =>
            string.IsNullOrWhiteSpace(config?.ScreenshotDir) ? DefaultScreenshotDir : config.ScreenshotDir.Trim();

        /// <summary>实际生效的截图保存目录（基于全局配置，供展示用）</summary>
        public static string EffectiveScreenshotDir => GetEffectiveScreenshotDir(Config);

        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoShaoLu", "burn_in_config.json");

        private static string LastSessionPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoShaoLu", "burn_in_last_session.json");

        #endregion

        #region 运行时会话

        /// <summary>当前运行的烧录会话（运行开始时设置，结束时清空）</summary>
        public static BurnInSession CurrentSession { get; private set; }

        /// <summary>用户配置（程序启动时加载，配置变更时保存；公开 setter 供单元测试注入）</summary>
        public static BurnInConfig Config { get; set; } = LoadConfig();

        /// <summary>
        /// 开始一次烧录会话：弹出输入窗口收集工令/操作者/零件名。
        /// 成功返回 true；用户取消返回 false。
        /// 当前已有会话时直接返回 true（不重复弹窗）。
        /// 窗口以非属主方式显示，主窗口最小化不会连带最小化输入窗口。
        /// 注意：必须在 UI 线程调用。
        /// </summary>
        public static async Task<bool> BeginSession()
        {
            if (CurrentSession != null)
                return true;

            try
            {
                var window = new Views.WindowBurnInInput();
                var tcs = new TaskCompletionSource<bool>();
                window.Closed += (s, e) => tcs.TrySetResult(window.Confirmed);
                window.Show();
                window.Activate();

                if (await tcs.Task && window.Result != null)
                {
                    CurrentSession = new BurnInSession
                    {
                        OrderNo = window.Result.OrderNo ?? string.Empty,
                        Operator = window.Result.Operator ?? string.Empty,
                        PartName = window.Result.PartName ?? string.Empty,
                        StartedAt = DateTime.Now,
                    };
                    // 记忆本次会话（供下次弹窗默认值）
                    SaveLastSession(CurrentSession);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "BeginSession failed");
            }
            return false;
        }

        /// <summary>结束当前会话</summary>
        public static void EndSession()
        {
            CurrentSession = null;
        }

        #endregion

        #region 配置持久化

        public static BurnInConfig LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                    return System.Text.Json.JsonSerializer.Deserialize<BurnInConfig>(json)
                           ?? new BurnInConfig();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Load BurnInConfig failed, using default");
            }
            return new BurnInConfig();
        }

        public static void SaveConfig(BurnInConfig config)
        {
            try
            {
                Config = config ?? new BurnInConfig();
                var json = System.Text.Json.JsonSerializer.Serialize(
                    Config,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Save BurnInConfig failed");
            }
        }

        private static void SaveLastSession(BurnInSession session)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    session.OrderNo,
                    session.Operator,
                    session.PartName,
                });
                File.WriteAllText(LastSessionPath, json, Encoding.UTF8);
            }
            catch { /* 非关键 */ }
        }

        /// <summary>读取上次会话（用于输入弹窗的默认值）</summary>
        public static BurnInSession LoadLastSession()
        {
            try
            {
                if (File.Exists(LastSessionPath))
                {
                    var json = File.ReadAllText(LastSessionPath, Encoding.UTF8);
                    return System.Text.Json.JsonSerializer.Deserialize<BurnInSession>(json);
                }
            }
            catch { }
            return null;
        }

        #endregion

        #region 记录 CRUD

        public static void Record(BurnInRecord record)
        {
            try
            {
                if (record == null) return;
                Fsql.Insert(record).ExecuteAffrows();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to insert burn_in_record");
            }
        }

        public static List<BurnInRecord> Query(
            string orderNo = null,
            DateTime? from = null,
            DateTime? to = null,
            BurnResult? result = null,
            int page = 1,
            int pageSize = 200,
            bool descending = true)
        {
            var q = Fsql.Select<BurnInRecord>();
            if (!string.IsNullOrWhiteSpace(orderNo))
                q = q.Where(x => x.OrderNo == orderNo);
            if (from.HasValue)
                q = q.Where(x => x.BurnFinishedAt >= from.Value);
            if (to.HasValue)
                q = q.Where(x => x.BurnFinishedAt <= to.Value);
            if (result.HasValue)
                q = q.Where(ResultFilter(result.Value));

            return q.OrderBy(descending ? "BurnFinishedAt DESC" : "BurnFinishedAt ASC")
                    .Page(page, pageSize).ToList();
        }

        public static long QueryCount(
            string orderNo = null,
            DateTime? from = null,
            DateTime? to = null,
            BurnResult? result = null)
        {
            var q = Fsql.Select<BurnInRecord>();
            if (!string.IsNullOrWhiteSpace(orderNo))
                q = q.Where(x => x.OrderNo == orderNo);
            if (from.HasValue) q = q.Where(x => x.BurnFinishedAt >= from.Value);
            if (to.HasValue) q = q.Where(x => x.BurnFinishedAt <= to.Value);
            if (result.HasValue) q = q.Where(ResultFilter(result.Value));
            return q.Count();
        }

        /// <summary>构造三态结果的过滤表达式</summary>
        private static System.Linq.Expressions.Expression<Func<BurnInRecord, bool>> ResultFilter(BurnResult result)
        {
            return result switch
            {
                BurnResult.Good => x => x.IsGood,
                BurnResult.BurnFailed => x => x.IsBurnFailed,
                _ => x => !x.IsGood && !x.IsBurnFailed,
            };
        }

        public static List<string> GetDistinctOrderNos()
        {
            return Fsql.Select<BurnInRecord>()
                .Distinct()
                .ToList(x => x.OrderNo)
                .Where(x => !string.IsNullOrEmpty(x))
                .OrderByDescending(x => x)
                .ToList();
        }

        #endregion

        #region 工令汇总

        /// <summary>按工令聚合的统计摘要</summary>
        public class OrderSummary
        {
            public string OrderNo { get; set; }
            public int GoodCount { get; set; }
            public int BadCount { get; set; }
            public int FailCount { get; set; }
            public int TotalCount { get; set; }
            public double GoodRate => TotalCount > 0 ? (double)GoodCount / TotalCount : 0;
            public DateTime LastAt { get; set; }
            public DateTime FirstAt { get; set; }
        }

        public static List<OrderSummary> GetOrderSummaries(DateTime? from = null, DateTime? to = null)
        {
            var q = Fsql.Select<BurnInRecord>();
            if (from.HasValue) q = q.Where(x => x.BurnFinishedAt >= from.Value);
            if (to.HasValue) q = q.Where(x => x.BurnFinishedAt <= to.Value);

            // 内存聚合（数据量通常较小）
            var rows = q.ToList();
            return rows
                .GroupBy(x => x.OrderNo ?? string.Empty)
                .Select(g => new OrderSummary
                {
                    OrderNo = g.Key,
                    GoodCount = g.Count(r => r.IsGood),
                    BadCount = g.Count(r => !r.IsGood && !r.IsBurnFailed),
                    FailCount = g.Count(r => r.IsBurnFailed),
                    TotalCount = g.Count(),
                    FirstAt = g.Min(r => r.BurnFinishedAt),
                    LastAt = g.Max(r => r.BurnFinishedAt),
                })
                .OrderByDescending(x => x.LastAt)
                .ToList();
        }

        #endregion

        #region 良品判定与截图

        /// <summary>
        /// 根据配置对一次烧录一次性判定 良品/不良品/烧录失败，并可选截图。
        /// 判定优先级：不良品 > 烧录失败 > 良品，命中即返回唯一结果。
        /// 关键字模式：ocrText 为完成步骤识别文本，依次匹配 不良/失败/良品 关键字；
        /// 图像模式：ocrText 传 null，按 不良/失败/良品 图像步骤命中情况判定；
        /// 均未命中时记为烧录失败，原始文本写入 remark 供追溯。
        /// </summary>
        public static (BurnResult Result, string GoodText, string BadText, string Remark, string ScreenshotPath)
            EvaluateAndCapture(BurnInConfig config, string ocrText, bool goodImageHit, bool badImageHit, bool failImageHit, string orderNoForFile)
        {
            string goodText = null, badText = null, remark = string.Empty;
            BurnResult result;

            if (!string.IsNullOrEmpty(ocrText))
            {
                // 关键字模式
                if (AnyKeywordHit(ocrText, config?.BadTextContains))
                {
                    result = BurnResult.Bad;
                    badText = ocrText;
                }
                else if (AnyKeywordHit(ocrText, config?.FailTextContains))
                {
                    result = BurnResult.BurnFailed;
                    remark = ocrText;
                }
                else if (AnyKeywordHit(ocrText, config?.GoodTextContains))
                {
                    result = BurnResult.Good;
                    goodText = ocrText;
                }
                else
                {
                    // 均未命中：记为烧录失败，保留原文供追溯
                    result = BurnResult.BurnFailed;
                    remark = ocrText;
                }
            }
            else
            {
                // 图像模式（或无识别文本）
                if (badImageHit)
                    result = BurnResult.Bad;
                else if (failImageHit)
                    result = BurnResult.BurnFailed;
                else if (goodImageHit)
                    result = BurnResult.Good;
                else
                    result = BurnResult.BurnFailed;
            }

            string screenshotPath = null;
            if (config?.CaptureScreenshot == true && config.HasRegion)
            {
                screenshotPath = CaptureAndSave(config, orderNoForFile);
            }

            return (result, goodText, badText, remark, screenshotPath);
        }

        /// <summary>判断文本是否命中任一关键字（忽略大小写）</summary>
        private static bool AnyKeywordHit(string text, string joinedKeywords)
        {
            foreach (var kw in SplitKeywords(joinedKeywords))
                if (text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static string[] SplitKeywords(string joined)
        {
            if (string.IsNullOrWhiteSpace(joined)) return Array.Empty<string>();
            return joined.Split(new[] { ',', '，', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        }

        /// <summary>截取配置的区域并保存为 PNG，返回文件路径；失败返回 null</summary>
        public static string CaptureAndSave(BurnInConfig config, string orderNo)
        {
            if (config == null || !config.HasRegion) return null;
            try
            {
                var region = new System.Drawing.Rectangle(
                    (int)config.RegionX.Value, (int)config.RegionY.Value,
                    (int)config.RegionW.Value, (int)config.RegionH.Value);
                using Bitmap bmp = Autogui.CaptureScreenRegion(region);
                if (bmp == null) return null;

                string dir = Path.Combine(GetEffectiveScreenshotDir(config), SanitizeFileName(orderNo ?? "unknown"));
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                bmp.Save(file, ImageFormat.Png);
                return file;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "CaptureAndSave failed");
                return null;
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name) sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.ToString();
        }

        #endregion

        #region 导出

        /// <summary>导出当前过滤条件下的明细到 CSV</summary>
        public static void ExportCsv(string path, List<BurnInRecord> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", new[]
            {
                "Id","OrderNo","Operator","PartName","StepFileName",
                "BurnStartedAt","BurnFinishedAt","BurnDurationMs",
                "IsGood","GoodText","BadText","ScreenshotPath","Remark"
            }));
            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    r.Id.ToString(),
                    CsvEscape(r.OrderNo),
                    CsvEscape(r.Operator),
                    CsvEscape(r.PartName),
                    CsvEscape(r.StepFileName),
                    r.BurnStartedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    r.BurnFinishedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    r.BurnDurationMs.ToString("F0"),
                    r.Result.ToString(),
                    CsvEscape(r.GoodText),
                    CsvEscape(r.BadText),
                    CsvEscape(r.ScreenshotPath),
                    CsvEscape(r.Remark),
                }));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string CsvEscape(string s)
        {
            if (s == null) return "";
            bool needQuote = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            s = s.Replace("\"", "\"\"");
            return needQuote ? $"\"{s}\"" : s;
        }

        /// <summary>导出当前过滤条件下的明细到 Excel（.xlsx）</summary>
        public static void ExportExcel(string path, List<BurnInRecord> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            // 许可已在 App.xaml.cs 中通过 ExcelPackage.License.SetNonCommercialPersonal 设置
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("BurnIn");
            string[] headers = {
                "Id","OrderNo","Operator","PartName","StepFileName",
                "BurnStartedAt","BurnFinishedAt","BurnDurationMs",
                "IsGood","GoodText","BadText","ScreenshotPath","Remark"
            };
            for (int c = 0; c < headers.Length; c++) ws.Cells[1, c + 1].Value = headers[c];

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                int row = i + 2;
                ws.Cells[row, 1].Value = r.Id;
                ws.Cells[row, 2].Value = r.OrderNo;
                ws.Cells[row, 3].Value = r.Operator;
                ws.Cells[row, 4].Value = r.PartName;
                ws.Cells[row, 5].Value = r.StepFileName;
                ws.Cells[row, 6].Value = r.BurnStartedAt.ToString("yyyy-MM-dd HH:mm:ss");
                ws.Cells[row, 7].Value = r.BurnFinishedAt.ToString("yyyy-MM-dd HH:mm:ss");
                ws.Cells[row, 8].Value = r.BurnDurationMs;
                ws.Cells[row, 9].Value = r.Result.ToString();
                ws.Cells[row, 10].Value = r.GoodText;
                ws.Cells[row, 11].Value = r.BadText;
                ws.Cells[row, 12].Value = r.ScreenshotPath;
                ws.Cells[row, 13].Value = r.Remark;
            }
            package.SaveAs(new FileInfo(path));
        }

        #endregion
    }
}
