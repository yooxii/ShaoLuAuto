using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels;
using ShaoLu.Viewmodels.AutomationStep;
using ShaoLu.Views;
using CommunityToolkit.Mvvm.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace ShaoLu
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Viewmodels.MainViewModel mainViewModel = SingletonLocator.Main;
        private readonly Viewmodels.StepsViewModel stepsViewModel = SingletonLocator.Steps;
        readonly AppSettings appSettings = SingletonLocator.Settings;

        private const int HOTKEY_START_ID = 9001;
        private const int HOTKEY_STOP_ID = 9002;
        private HwndSource _source;
        private WindowPlayground windowPlayground = new();

        #region 文件操作快捷键命令（仅主窗口内有效）

        public static readonly RoutedUICommand NewCommand = new RoutedUICommand("New", nameof(NewCommand), typeof(MainWindow));
        public static readonly RoutedUICommand OpenCommand = new RoutedUICommand("Open", nameof(OpenCommand), typeof(MainWindow));
        public static readonly RoutedUICommand SaveCommand = new RoutedUICommand("Save", nameof(SaveCommand), typeof(MainWindow));
        public static readonly RoutedUICommand SaveAsCommand = new RoutedUICommand("SaveAs", nameof(SaveAsCommand), typeof(MainWindow));

        #endregion

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _source = PresentationSource.FromVisual(this) as HwndSource;
            _source?.AddHook(WndProc);

            var handle = new WindowInteropHelper(this).Handle;

            // 1. 先注销所有旧热键
            NativeMethods.UnregisterHotKey(handle, HOTKEY_START_ID);
            NativeMethods.UnregisterHotKey(handle, HOTKEY_STOP_ID);

            // 2. 注册“开始”热键
            NativeMethods.RegisterHotKey(handle, HOTKEY_START_ID,
                (uint)appSettings.Step.StartHotKey.Modifiers,
                (uint)KeyInterop.VirtualKeyFromKey(appSettings.Step.StartHotKey.Key));

            // 3. 注册“停止”热键
            NativeMethods.RegisterHotKey(handle, HOTKEY_STOP_ID,
                (uint)appSettings.Step.StopHotKey.Modifiers,
                (uint)KeyInterop.VirtualKeyFromKey(appSettings.Step.StopHotKey.Key));
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();

                if (hotkeyId == HOTKEY_START_ID)
                {
                    if (stepsViewModel.RunCommand.CanExecute(null))
                        stepsViewModel.RunCommand.Execute(null); // 触发开始逻辑
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_STOP_ID)
                {
                    stepsViewModel.StopCommand.Execute(null);  // 触发停止逻辑
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }


        public MainWindow()
        {
            InitializeComponent();
            DataContext = mainViewModel;

            Loaded += MainWindow_Loaded;

        }
        protected override void OnClosed(EventArgs e)
        {
            // 窗口关闭时务必注销热键，防止资源泄漏
            _source?.RemoveHook(WndProc);
            NativeMethods.UnregisterHotKey(new WindowInteropHelper(this).Handle, HOTKEY_STOP_ID);

            // 关闭所有子窗口
            foreach (var window in Application.Current.Windows.Cast<Window>().Where(w => w != this).ToList())
            {
                window.Close();
            }

            base.OnClosed(e);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Check_current_lang();

            FontFamily = new System.Windows.Media.FontFamily(appSettings.App.WindowFont.FontFamily);
            FontSize = appSettings.App.WindowFont.FontSize;

            UpdateLoginStateUI();

            BuildExamplesMenu();
        }

        #region 输入校验
        private void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            Regex regex = new(@"[^0-9\.]+");

            // 1. 拦截非数字和小数点的字符
            // 2. 拦截重复的小数点
            if (regex.IsMatch(e.Text) || (e.Text == "." && textBox.Text.Contains(".")))
            {
                e.Handled = true;
            }
        }

        private void TextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                Regex regex = new(@"[^0-9\.]+");
                // 如果粘贴的内容包含非法字符，取消粘贴操作
                if (regex.IsMatch(text))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }
        #endregion

        private void Check_current_lang()
        {
            mainViewModel.English = mainViewModel.Simplified = mainViewModel.Tranditional = false;
            var current_lang = Services.LanguageService.GetCurrentLanguage();
            switch (current_lang)
            {
                case "en-US":
                    mainViewModel.English = true;
                    break;
                case "zh-CN":
                    mainViewModel.Simplified = true;
                    break;
                case "zh-TW":
                    mainViewModel.Tranditional = true;
                    break;
                default:
                    mainViewModel.Simplified = true;
                    break;
            }
        }

        private void Language_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item)
            {
                Services.LanguageService.SetLanguage(item.Tag as string);
                Check_current_lang();
            }
        }

        private void Quit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TestCrop_Click(object sender, RoutedEventArgs e)
        {
            //WindowCropImage windowCropImage = new();
            //windowCropImage.Show();
            WindowEditImage windowEditImage = new();
            windowEditImage.Show();
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureLoggedIn()) return;
            stepsViewModel.AutomationStepBases.Clear();
            // 清空文件路径
            mainViewModel.StepFilePath = null;
            mainViewModel.StepImageWorkDir = null;
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var filePath = PathServices.OpenPathDialog(
                LanguageService.GetLocalizedString("OpenFile"),
                $"AutoStep Files|*.autostep|{LanguageService.GetLocalizedString("StepFile")}|*.json");
            if (filePath != null)
            {
                LoadStepFile(filePath);
            }
        }

        /// <summary>
        /// 加载步骤文件（支持 .autostep 与旧版 .json）
        /// </summary>
        private void LoadStepFile(string filePath)
        {
            try
            {
                var ext = Path.GetExtension(filePath).ToLower();
                ObservableCollection<AutomationStepBase> loadedSteps;

                if (ext == ".autostep")
                {
                    loadedSteps = StepsFile.LoadFromAutoStepPackage(filePath);
                    mainViewModel.StepFilePath = filePath;
                    mainViewModel.StepImageWorkDir = StepsFile.GetWorkDirPath(filePath);
                }
                else
                {
                    // 兼容旧版 JSON 格式
                    loadedSteps = StepsFile.LoadStepsFromJson(filePath);
                    mainViewModel.StepFilePath = filePath;
                    mainViewModel.StepImageWorkDir = Path.GetDirectoryName(filePath);
                }

                stepsViewModel.AutomationStepBases.Clear();
                stepsViewModel.InsertSteps(loadedSteps);

                // 记录到最近打开文件列表
                RecentFilesService.AddRecent(filePath);
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Error(ex, "Open file failed: {0}", filePath);
                WindowAsyncPopup.Show(
                    $"{LanguageService.GetLocalizedString("OpenFileFailed")}: {ex.Message}",
                    LanguageService.GetLocalizedString("OpenFile"),
                    PopupButtons.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 展开“最近打开的文件”菜单时动态构建子菜单（最多 10 个）
        /// </summary>
        private void RecentFiles_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            RecentFiles.Items.Clear();
            var files = RecentFilesService.Load();
            if (files.Count == 0)
            {
                RecentFiles.Items.Add(new MenuItem
                {
                    Header = LanguageService.GetLocalizedString("NoRecentFiles"),
                    IsEnabled = false,
                });
                return;
            }
            foreach (var path in files)
            {
                var item = new MenuItem { Header = path, ToolTip = path };
                item.Click += (s, args) => OpenRecentFile(path);
                RecentFiles.Items.Add(item);
            }
        }

        /// <summary>打开最近文件列表中的文件；文件不存在时提示并从列表移除</summary>
        private void OpenRecentFile(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
            {
                RecentFilesService.Remove(filePath);
                WindowAsyncPopup.Show(
                    $"{LanguageService.GetLocalizedString("OpenFileFailed")}: {filePath}",
                    LanguageService.GetLocalizedString("OpenFile"),
                    PopupButtons.OK, MessageBoxImage.Error);
                return;
            }
            LoadStepFile(filePath);
        }

        /// <summary>
        /// 构建“示例”子菜单：扫描 Examples 目录下的 .autostep 文件
        /// </summary>
        private void BuildExamplesMenu()
        {
            Examples.Items.Clear();
            string examplesDir = Path.Combine(AppContext.BaseDirectory, "Examples");
            try
            {
                if (Directory.Exists(examplesDir))
                {
                    var files = Directory.GetFiles(examplesDir, "*.autostep")
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    foreach (var file in files)
                    {
                        var item = new MenuItem
                        {
                            Header = Path.GetFileNameWithoutExtension(file),
                            Tag = file
                        };
                        item.Click += Example_Click;
                        Examples.Items.Add(item);
                    }
                    if (files.Count > 0) return;
                }
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Warn(ex, "Scan Examples folder failed");
            }

            // 目录不存在或无示例文件时，显示禁用占位项
            Examples.Items.Add(new MenuItem
            {
                Header = LanguageService.GetLocalizedString("NoExamples"),
                IsEnabled = false
            });
        }

        private void Example_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string filePath)
            {
                if (windowPlayground == null || !windowPlayground.IsLoaded)
                {
                    windowPlayground = new WindowPlayground();
                    windowPlayground.Closed += (s, e) => windowPlayground = null;
                    windowPlayground.Show();
                }
                else
                {
                    windowPlayground.Activate();
                }
                LoadStepFile(filePath);
            }
        }

        private void Playground_Click(object sender, RoutedEventArgs e)
        {
            if (windowPlayground == null || !windowPlayground.IsLoaded)
            {
                windowPlayground = new WindowPlayground();
                windowPlayground.Closed += (s, e) => windowPlayground = null;
                windowPlayground.Show();
            }
            else
            {
                windowPlayground.Activate();
            }
        }

        private void UserDoc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string docsDir = Path.Combine(AppContext.BaseDirectory, "Docs");
                string docPath = Directory.Exists(docsDir)
                    ? Directory.GetFiles(docsDir, "*.xps").OrderBy(f => f).FirstOrDefault()
                    : null;

                if (docPath == null)
                {
                    WindowAsyncPopup.Show(
                        LanguageService.GetLocalizedString("UserDocNotFound"),
                        LanguageService.GetLocalizedString("UserDoc"),
                        PopupButtons.OK, MessageBoxImage.Warning);
                    return;
                }

                var viewer = new WindowDocumentViewer(docPath);
                viewer.Show();
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Error(ex, "Open user document failed");
                WindowAsyncPopup.Show(
                    $"{LanguageService.GetLocalizedString("OpenFileFailed")}: {ex.Message}",
                    LanguageService.GetLocalizedString("UserDoc"),
                    PopupButtons.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 保存：已有文件路径时直接覆盖原文件，否则弹出另存为对话框
        /// </summary>
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureLoggedIn()) return;

            string filePath = mainViewModel.StepFilePath;
            if (string.IsNullOrEmpty(filePath))
            {
                SaveAs_Click(sender, e);
                return;
            }

            DoSave(filePath);
        }

        /// <summary>
        /// 另存为：始终弹出对话框选择新路径
        /// </summary>
        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureLoggedIn()) return;

            var filePath = PathServices.SavePathDialog(
                LanguageService.GetLocalizedString("SaveAs"),
                "AutoStep Files|*.autostep",
                saveName: "NewSteps.autostep");
            if (filePath != null)
            {
                DoSave(filePath);
            }
        }

        private void DoSave(string filePath)
        {
            try
            {
                foreach (var step in stepsViewModel.AutomationStepBases)
                {
                    step.IsSave = true;
                }

                StepsFile.SaveAsAutoStepPackage(stepsViewModel.AutomationStepBases, filePath);

                // 更新当前文件路径和工作目录
                mainViewModel.StepFilePath = filePath;
                mainViewModel.StepImageWorkDir = StepsFile.GetWorkDirPath(filePath);

                // 保存成功反馈
                WindowAsyncPopup.Show(
                    LanguageService.GetLocalizedString("SaveSuccess"),
                    LanguageService.GetLocalizedString("Save"),
                    PopupButtons.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Error(ex, "Save file failed: {0}", filePath);
                WindowAsyncPopup.Show(
                    $"{LanguageService.GetLocalizedString("SaveFailed")}: {ex.Message}",
                    LanguageService.GetLocalizedString("Save"),
                    PopupButtons.OK, MessageBoxImage.Error);
            }
        }

        private void Setting_Click(object sender, RoutedEventArgs e)
        {
            WindowSettings windowSettings = new();
            windowSettings.ShowDialog();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            WindowAbout windowAbout = new();
            windowAbout.ShowDialog();
        }

        private void ExecutionLog_Click(object sender, RoutedEventArgs e)
        {
            WindowExecutionLog window = new();
            window.Show();
        }

        private void BurnInStatistics_Click(object sender, RoutedEventArgs e)
        {
            WindowBurnInStatistics.ShowSingleton();
        }

        private void UserManagement_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureLoggedIn()) return;

            var userManagementVm = Ioc.Default.GetRequiredService<ShaoLu.Viewmodels.UserManagementViewModel>();
            var window = new WindowUserManagement(userManagementVm);
            window.Owner = this;
            window.ShowDialog();
        }

        private void LoginLogout_Click(object sender, RoutedEventArgs e)
        {
            if (SingletonLocator.UserService.CurrentUser != null)
            {
                // 已登录 → 注销
                SingletonLocator.UserService.Logout();
                mainViewModel.RefreshLoginState();
                UpdateLoginStateUI();
            }
            else
            {
                // 未登录 → 弹出登录窗口
                EnsureLoggedIn();
            }
        }

        /// <summary>
        /// 更新登录/注销菜单项的显示文本
        /// </summary>
        private void UpdateLoginStateUI()
        {
            if (SingletonLocator.UserService.CurrentUser != null)
            {
                LoginLogout.Header = string.Format(LanguageService.GetLocalizedString("Menu_Logout"), SingletonLocator.UserService.CurrentUser.Username);
            }
            else
            {
                LoginLogout.Header = LanguageService.GetLocalizedString("Menu_Login");
            }
        }

        /// <summary>
        /// 确保用户已登录，未登录时弹出登录窗口
        /// </summary>
        private bool EnsureLoggedIn()
        {
            if (SingletonLocator.UserService.CurrentUser != null)
                return true;

            var loginVm = Ioc.Default.GetRequiredService<Viewmodels.LoginViewModel>();
            var loginWindow = new Views.WindowLogin(loginVm);
            loginWindow.Owner = this;
            var result = loginWindow.ShowDialog();

            if (result == true)
            {
                mainViewModel.RefreshLoginState();
                UpdateLoginStateUI();
            }
            return result == true;
        }
    }
}
