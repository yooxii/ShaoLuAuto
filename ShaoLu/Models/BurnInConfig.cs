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

        /// <summary>高识别精度（颜色识别）：勾选后除结构相似度外，还要求颜色相似度达标</summary>
        public bool HighPrecision { get; set; } = false;

        /// <summary>颜色相似度阈值（0-1，仅在高识别精度启用时参与判定）</summary>
        public double ColorThreshold { get; set; } = 0.95;

        /// <summary>是否已配置任一判据模板（图像判定模式下使用）</summary>
        public bool HasAnyTemplate =>
            !string.IsNullOrEmpty(GoodTemplateName)
            || !string.IsNullOrEmpty(BadTemplateName)
            || !string.IsNullOrEmpty(FailTemplateName);

        /// <summary>是否启用截图留痕</summary>
        public bool CaptureScreenshot { get; set; }

        /// <summary>截图保存目录（留空使用默认位置：AppData\AutoShaoLu\screenshots）</summary>
        public string ScreenshotDir { get; set; } = string.Empty;

        private bool _regionRelative;
        /// <summary>截图区域是否为屏幕相对坐标（RegionX/RegionY/RegionW/RegionH 存百分比；切换时自动换算）</summary>
        public bool RegionRelative
        {
            get => _regionRelative;
            set
            {
                if (_regionRelative == value) return;
                _regionRelative = value;
                RegionX = ConvertNullable(RegionX, !value, value, Utils.ScreenCoordinates.ConvertX);
                RegionY = ConvertNullable(RegionY, !value, value, Utils.ScreenCoordinates.ConvertY);
                RegionW = ConvertNullable(RegionW, !value, value, Utils.ScreenCoordinates.ConvertWidth);
                RegionH = ConvertNullable(RegionH, !value, value, Utils.ScreenCoordinates.ConvertHeight);
            }
        }

        private static double? ConvertNullable(double? value, bool fromRelative, bool toRelative, Func<double, bool, bool, double> convert)
            => value.HasValue ? convert(value.Value, fromRelative, toRelative) : value;

        /// <summary>截图区域（绝对模式为屏幕绝对坐标逻辑像素，相对模式为占屏幕宽/高的百分比）</summary>
        public double? RegionX { get; set; }
        public double? RegionY { get; set; }
        public double? RegionW { get; set; }
        public double? RegionH { get; set; }

        /// <summary>是否已配置有效区域</summary>
        public bool HasRegion => RegionW.HasValue && RegionH.HasValue
            && RegionW.Value > 0 && RegionH.Value > 0;
    }
}
