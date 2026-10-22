using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels;
using ShaoLu.Viewmodels.AutomationStep;
using ShaoLu.Views;
using CommunityToolkit.Mvvm.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

        /// <summary>当前工程是否有未保存的修改（用于新建/打开/关闭前提示）</summary>
        private bool _isDirty;
        /// <summary>关闭确认已经处理过，避免 Close() 递归再次弹窗</summary>
        private bool _closeConfirmed;

        #region 文件操作快捷键命令（仅主窗口内有效）

        public static readonly RoutedUICommand NewCommand = new RoutedUICommand("New", nameof(NewCommand), typeof(MainWindow));
        public static readonly RoutedUICommand OpenCommand = new RoutedUICommand("Open", nameof(OpenCommand), typeof(MainWindow));
        public static readonly RoutedUICommand SaveCommand = new RoutedUICommand("Save", nameof(SaveCommand), typeof(MainWindow));
        public static readonly RoutedUICommand SaveAsCommand = new RoutedUICommand("SaveAs", nameof(SaveAsCommand), typeof(MainWindow));

        // 撤销/恢复（按焦点区域路由到步骤列表或步骤内部）
        public static readonly RoutedUICommand UndoCommand = new RoutedUICommand("Undo", nameof(UndoCommand), typeof(MainWindow));
        public static readonly RoutedUICommand RedoCommand = new RoutedUICommand("Redo", nameof(RedoCommand), typeof(MainWindow));

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

            // 跟踪未保存修改：集合增删 + 单步属性变更（运行期状态变化不算用户编辑）
            stepsViewModel.AutomationStepBases.CollectionChanged += AutomationStepBases_CollectionChanged;
            foreach (var step in stepsViewModel.AutomationStepBases)
                step.PropertyChanged += Step_PropertyChanged;

            Closing += MainWindow_Closing;

            Loaded += MainWindow_Loaded;

        }

        #region 未保存修改跟踪 / 文件操作防护

        private void AutomationStepBases_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (AutomationStepBase step in e.OldItems)
                    step.PropertyChanged -= Step_PropertyChanged;
            }
            if (e.NewItems != null)
            {
                foreach (AutomationStepBase step in e.NewItems)
                    step.PropertyChanged += Step_PropertyChanged;
            }
            MarkDirty();
        }

        private void Step_PropertyChanged(object sender, PropertyChangedEventArgs e) => MarkDirty();

        /// <summary>标记工程为"有未保存修改"；运行中的执行状态（IsTrue/IsError 等）不计入</summary>
        private void MarkDirty()
        {
            if (stepsViewModel.IsRunning) return;
            _isDirty = true;
        }

        /// <summary>运行中禁止新建/打开工程：否则步骤集合被清空会让循环索引错位、日志写错文件</summary>
        private bool GuardRunningForFileOperation()
        {
            if (!stepsViewModel.IsRunning) return true;

            WindowAsyncPopup.Show(
                LanguageService.GetLocalizedString("StopBeforeFileOperation", "正在运行自动化，请先停止后再新建或打开工程。"),
                LanguageService.GetLocalizedString("Warning", "Warning"),
                PopupButtons.OK, MessageBoxImage.Warning);
            return false;
        }

        /// <summary>有未保存修改时询问是否放弃；返回 true 表示可以继续（放弃修改）</summary>
        private async Task<bool> ConfirmDiscardChangesAsync()
        {
            if (!_isDirty) return true;

            var (_, popupTask) = WindowAsyncPopup.Show(
                LanguageService.GetLocalizedString("UnsavedChangesPrompt", "当前工程有未保存的修改，确定放弃这些修改吗？"),
                LanguageService.GetLocalizedString("Save", "Save"),
                PopupButtons.YesNo, MessageBoxImage.Warning);
            return await popupTask == PopupButton.YesValue;
        }

        private async void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_closeConfirmed)
                return;

            // 运行中直接关闭：由 StepsViewModel 的停止逻辑处理，这里只提示一次
            if (!_isDirty && !stepsViewModel.IsRunning)
                return;

            e.Cancel = true;

            if (stepsViewModel.IsRunning)
            {
                WindowAsyncPopup.Show(
                    LanguageService.GetLocalizedString("StopBeforeClose", "正在运行自动化，请先停止后再关闭窗口。"),
                    LanguageService.GetLocalizedString("Warning", "Warning"),
                    PopupButtons.OK, MessageBoxImage.Warning);
                return;
            }

            if (!await ConfirmDiscardChangesAsync())
                return;

            _closeConfirmed = true;
            Close();
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            // 窗口关闭时务必注销热键，防止资源泄漏（开始 + 停止两个都要注销）
            _source?.RemoveHook(WndProc);
            var handle = new WindowInteropHelper(this).Handle;
            NativeMethods.UnregisterHotKey(handle, HOTKEY_START_ID);
            NativeMethods.UnregisterHotKey(handle, HOTKEY_STOP_ID);

            stepsViewModel.AutomationStepBases.CollectionChanged -= AutomationStepBases_CollectionChanged;
            foreach (var step in stepsViewModel.AutomationStepBases)
                step.PropertyChanged -= Step_PropertyChanged;

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
            BuildRecentFilesMenu();
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
                // 标题含本地化程序名，切换语言后刷新
                (DataContext as Viewmodels.MainViewModel)?.RefreshLocalizedText();
            }
        }

        private void Quit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #region 撤销 / 恢复命令（按焦点区域路由）

        private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Steps?.RouteUndo();
        }

        private void Undo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Steps != null && Steps.CanRouteUndo();
        }

        private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Steps?.RouteRedo();
        }

        private void Redo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Steps != null && Steps.CanRouteRedo();
        }

        /// <summary>
        /// Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z：按当前焦点区域路由撤销恢复。
        /// 在 Preview 阶段处理，避免焦点在 TextBox 时被文本框自身的撤销拦截。
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Steps == null) return;

            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Steps.CanRouteUndo())
                {
                    Steps.RouteUndo();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Steps.CanRouteRedo())
                {
                    Steps.RouteRedo();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (Steps.CanRouteRedo())
                {
                    Steps.RouteRedo();
                    e.Handled = true;
                }
            }
        }

        #endregion

        private void TestCrop_Click(object sender, RoutedEventArgs e)
        {
            //WindowCropImage windowCropImage = new();
            //windowCropImage.Show();
            WindowEditImage windowEditImage = new();
            windowEditImage.Show();
        }

        private async void New_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureLoggedIn()) return;
            if (!GuardRunningForFileOperation()) return;
            if (!await ConfirmDiscardChangesAsync()) return;

            stepsViewModel.AutomationStepBases.Clear();
            stepsViewModel.ClearUndoRedo();
            // 清空文件路径
            mainViewModel.StepFilePath = null;
            mainViewModel.StepImageWorkDir = null;
            _isDirty = false;
        }

        private async void Open_Click(object sender, RoutedEventArgs e)
        {
            if (!GuardRunningForFileOperation()) return;
            if (!await ConfirmDiscardChangesAsync()) return;

            var filePath = PathServices.OpenPathDialog(
                LanguageService.GetLocalizedString("OpenFile"),
                $"AutoStep Files|*.autostep|{LanguageService.GetLocalizedString("StepFile")}|*.json");
            if (filePath != null)
            {
                LoadStepFile(filePath);
            }
        }

        /// <summary>
        /// 「用户输入」步骤：打开工程后询问勾选了 AskOnFileOpen 的步骤。
        /// 用户取消不影响工程打开；确定后把值立即写入执行上下文。
        /// 弹窗自身异常只记日志，不把打开工程判成失败。
        /// </summary>
        private void TryPromptUserInputsOnFileOpen()
        {
            try
            {
                var inputSteps = stepsViewModel.AutomationStepBases
                    .OfType<UserInputStep>()
                    .Where(s => s != null && s.HasFields && s.AskOnFileOpen)
                    .ToList();
                if (inputSteps.Count == 0) return;

                if (!WindowUserInput.ShowAndEdit(inputSteps, this))
                {
                    NLog.LogManager.GetCurrentClassLogger().Info("UserInput: file-open prompt cancelled by user");
                    return;
                }

                foreach (var inputStep in inputSteps)
                    StepExecutionContext.Instance.SetUserInput(inputStep.Uid, inputStep.Fields);
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Warn(ex, "UserInput: prompt on file open failed");
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
                stepsViewModel.InsertSteps(loadedSteps, cloneSteps: false);
                stepsViewModel.ClearUndoRedo();

                // 记录到最近打开文件列表
                RecentFilesService.AddRecent(filePath);
                _isDirty = false;

                // 「用户输入」步骤：打开工程后询问勾选了"打开工程时询问"的步骤。
                // 用户取消不影响工程打开（只是不更新值）；确定后立即写入执行上下文。
                TryPromptUserInputsOnFileOpen();

                // 缺失资源显式告警：图片或烧录判据模板缺失会让匹配静默失败
                var missingAssets = StepsFile.CollectMissingAssets(loadedSteps, mainViewModel.StepImageWorkDir);
                if (missingAssets.Count > 0)
                {
                    WindowAsyncPopup.Show(
                        $"{LanguageService.GetLocalizedString("MissingStepAssets", "工程引用的图片/模板缺失")}：{missingAssets.Count}\n" +
                        string.Join("\n", missingAssets.Take(8)) +
                        (missingAssets.Count > 8 ? "\n..." : ""),
                        LanguageService.GetLocalizedString("OpenFile"),
                        PopupButtons.OK, MessageBoxImage.Warning);
                }
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
        /// 构建“最近打开的文件”子菜单（最多 10 个）。
        /// 启动时预构建一次保证菜单可展开（无子项的 MenuItem 不会触发 SubmenuOpened），展开时再刷新。
        /// </summary>
        private void RecentFiles_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            BuildRecentFilesMenu();
        }

        private void BuildRecentFilesMenu()
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
        private async void OpenRecentFile(string filePath)
        {
            if (!GuardRunningForFileOperation()) return;

            if (!System.IO.File.Exists(filePath))
            {
                RecentFilesService.Remove(filePath);
                WindowAsyncPopup.Show(
                    $"{LanguageService.GetLocalizedString("OpenFileFailed")}: {filePath}",
                    LanguageService.GetLocalizedString("OpenFile"),
                    PopupButtons.OK, MessageBoxImage.Error);
                return;
            }
            if (!await ConfirmDiscardChangesAsync()) return;
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

        private async void Example_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string filePath)
            {
                if (!GuardRunningForFileOperation()) return;
                if (!await ConfirmDiscardChangesAsync()) return;

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
                // 启动时已检测 OS 版本：Win10+ 且已安装 WebView2 时用 WebView2 浏览 PDF，否则回退到 DocumentViewer 浏览 XPS
                bool usePdf = OsVersionHelper.IsWindows10OrLater && OsVersionHelper.IsWebView2Installed;
                string docsDir = Path.Combine(AppContext.BaseDirectory, "Docs");
                string docPath = FindDoc(docsDir, usePdf ? "*.pdf" : "*.xps");
                if (docPath == null)
                    docPath = FindDoc(docsDir, usePdf ? "*.xps" : "*.pdf");

                if (docPath == null)
                {
                    WindowAsyncPopup.Show(
                        LanguageService.GetLocalizedString("UserDocNotFound"),
                        LanguageService.GetLocalizedString("UserDoc"),
                        PopupButtons.OK, MessageBoxImage.Warning);
                    return;
                }

                if (usePdf && docPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    new WindowPdfViewer(docPath).Show();
                else
                    new WindowDocumentViewer(docPath).Show();
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

        /// <summary>在 Docs 目录下查找第一个指定扩展名的文档；目录不存在或为空返回 null</summary>
        private static string FindDoc(string docsDir, string pattern)
        {
            try
            {
                return Directory.Exists(docsDir)
                    ? Directory.GetFiles(docsDir, pattern).OrderBy(f => f).FirstOrDefault()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 保存：已有文件路径时直接覆盖原文件，否则弹出另存为对话框；示例文件只允许另存为，不可直接覆盖
        /// </summary>
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureLoggedIn()) return;

            string filePath = mainViewModel.StepFilePath;
            if (string.IsNullOrEmpty(filePath) || IsExampleFile(filePath))
            {
                SaveAs_Click(sender, e);
                return;
            }

            DoSave(filePath, await AskIncludeSourceImages());
        }

        /// <summary>判断文件是否位于程序自带的 Examples 目录（示例文件）</summary>
        private static bool IsExampleFile(string filePath)
        {
            try
            {
                string examplesDir = Path.Combine(AppContext.BaseDirectory, "Examples");
                return filePath.StartsWith(examplesDir, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 另存为：始终弹出对话框选择新路径
        /// </summary>
        private async void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureLoggedIn()) return;

            var filePath = PathServices.SavePathDialog(
                LanguageService.GetLocalizedString("SaveAs"),
                "AutoStep Files|*.autostep",
                saveName: "NewSteps.autostep");
            if (filePath != null)
            {
                DoSave(filePath, await AskIncludeSourceImages());
            }
        }

        /// <summary>询问保存时是否将原图一并打包（供其他电脑重新编辑图片）</summary>
        private static async Task<bool> AskIncludeSourceImages()
        {
            var (_, popupTask) = WindowAsyncPopup.Show(
                LanguageService.GetLocalizedString("SaveWithSourceImages"),
                LanguageService.GetLocalizedString("Save"),
                PopupButtons.YesNo, MessageBoxImage.Question);
            string result = await popupTask;
            return result == PopupButton.YesValue;
        }

        private void DoSave(string filePath, bool includeSourceImages = false)
        {
            var savedFlags = stepsViewModel.AutomationStepBases
                .Select(step => (Step: step, step.IsSave))
                .ToList();
            try
            {
                foreach (var step in stepsViewModel.AutomationStepBases)
                {
                    step.IsSave = true;
                }

                // 原子保存：包写成功后才会替换旧文件（并保留 .bak）；图片会落地到包对应的工作目录
                var result = StepsFile.SaveAsAutoStepPackage(stepsViewModel.AutomationStepBases, filePath, includeSourceImages);

                // 更新当前文件路径和工作目录（图片此时已落地，再按 Ctrl+S 不会丢图）
                mainViewModel.StepFilePath = filePath;
                mainViewModel.StepImageWorkDir = result.WorkDir;
                _isDirty = false;

                // 保存成功反馈；缺失图片/落地失败必须显式告警，不能只报"成功"
                string message = LanguageService.GetLocalizedString("SaveSuccess");
                bool hasProblem = result.HasMissingImages || result.HasWarnings;

                if (result.HasMissingImages)
                {
                    message += "\n\n" + LanguageService.GetLocalizedString("SaveMissingImages", "以下图片在磁盘上不存在，未写入工程包：")
                        + "\n" + string.Join("\n", result.MissingImages.Take(8))
                        + (result.MissingImages.Count > 8 ? "\n..." : "");
                }
                if (result.HasWarnings)
                {
                    message += "\n\n" + LanguageService.GetLocalizedString("SaveWarnings", "图片复制到工作目录时出现问题：")
                        + "\n" + string.Join("\n", result.Warnings.Take(8));
                }

                WindowAsyncPopup.Show(
                    message,
                    LanguageService.GetLocalizedString("Save"),
                    PopupButtons.OK,
                    hasProblem ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // 保存失败：恢复 IsSave 标记（文件并未真正保存）
                foreach (var (step, flag) in savedFlags)
                {
                    step.IsSave = flag;
                }

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

            // 用户管理是管理员操作：普通用户不得打开（服务层同样会拒绝）
            if (!SingletonLocator.UserService.IsAdmin)
            {
                WindowAsyncPopup.Show(
                    LanguageService.GetLocalizedString("RequireAdminPermission", "需要管理员权限才能管理用户。"),
                    LanguageService.GetLocalizedString("UserManagementTitle"),
                    PopupButtons.OK, MessageBoxImage.Warning);
                return;
            }

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
