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

        private static string ScreenshotDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoShaoLu", "screenshots");

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
        /// 注意：必须在 UI 线程调用。
        /// </summary>
        public static bool BeginSession()
        {
            try
            {
                var window = new Views.WindowBurnInInput();
                if (window.ShowDialog() == true && window.Result != null)
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
            bool? isGood = null,
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
            if (isGood.HasValue)
                q = q.Where(x => x.IsGood == isGood.Value);

            return q.OrderBy(descending ? "BurnFinishedAt DESC" : "BurnFinishedAt ASC")
                    .Page(page, pageSize).ToList();
        }

        public static long QueryCount(
            string orderNo = null,
            DateTime? from = null,
            DateTime? to = null,
            bool? isGood = null)
        {
            var q = Fsql.Select<BurnInRecord>();
            if (!string.IsNullOrWhiteSpace(orderNo))
                q = q.Where(x => x.OrderNo == orderNo);
            if (from.HasValue) q = q.Where(x => x.BurnFinishedAt >= from.Value);
            if (to.HasValue) q = q.Where(x => x.BurnFinishedAt <= to.Value);
            if (isGood.HasValue) q = q.Where(x => x.IsGood == isGood.Value);
            return q.Count();
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
                    BadCount = g.Count(r => !r.IsGood),
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
        /// 根据用户配置对一次烧录判定良品/不良品，并可选截图。
        /// 返回 (isGood, goodText, badText, remark, screenshotPath)。
        /// 未命中任何关键字/图像时按不良处理，原始文本写入 remark 供追溯。
        /// </summary>
        public static (bool IsGood, string GoodText, string BadText, string Remark, string ScreenshotPath)
            EvaluateAndCapture(string ocrText, bool goodImageHit, bool badImageHit, string orderNoForFile)
        {
            string goodText = null, badText = null, remark = string.Empty;
            bool goodByKeyword = false, badByKeyword = false;

            // 关键字判定
            if (!string.IsNullOrEmpty(ocrText))
            {
                var goodKeywords = SplitKeywords(Config?.GoodTextContains);
                var badKeywords = SplitKeywords(Config?.BadTextContains);
                foreach (var kw in goodKeywords)
                    if (ocrText.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    { goodByKeyword = true; goodText = ocrText; break; }
                foreach (var kw in badKeywords)
                    if (ocrText.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    { badByKeyword = true; badText = ocrText; break; }

                // 均未命中时保留原始文本供追溯
                if (!goodByKeyword && !badByKeyword)
                    remark = ocrText;
            }

            // 综合判定：文本或图像任一命中良品且未命中不良 => 良品
            bool goodHit = goodByKeyword || goodImageHit;
            bool badHit = badByKeyword || badImageHit;
            bool isGood = goodHit && !badHit;

            string screenshotPath = null;
            if (Config?.CaptureScreenshot == true && Config.HasRegion)
            {
                screenshotPath = CaptureAndSave(orderNoForFile);
            }

            return (isGood, goodText, badText, remark, screenshotPath);
        }

        private static string[] SplitKeywords(string joined)
        {
            if (string.IsNullOrWhiteSpace(joined)) return Array.Empty<string>();
            return joined.Split(new[] { ',', '，', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        }

        /// <summary>截取配置的区域并保存为 PNG，返回文件路径；失败返回 null</summary>
        public static string CaptureAndSave(string orderNo)
        {
            if (Config == null || !Config.HasRegion) return null;
            try
            {
                var region = new System.Drawing.Rectangle(
                    (int)Config.RegionX.Value, (int)Config.RegionY.Value,
                    (int)Config.RegionW.Value, (int)Config.RegionH.Value);
                using Bitmap bmp = Autogui.CaptureScreenRegion(region);
                if (bmp == null) return null;

                string dir = Path.Combine(ScreenshotDir, SanitizeFileName(orderNo ?? "unknown"));
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
                    r.IsGood ? "Good" : "Bad",
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
                ws.Cells[row, 9].Value = r.IsGood ? "Good" : "Bad";
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
