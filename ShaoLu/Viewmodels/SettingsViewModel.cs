using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShaoLu.Models;
using ShaoLu.Properties;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Views;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace ShaoLu.Viewmodels
{
    // ===== 树节点模型 =====
    public partial class SettingsCategory : ObservableObject
    {
        private string _title;
        private object _viewModel;
        private bool _isExpanded;

        public string Title { get => _title; set => SetProperty(ref _title, value); }
        public object ViewModel { get => _viewModel; set => SetProperty(ref _viewModel, value); }
        public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

        public ObservableCollection<SettingsCategory> Children { get; } = [];
    }

    // ===== App 设置 ViewModel =====
    public partial class AppSettingsViewModel : ObservableObject
    {
        private string _theme;
        private FontModel _font;

        public string Theme { get => _theme; set => SetProperty(ref _theme, value); }
        public FontModel Font { get => _font; set => SetProperty(ref _font, value); }

        public AppSettingsViewModel(AppSettingsModel model)
        {
            Theme = model.Theme;
            Font = model.WindowFont;
        }


        public void ApplyTo(AppSettingsModel model)
        {
            model.Theme = Theme;
            model.WindowFont = Font;
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

        public bool ShowErrorPopup { get => _showErrorPopup; set => SetProperty(ref _showErrorPopup, value); }
        public bool MinimizeOnRun { get => _minimizeOnRun; set => SetProperty(ref _minimizeOnRun, value); }


        public StepSettingsViewModel(StepSettingsModel model)
        {
            ShowErrorPopup = model.ShowErrorPopup;
            MinimizeOnRun = model.MinimizeOnRun;
        }


        public void ApplyTo(StepSettingsModel model)
        {
            model.ShowErrorPopup = ShowErrorPopup;
            model.MinimizeOnRun = MinimizeOnRun;
        }
    }

    // ===== 设置窗口主 ViewModel =====
    public partial class SettingsWindowViewModel : ObservableObject
    {
        private SettingsCategory _selectedCategory;

        private AppSettings _settings;

        public event Action windowClosed;


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
                IsExpanded = true
            };

            var stepNode = new SettingsCategory
            {
                Title = "Step " + LanguageService.GetLocalizedString("Settings"),
                ViewModel = new StepSettingsViewModel(Settings.Step),
                IsExpanded = true
            };

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

            // 可以在这里添加保存成功的提示或关闭窗口逻辑
            string msg = LanguageService.GetLocalizedString("SaveAndBack");
            string title = LanguageService.GetLocalizedString("Save");
            var (_, popup) = WindowAsyncPopup.Show(msg, title, PopupButtons.YesCancel, MessageBoxImage.Information);
            var res = await popup;
            if (res == PopupButton.Yes.Value)
            {
                windowClosed?.Invoke();
            }
        }
    }
}