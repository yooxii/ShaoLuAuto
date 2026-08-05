using FreeSql.DataAnnotations;
using System;
using System.IO;

namespace ShaoLu.Models
{
    /// <summary>
    /// 零件烧录记录（持久化到 SQLite 表 burn_in_record）
    /// 每次运行结束时写入一条，用于按工令合并与导出
    /// </summary>
    [Table(Name = "burn_in_record")]
    public class BurnInRecord
    {
        [Column(IsIdentity = true, IsPrimary = true)]
        public long Id { get; set; }

        /// <summary>工令号（按工令合并的分组键）</summary>
        [Column(StringLength = 256)]
        public string OrderNo { get; set; }

        /// <summary>操作者</summary>
        [Column(StringLength = 128)]
        public string Operator { get; set; }

        /// <summary>零件名称</summary>
        [Column(StringLength = 256)]
        public string PartName { get; set; }

        /// <summary>所属步骤文件名（无扩展名）</summary>
        [Column(StringLength = 256)]
        public string StepFileName { get; set; }

        /// <summary>烧录开始时间</summary>
        public DateTime BurnStartedAt { get; set; }

        /// <summary>烧录结束时间</summary>
        public DateTime BurnFinishedAt { get; set; }

        /// <summary>烧录耗时（毫秒）</summary>
        public double BurnDurationMs { get; set; }

        /// <summary>是否良品（true=良品，false=不良品/未判定）</summary>
        public bool IsGood { get; set; }

        /// <summary>命中良品的识别文本（用于追溯）</summary>
        [Column(StringLength = -1)]
        public string GoodText { get; set; }

        /// <summary>命中不良的识别文本</summary>
        [Column(StringLength = -1)]
        public string BadText { get; set; }

        /// <summary>截图文件路径（可为空）</summary>
        [Column(StringLength = 512)]
        public string ScreenshotPath { get; set; }

        /// <summary>备注（例如运行被取消时写 "Cancelled"）</summary>
        [Column(StringLength = 512)]
        public string Remark { get; set; }

        /// <summary>
        /// 供统计窗口明细列显示的合并文本（优先 GoodText，其次 BadText）
        /// 不持久化到数据库
        /// </summary>
        [Column(IsIgnore = true)]
        public string DisplayMatchedText =>
            !string.IsNullOrEmpty(GoodText) ? GoodText :
            !string.IsNullOrEmpty(BadText) ? BadText :
            string.Empty;

        /// <summary>是否有可预览的截图</summary>
        [Column(IsIgnore = true)]
        public bool HasScreenshot => !string.IsNullOrEmpty(ScreenshotPath)
            && File.Exists(ScreenshotPath);
    }
}
