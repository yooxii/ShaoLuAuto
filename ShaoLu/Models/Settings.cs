using ShaoLu.Services;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace ShaoLu.Models
{
    public class AppSettings
    {
        public AppSettingsModel App { get; set; } = new();
        public StepSettingsModel Step { get; set; } = new();
    }

    public class AppSettingsModel
    {
        public bool ThemeLight { get; set; } = true;
        public double WindowWidth { get; set; } = 1000;
        public double WindowHeight { get; set; } = 600;

        public FontModel WindowFont { get; set; } = new FontModel();

    }
    public class StepSettingsModel
    {
        public bool ShowErrorPopup { get; set; } = false;
        public bool MinimizeOnRun { get; set; } = true;


        public HotKeySetting StartHotKey { get; set; } = new HotKeySetting { Modifiers = ModifierKeys.Control | ModifierKeys.Alt, Key = Key.F9 };
        public HotKeySetting StopHotKey { get; set; } = new HotKeySetting { Modifiers = ModifierKeys.Control | ModifierKeys.Alt, Key = Key.F10 };
    }

    public class HotKeySetting
    {
        public ModifierKeys Modifiers { get; set; } // 修饰键
        public Key Key { get; set; } // 主键
    }
}
