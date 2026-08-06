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

        /// <summary>良品截图命中步骤（FindImage）Uid；该步骤 IsTrue 时记为良品</summary>
        public Guid? GoodImageStepUid { get; set; }

        /// <summary>不良截图命中步骤（FindImage）Uid；该步骤 IsTrue 时记为不良</summary>
        public Guid? BadImageStepUid { get; set; }

        /// <summary>烧录失败判定步骤（图像类）Uid；该步骤 IsTrue 时记为烧录失败</summary>
        public Guid? FailImageStepUid { get; set; }

        /// <summary>是否启用截图留痕</summary>
        public bool CaptureScreenshot { get; set; }

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
