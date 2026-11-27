using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Services;
using ShaoLu.Utils;
using System;
using System.IO;

namespace ShaoLu.Viewmodels
{
    public class MainViewModel : ObservableObject
    {
        #region 属性

        #region 语言

        private bool _english;
        private bool _simplified;
        private bool _traditional;


        public bool English { get => _english; set => SetProperty(ref _english, value); }
        public bool Simplified { get => _simplified; set => SetProperty(ref _simplified, value); }
        public bool Tranditional { get => _traditional; set => SetProperty(ref _traditional, value); }

        #endregion


        private string _rootPath = Directory.GetCurrentDirectory();
        private string _tempPath = Path.GetTempPath();
        private string _stepFilePath;
        private string _stepImageWorkDir;
        private string _imageFilePath;
        private bool _isLoggedIn;

        /// <summary>
        /// 是否已登录（拥有编辑权限）
        /// </summary>
        public bool IsLoggedIn
        {
            get => _isLoggedIn;
            set => SetProperty(ref _isLoggedIn, value);
        }

        /// <summary>
        /// 当前登录用户名（未登录时为空）
        /// </summary>
        public string CurrentUsername => SingletonLocator.UserService.CurrentUser?.Username;

        /// <summary>
        /// 刷新登录状态（登录/注销后调用）
        /// </summary>
        public void RefreshLoginState()
        {
            IsLoggedIn = SingletonLocator.UserService.CurrentUser != null;
            OnPropertyChanged(nameof(CurrentUsername));
        }

        public string RootDir { get => _rootPath; set => _rootPath = value; }
        public string TempDir { get => _tempPath; set => _tempPath = value; }

        /// <summary>
        /// 当前打开/保存的 .autostep 文件完整路径
        /// </summary>
        public string StepFilePath
        {
            get => _stepFilePath;
            set
            {
                if (SetProperty(ref _stepFilePath, value))
                    OnPropertyChanged(nameof(WindowTitle));
            }
        }

        /// <summary>窗口标题：程序名 + 当前工程文件名（未保存时显示「未保存」）</summary>
        public string WindowTitle
        {
            get
            {
                string app = Services.LanguageService.GetLocalizedString("AppTitle");
                string file = string.IsNullOrWhiteSpace(_stepFilePath)
                    ? Services.LanguageService.GetLocalizedString("Title_Unsaved")
                    : System.IO.Path.GetFileNameWithoutExtension(_stepFilePath);
                return $"{app} - {file}";
            }
        }

        /// <summary>语言切换后刷新标题等本地化文案</summary>
        public void RefreshLocalizedText()
        {
            OnPropertyChanged(nameof(WindowTitle));
        }

        /// <summary>
        /// 当前步骤包的图片解压工作目录
        /// </summary>
        public string StepImageWorkDir { get => _stepImageWorkDir; set => SetProperty(ref _stepImageWorkDir, value); }

        public string ImageFilePath { get => _imageFilePath; set => _imageFilePath = value; }

        /// <summary>当前步骤列表视图模型（供菜单与状态栏绑定）</summary>
        public StepsViewModel Steps => SingletonLocator.Steps;

        #endregion

        #region 调试

        /// <summary>调试模式：每一步的执行细节写进独立的调试日志（logs\debug\），与执行记录 / 程序日志分开保存</summary>
        public bool DebugMode
        {
            get => ReadStepSetting(s => s.DebugMode);
            set => WriteStepSetting(s => s.DebugMode = value, nameof(DebugMode));
        }

        /// <summary>运行时在屏幕上标示 OCR / 识别区域</summary>
        public bool ShowOcrRegionOnRun
        {
            get => ReadStepSetting(s => s.ShowOCRRegionOnRun);
            set => WriteStepSetting(s => s.ShowOCRRegionOnRun = value, nameof(ShowOcrRegionOnRun));
        }

        /// <summary>运行时在屏幕上标示找到的图像区域</summary>
        public bool ShowFoundImageOnRun
        {
            get => ReadStepSetting(s => s.ShowFoundImageRegionOnRun);
            set => WriteStepSetting(s => s.ShowFoundImageRegionOnRun = value, nameof(ShowFoundImageOnRun));
        }

        /// <summary>运行时在屏幕上标示点击位置</summary>
        public bool ShowClickPositionOnRun
        {
            get => ReadStepSetting(s => s.ShowClickPositionOnRun);
            set => WriteStepSetting(s => s.ShowClickPositionOnRun = value, nameof(ShowClickPositionOnRun));
        }

        /// <summary>设置里改了这些开关后，刷新菜单 / 工具栏上的勾选状态</summary>
        public void RefreshDebugSettings()
        {
            OnPropertyChanged(nameof(DebugMode));
            OnPropertyChanged(nameof(ShowOcrRegionOnRun));
            OnPropertyChanged(nameof(ShowFoundImageOnRun));
            OnPropertyChanged(nameof(ShowClickPositionOnRun));
        }

        private static bool ReadStepSetting(Func<Models.StepSettingsModel, bool> read)
        {
            try { return read(SingletonLocator.Settings.Step); }
            catch { return false; }
        }

        /// <summary>改一个调试开关：立刻生效并落盘（写盘失败只记日志，不影响本次使用）</summary>
        private void WriteStepSetting(Action<Models.StepSettingsModel> write, string propertyName)
        {
            try
            {
                write(SingletonLocator.Settings.Step);
                OnPropertyChanged(propertyName);
                _ = SettingsService.SaveAsync(SingletonLocator.Settings);
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Warn(ex, "保存调试设置失败");
            }
        }

        #endregion
    }
}
