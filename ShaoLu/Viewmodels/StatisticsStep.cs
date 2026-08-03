using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShaoLu.Models;
using ShaoLu.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ShaoLu.Viewmodels.AutomationStep
{
    /// <summary>
    /// 统计项类型
    /// </summary>
    public enum StatisticsItemType
    {
        TotalExecutionTime,
        StepExecutionTime,
        StepExecutionCount,
        StepExecutionResult,
        /// <summary>条件计数：按自定义条件统计 true/false 次数</summary>
        ConditionCount,
    }

    /// <summary>
    /// 步骤统计范围
    /// </summary>
    public enum StepScope
    {
        /// <summary>所有步骤</summary>
        All,
        /// <summary>仅勾选了记录执行日志的步骤</summary>
        LoggedOnly,
        /// <summary>自选步骤</summary>
        Custom,
    }

    /// <summary>
    /// 统计项配置（含子设置：统计范围 / 条件计数规则）
    /// </summary>
    public class StatisticsItemConfig : ObservableObject
    {
        private bool _isEnabled;
        private StatisticsItemType _type;
        private StepScope _scope = StepScope.All;
        private ObservableCollection<Guid> _customStepUids = new();
        private ObservableCollection<StepCondition> _conditions = new();

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetProperty(ref _isEnabled, value))
                {
                    OnPropertyChanged(nameof(ShowScopeSettings));
                    OnPropertyChanged(nameof(ShowCustomList));
                    OnPropertyChanged(nameof(ShowConditionSettings));
                }
            }
        }
        public StatisticsItemType Type
        {
            get => _type;
            set
            {
                if (SetProperty(ref _type, value))
                {
                    OnPropertyChanged(nameof(IsStepLevel));
                    OnPropertyChanged(nameof(ShowScopeSettings));
                    OnPropertyChanged(nameof(ShowCustomList));
                    OnPropertyChanged(nameof(ShowConditionSettings));
                }
            }
        }

        /// <summary>统计范围（仅对步骤级统计项有效）</summary>
        public StepScope Scope
        {
            get => _scope;
            set
            {
                if (SetProperty(ref _scope, value))
                    OnPropertyChanged(nameof(ShowCustomList));
            }
        }

        /// <summary>自选步骤 Uid 列表（Scope=Custom 时使用）</summary>
        public ObservableCollection<Guid> CustomStepUids { get => _customStepUids; set => SetProperty(ref _customStepUids, value); }

        /// <summary>条件计数规则行列表（Type=ConditionCount 时使用）</summary>
        public ObservableCollection<StepCondition> Conditions { get => _conditions; set => SetProperty(ref _conditions, value); }

        #region 条件计数运行时状态（不序列化）

        /// <summary>条件评估为 true 的累计次数</summary>
        [JsonIgnore]
        public int TrueCount { get; set; }

        /// <summary>条件评估为 false 的累计次数</summary>
        [JsonIgnore]
        public int FalseCount { get; set; }

        #endregion

        /// <summary>是否为步骤级统计项（决定是否显示范围设置）</summary>
        [JsonIgnore]
        public bool IsStepLevel => Type != StatisticsItemType.TotalExecutionTime && Type != StatisticsItemType.ConditionCount;

        /// <summary>是否显示范围设置（启用且为步骤级）</summary>
        [JsonIgnore]
        public bool ShowScopeSettings => IsEnabled && IsStepLevel;

        /// <summary>是否显示自选步骤列表（启用且范围为 Custom）</summary>
        [JsonIgnore]
        public bool ShowCustomList => IsEnabled && IsStepLevel && Scope == StepScope.Custom;

        /// <summary>是否显示条件编辑区（启用且为条件计数）</summary>
        [JsonIgnore]
        public bool ShowConditionSettings => IsEnabled && Type == StatisticsItemType.ConditionCount;

        /// <summary>所有范围选项（供 ComboBox 绑定）</summary>
        [JsonIgnore]
        public List<StepScope> AllScopes { get; } = new() { StepScope.All, StepScope.LoggedOnly, StepScope.Custom };

        /// <summary>统计项显示名称（国际化）</summary>
        [JsonIgnore]
        public string DisplayName => Type switch
        {
            StatisticsItemType.TotalExecutionTime => LanguageService.GetLocalizedString("Stats_TotalTime"),
            StatisticsItemType.StepExecutionTime => LanguageService.GetLocalizedString("Stats_StepTime"),
            StatisticsItemType.StepExecutionCount => LanguageService.GetLocalizedString("Stats_StepCount"),
            StatisticsItemType.StepExecutionResult => LanguageService.GetLocalizedString("Stats_StepResult"),
            StatisticsItemType.ConditionCount => LanguageService.GetLocalizedString("Stats_ConditionCount"),
            _ => Type.ToString(),
        };

        #region 条件编辑命令

        [JsonIgnore]
        private ICommand addConditionCommand;
        [JsonIgnore]
        public ICommand AddConditionCommand => addConditionCommand ??= new RelayCommand(() => Conditions.Add(new StepCondition()));

        [JsonIgnore]
        private ICommand removeConditionCommand;
        [JsonIgnore]
        public ICommand RemoveConditionCommand => removeConditionCommand ??= new RelayCommand(() =>
        {
            if (Conditions.Count > 0)
                Conditions.RemoveAt(Conditions.Count - 1);
        });

        #endregion

        /// <summary>重置条件计数（每次运行前调用）</summary>
        public void ResetCounters()
        {
            TrueCount = 0;
            FalseCount = 0;
        }

        /// <summary>根据最新步骤执行结果评估条件并累计计数</summary>
        public void EvaluateAndCount(StepExecutionContext context, StepExecutionResult currentResult)
        {
            if (!IsEnabled || Type != StatisticsItemType.ConditionCount) return;
            bool result = ConditionEvaluator.Evaluate(Conditions, context, currentResult);
            if (result) TrueCount++;
            else FalseCount++;
        }

        /// <summary>判断指定步骤是否在此统计项的范围内</summary>
        public bool IsStepInScope(AutomationStepBase step)
        {
            if (!IsStepLevel) return true;
            return Scope switch
            {
                StepScope.All => true,
                StepScope.LoggedOnly => step.EnableLog,
                StepScope.Custom => CustomStepUids.Contains(step.Uid),
                _ => true,
            };
        }
    }

    /// <summary>
    /// 统计信息步骤：执行时打开悬浮窗展示运行时统计
    /// </summary>
    public class StatisticsStep : AutomationStepBase
    {
        private string _windowTitle = "Statistics";
        private double _windowX = 100;
        private double _windowY = 100;
        private bool _alwaysOnTop = true;
        private double _autoCloseSeconds = 0;
        private bool _closeOnStop = true;

        private ObservableCollection<StatisticsItemConfig> _statisticsItems = new()
        {
            new StatisticsItemConfig { Type = StatisticsItemType.TotalExecutionTime, IsEnabled = true },
            new StatisticsItemConfig { Type = StatisticsItemType.StepExecutionTime, IsEnabled = false },
            new StatisticsItemConfig { Type = StatisticsItemType.StepExecutionCount, IsEnabled = false },
            new StatisticsItemConfig { Type = StatisticsItemType.StepExecutionResult, IsEnabled = false },
            new StatisticsItemConfig { Type = StatisticsItemType.ConditionCount, IsEnabled = false },
        };

        /// <summary>窗口标题</summary>
        public string WindowTitle { get => _windowTitle; set => SetProperty(ref _windowTitle, value); }

        /// <summary>窗口 X 位置</summary>
        public double WindowX { get => _windowX; set => SetProperty(ref _windowX, value); }

        /// <summary>窗口 Y 位置</summary>
        public double WindowY { get => _windowY; set => SetProperty(ref _windowY, value); }

        /// <summary>是否始终置顶</summary>
        public bool AlwaysOnTop { get => _alwaysOnTop; set => SetProperty(ref _alwaysOnTop, value); }

        /// <summary>自动关闭时间（0=不自动关闭）</summary>
        public double AutoCloseSeconds { get => _autoCloseSeconds; set => SetProperty(ref _autoCloseSeconds, value); }

        /// <summary>运行停止后是否自动关闭统计窗口</summary>
        public bool CloseOnStop { get => _closeOnStop; set => SetProperty(ref _closeOnStop, value); }

        /// <summary>统计项配置集合</summary>
        public ObservableCollection<StatisticsItemConfig> StatisticsItems { get => _statisticsItems; set => SetProperty(ref _statisticsItems, value); }

        #region 构造

        public StatisticsStep() : base()
        {
            Type = StepType.Statistics;
        }

        public StatisticsStep(string name) : base()
        {
            Type = StepType.Statistics;
            Name = name;
        }

        public StatisticsStep(string name, string description) : base()
        {
            Type = StepType.Statistics;
            Name = name;
            Description = description;
        }

        #endregion

        public override AutomationStepBase Clone()
        {
            var clone = new StatisticsStep(Name, Description)
            {
                WindowTitle = WindowTitle,
                WindowX = WindowX,
                WindowY = WindowY,
                AlwaysOnTop = AlwaysOnTop,
                AutoCloseSeconds = AutoCloseSeconds,
                CloseOnStop = CloseOnStop,
                WaitTime = WaitTime,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                ConditionMode = ConditionMode,
                Conditions = new(Conditions),
            };
            foreach (var item in StatisticsItems)
            {
                clone.StatisticsItems.Add(new StatisticsItemConfig
                {
                    Type = item.Type,
                    IsEnabled = item.IsEnabled,
                    Scope = item.Scope,
                    CustomStepUids = new ObservableCollection<Guid>(item.CustomStepUids),
                    Conditions = new ObservableCollection<StepCondition>(item.Conditions.Select(c => c.Clone())),
                });
            }
            return clone;
        }

        /// <summary>重置所有条件计数（每次运行前调用）</summary>
        public void ResetCounters()
        {
            foreach (var item in StatisticsItems)
            {
                item.ResetCounters();
            }
        }

        /// <summary>
        /// 每个步骤执行后调用：对条件计数统计项累计 true/false 次数
        /// </summary>
        public void UpdateConditionCounts(StepExecutionContext context, StepExecutionResult currentResult)
        {
            foreach (var item in StatisticsItems)
            {
                item.EvaluateAndCount(context, currentResult);
            }
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            await Task.Delay((int)(WaitTime * 1000), cancellationToken);

            // 在 UI 线程检查/创建统计窗口（同一步骤 UID 全局仅一个窗口）
            bool success = false;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var existing = Views.WindowStatistics.FindOpenByStepUid(Uid);
                if (existing != null)
                {
                    // 已有本步骤打开的窗口，激活即可，不新建
                    existing.Activate();
                    success = true;
                }
                else
                {
                    try
                    {
                        var window = new Views.WindowStatistics(this);
                        window.Show();
                        success = true;
                    }
                    catch
                    {
                        success = false;
                    }
                }
            });

            // 统计成功为 true，失败为 false
            IsTrue = success;
            IsError = !success;
            ErrorType = success ? StepErrorType.None : StepErrorType.Unknown;

            LastResult = new StepExecutionResult
            {
                IsTrue = success,
                ExecutedAt = DateTime.Now,
            };

            return IsTrue;
        }
    }
}
