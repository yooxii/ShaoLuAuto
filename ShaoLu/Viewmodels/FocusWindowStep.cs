using CommunityToolkit.Mvvm.Input;
using ShaoLu.Models;
using ShaoLu.Utils;
using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ShaoLu.Viewmodels.AutomationStep
{
    /// <summary>
    /// 窗口聚焦步骤：把指定窗口放在最前方并激活（最小化时先恢复）
    /// 目标窗口按“选定标题精确匹配 → 关键字包含匹配”的顺序查找
    /// </summary>
    public class FocusWindowStep : AutomationStepBase
    {
        private readonly static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private string _windowTitleKeyword = string.Empty;
        private string _selectedWindowTitle = string.Empty;

        /// <summary>窗口标题关键字（包含匹配，不区分大小写）</summary>
        public string WindowTitleKeyword { get => _windowTitleKeyword; set => SetProperty(ref _windowTitleKeyword, value); }

        /// <summary>枚举选定的窗口标题（精确匹配，优先于关键字）</summary>
        public string SelectedWindowTitle { get => _selectedWindowTitle; set => SetProperty(ref _selectedWindowTitle, value); }

        /// <summary>枚举到的窗口标题列表（供 ComboBox 绑定，不参与序列化）</summary>
        [JsonIgnore]
        public ObservableCollection<string> WindowTitles { get; } = new();

        #region 命令

        [JsonIgnore]
        private ICommand refreshWindowsCommand;
        /// <summary>枚举当前可见窗口，填充下拉列表</summary>
        [JsonIgnore]
        public ICommand RefreshWindowsCommand => refreshWindowsCommand ??= new RelayCommand(RefreshWindows);

        #endregion

        #region 构造

        public FocusWindowStep() : base()
        {
            Type = StepType.FocusWindow;
        }

        public FocusWindowStep(string name) : base()
        {
            Type = StepType.FocusWindow;
            Name = name;
        }

        public FocusWindowStep(string name, string description) : base()
        {
            Type = StepType.FocusWindow;
            Name = name;
            Description = description;
        }

        #endregion

        public override AutomationStepBase Clone()
        {
            return new FocusWindowStep(Name, Description)
            {
                WindowTitleKeyword = WindowTitleKeyword,
                SelectedWindowTitle = SelectedWindowTitle,
                WaitTime = WaitTime,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
                ConditionMode = ConditionMode,
                Conditions = new(Conditions),
            };
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            await Task.Delay((int)(WaitTime * 1000), cancellationToken);

            IntPtr? hwnd = WindowServices.FindWindow(SelectedWindowTitle, WindowTitleKeyword);
            if (!hwnd.HasValue)
            {
                logger.Warn("FocusWindow: window not found. ExactTitle={0}, Keyword={1}", SelectedWindowTitle, WindowTitleKeyword);
                IsTrue = false;
                IsError = true;
                ErrorType = StepErrorType.Unknown;
                LastResult = new StepExecutionResult
                {
                    IsTrue = false,
                    ExecutedAt = DateTime.Now,
                };
                return false;
            }

            bool ok = WindowServices.FocusWindow(hwnd.Value);

            IsTrue = ok;
            IsError = !ok;
            ErrorType = ok ? StepErrorType.None : StepErrorType.Unknown;
            LastResult = new StepExecutionResult
            {
                IsTrue = ok,
                ExecutedAt = DateTime.Now,
            };
            return ok;
        }

        #region 私有方法

        private void RefreshWindows()
        {
            WindowTitles.Clear();
            try
            {
                foreach (var w in WindowServices.GetVisibleTopLevelWindows())
                    WindowTitles.Add(w.Value);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Enumerate windows failed");
            }
        }

        #endregion
    }
}
