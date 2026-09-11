using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using ShaoLu.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ShaoLu.Viewmodels
{
    public class StepsViewModel : ObservableObject
    {
        private readonly static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        private CancellationTokenSource _cts;
        private readonly StepSettingsModel _stepSettings = SingletonLocator.Settings.Step;
        private readonly StepExecutionContext _executionContext = StepExecutionContext.Instance;

        #region 属性

        private volatile bool _stopSignal = false;
        public bool StopSignal { get => _stopSignal; set { _stopSignal = value; IsRunning = !value; } }

        private volatile bool _isPaused = false;
        /// <summary>是否处于暂停状态</summary>
        public bool IsPaused { get => _isPaused; set => SetProperty(ref _isPaused, value); }


        public bool _isRunning = false;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    RunCommand.RaiseCanExecuteChanged();
                    StopCommand.RaiseCanExecuteChanged();
                    AddStepCommand.RaiseCanExecuteChanged();
                    DelStepCommand.RaiseCanExecuteChanged();
                    UpStepCommand.RaiseCanExecuteChanged();
                    DownStepCommand.RaiseCanExecuteChanged();
                    CopyStepCommand.RaiseCanExecuteChanged();
                    CutStepCommand.RaiseCanExecuteChanged();
                    PasteStepCommand.RaiseCanExecuteChanged();
                    UndoCommand.RaiseCanExecuteChanged();
                    RedoCommand.RaiseCanExecuteChanged();
                    UndoStepCommand.RaiseCanExecuteChanged();
                    RedoStepCommand.RaiseCanExecuteChanged();
                }
            }
        }


        // 选中步骤
        private AutomationStepBase _selectedStep;
        public AutomationStepBase SelectedStep
        {
            get => _selectedStep;
            set
            {
                if (SetProperty(ref _selectedStep, value))
                    OnSelectedStepChanged(value);
            }
        }

        private AutomationStepBase _errorStep;
        public AutomationStepBase ErrorStep { get => _errorStep; set => SetProperty(ref _errorStep, value); }

        public ObservableCollection<AutomationStepBase> SelectedSteps { get; set; } = [];

        public ObservableCollection<AutomationStepBase> PasteSteps { get; set; } = [];

        // ------- 撤销 / 恢复 -------
        // 步骤列表历史：记录整表快照（增删/移动/粘贴/剪切），生命周期到程序关闭或打开新文件
        private readonly List<List<AutomationStepBase>> _listUndo = new();
        private readonly List<List<AutomationStepBase>> _listRedo = new();
        private const int MaxListHistory = 50;
        private bool _suppressListTracking;

        // 步骤内部历史：每个步骤独立，切换到其他步骤时清空
        private readonly List<AutomationStepBase> _stepUndo = new();
        private readonly List<AutomationStepBase> _stepRedo = new();
        private const int MaxStepHistory = 100;
        private AutomationStepBase _editStep;
        private AutomationStepBase _editBaseline;
        private bool _suppressStepTracking;



        private ObservableCollection<AutomationStepBase> _automationStepBases = [];
        public ObservableCollection<AutomationStepBase> AutomationStepBases { get => _automationStepBases; set => SetProperty(ref _automationStepBases, value); }


        #endregion


        #region 命令

        private RelayCommand runCommand;
        public RelayCommand RunCommand => runCommand ??= new RelayCommand(Run, CanRun);

        private RelayCommand stopCommand;
        public RelayCommand StopCommand => stopCommand ??= new RelayCommand(Stop);

        private RelayCommand pauseCommand;
        public RelayCommand PauseCommand => pauseCommand ??= new RelayCommand(TogglePause);

        private void TogglePause()
        {
            if (!_isRunning) return;
            IsPaused = !IsPaused;
            logger.Info(IsPaused ? "Pause Auto" : "Resume Auto");
        }


        private RelayParameterCommand addStepCommand;
        public RelayParameterCommand AddStepCommand => addStepCommand ??= new RelayParameterCommand(AddStep, CanAlertStep);


        private RelayCommand delStepCommand;
        public RelayCommand DelStepCommand => delStepCommand ??= new RelayCommand(DelStep, CanAlertStep);


        private RelayCommand upStepCommand;
        public RelayCommand UpStepCommand => upStepCommand ??= new RelayCommand(UpStep, CanAlertStep);


        private RelayCommand downStepCommand;
        public RelayCommand DownStepCommand => downStepCommand ??= new RelayCommand(DownStep, CanAlertStep);


        private RelayCommand selectStepCommand;
        public RelayCommand SelectStepCommand => selectStepCommand ??= new RelayCommand(SelectStep, CanAlertStep);


        private RelayCommand copyStepCommand;
        public RelayCommand CopyStepCommand => copyStepCommand ??= new RelayCommand(CopyStep, CanAlertStep);


        private RelayCommand cutStepCommand;
        public RelayCommand CutStepCommand => cutStepCommand ??= new RelayCommand(CutStep, CanAlertStep);


        private RelayCommand pasteStepCommand;
        public RelayCommand PasteStepCommand => pasteStepCommand ??= new RelayCommand(PasteStep, CanPasteStep);


        private RelayCommand undoCommand;
        /// <summary>撤销步骤列表操作（增删/移动/粘贴/剪切）</summary>
        public RelayCommand UndoCommand => undoCommand ??= new RelayCommand(Undo, () => CanAlertStep() && _listUndo.Count > 0);


        private RelayCommand redoCommand;
        /// <summary>恢复步骤列表操作</summary>
        public RelayCommand RedoCommand => redoCommand ??= new RelayCommand(Redo, () => CanAlertStep() && _listRedo.Count > 0);


        private RelayCommand undoStepCommand;
        /// <summary>撤销当前步骤内部的编辑（每步独立）</summary>
        public RelayCommand UndoStepCommand => undoStepCommand ??= new RelayCommand(UndoStep, () => CanAlertStep() && _stepUndo.Count > 0);


        private RelayCommand redoStepCommand;
        /// <summary>恢复当前步骤内部的编辑</summary>
        public RelayCommand RedoStepCommand => redoStepCommand ??= new RelayCommand(RedoStep, () => CanAlertStep() && _stepRedo.Count > 0);

        #endregion

        public StepsViewModel()
        {
            AutomationStepBases.CollectionChanged += (s, e) =>
            {
                UpdateAutomationStepBases();
                if (e.OldItems != null)
                {
                    foreach (var item in e.OldItems)
                    {
                        if (item is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                }
            };
        }

        private void UpdateAutomationStepBases()
        {
            RunCommand.RaiseCanExecuteChanged();
            // 遍历集合并更新每个步骤的行号 (从1开始)
            for (int i = 0; i < AutomationStepBases.Count; i++)
            {
                AutomationStepBases[i].LineNo = i + 1;
            }
        }

        #region 撤销 / 恢复

        /// <summary>
        /// 步骤深拷贝：各步骤的 Clone() 未统一复制基类状态（条件判断、IsSave 等），
        /// 这里在 Clone() 基础上补齐，供复制/粘贴与撤销快照使用。
        /// Uid 由调用方决定是否保留。
        /// </summary>
        private static AutomationStepBase CloneStepCore(AutomationStepBase step)
        {
            var clone = step.Clone();
            if (step.Name != null) clone.Name = step.Name;
            clone.Description = step.Description;
            clone.IsNeed = step.IsNeed;
            clone.IsSave = step.IsSave;
            clone.WaitTime = step.WaitTime;
            clone.EnableLog = step.EnableLog;
            clone.SelfReferenceLimit = step.SelfReferenceLimit;
            clone.TrueGotoUid = step.TrueGotoUid;
            clone.FalseGotoUid = step.FalseGotoUid;
            clone.ConditionMode = step.ConditionMode;
            clone.Conditions = CloneConditions(step.Conditions);
            return clone;
        }

        /// <summary>深拷贝（保留 Uid，用于快照恢复，保证跳转/条件引用不失效）</summary>
        private static AutomationStepBase CloneStepPreservingUid(AutomationStepBase step)
        {
            var clone = CloneStepCore(step);
            clone.Uid = step.Uid;
            return clone;
        }

        private static ObservableCollection<StepCondition> CloneConditions(IEnumerable<StepCondition> conditions)
        {
            var result = new ObservableCollection<StepCondition>();
            if (conditions == null) return result;
            foreach (var c in conditions)
                result.Add(CloneCondition(c));
            return result;
        }

        private static StepCondition CloneCondition(StepCondition c) => new()
        {
            ParentStepUid = c.ParentStepUid,
            LegacyStepLineNo = c.LegacyStepLineNo,
            Variable = c.Variable,
            StepUid = c.StepUid,
            Operator = c.Operator,
            Value = c.Value,
            Connector = c.Connector,
            TextExtractMode = c.TextExtractMode,
            ExtractLines = c.ExtractLines,
            ExtractMarker = c.ExtractMarker,
            ExtractLength = c.ExtractLength,
            ExtractUnit = c.ExtractUnit,
        };

        #region 步骤列表撤销 / 恢复

        private List<AutomationStepBase> CaptureListSnapshot()
            => AutomationStepBases?.Select(CloneStepPreservingUid).ToList() ?? new List<AutomationStepBase>();

        /// <summary>在列表发生增删/移动/粘贴/剪切前调用，记录快照</summary>
        private void PushListUndoSnapshot()
        {
            if (_suppressListTracking) return;
            _listUndo.Add(CaptureListSnapshot());
            if (_listUndo.Count > MaxListHistory)
                _listUndo.RemoveAt(0);
            _listRedo.Clear();
            RaiseUndoRedoCanExecuteChanged();
        }

        private void RestoreListSnapshot(List<AutomationStepBase> snapshot)
        {
            if (snapshot == null) return;
            Guid? selectedUid = SelectedStep?.Uid;
            _suppressListTracking = true;
            try
            {
                AutomationStepBases.Clear();
                foreach (var s in snapshot)
                    AutomationStepBases.Add(CloneStepPreservingUid(s));
                UpdateAutomationStepBases();
                SelectedStep = selectedUid.HasValue
                    ? AutomationStepBases.FirstOrDefault(s => s.Uid == selectedUid.Value)
                    : null;
            }
            finally
            {
                _suppressListTracking = false;
            }
        }

        private void Undo()
        {
            if (_listUndo.Count == 0) return;
            var target = _listUndo[_listUndo.Count - 1];
            _listUndo.RemoveAt(_listUndo.Count - 1);
            _listRedo.Add(CaptureListSnapshot());
            RestoreListSnapshot(target);
            RaiseUndoRedoCanExecuteChanged();
        }

        private void Redo()
        {
            if (_listRedo.Count == 0) return;
            var target = _listRedo[_listRedo.Count - 1];
            _listRedo.RemoveAt(_listRedo.Count - 1);
            _listUndo.Add(CaptureListSnapshot());
            RestoreListSnapshot(target);
            RaiseUndoRedoCanExecuteChanged();
        }

        /// <summary>清空列表与步骤内部历史（程序启动 / 新建 / 打开文件时调用）</summary>
        public void ClearUndoRedo()
        {
            _listUndo.Clear();
            _listRedo.Clear();
            DetachStepHistory();
            RaiseUndoRedoCanExecuteChanged();
        }

        private void RaiseUndoRedoCanExecuteChanged()
        {
            UndoCommand.RaiseCanExecuteChanged();
            RedoCommand.RaiseCanExecuteChanged();
            UndoStepCommand.RaiseCanExecuteChanged();
            RedoStepCommand.RaiseCanExecuteChanged();
        }

        #endregion

        #region 步骤内部撤销 / 恢复

        private void OnSelectedStepChanged(AutomationStepBase step)
        {
            if (_isRunning || step == null)
            {
                DetachStepHistory();
                return;
            }

            // 同一个 Uid 视为同一步骤（列表刷新/撤销恢复时不重置历史）
            if (ReferenceEquals(_editStep, step))
                return;

            // 切换到另一个步骤：上一个步骤的编辑历史失效
            DetachStepHistory();
            AttachStepHistory(step);
        }

        private void AttachStepHistory(AutomationStepBase step)
        {
            _editStep = step;
            _stepUndo.Clear();
            _stepRedo.Clear();
            _editBaseline = CloneStepPreservingUid(step);

            step.PropertyChanged += OnEditStepPropertyChanged;
            if (step.Conditions != null)
            {
                step.Conditions.CollectionChanged += OnEditStepConditionsChanged;
                foreach (var c in step.Conditions)
                    c.PropertyChanged += OnEditConditionPropertyChanged;
            }
            RaiseUndoRedoCanExecuteChanged();
        }

        private void DetachStepHistory()
        {
            if (_editStep != null)
            {
                _editStep.PropertyChanged -= OnEditStepPropertyChanged;
                if (_editStep.Conditions != null)
                {
                    _editStep.Conditions.CollectionChanged -= OnEditStepConditionsChanged;
                    foreach (var c in _editStep.Conditions)
                        c.PropertyChanged -= OnEditConditionPropertyChanged;
                }
            }
            _editStep = null;
            _editBaseline = null;
            _stepUndo.Clear();
            _stepRedo.Clear();
            RaiseUndoRedoCanExecuteChanged();
        }

        private static bool ShouldIgnoreStepProperty(string propertyName)
        {
            return propertyName == nameof(AutomationStepBase.ConditionSummary)
                || propertyName == nameof(AutomationStepBase.LineNo)
                || propertyName == nameof(AutomationStepBase.IsError)
                || propertyName == nameof(AutomationStepBase.ErrorMessage)
                || propertyName == nameof(AutomationStepBase.IsTrue)
                || propertyName == nameof(AutomationStepBase.LastResult);
        }

        private void OnEditStepPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (ShouldIgnoreStepProperty(e.PropertyName)) return;
            RecordStepChange();
        }

        private void OnEditStepConditionsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (StepCondition c in e.OldItems)
                    c.PropertyChanged -= OnEditConditionPropertyChanged;
            if (e.NewItems != null)
                foreach (StepCondition c in e.NewItems)
                    c.PropertyChanged += OnEditConditionPropertyChanged;
            RecordStepChange();
        }

        private void OnEditConditionPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RecordStepChange();
        }

        /// <summary>记录一次步骤内部修改：把上一次基线压入撤销栈</summary>
        private void RecordStepChange()
        {
            if (_suppressStepTracking || _editStep == null) return;
            _stepUndo.Add(_editBaseline ?? CloneStepPreservingUid(_editStep));
            if (_stepUndo.Count > MaxStepHistory)
                _stepUndo.RemoveAt(0);
            _stepRedo.Clear();
            _editBaseline = CloneStepPreservingUid(_editStep);
            RaiseUndoRedoCanExecuteChanged();
        }

        private void UndoStep()
        {
            if (_stepUndo.Count == 0 || _editStep == null) return;
            var target = _stepUndo[_stepUndo.Count - 1];
            _stepUndo.RemoveAt(_stepUndo.Count - 1);
            _stepRedo.Add(CloneStepPreservingUid(_editStep));
            RestoreStepState(target);
            RaiseUndoRedoCanExecuteChanged();
        }

        private void RedoStep()
        {
            if (_stepRedo.Count == 0 || _editStep == null) return;
            var target = _stepRedo[_stepRedo.Count - 1];
            _stepRedo.RemoveAt(_stepRedo.Count - 1);
            _stepUndo.Add(CloneStepPreservingUid(_editStep));
            RestoreStepState(target);
            RaiseUndoRedoCanExecuteChanged();
        }

        private static readonly HashSet<string> StepStateSkipProperties = new()
        {
            "Uid", "Type", "LineNo", "LastResult", "IsError", "ErrorMessage", "IsTrue",
            "SelfReferenceCount", "ConditionSummary",
        };

        /// <summary>把快照状态复制回当前步骤实例（保持实例与 Uid 不变，便于 UI 与引用继续有效）</summary>
        private void RestoreStepState(AutomationStepBase snapshot)
        {
            if (_editStep == null || snapshot == null) return;
            _suppressStepTracking = true;
            try
            {
                // 条件集合会被整体替换，先解除旧集合与条目的事件订阅
                if (_editStep.Conditions != null)
                {
                    _editStep.Conditions.CollectionChanged -= OnEditStepConditionsChanged;
                    foreach (var c in _editStep.Conditions)
                        c.PropertyChanged -= OnEditConditionPropertyChanged;
                }

                CopyStepState(snapshot, _editStep);

                if (_editStep.Conditions != null)
                {
                    _editStep.Conditions.CollectionChanged += OnEditStepConditionsChanged;
                    foreach (var c in _editStep.Conditions)
                        c.PropertyChanged += OnEditConditionPropertyChanged;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "RestoreStepState failed");
            }
            finally
            {
                _suppressStepTracking = false;
            }
            _editBaseline = CloneStepPreservingUid(_editStep);
        }

        private static void CopyStepState(AutomationStepBase source, AutomationStepBase target)
        {
            if (source == null || target == null) return;
            foreach (var p in source.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0) continue;
                if (StepStateSkipProperties.Contains(p.Name)) continue;
                try
                {
                    object value = p.Name == "Conditions" ? CloneConditions(source.Conditions) : p.GetValue(source);
                    p.SetValue(target, value);
                }
                catch
                {
                    // 个别只读/特殊属性忽略
                }
            }
        }

        #endregion

        #endregion

        #region 步骤增删

        private bool CanAlertStep()
        {
            return !_isRunning;
        }

        private bool CanPasteStep()
        {
            return CanAlertStep() && PasteSteps != null && PasteSteps.Count > 0;
        }

        /// <summary>
        /// 确保用户已登录，未登录时弹出登录窗口
        /// </summary>
        /// <returns>登录成功返回true，取消或失败返回false</returns>
        private bool EnsureLoggedIn()
        {
            if (SingletonLocator.UserService.CurrentUser != null)
                return true;

            var loginVm = Ioc.Default.GetRequiredService<LoginViewModel>();
            var loginWindow = new WindowLogin(loginVm);
            loginWindow.Owner = Application.Current.MainWindow;
            var result = loginWindow.ShowDialog();

            if (result == true)
            {
                SingletonLocator.Main.RefreshLoginState();
            }
            return result == true;
        }

        private void AddStep(object parameter)
        {
            if (!EnsureLoggedIn()) return;

            AutomationStepBase step;
            if (parameter is string param)
            {
                AutomationStepBases ??= [];
                // 烧录配置步骤全局仅允许一个
                if (param == "BurnInConfig" && AutomationStepBases.Any(t => t.Type == StepType.BurnInConfig))
                {
                    WindowAsyncPopup.Show(
                        LanguageService.GetLocalizedString("BurnIn_OnlyOneAllowed"),
                        LanguageService.GetLocalizedString("Warning"),
                        PopupButtons.OK, MessageBoxImage.Warning);
                    return;
                }
                step = param switch
                {
                    "ClickImage" => new ClickImageStep($"ClickImage_{AutomationStepBases.Count(t => t.Type == StepType.ClickImage) + 1}"),
                    "FindImage" => new FindImageStep($"FindImage_{AutomationStepBases.Count(t => t.Type == StepType.FindImage) + 1}"),
                    "ClickImages" => new ClickImagesStep($"ClickImages_{AutomationStepBases.Count(t => t.Type == StepType.ClickImages) + 1}"),
                    "FindImages" => new FindImagesStep($"FindImages_{AutomationStepBases.Count(t => t.Type == StepType.FindImages) + 1}"),
                    "TypeText" => new TypeTextStep($"TypeText_{AutomationStepBases.Count(t => t.Type == StepType.TypeText) + 1}"),
                    "TypeTextMore" => new TypeTextMoreStep($"TypeTextMore_{AutomationStepBases.Count(t => t.Type == StepType.TypeTextMore) + 1}"),
                    "TypeTextFromFile" => new TypeTextFromFileStep($"TypeTextFromFile_{AutomationStepBases.Count(t => t.Type == StepType.TypeTextFromFile) + 1}"),
                    "Popup" => new PopupStep($"Popup_{AutomationStepBases.Count(t => t.Type == StepType.Popup) + 1}"),
                    "GetInput" => new GetInputStep($"GetInput_{AutomationStepBases.Count(t => t.Type == StepType.GetInput) + 1}"),
                    "MouseAction" => new MouseActionStep($"MouseAction_{AutomationStepBases.Count(t => t.Type == StepType.MouseAction) + 1}"),
                    "Statistics" => new StatisticsStep($"Statistics_{AutomationStepBases.Count(t => t.Type == StepType.Statistics) + 1}"),
                    "BurnInConfig" => new BurnInConfigStep(LanguageService.GetLocalizedString("BurnInConfig_StepName")),
                    "FocusWindow" => new FocusWindowStep($"FocusWindow_{AutomationStepBases.Count(t => t.Type == StepType.FocusWindow) + 1}"),
                    "Empty" => new EmptyStep($"Empty_{AutomationStepBases.Count(t => t.Type == StepType.Empty) + 1}"),
                    _ => new ClickImageStep($"ClickImage_{AutomationStepBases.Count(t => t.Type == StepType.ClickImage) + 1}"),
                };
                ApplyDefaultSettings(step);
                PushListUndoSnapshot();
                if (SelectedStep is AutomationStepBase automationStepBase)
                {
                    int index = AutomationStepBases.IndexOf(automationStepBase) + 1;
                    AutomationStepBases.Insert(index, step);
                }
                else
                {
                    AutomationStepBases.Add(step);
                }
            }
            else
            {
                step = new ClickImageStep();
                ApplyDefaultSettings(step);
                PushListUndoSnapshot();
                AutomationStepBases.Add(step);
            }
            SelectedStep = step;
        }

        /// <summary>
        /// 将设置中的默认参数应用到新建步骤
        /// </summary>
        private void ApplyDefaultSettings(AutomationStepBase step)
        {
            step.SelfReferenceLimit = _stepSettings.DefaultSelfReferenceLimit;
            step.WaitTime = _stepSettings.DefaultWaitTime;

            if (step is ImageRecognitionBase imageStep)
            {
                imageStep.SimilarityThreshold = _stepSettings.DefaultSimilarityThreshold;
            }
            if (step is ClickImageStep clickStep)
            {
                clickStep.Clicks = _stepSettings.DefaultClicks;
                clickStep.Timeout = _stepSettings.DefaultTimeout;
            }
            else if (step is FindImageStep findStep)
            {
                findStep.Timeout = _stepSettings.DefaultTimeout;
            }
            else if (step is GetInputStep getInputStep)
            {
                getInputStep.Timeout = _stepSettings.DefaultTimeout;
            }
        }


        private async void DelStep()
        {
            if (!EnsureLoggedIn()) return;
            await RemoveStepsAsync(confirm: true);
        }

        /// <summary>
        /// 核心删除逻辑，confirm=true 时弹出确认对话框
        /// </summary>
        private async Task RemoveStepsAsync(bool confirm)
        {
            try
            {
                if (SelectedSteps.Count == 0) return;

                // 检查待删除步骤是否被其他步骤引用
                var deletingUids = SelectedSteps.Select(s => s.Uid).ToHashSet();
                var referencingSteps = new List<string>();

                foreach (var step in AutomationStepBases)
                {
                    if (deletingUids.Contains(step.Uid)) continue; // 跳过待删除的步骤本身

                    bool references = false;
                    if (step.TrueGotoUid.HasValue && deletingUids.Contains(step.TrueGotoUid.Value))
                        references = true;
                    if (step.FalseGotoUid.HasValue && deletingUids.Contains(step.FalseGotoUid.Value))
                        references = true;
                    if (step.Conditions != null)
                    {
                        foreach (var cond in step.Conditions)
                        {
                            if (cond.StepUid.HasValue && deletingUids.Contains(cond.StepUid.Value))
                            {
                                references = true;
                                break;
                            }
                        }
                    }

                    if (references)
                        referencingSteps.Add($"{step.LineNo} - {step.Name}");
                }

                // 如果需要确认，弹出确认对话框
                if (confirm)
                {
                    string message = LanguageService.GetLocalizedString("Msg_ConfirmDeleteSteps");
                    if (referencingSteps.Count > 0)
                    {
                        message += "\n\n" + LanguageService.GetLocalizedString("Msg_DeleteReferenced") + "\n"
                            + string.Join("\n", referencingSteps);
                    }

                    var (_, popup) = WindowAsyncPopup.Show(message, LanguageService.GetLocalizedString("DeleteStepTitle"), PopupButtons.YesNo, MessageBoxImage.Warning);
                    var res = await popup;
                    if (res != PopupButton.YesValue)
                        return;
                }

                PushListUndoSnapshot();

                // 删除前清空引用
                foreach (var step in AutomationStepBases)
                {
                    if (deletingUids.Contains(step.Uid)) continue;
                    if (step.TrueGotoUid.HasValue && deletingUids.Contains(step.TrueGotoUid.Value))
                        step.TrueGotoUid = null;
                    if (step.FalseGotoUid.HasValue && deletingUids.Contains(step.FalseGotoUid.Value))
                        step.FalseGotoUid = null;
                    if (step.Conditions != null)
                    {
                        foreach (var cond in step.Conditions)
                        {
                            if (cond.StepUid.HasValue && deletingUids.Contains(cond.StepUid.Value))
                                cond.StepUid = null;
                        }
                    }
                }

                for (int i = SelectedSteps.Count - 1; i >= 0; i--)
                {
                    AutomationStepBases.Remove(SelectedSteps[i]);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "RemoveSteps error: ");
            }
        }

        private void UpStep()
        {
            if (!EnsureLoggedIn()) return;
            if (SelectedStep == null)
                return;
            if (SelectedStep.LineNo <= 1)
                return;
            PushListUndoSnapshot();
            AutomationStepBases.Move(SelectedStep.LineNo - 1, SelectedStep.LineNo - 2);
        }

        private void DownStep()
        {
            if (!EnsureLoggedIn()) return;
            if (SelectedStep == null)
                return;
            if (SelectedStep.LineNo >= AutomationStepBases.Count)
                return;
            PushListUndoSnapshot();
            AutomationStepBases.Move(SelectedStep.LineNo - 1, SelectedStep.LineNo);
        }

        private void SelectStep()
        {
            if (SelectedStep == null) return;
            if (SelectedSteps == null || SelectedSteps.Count == 0) return;
            foreach (var step in SelectedSteps)
            {
                step.IsNeed = !step.IsNeed;
            }
        }

        /// <summary>
        /// 插入步骤。
        /// </summary>
        /// <param name="cloneSteps">
        /// true：插入前深拷贝（用于粘贴，保证多次粘贴互相独立、Uid 唯一）；
        /// false：直接插入（用于加载文件，需保留原有 Uid 与跳转引用）。
        /// </param>
        public void InsertSteps(ObservableCollection<AutomationStepBase> steps, int index = -1, bool cloneSteps = true)
        {
            AutomationStepBases ??= [];
            if (steps == null || steps.Count == 0) return;

            // 烧录配置步骤全局仅允许一个：跳过多余的 BurnInConfigStep
            var toInsert = new List<AutomationStepBase>(steps);
            bool hasExistingBurnIn = AutomationStepBases.Any(s => s.Type == StepType.BurnInConfig);
            bool skipped = false;
            for (int i = toInsert.Count - 1; i >= 0; i--)
            {
                if (toInsert[i] is not BurnInConfigStep) continue;
                if (hasExistingBurnIn)
                {
                    toInsert.RemoveAt(i);
                    skipped = true;
                }
                else
                {
                    hasExistingBurnIn = true; // 保留第一个，后续同类型跳过
                }
            }
            // 保留第一个后若仍有多个（列表内重复），继续去重
            bool seen = false;
            for (int i = 0; i < toInsert.Count; i++)
            {
                if (toInsert[i] is not BurnInConfigStep) continue;
                if (seen) { toInsert.RemoveAt(i); i--; skipped = true; }
                else seen = true;
            }
            if (skipped)
            {
                WindowAsyncPopup.Show(
                    LanguageService.GetLocalizedString("BurnIn_OnlyOneAllowed"),
                    LanguageService.GetLocalizedString("Warning"),
                    PopupButtons.OK, MessageBoxImage.Warning);
            }
            if (toInsert.Count == 0) return;

            // 需要拷贝时统一深拷贝，避免粘贴后与原对象/剪贴板共享引用
            var finalSteps = cloneSteps ? toInsert.Select(CloneStepCore).ToList() : toInsert;

            if (index >= 0 && index < AutomationStepBases.Count)
            {
                for (int i = 0; i < finalSteps.Count; i++)
                {
                    var step = finalSteps[i];
                    if (!cloneSteps && AutomationStepBases.Contains(step))
                        step = CloneStepCore(step);
                    AutomationStepBases.Insert(index + i, step);
                }
            }
            else
            {
                foreach (var step in finalSteps)
                {
                    var target = (!cloneSteps && AutomationStepBases.Contains(step)) ? CloneStepCore(step) : step;
                    AutomationStepBases.Add(target);
                }
            }
        }

        private void CopyStep()
        {
            if (!EnsureLoggedIn()) return;
            CopySelectedStepsToClipboard();
        }

        /// <summary>把选中步骤深拷贝到剪贴板（PasteSteps）；无选中项返回 false</summary>
        private bool CopySelectedStepsToClipboard()
        {
            PasteSteps ??= [];

            if (SelectedSteps == null || SelectedSteps.Count == 0)
            {
                PasteSteps.Clear();
                PasteStepCommand.RaiseCanExecuteChanged();
                return false;
            }

            PasteSteps.Clear();
            // 使用统一深拷贝：补齐 Clone() 未复制的条件判断等基类状态，且生成新 Uid
            foreach (var step in SelectedSteps)
                PasteSteps.Add(CloneStepCore(step));

            PasteStepCommand.RaiseCanExecuteChanged();
            return PasteSteps.Count > 0;
        }

        private async void CutStep()
        {
            if (!EnsureLoggedIn()) return;
            if (!CopySelectedStepsToClipboard()) return;
            await RemoveStepsAsync(confirm: false);
        }

        private void PasteStep()
        {
            if (!EnsureLoggedIn()) return;
            if (PasteSteps == null || PasteSteps.Count == 0) return;

            int index = SelectedStep != null ? SelectedStep.LineNo - 1 : -1;
            PushListUndoSnapshot();
            InsertSteps(PasteSteps, index, cloneSteps: true);
            PasteStepCommand.RaiseCanExecuteChanged();
        }
        #endregion

        #region 步骤执行

        private void Stop()
        {
            logger.Info("Stop Auto");
            StopSignal = true;
            _cts?.Cancel();
        }

        private bool CanRun()
        {
            return AutomationStepBases != null && AutomationStepBases.Count > 0 && !_isRunning;
        }

        private void PreRun()
        {
            // 重置停止信号和暂停状态
            StopSignal = false;
            IsPaused = false;
            // 初始化自动化引擎
            Autogui.StartAuto();
            // 清空执行上下文并启动总计时
            _executionContext.Clear();
            _executionContext.StartTimer();
            if (_stepSettings.MinimizeOnRun)
                Application.Current.MainWindow.WindowState = WindowState.Minimized;
            foreach (var step in AutomationStepBases)
            {
                // 重置自引用计数器
                step.SelfReferenceCount = 0;
                step.ErrorType = StepErrorType.None;
                step.LastResult = null;
                // 重置统计步骤的条件计数
                if (step is StatisticsStep statisticsStep)
                    statisticsStep.ResetCounters();
                if (step is TypeTextMoreStep textMoreStep)
                {
                    if (textMoreStep.ReloadText)
                        textMoreStep.Reload();
                }
                else if (step is TypeTextFromFileStep textFromFileStep)
                {
                    if (textFromFileStep.ReloadIndex)
                        textFromFileStep.Index = 0;
                }
            }
        }

        /// <summary>
        /// 启动自动化运行，将耗时任务移至后台线程
        /// </summary>
        public async void Run()
        {
            // 烧录完成判定的运行时状态（声明于 try 外，供 finally 访问）：
            // burnFinishStepUid = 配置的烧录完成步骤；burnCycleStart = 本轮烧录起始时间；
            // burnJudged = 本次运行是否至少判定过一次
            Guid? burnFinishStepUid = (AutomationStepBases?.FirstOrDefault(s => s is BurnInConfigStep)
                as BurnInConfigStep)?.Config?.BurnFinishStepUid;
            DateTime? burnCycleStart = null;
            bool burnJudged = false;

            try
            {
                // 运行前确认
                if (_stepSettings.ConfirmBeforeRun)
                {
                    var (_, confirmTask) = WindowAsyncPopup.Show(
                        LanguageService.GetLocalizedString("Msg_ConfirmRun"), LanguageService.GetLocalizedString("Question"),
                        PopupButtons.YesNo, MessageBoxImage.Question);
                    var confirmResult = await confirmTask;
                    if (confirmResult != PopupButton.YesValue)
                        return;
                }

                PreRun();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                logger.Info("Start Auto");

                for (int i = 0; i < AutomationStepBases.Count; i++)
                {
                    // 在每个步骤开始前检查停止信号和取消标志
                    token.ThrowIfCancellationRequested();
                    if (StopSignal || i < 0)
                    {
                        break;
                    }

                    // 暂停等待：处于暂停状态时在每个步骤间等待，直到恢复或停止
                    while (IsPaused && !StopSignal)
                    {
                        token.ThrowIfCancellationRequested();
                        await Task.Delay(100, token);
                    }
                    if (StopSignal)
                        break;

                    var step = AutomationStepBases[i];

                    // StepReached 模式：检查是否有弹窗需要在到达此步骤时关闭
                    CheckAndCloseStepReachedPopups(step.Uid);

                    if (!step.IsNeed)
                        continue;

                    try
                    {
                        SelectedStep = step;

                        // 记录当前正在执行的步骤（供统计窗口展示）
                        _executionContext.RunningStepUid = step.Uid;

                        // 计时执行
                        var sw = Stopwatch.StartNew();
                        await step.RunAsync(token);
                        sw.Stop();

                        step.IsError = false;
                        step.ErrorType = StepErrorType.None;

                        // 构建执行结果
                        var result = step.LastResult ?? new StepExecutionResult();
                        result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
                        result.IsTrue = step.IsTrue;
                        result.ExecutedAt = DateTime.Now;
                        step.LastResult = result;

                        // 存入执行上下文
                        _executionContext.SetResult(step.LineNo, result);
                        _executionContext.SetResultByUid(step.Uid, result);
                        _executionContext.CurrentStepUid = step.Uid;

                        // 统计步骤的条件计数累计
                        foreach (var s in AutomationStepBases)
                        {
                            if (s is StatisticsStep statsStep)
                                statsStep.UpdateConditionCounts(_executionContext, result);
                        }

                        // 自定义条件判断（统计步骤不参与条件判断）
                        if (step is not StatisticsStep
                            && step.ConditionMode == ConditionMode.Custom && step.Conditions.Count > 0)
                        {
                            step.IsTrue = ConditionEvaluator.Evaluate(step.Conditions, _executionContext, result);
                        }

                        // 通用日志记录
                        if (step.EnableLog)
                        {
                            string fileName = Path.GetFileNameWithoutExtension(SingletonLocator.Main.StepFilePath ?? "unsaved");
                            string logContent = $"[Result:{step.IsTrue}] [Time:{result.ExecutionTimeMs:F0}ms]";
                            if (result.Similarity >= 0)
                                logContent += $" [Similarity:{result.Similarity:F3}]";
                            ExecutionLogService.Log(step.Uid, fileName, step.Name, logContent, result.OCRText);
                        }

                        // 烧录完成判定：当前步骤为配置的烧录完成步骤时立即判定并记录
                        // （循环流程中每轮都会触发，每轮独立计时）
                        if (burnFinishStepUid.HasValue && step.Uid == burnFinishStepUid.Value
                            && BurnInService.CurrentSession != null)
                        {
                            burnCycleStart ??= BurnInService.CurrentSession.StartedAt;
                            try
                            {
                                JudgeAndRecordBurnIn(BurnInService.CurrentSession, step, burnCycleStart.Value);
                                burnJudged = true;
                                burnCycleStart = DateTime.Now; // 下一轮烧录的起始时间
                            }
                            catch (Exception burnEx)
                            {
                                logger.Warn(burnEx, "JudgeAndRecordBurnIn failed");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        logger.Info("Operation Canceled");
                        step.ErrorType = StepErrorType.CancelledByUser;
                        break;
                    }
                    catch (Exception ex)
                    {
                        // 记录单个步骤的错误，防止整个流程崩溃
                        logger.Warn(ex, "Step \"{0}\" execution Failed:", step.Name);

                        step.IsError = true;
                        step.ErrorMessage = ex.Message;
                        step.ErrorType = InferErrorType(ex);
                        if (_stepSettings.ShowErrorPopup)
                        {
                            var (_, popupTask) = WindowAsyncPopup.Show(
                                $"{LanguageService.GetLocalizedString("Step")}{step.Name}{LanguageService.GetLocalizedString("ExecutionFailed")}{step.ErrorMessage}", "Error",
                                PopupButtons.YesCancel, MessageBoxImage.Error);
                            await popupTask;
                        }

                        if (!step.FalseGotoUid.HasValue || step.FalseGotoUid.Value == Guid.Empty)
                        {
                            StopSignal = true;
                            break;
                        }
                        else
                        {
                            step.IsTrue = false;
                        }
                    }

                    // 确定下一个执行索引（通过 Uid 查找目标步骤）
                    Guid? targetUid = step.IsTrue ? step.TrueGotoUid : step.FalseGotoUid;

                    if (targetUid.HasValue && targetUid.Value != Guid.Empty)
                    {
                        int nextIndex = FindIndexByUid(targetUid.Value);

                        if (nextIndex < 0)
                        {
                            // 目标步骤不存在（可能被删除），继续下一步
                            step.SelfReferenceCount = 0;
                        }
                        // 自引用检测
                        else if (nextIndex == i) // 指向自身
                        {
                            step.SelfReferenceCount++;
                            // SelfReferenceLimit: -1=无限制, 0=禁止自引用, >0=限制次数
                            if (step.SelfReferenceLimit >= 0 && step.SelfReferenceCount >= step.SelfReferenceLimit)
                            {
                                // 达到自引用上限：自身结果强制为 false
                                step.IsTrue = false;
                                step.ErrorMessage = string.Format(LanguageService.GetLocalizedString("Msg_SelfReferenceLimit"), step.Name, step.SelfReferenceLimit);
                                step.SelfReferenceCount = 0;

                                // 按 false 结果重新解析跳转目标
                                if (step.FalseGotoUid.HasValue && step.FalseGotoUid.Value != Guid.Empty)
                                {
                                    int falseIndex = FindIndexByUid(step.FalseGotoUid.Value);
                                    if (falseIndex >= 0 && falseIndex != i)
                                    {
                                        i = falseIndex - 1; // -1 因为 for 循环会 i++
                                    }
                                    // 目标仍指向自身时不跳转，继续下一步
                                }
                                // FalseGoto 为空时不跳转，继续下一步
                            }
                            else
                            {
                                i = nextIndex - 1; // -1 因为 for 循环会 i++
                            }
                        }
                        else
                        {
                            step.SelfReferenceCount = 0; // 非自引用时重置
                            i = nextIndex - 1;
                        }
                    }
                    // targetUid 为空时不修改 i，for 循环 i++ 自然进入下一步
                }
                StopSignal = true;
                _executionContext.StopTimer();
                Views.WindowStatistics.CloseAllOnStop();
                Application.Current.MainWindow.WindowState = WindowState.Normal;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Application.Current.MainWindow.Activate();
                });
                logger.Info("Auto Finished");
            }
            catch (OperationCanceledException)
            {
                logger.Info("Operation Canceled");
                StopSignal = true;
                _executionContext.StopTimer();
                Views.WindowStatistics.CloseAllOnStop();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Run Error: ");
            }
            finally
            {
                // 烧录统计：运行结束但从未到达烧录完成步骤时，补记一条未完成烧录供追溯
                try
                {
                    if (BurnInService.CurrentSession != null && !burnJudged)
                        RecordBurnInNotFinished(BurnInService.CurrentSession,
                            burnCycleStart ?? BurnInService.CurrentSession.StartedAt);
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "RecordBurnInNotFinished failed");
                }
                BurnInService.EndSession();
            }
        }

        /// <summary>
        /// 根据异常类型推断 ErrorType
        /// </summary>
        private static StepErrorType InferErrorType(Exception ex)
        {
            return ex switch
            {
                FileNotFoundException => StepErrorType.FileNotFound,
                OperationCanceledException => StepErrorType.CancelledByUser,
                IndexOutOfRangeException => StepErrorType.IndexOutOfRange,
                InvalidOperationException => StepErrorType.Unknown,
                TimeoutException => StepErrorType.ExecutionTimeout,
                _ => StepErrorType.Unknown
            };
        }

        /// <summary>
        /// 烧录完成判定并记录（执行到烧录完成步骤时即时调用）。
        /// 判定方式与完成步骤类型互斥：
        /// 完成步骤为获取输入→按其识别文本+关键字判定；
        /// 其他类型步骤→立即截屏与判据模板比对，一次得出 良品/不良品/烧录失败。
        /// cycleStart 为本轮烧录的起始时间（循环流程中每轮独立计时）。
        /// </summary>
        private void JudgeAndRecordBurnIn(BurnInSession session, AutomationStepBase finishStep, DateTime cycleStart)
        {
            var finishedAt = DateTime.Now;
            string stepFileName = Path.GetFileNameWithoutExtension(
                SingletonLocator.Main.StepFilePath ?? "unsaved");

            var configStep = AutomationStepBases?.FirstOrDefault(s => s is BurnInConfigStep) as BurnInConfigStep;
            var config = configStep?.Config;
            // 判定方式由完成步骤类型决定：获取输入→关键字模式；其他→图像模式
            bool keywordMode = finishStep is GetInputStep;
            bool hasTemplate = config?.HasAnyTemplate == true;
            if (config == null || (!keywordMode && !hasTemplate))
            {
                BurnInService.Record(new BurnInRecord
                {
                    OrderNo = session.OrderNo,
                    Operator = session.Operator,
                    PartName = session.PartName,
                    StepFileName = stepFileName,
                    BurnStartedAt = cycleStart,
                    BurnFinishedAt = finishedAt,
                    BurnDurationMs = (finishedAt - cycleStart).TotalMilliseconds,
                    IsGood = false,
                    IsBurnFailed = true,
                    Remark = "NotConfigured",
                });
                return;
            }

            string ocrText = null;
            bool goodImageHit = false, badImageHit = false, failImageHit = false;
            if (keywordMode)
            {
                // 关键字模式：只用完成步骤的识别文本（判据模板不参与判定）
                ocrText = finishStep.LastResult?.OCRText;
            }
            else
            {
                // 图像模式：立即截屏与判据模板逐一比对（关键字不参与判定）
                goodImageHit = MatchTemplate(config.GoodTemplateName, config.SimilarityThreshold);
                badImageHit = MatchTemplate(config.BadTemplateName, config.SimilarityThreshold);
                failImageHit = MatchTemplate(config.FailTemplateName, config.SimilarityThreshold);
            }

            var (result, goodText, badText, remark, screenshotPath) =
                BurnInService.EvaluateAndCapture(config, ocrText, goodImageHit, badImageHit, failImageHit, session.OrderNo);

            BurnInService.Record(new BurnInRecord
            {
                OrderNo = session.OrderNo,
                Operator = session.Operator,
                PartName = session.PartName,
                StepFileName = stepFileName,
                BurnStartedAt = cycleStart,
                BurnFinishedAt = finishedAt,
                BurnDurationMs = (finishedAt - cycleStart).TotalMilliseconds,
                IsGood = result == BurnResult.Good,
                IsBurnFailed = result == BurnResult.BurnFailed,
                GoodText = goodText,
                BadText = badText,
                ScreenshotPath = screenshotPath,
                Remark = remark,
            });
        }

        /// <summary>
        /// 运行结束（正常/取消/异常）但从未到达烧录完成步骤时，
        /// 补记一条未完成烧录供追溯（仅当存在烧录配置步骤时）
        /// </summary>
        private void RecordBurnInNotFinished(BurnInSession session, DateTime cycleStart)
        {
            var configStep = AutomationStepBases?.FirstOrDefault(s => s is BurnInConfigStep) as BurnInConfigStep;
            if (configStep == null) return;

            var finishedAt = DateTime.Now;
            BurnInService.Record(new BurnInRecord
            {
                OrderNo = session.OrderNo,
                Operator = session.Operator,
                PartName = session.PartName,
                StepFileName = Path.GetFileNameWithoutExtension(
                    SingletonLocator.Main.StepFilePath ?? "unsaved"),
                BurnStartedAt = cycleStart,
                BurnFinishedAt = finishedAt,
                BurnDurationMs = (finishedAt - cycleStart).TotalMilliseconds,
                IsGood = false,
                IsBurnFailed = true,
                Remark = "NotFinished",
            });
        }

        private AutomationStepBase FindStepByUid(Guid uid)
        {
            if (AutomationStepBases == null) return null;
            foreach (var s in AutomationStepBases)
                if (s.Uid == uid) return s;
            return null;
        }

        /// <summary>
        /// 截屏并与判据模板图比对（单次匹配，不重试）；模板未配置或文件缺失返回 false
        /// </summary>
        private static bool MatchTemplate(string templateName, double threshold)
        {
            if (string.IsNullOrEmpty(templateName)) return false;
            try
            {
                string workDir = SingletonLocator.Main?.StepImageWorkDir;
                if (string.IsNullOrEmpty(workDir)) return false;
                string fullPath = Path.Combine(workDir,
                    templateName.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath)) return false;

                using (var template = new System.Drawing.Bitmap(fullPath))
                {
                    // timeout=0 单次匹配；未命中时 FindImageOnScreen 抛异常，同样视为未命中
                    var rect = Autogui.FindImageOnScreen(template, threshold, gaptime: 0, timeout: 0);
                    return !rect.IsEmpty;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "BurnIn MatchTemplate failed");
                return false;
            }
        }

        /// <summary>
        /// StepReached 模式：检查所有 PopupStep，如果有活跃弹窗的目标步骤是当前步骤，则关闭弹窗
        /// </summary>
        private void CheckAndCloseStepReachedPopups(Guid currentStepUid)
        {
            foreach (var s in AutomationStepBases)
            {
                if (s is PopupStep popupStep
                    && popupStep.CloseMode.HasFlag(PopupCloseMode.StepReached)
                    && popupStep.CloseOnStepUid == currentStepUid
                    && popupStep.ActivePopupWindow != null)
                {
                    var defaultResult = popupStep.PopupButtons.DefaultButton?.Value ?? string.Empty;
                    popupStep.ActivePopupWindow.CloseWithResult(defaultResult);
                    popupStep.ActivePopupWindow = null;
                }
            }
        }

        #endregion

        /// <summary>
        /// 根据 Uid 查找步骤在集合中的索引，找不到返回 -1
        /// </summary>
        public int FindIndexByUid(Guid uid)
        {
            if (AutomationStepBases == null) return -1;
            for (int i = 0; i < AutomationStepBases.Count; i++)
            {
                if (AutomationStepBases[i].Uid == uid) return i;
            }
            return -1;
        }

    }
}
