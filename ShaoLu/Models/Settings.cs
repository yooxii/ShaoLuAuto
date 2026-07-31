using ShaoLu.Services;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace ShaoLu.Models
{
    public class AppSettings
    {
        public AppSettingsModel App { get; set; } = new();
        public StepSettingsModel Step { get; set; } = new();
        public UserSettingsModel UserSettings { get; set; } = new();
    }

    public class UserSettingsModel
    {
        public bool RememberUser { get; set; } = false;
        public string LastUsername { get; set; } = string.Empty;
    }

    public class AppSettingsModel
    {
        public double WindowWidth { get; set; } = 1000;
        public double WindowHeight { get; set; } = 600;

        public FontModel WindowFont { get; set; } = new FontModel();

        /// <summary>
        /// 执行日志保留天数（0 = 永不清理）
        /// </summary>
        public int LogRetentionDays { get; set; } = 0;

    }
    public class StepSettingsModel
    {
        public bool ShowErrorPopup { get; set; } = false;
        public bool MinimizeOnRun { get; set; } = true;
        /// <summary>
        /// 默认自引用次数上限（-1=无限制, 0=禁止自引用, >0=限制次数）
        /// </summary>
        public int DefaultSelfReferenceLimit { get; set; } = 10;

        /// <summary>
        /// 运行前是否弹出确认对话框
        /// </summary>
        public bool ConfirmBeforeRun { get; set; } = false;

        /// <summary>
        /// 执行 OCR 时是否在屏幕上标示识别区域
        /// </summary>
        public bool ShowOCRRegionOnRun { get; set; } = true;

        /// <summary>
        /// 找到图像时是否在屏幕上标示匹配区域
        /// </summary>
        public bool ShowFoundImageRegionOnRun { get; set; } = false;

        /// <summary>
        /// 点击时是否在屏幕上标示点击位置
        /// </summary>
        public bool ShowClickPositionOnRun { get; set; } = false;

        /// <summary>
        /// OCR 区域覆盖层设置
        /// </summary>
        public OverlaySetting OCRRegionOverlay { get; set; } = new() { Color = "#00CC00", Duration = 2.0 };

        /// <summary>
        /// 找到图像覆盖层设置
        /// </summary>
        public OverlaySetting FoundImageOverlay { get; set; } = new() { Color = "#0088FF", Duration = 2.0 };

        /// <summary>
        /// 点击位置覆盖层设置
        /// </summary>
        public OverlaySetting ClickPositionOverlay { get; set; } = new() { Color = "#FF4444", Duration = 1.5 };

        /// <summary>
        /// 新建图像步骤的默认相似度阈值
        /// </summary>
        public double DefaultSimilarityThreshold { get; set; } = 0.85;

        /// <summary>
        /// 新建步骤的默认等待时间(s)
        /// </summary>
        public double DefaultWaitTime { get; set; } = 0.1;

        /// <summary>
        /// 新建步骤的默认超时时间(s)
        /// </summary>
        public double DefaultTimeout { get; set; } = 3;

        /// <summary>
        /// 新建点击步骤的默认点击次数
        /// </summary>
        public int DefaultClicks { get; set; } = 1;


        public HotKeySetting StartHotKey { get; set; } = new HotKeySetting { Modifiers = ModifierKeys.Control | ModifierKeys.Alt, Key = Key.F9 };
        public HotKeySetting StopHotKey { get; set; } = new HotKeySetting { Modifiers = ModifierKeys.Control | ModifierKeys.Alt, Key = Key.F10 };
    }

    public class HotKeySetting
    {
        public ModifierKeys Modifiers { get; set; } // 修饰键
        public Key Key { get; set; } // 主键
    }

    /// <summary>
    /// 调试覆盖层配置（颜色 + 显示时长）
    /// </summary>
    public class OverlaySetting
    {
        public string Color { get; set; } = "#00CC00";
        public double Duration { get; set; } = 2.0;
    }
}
