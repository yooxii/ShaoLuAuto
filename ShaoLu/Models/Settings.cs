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
    }
}
