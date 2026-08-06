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

        /// <summary>良品图像判定步骤 Uid（代理属性）</summary>
        [JsonIgnore]
        public Guid? GoodImageStepUid
        {
            get => Config.GoodImageStepUid;
            set { if (Config.GoodImageStepUid != value) { Config.GoodImageStepUid = value; OnPropertyChanged(nameof(GoodImageStepUid)); } }
        }

        /// <summary>不良图像判定步骤 Uid（代理属性）</summary>
        [JsonIgnore]
        public Guid? BadImageStepUid
        {
            get => Config.BadImageStepUid;
            set { if (Config.BadImageStepUid != value) { Config.BadImageStepUid = value; OnPropertyChanged(nameof(BadImageStepUid)); } }
        }

        /// <summary>烧录失败图像判定步骤 Uid（代理属性）</summary>
        [JsonIgnore]
        public Guid? FailImageStepUid
        {
            get => Config.FailImageStepUid;
            set { if (Config.FailImageStepUid != value) { Config.FailImageStepUid = value; OnPropertyChanged(nameof(FailImageStepUid)); } }
        }

        /// <summary>是否启用截图留痕（代理属性）</summary>
        [JsonIgnore]
        public bool CaptureScreenshot
        {
            get => Config.CaptureScreenshot;
            set { if (Config.CaptureScreenshot != value) { Config.CaptureScreenshot = value; OnPropertyChanged(nameof(CaptureScreenshot)); } }
        }

        /// <summary>烧录完成步骤是否为获取输入类型（判定方式=关键字）</summary>
        [JsonIgnore]
        public bool ShowKeywordSettings => GetFinishStep() is GetInputStep;

        /// <summary>烧录完成步骤是否为图像类型（判定方式=图像步骤）</summary>
        [JsonIgnore]
        public bool ShowImageSettings
        {
            get
            {
                var step = GetFinishStep();
                return step is ClickImageStep || step is FindImageStep
                    || step is ClickImagesStep || step is FindImagesStep;
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
                GoodImageStepUid = src.GoodImageStepUid,
                BadImageStepUid = src.BadImageStepUid,
                FailImageStepUid = src.FailImageStepUid,
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
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        success = BurnInService.BeginSession();
                    }
                    catch (Exception ex)
                    {
                        NLog.LogManager.GetCurrentClassLogger().Warn(ex, "BurnIn BeginSession failed");
                        success = false;
                    }
                });
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
