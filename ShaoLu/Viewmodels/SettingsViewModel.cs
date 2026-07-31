using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using WPFDevelopers;

namespace ShaoLu.Viewmodels
{
    // ===== 树节点模型 =====
    public partial class SettingsCategory : ObservableObject
    {
        private string _title;
        private object _viewModel;
        private bool _isExpanded;
        private string _sectionKey;

        public string Title { get => _title; set => SetProperty(ref _title, value); }
        public object ViewModel { get => _viewModel; set => SetProperty(ref _viewModel, value); }
        public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
        /// <summary>
        /// 用于右侧滚动定位的标识键
        /// </summary>
        public string SectionKey { get => _sectionKey; set => SetProperty(ref _sectionKey, value); }

        public ObservableCollection<SettingsCategory> Children { get; } = [];
    }

    // ===== App 设置 ViewModel =====
    public partial class AppSettingsViewModel : ObservableObject
    {
        private bool _themeLight;
        private FontModel _font;
        private int _logRetentionDays;

        public bool ThemeLight { get => _themeLight; set => SetProperty(ref _themeLight, value); }
        public FontModel Font { get => _font; set => SetProperty(ref _font, value); }
        public int LogRetentionDays { get => _logRetentionDays; set => SetProperty(ref _logRetentionDays, value); }

        public AppSettingsViewModel(AppSettingsModel model)
        {
            ThemeLight = model.ThemeLight;
            Font = model.WindowFont;
            LogRetentionDays = model.LogRetentionDays;
        }


        public void ApplyTo(AppSettingsModel model)
        {
            model.ThemeLight = ThemeLight;
            model.WindowFont = Font;
            model.LogRetentionDays = LogRetentionDays;
        }

        [RelayCommand]
        private void FontSelect()
        {

            var fontDialog = new FontDialog()
            {
                Font = Font?.Font ?? new System.Drawing.Font("Arial", 12)
            };

            if (fontDialog.ShowDialog() == DialogResult.OK)
            {
                System.Drawing.Font selectedFont = fontDialog.Font;
                Font = new FontModel()
                {
                    FontFamily = selectedFont.FontFamily.Name,
                    FontSize = selectedFont.Size,
                    FontStyle = selectedFont.Italic ? FontStyles.Italic : FontStyles.Normal,
                    FontWeight = selectedFont.Bold ? FontWeights.Bold : FontWeights.Normal,
                    Style = selectedFont.Style,
                    Unit = selectedFont.Unit,
                };
            }
        }
    }

    // ===== Step 设置 ViewModel =====
    public partial class StepSettingsViewModel : ObservableObject
    {
        private bool _showErrorPopup;
        private bool _minimizeOnRun;
        private bool _confirmBeforeRun;
        private bool _showOCRRegionOnRun;
        private bool _showFoundImageRegionOnRun;
        private bool _showClickPositionOnRun;
        private int _defaultSelfReferenceLimit;
        private double _defaultSimilarityThreshold;
        private double _defaultWaitTime;
        private double _defaultTimeout;
        private int _defaultClicks;
        private HotKeySetting _startHotKey;
        private HotKeySetting _stopHotKey;

        public bool ShowErrorPopup { get => _showErrorPopup; set => SetProperty(ref _showErrorPopup, value); }
        public bool MinimizeOnRun { get => _minimizeOnRun; set => SetProperty(ref _minimizeOnRun, value); }
        public bool ConfirmBeforeRun { get => _confirmBeforeRun; set => SetProperty(ref _confirmBeforeRun, value); }
        public bool ShowOCRRegionOnRun { get => _showOCRRegionOnRun; set => SetProperty(ref _showOCRRegionOnRun, value); }
        public bool ShowFoundImageRegionOnRun { get => _showFoundImageRegionOnRun; set => SetProperty(ref _showFoundImageRegionOnRun, value); }
        public bool ShowClickPositionOnRun { get => _showClickPositionOnRun; set => SetProperty(ref _showClickPositionOnRun, value); }
        public int DefaultSelfReferenceLimit { get => _defaultSelfReferenceLimit; set => SetProperty(ref _defaultSelfReferenceLimit, value); }
        public double DefaultSimilarityThreshold { get => _defaultSimilarityThreshold; set => SetProperty(ref _defaultSimilarityThreshold, value); }
        public double DefaultWaitTime { get => _defaultWaitTime; set => SetProperty(ref _defaultWaitTime, value); }
        public double DefaultTimeout { get => _defaultTimeout; set => SetProperty(ref _defaultTimeout, value); }
        public int DefaultClicks { get => _defaultClicks; set => SetProperty(ref _defaultClicks, value); }
        public HotKeySetting StartHotKey { get => _startHotKey; set => SetProperty(ref _startHotKey, value); }
        public HotKeySetting StopHotKey { get => _stopHotKey; set => SetProperty(ref _stopHotKey, value); }


        public StepSettingsViewModel(StepSettingsModel model)
        {
            ShowErrorPopup = model.ShowErrorPopup;
            MinimizeOnRun = model.MinimizeOnRun;
            ConfirmBeforeRun = model.ConfirmBeforeRun;
            ShowOCRRegionOnRun = model.ShowOCRRegionOnRun;
            ShowFoundImageRegionOnRun = model.ShowFoundImageRegionOnRun;
            ShowClickPositionOnRun = model.ShowClickPositionOnRun;
            DefaultSelfReferenceLimit = model.DefaultSelfReferenceLimit;
            DefaultSimilarityThreshold = model.DefaultSimilarityThreshold;
            DefaultWaitTime = model.DefaultWaitTime;
            DefaultTimeout = model.DefaultTimeout;
            DefaultClicks = model.DefaultClicks;
            StartHotKey = model.StartHotKey;
            StopHotKey = model.StopHotKey;
        }


        public void ApplyTo(StepSettingsModel model)
        {
            model.ShowErrorPopup = ShowErrorPopup;
            model.MinimizeOnRun = MinimizeOnRun;
            model.ConfirmBeforeRun = ConfirmBeforeRun;
            model.ShowOCRRegionOnRun = ShowOCRRegionOnRun;
            model.ShowFoundImageRegionOnRun = ShowFoundImageRegionOnRun;
            model.ShowClickPositionOnRun = ShowClickPositionOnRun;
            model.DefaultSelfReferenceLimit = DefaultSelfReferenceLimit;
            model.DefaultSimilarityThreshold = DefaultSimilarityThreshold;
            model.DefaultWaitTime = DefaultWaitTime;
            model.DefaultTimeout = DefaultTimeout;
            model.DefaultClicks = DefaultClicks;
            model.StartHotKey = StartHotKey;
            model.StopHotKey = StopHotKey;
        }
    }

    // ===== 设置窗口主 ViewModel =====
    public partial class SettingsWindowViewModel : ObservableObject
    {
        private SettingsCategory _selectedCategory;

        private AppSettings _settings;

        public event Action WindowClosed;


        public SettingsCategory SelectedCategory { get => _selectedCategory; set => SetProperty(ref _selectedCategory, value); }
        public AppSettings Settings { get => _settings; set => SetProperty(ref _settings, value); }

        public ObservableCollection<SettingsCategory> Categories { get; } = [];


        public SettingsWindowViewModel()
        {
            Settings = SingletonLocator.Settings;
            BuildTree();
        }

        private void BuildTree()
        {
            var appNode = new SettingsCategory
            {
                Title = "App " + LanguageService.GetLocalizedString("Settings"),
                ViewModel = new AppSettingsViewModel(Settings.App),
                IsExpanded = true,
                SectionKey = "App"
            };
            appNode.Children.Add(new SettingsCategory { Title = LanguageService.GetLocalizedString("Theme"), SectionKey = "App_Theme" });
            appNode.Children.Add(new SettingsCategory { Title = LanguageService.GetLocalizedString("Font"), SectionKey = "App_Font" });
            appNode.Children.Add(new SettingsCategory { Title = LanguageService.GetLocalizedString("LogRetentionDays"), SectionKey = "App_Log" });

            var stepNode = new SettingsCategory
            {
                Title = "Step " + LanguageService.GetLocalizedString("Settings"),
                ViewModel = new StepSettingsViewModel(Settings.Step),
                IsExpanded = true,
                SectionKey = "Step"
            };
            stepNode.Children.Add(new SettingsCategory { Title = LanguageService.GetLocalizedString("Settings_RunBehavior"), SectionKey = "Step_Run" });
            stepNode.Children.Add(new SettingsCategory { Title = LanguageService.GetLocalizedString("DefaultStepParams"), SectionKey = "Step_Params" });
            stepNode.Children.Add(new SettingsCategory { Title = LanguageService.GetLocalizedString("Settings_DebugSettings"), SectionKey = "Step_Debug" });

            Categories.Add(appNode);
            Categories.Add(stepNode);

            SelectedCategory = appNode;
        }

        // 异步保存命令
        [RelayCommand]
        private async Task SaveAsync()
        {
            // 将 ViewModel 的值回写到模型
            foreach (var cat in Categories)
            {
                if (cat.ViewModel is AppSettingsViewModel appVm)
                    appVm.ApplyTo(Settings.App);
                else if (cat.ViewModel is StepSettingsViewModel stepVm)
                    stepVm.ApplyTo(Settings.Step);
            }

            await SettingsService.SaveAsync(Settings);
            ApplyWindowSettings();

            // 可以在这里添加保存成功的提示或关闭窗口逻辑
            string msg = LanguageService.GetLocalizedString("SaveAndBack");
            string title = LanguageService.GetLocalizedString("Save");
            var (_, popup) = WindowAsyncPopup.Show(msg, title, PopupButtons.YesCancel, MessageBoxImage.Information);
            var res = await popup;
            if (res == PopupButton.YesValue)
            {
                WindowClosed?.Invoke();
            }
        }

        private void ApplyWindowSettings()
        {
            ThemeManager.Instance.SetTheme(Settings.App.ThemeLight ? ThemeType.Light : ThemeType.Dark);
            ApplyGlobalFont(Settings.App.WindowFont);
        }

        /// <summary>
        /// 将字体设置应用到所有已打开的窗口
        /// </summary>
        public static void ApplyGlobalFont(FontModel font)
        {
            if (font == null || string.IsNullOrEmpty(font.FontFamily)) return;

            var fontFamily = new System.Windows.Media.FontFamily(font.FontFamily);
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                window.FontFamily = fontFamily;
                window.FontSize = font.FontSize;
                window.FontWeight = font.FontWeight;
                window.FontStyle = font.FontStyle;
            }
        }
    }
}