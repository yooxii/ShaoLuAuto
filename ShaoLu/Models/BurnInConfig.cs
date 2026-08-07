using System;

namespace ShaoLu.Models
{
    /// <summary>
    /// 烧录统计的用户配置（持久化到 JSON：AppData\AutoShaoLu\burn_in_config.json）
    /// 描述"哪个步骤代表一个零件烧录完成""如何判定良品/不良""截图区域"等
    /// </summary>
    public class BurnInConfig
    {
        /// <summary>代表"一个零件烧录完成"的步骤 Uid（流程结束时取该步骤的 LastResult）</summary>
        public Guid? BurnFinishStepUid { get; set; }

        /// <summary>良品关键字（逗号分隔，任一命中即为良品）</summary>
        public string GoodTextContains { get; set; } = string.Empty;

        /// <summary>不良品关键字（逗号分隔，任一命中即为不良品）</summary>
        public string BadTextContains { get; set; } = string.Empty;

        /// <summary>烧录失败关键字（逗号分隔，任一命中即为烧录失败）</summary>
        public string FailTextContains { get; set; } = string.Empty;

        /// <summary>良品判据模板图（步骤包内相对路径，如 images/burnin_{Uid}_good.png）；图像判定模式下使用</summary>
        public string GoodTemplateName { get; set; }

        /// <summary>不良品判据模板图（步骤包内相对路径）；图像判定模式下使用</summary>
        public string BadTemplateName { get; set; }

        /// <summary>烧录失败判据模板图（步骤包内相对路径）；图像判定模式下使用</summary>
        public string FailTemplateName { get; set; }

        /// <summary>模板匹配相似度阈值（0-1）</summary>
        public double SimilarityThreshold { get; set; } = 0.8;

        /// <summary>是否已配置任一判据模板（图像判定模式下使用）</summary>
        public bool HasAnyTemplate =>
            !string.IsNullOrEmpty(GoodTemplateName)
            || !string.IsNullOrEmpty(BadTemplateName)
            || !string.IsNullOrEmpty(FailTemplateName);

        /// <summary>是否启用截图留痕</summary>
        public bool CaptureScreenshot { get; set; }

        /// <summary>截图保存目录（留空使用默认位置：AppData\AutoShaoLu\screenshots）</summary>
        public string ScreenshotDir { get; set; } = string.Empty;

        /// <summary>截图区域（屏幕绝对坐标，逻辑像素）</summary>
        public double? RegionX { get; set; }
        public double? RegionY { get; set; }
        public double? RegionW { get; set; }
        public double? RegionH { get; set; }

        /// <summary>是否已配置有效区域</summary>
        public bool HasRegion => RegionW.HasValue && RegionH.HasValue
            && RegionW.Value > 0 && RegionH.Value > 0;
    }
}
