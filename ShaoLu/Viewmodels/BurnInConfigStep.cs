using CommunityToolkit.Mvvm.Input;
using ShaoLu.Models;
using ShaoLu.Services;
using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ShaoLu.Viewmodels.AutomationStep
{
    /// <summary>
    /// 烧录配置步骤：承载工令输入与烧录统计配置。
    /// 每次运行时首次执行会弹出工令输入窗口；配置保存在细节栏并随步骤文件持久化。
    /// 全局仅允许存在一个该类型步骤。
    /// </summary>
    public class BurnInConfigStep : AutomationStepBase
    {
        private BurnInConfig _config;

        /// <summary>烧录统计配置（随步骤文件 JSON 序列化）</summary>
        public BurnInConfig Config
        {
            get => _config;
            set => SetProperty(ref _config, value ?? new BurnInConfig());
        }

        public BurnInConfigStep() : base()
        {
            Type = StepType.BurnInConfig;
            // 从旧全局配置一次性迁移（反序列化时 JSON 中的 Config 会覆盖此值）
            _config = CloneConfig(BurnInService.Config);
        }

        public BurnInConfigStep(string name) : this()
        {
            Name = name;
        }

        public BurnInConfigStep(string name, string description) : this()
        {
            Name = name;
            Description = description;
        }

        #region 细节栏绑定辅助属性

        /// <summary>所有步骤集合（供 ComboBox 选择判定步骤，不含占位项）</summary>
        [JsonIgnore]
        public System.Collections.ObjectModel.ObservableCollection<AutomationStepBase> BurnSteps
        {
            get
            {
                try { return Utils.SingletonLocator.Steps.AutomationStepBases; }
                catch { return null; }
            }
        }

        /// <summary>烧录完成步骤 Uid（代理属性，变更时刷新判定方式显隐）</summary>
        [JsonIgnore]
        public Guid? BurnFinishStepUid
        {
            get => Config.BurnFinishStepUid;
            set
            {
                if (Config.BurnFinishStepUid != value)
                {
                    Config.BurnFinishStepUid = value;
                    OnPropertyChanged(nameof(BurnFinishStepUid));
                    OnPropertyChanged(nameof(ShowKeywordSettings));
                    OnPropertyChanged(nameof(ShowImageSettings));
                }
            }
        }

        /// <summary>良品关键字（代理属性）</summary>
        [JsonIgnore]
        public string GoodTextContains
        {
            get => Config.GoodTextContains;
            set { if (Config.GoodTextContains != value) { Config.GoodTextContains = value; OnPropertyChanged(nameof(GoodTextContains)); } }
        }

        /// <summary>不良品关键字（代理属性）</summary>
        [JsonIgnore]
        public string BadTextContains
        {
            get => Config.BadTextContains;
            set { if (Config.BadTextContains != value) { Config.BadTextContains = value; OnPropertyChanged(nameof(BadTextContains)); } }
        }

        /// <summary>烧录失败关键字（代理属性）</summary>
        [JsonIgnore]
        public string FailTextContains
        {
            get => Config.FailTextContains;
            set { if (Config.FailTextContains != value) { Config.FailTextContains = value; OnPropertyChanged(nameof(FailTextContains)); } }
        }

        /// <summary>良品判据模板图相对路径（代理属性）</summary>
        [JsonIgnore]
        public string GoodTemplateName
        {
            get => Config.GoodTemplateName;
            set
            {
                if (Config.GoodTemplateName != value)
                {
                    Config.GoodTemplateName = value;
                    OnPropertyChanged(nameof(GoodTemplateName));
                    OnPropertyChanged(nameof(GoodTemplateThumb));
                }
            }
        }

        /// <summary>不良品判据模板图相对路径（代理属性）</summary>
        [JsonIgnore]
        public string BadTemplateName
        {
            get => Config.BadTemplateName;
            set
            {
                if (Config.BadTemplateName != value)
                {
                    Config.BadTemplateName = value;
                    OnPropertyChanged(nameof(BadTemplateName));
                    OnPropertyChanged(nameof(BadTemplateThumb));
                }
            }
        }

        /// <summary>烧录失败判据模板图相对路径（代理属性）</summary>
        [JsonIgnore]
        public string FailTemplateName
        {
            get => Config.FailTemplateName;
            set
            {
                if (Config.FailTemplateName != value)
                {
                    Config.FailTemplateName = value;
                    OnPropertyChanged(nameof(FailTemplateName));
                    OnPropertyChanged(nameof(FailTemplateThumb));
                }
            }
        }

        /// <summary>模板匹配相似度阈值（代理属性）</summary>
        [JsonIgnore]
        public double SimilarityThreshold
        {
            get => Config.SimilarityThreshold;
            set { if (Config.SimilarityThreshold != value) { Config.SimilarityThreshold = value; OnPropertyChanged(nameof(SimilarityThreshold)); } }
        }

        /// <summary>良品判据模板缩略图</summary>
        [JsonIgnore]
        public System.Windows.Media.ImageSource GoodTemplateThumb => LoadThumb(Config.GoodTemplateName);

        /// <summary>不良品判据模板缩略图</summary>
        [JsonIgnore]
        public System.Windows.Media.ImageSource BadTemplateThumb => LoadThumb(Config.BadTemplateName);

        /// <summary>烧录失败判据模板缩略图</summary>
        [JsonIgnore]
        public System.Windows.Media.ImageSource FailTemplateThumb => LoadThumb(Config.FailTemplateName);

        /// <summary>是否启用截图留痕（代理属性）</summary>
        [JsonIgnore]
        public bool CaptureScreenshot
        {
            get => Config.CaptureScreenshot;
            set { if (Config.CaptureScreenshot != value) { Config.CaptureScreenshot = value; OnPropertyChanged(nameof(CaptureScreenshot)); } }
        }

        /// <summary>截图保存目录（代理属性，留空使用默认位置）</summary>
        [JsonIgnore]
        public string ScreenshotDir
        {
            get => Config.ScreenshotDir;
            set
            {
                if (Config.ScreenshotDir != value)
                {
                    Config.ScreenshotDir = value;
                    OnPropertyChanged(nameof(ScreenshotDir));
                    OnPropertyChanged(nameof(EffectiveScreenshotDir));
                }
            }
        }

        /// <summary>当前实际生效的截图保存目录（供展示）</summary>
        [JsonIgnore]
        public string EffectiveScreenshotDir => Services.BurnInService.GetEffectiveScreenshotDir(Config);

        /// <summary>烧录完成步骤是否为获取输入类型（判定方式=关键字，与图像判定互斥）</summary>
        [JsonIgnore]
        public bool ShowKeywordSettings => GetFinishStep() is GetInputStep;

        /// <summary>已选择完成步骤且非获取输入类型时显示图像判定区（与关键字判定互斥）</summary>
        [JsonIgnore]
        public bool ShowImageSettings
        {
            get
            {
                var finishStep = GetFinishStep();
                return finishStep != null && !(finishStep is GetInputStep);
            }
        }

        private AutomationStepBase GetFinishStep()
        {
            if (!Config.BurnFinishStepUid.HasValue) return null;
            var steps = AllSteps;
            if (steps == null) return null;
            foreach (var s in steps)
                if (s.Uid == Config.BurnFinishStepUid.Value) return s;
            return null;
        }

        #endregion

        #region 命令

        [JsonIgnore]
        private ICommand selectRegionCommand;
        /// <summary>选择截图区域</summary>
        [JsonIgnore]
        public ICommand SelectRegionCommand => selectRegionCommand ??= new RelayCommand(SelectRegion);

        [JsonIgnore]
        private ICommand browseScreenshotDirCommand;
        /// <summary>浏览选择截图保存目录</summary>
        [JsonIgnore]
        public ICommand BrowseScreenshotDirCommand => browseScreenshotDirCommand ??= new RelayCommand(BrowseScreenshotDir);

        private void BrowseScreenshotDir()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = LanguageService.GetLocalizedString("BurnIn_ScreenshotDir", "Screenshot Save Directory"),
                SelectedPath = string.IsNullOrWhiteSpace(ScreenshotDir) ? EffectiveScreenshotDir : ScreenshotDir,
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ScreenshotDir = dialog.SelectedPath;
            }
        }

        private void SelectRegion()
        {
            var win = new Views.WindowSelectRegion();
            if (win.ShowDialog() == true && win.SelectedRegion != Rect.Empty)
            {
                Config.RegionX = win.SelectedRegion.X;
                Config.RegionY = win.SelectedRegion.Y;
                Config.RegionW = win.SelectedRegion.Width;
                Config.RegionH = win.SelectedRegion.Height;
                OnPropertyChanged(nameof(Config));
            }
        }

        [JsonIgnore]
        private ICommand selectTemplateCommand;
        /// <summary>选择原图并通过裁剪窗口裁剪为指定类型判据模板（参数：Good/Bad/Fail）</summary>
        [JsonIgnore]
        public ICommand SelectTemplateCommand => selectTemplateCommand ??= new RelayCommand<object>(SelectTemplate, null);

        [JsonIgnore]
        private ICommand clearTemplateCommand;
        /// <summary>清除指定类型判据模板（参数：Good/Bad/Fail）</summary>
        [JsonIgnore]
        public ICommand ClearTemplateCommand => clearTemplateCommand ??= new RelayCommand<object>(ClearTemplate, null);

        private void SelectTemplate(object parameter)
        {
            if (!(parameter is string kind)) return;

            // 1. 选择原图
            string title = LanguageService.GetLocalizedString("Select_target_pic", "Open Image File");
            string filter = LanguageService.GetLocalizedString("Image_File", "Image Files") + "(*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";
            string imagePath = PathServices.OpenPathDialog(title, filter);
            if (string.IsNullOrEmpty(imagePath)) return;

            var imgSrc = LoadFrozenBitmap(imagePath);
            if (imgSrc == null) return;

            // 2. 打开裁剪窗口，裁剪完成后保存为判据模板
            var editWindow = new Views.WindowEditImage();
            editWindow.Show();
            // 使用 Background 优先级，确保裁剪控件完成内部布局和渲染后再设置图片
            editWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                editWindow.editImageViewModel.ImgSrc = imgSrc;
                editWindow.UpdateLayout();
            }), System.Windows.Threading.DispatcherPriority.Background);
            editWindow.editImageViewModel.OnImageSaved += (img, rect, clickThumbs, ocrRect) =>
            {
                SaveTemplate(img, kind);
            };
        }

        /// <summary>将裁剪图保存为指定类型判据模板（{工作目录}\images\burnin_{Uid}_{kind}.png）</summary>
        private void SaveTemplate(System.Windows.Media.ImageSource imageSource, string kind)
        {
            try
            {
                if (!(imageSource is System.Windows.Media.Imaging.BitmapSource bitmapSource)) return;

                var mainVM = Utils.SingletonLocator.Main;
                string workDir = mainVM?.StepImageWorkDir;
                if (string.IsNullOrEmpty(workDir))
                {
                    workDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AutoShaoLu", "images");
                    if (mainVM != null) mainVM.StepImageWorkDir = workDir;
                }
                string imagesDir = System.IO.Path.Combine(workDir, "images");
                System.IO.Directory.CreateDirectory(imagesDir);

                string fileName = $"burnin_{Uid}_{kind.ToLower()}.png";
                string fullPath = System.IO.Path.Combine(imagesDir, fileName);

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                using (var stream = new System.IO.FileStream(fullPath, System.IO.FileMode.Create))
                {
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
                    encoder.Save(stream);
                }

                string relativeName = $"images/{fileName}";
                switch (kind)
                {
                    case "Good": GoodTemplateName = relativeName; break;
                    case "Bad": BadTemplateName = relativeName; break;
                    case "Fail": FailTemplateName = relativeName; break;
                }

                // 二次裁剪时文件名不变，代理属性的变更通知不会触发，
                // 此处强制刷新缩略图，避免界面显示旧图
                switch (kind)
                {
                    case "Good":
                        OnPropertyChanged(nameof(GoodTemplateName));
                        OnPropertyChanged(nameof(GoodTemplateThumb));
                        break;
                    case "Bad":
                        OnPropertyChanged(nameof(BadTemplateName));
                        OnPropertyChanged(nameof(BadTemplateThumb));
                        break;
                    case "Fail":
                        OnPropertyChanged(nameof(FailTemplateName));
                        OnPropertyChanged(nameof(FailTemplateThumb));
                        break;
                }
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Warn(ex, "SaveTemplate failed");
            }
        }

        /// <summary>加载图片文件为冻结的 BitmapImage；失败返回 null</summary>
        private System.Windows.Media.ImageSource LoadFrozenBitmap(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                // 流式加载：避免 UriSource 按 URI 缓存导致同路径文件更新后仍返回旧图
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = fs;
                    bitmap.EndInit();
                }
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Warn(ex, "LoadFrozenBitmap failed");
                return null;
            }
        }

        private void ClearTemplate(object parameter)
        {
            switch (parameter as string)
            {
                case "Good": GoodTemplateName = null; break;
                case "Bad": BadTemplateName = null; break;
                case "Fail": FailTemplateName = null; break;
            }
        }

        /// <summary>从工作目录加载模板缩略图；文件不存在或加载失败返回 null</summary>
        private System.Windows.Media.ImageSource LoadThumb(string templateName)
        {
            try
            {
                string workDir = Utils.SingletonLocator.Main?.StepImageWorkDir;
                if (string.IsNullOrEmpty(templateName) || string.IsNullOrEmpty(workDir)) return null;
                string fullPath = System.IO.Path.Combine(workDir,
                    templateName.Replace('/', System.IO.Path.DirectorySeparatorChar));
                return LoadFrozenBitmap(fullPath);
            }
            catch { return null; }
        }

        #endregion

        public override AutomationStepBase Clone()
        {
            var clone = new BurnInConfigStep(Name, Description)
            {
                Config = CloneConfig(Config),
                WaitTime = WaitTime,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
                ConditionMode = ConditionMode,
                Conditions = new(Conditions),
            };
            return clone;
        }

        /// <summary>深拷贝配置（避免多个步骤共享同一配置对象）</summary>
        private static BurnInConfig CloneConfig(BurnInConfig src)
        {
            src ??= new BurnInConfig();
            return new BurnInConfig
            {
                BurnFinishStepUid = src.BurnFinishStepUid,
                GoodTextContains = src.GoodTextContains,
                BadTextContains = src.BadTextContains,
                FailTextContains = src.FailTextContains,
                GoodTemplateName = src.GoodTemplateName,
                BadTemplateName = src.BadTemplateName,
                FailTemplateName = src.FailTemplateName,
                SimilarityThreshold = src.SimilarityThreshold,
                CaptureScreenshot = src.CaptureScreenshot,
                RegionX = src.RegionX,
                RegionY = src.RegionY,
                RegionW = src.RegionW,
                RegionH = src.RegionH,
            };
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            await Task.Delay((int)(WaitTime * 1000), cancellationToken);

            // 仅本次运行首次执行时弹出工令输入窗口；已有会话时直接跳过
            bool success = true;
            if (BurnInService.CurrentSession == null)
            {
                try
                {
                    // UI 线程创建非属主输入窗口，后台等待用户输入（主窗口最小化不影响该窗口）
                    var sessionTask = Application.Current.Dispatcher.Invoke(() => BurnInService.BeginSession());
                    success = await sessionTask;
                }
                catch (Exception ex)
                {
                    NLog.LogManager.GetCurrentClassLogger().Warn(ex, "BurnIn BeginSession failed");
                    success = false;
                }
            }

            IsTrue = success;
            if (!success)
                ErrorMessage = LanguageService.GetLocalizedString("BurnIn_SessionCancelled");

            LastResult = new StepExecutionResult
            {
                IsTrue = success,
                ExecutedAt = DateTime.Now,
            };

            return IsTrue;
        }
    }
}
