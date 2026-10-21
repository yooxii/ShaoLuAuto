using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Converters;
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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ShaoLu.Viewmodels
{
    public class StepsViewModel : ObservableObject
    {
        private readonly static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        private CancellationTokenSource _cts;

        /// <summary>多线程：本次运行创建的子线程（主流程结束时据此等待或取消）</summary>
        private readonly List<SubThreadRunner> _subThreadRunners = new List<SubThreadRunner>();
        private readonly object _subThreadLock = new object();

        /// <summary>待处理中断（子线程发出，主线程在每个步骤开始前消费）</summary>
        private readonly object _interruptLock = new object();
        private Guid? _pendingInterruptUid;
        private readonly StepSettingsModel _stepSettings = SingletonLocator.Settings.Step;
        private readonly StepExecutionContext _executionContext = StepExecutionContext.Instance;

        /// <summary>
        /// 运行中标志（0=空闲，1=已有一个运行循环在跑）。
        /// 用 Interlocked 原子在 Run() 入口占用、在 finally 释放，保证"停止→立刻再点开始"
        /// 不会在旧循环还没退出时插入第二套并行循环（S8）。
        /// </summary>
        private int _runActive;

        /// <summary>本次运行内的跳转链访问计数（Q3 环形跳转检测用，每次运行前清空）</summary>
        private readonly Dictionary<Guid, int> _jumpVisits = new();

        #region 属性

        private volatile bool _stopSignal = false;
        /// <summary>
        /// 停止请求：只表达"希望当前运行停下来"，不再反向修改 IsRunning（S8）。
        /// 运行态由 IsRunning 表示，且只在 Run() 的 finally 里复位；
        /// 否则停止瞬间 IsRunning 就变 false，用户立刻点开始就会叠加出第二套循环。
        /// </summary>
        public bool StopSignal { get => _stopSignal; set => _stopSignal = value; }

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

        // M22：属性变更合并提交（防抖）——避免拖动滑块时每帧深拷贝整步并塞满撤销栈
        private const int StepChangeMergeMs = 300;
        private DispatcherTimer _stepChangeTimer;
        private bool _hasPendingStepChange;



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
        /// 快照用 JSON 选项：多态基类转换器 + 不缩进。
        /// 快照只存在于内存（步骤对象 → JSON 字符串 → 步骤对象），不落盘。
        /// </summary>
        private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new AutomationStepBaseJsonConverter() },
        };

        /// <summary>
        /// 步骤深拷贝（M15）：走 JSON 序列化往返，派生类型的所有集合/配置都真正深拷贝。
        /// 原来的做法是 Clone() + 手工补基类字段，只有 Conditions 被深拷贝，
        /// 其余集合（统计项、自选步骤 Uid、输入内容列表、弹窗按钮、多线程配置等）都只是赋引用，
        /// 撤销/恢复后与原步骤共享同一批子对象，改一处会连带改另一处。
        /// 另外走 JSON 也避开了各步骤 Clone() 里的 PNG 落盘（M22：克隆/快照是高频路径）。
        /// 说明：裁剪图等 [JsonIgnore] 运行时缓存不参与深拷贝，恢复后按磁盘文件懒加载。
        /// </summary>
        private static AutomationStepBase DeepCopyStep(AutomationStepBase step)
        {
            if (step == null) return null;
            try
            {
                string json = JsonSerializer.Serialize(step, SnapshotJsonOptions);
                var copy = JsonSerializer.Deserialize<AutomationStepBase>(json, SnapshotJsonOptions);
                if (copy != null) return copy;
                logger.Warn("DeepCopyStep 反序列化为空，退化为显式深拷贝：{0}", step.Name);
            }
            catch (Exception ex)
            {
                // 某类型无法 JSON 序列化时退化（Clone + 基类字段 + 条件集合），并留下告警便于排查
                logger.Warn(ex, "DeepCopyStep JSON 往返失败，退化为显式深拷贝：{0}", step.Name);
            }
            return CloneStepFallback(step);
        }

        /// <summary>
        /// 退化深拷贝：JSON 路线不可用时使用。覆盖基类状态与条件集合，
        /// 派生类型自身的集合依赖各自的 Clone()（可能仍共享引用，故仅在异常路径使用）。
        /// </summary>
        private static AutomationStepBase CloneStepFallback(AutomationStepBase step)
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
            var clone = DeepCopyStep(step);
            if (clone != null) clone.Uid = step.Uid;
            return clone;
        }

        /// <summary>深拷贝并生成新 Uid（复制/粘贴/插入用，保证多次粘贴互相独立）</summary>
        private static AutomationStepBase CloneStepWithNewUid(AutomationStepBase step)
        {
            var clone = DeepCopyStep(step);
            if (clone != null) clone.Uid = Guid.NewGuid();
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
            // 本步骤的编辑历史即将失效：直接丢弃未提交的合并窗口（修改已写入步骤对象本身）
            _stepChangeTimer?.Stop();
            _hasPendingStepChange = false;

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

        /// <summary>
        /// 记录一次步骤内部修改：把上一次基线压入撤销栈。
        /// M22：滑块拖动 / 连续输入会在极短时间内产生大量 PropertyChanged，
        /// 每次都深拷贝整步会卡顿并把撤销栈塞满，这里只在内存里标记"有待提交的修改"，
        /// 用 DispatcherTimer 短延迟合并，最后只提交一条撤销记录（一次连续编辑 = 一条记录，
        /// 撤销/重做的语义不变）。
        /// </summary>
        private void RecordStepChange()
        {
            if (_suppressStepTracking || _editStep == null) return;
            _hasPendingStepChange = true;
            var timer = EnsureStepChangeTimer();
            if (timer == null)
            {
                // 无 UI 上下文（单元测试等）时立即提交，保持原来的同步语义
                CommitPendingStepChange();
                return;
            }
            // 重新计时：合并窗口从最后一次修改算起
            timer.Stop();
            timer.Start();
        }

        /// <summary>取（或创建）合并提交定时器；须在 UI 线程创建</summary>
        private DispatcherTimer EnsureStepChangeTimer()
        {
            if (_stepChangeTimer != null) return _stepChangeTimer;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return null;
            var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(StepChangeMergeMs),
            };
            timer.Tick += (s, e) => CommitPendingStepChange();
            _stepChangeTimer = timer;
            return timer;
        }

        /// <summary>
        /// 把合并窗口内的连续修改一次性提交为一条撤销记录。
        /// _editBaseline 只在提交时更新，因此合并期间它始终是"本次修改前"的状态。
        /// </summary>
        private void CommitPendingStepChange()
        {
            _stepChangeTimer?.Stop();
            if (!_hasPendingStepChange) return;
            _hasPendingStepChange = false;
            if (_editStep == null) return;

            _stepUndo.Add(_editBaseline ?? CloneStepPreservingUid(_editStep));
            if (_stepUndo.Count > MaxStepHistory)
                _stepUndo.RemoveAt(0);
            _stepRedo.Clear();
            _editBaseline = CloneStepPreservingUid(_editStep);
            RaiseUndoRedoCanExecuteChanged();
        }

        private void UndoStep()
        {
            CommitPendingStepChange();
            if (_stepUndo.Count == 0 || _editStep == null) return;
            var target = _stepUndo[_stepUndo.Count - 1];
            _stepUndo.RemoveAt(_stepUndo.Count - 1);
            _stepRedo.Add(CloneStepPreservingUid(_editStep));
            RestoreStepState(target);
            RaiseUndoRedoCanExecuteChanged();
        }

        private void RedoStep()
        {
            CommitPendingStepChange();
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
            // 裁剪图是 [JsonIgnore] 运行时缓存：快照里没有它，赋值会触发磁盘读 + PNG 回写（M22），
            // 且恢复后本来也只能按同名文件懒加载，故跳过该属性
            "CroppedImg",
        };

        /// <summary>
        /// 把快照状态复制回当前步骤实例（保持实例与 Uid 不变，便于 UI 与引用继续有效）。
        /// M15：先把快照整体深拷贝一份，再把拷贝的属性引用交给当前实例，
        /// 这样当前步骤与撤销栈里保存的快照不会共享任何集合/子对象。
        /// </summary>
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

                var fresh = DeepCopyStep(snapshot) ?? snapshot;
                CopyStepState(fresh, _editStep);

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

        /// <summary>
        /// 按属性名逐个复制状态。source 必须是"本次恢复专用"的深拷贝副本（见 RestoreStepState），
        /// 因此这里直接按引用赋值：集合与嵌套配置整体转移给目标实例，不会再与原快照共享。
        /// </summary>
        private static void CopyStepState(AutomationStepBase source, AutomationStepBase target)
        {
            if (source == null || target == null) return;
            foreach (var p in source.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0) continue;
                if (StepStateSkipProperties.Contains(p.Name)) continue;
                try
                {
                    object value = p.GetValue(source);
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
                    "AcbelUpload" => new AcbelUploadStep($"AcbelUpload_{AutomationStepBases.Count(t => t.Type == StepType.AcbelUpload) + 1}"),
                    "GetInput" => new GetInputStep($"GetInput_{AutomationStepBases.Count(t => t.Type == StepType.GetInput) + 1}"),
                    "MouseAction" => new MouseActionStep($"MouseAction_{AutomationStepBases.Count(t => t.Type == StepType.MouseAction) + 1}"),
                    "Statistics" => new StatisticsStep($"Statistics_{AutomationStepBases.Count(t => t.Type == StepType.Statistics) + 1}"),
                    "BurnInConfig" => new BurnInConfigStep(LanguageService.GetLocalizedString("BurnInConfig_StepName")),
                    "FocusWindow" => new FocusWindowStep($"FocusWindow_{AutomationStepBases.Count(t => t.Type == StepType.FocusWindow) + 1}"),
                    "MultiThread" => new MultiThreadStartStep($"MultiThread_{AutomationStepBases.Count(t => t.Type == StepType.MultiThread) + 1}"),
                    "MultiThreadEnd" => new MultiThreadEndStep($"MultiThreadEnd_{AutomationStepBases.Count(t => t.Type == StepType.MultiThreadEnd) + 1}"),
                    "SendSignal" => new SendSignalStep($"SendSignal_{AutomationStepBases.Count(t => t.Type == StepType.SendSignal) + 1}"),
                    "ReceiveSignal" => new ReceiveSignalStep($"ReceiveSignal_{AutomationStepBases.Count(t => t.Type == StepType.ReceiveSignal) + 1}"),
                    "Interrupt" => new InterruptStep($"Interrupt_{AutomationStepBases.Count(t => t.Type == StepType.Interrupt) + 1}"),
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
        /// 统一维护"对某些步骤 Uid 的引用"（M5），删除步骤时用于检查与清理，避免漏字段。
        /// 覆盖代码中全部跨步骤 Guid 引用点：
        ///  - 跳转：TrueGotoUid / FalseGotoUid
        ///  - 条件行引用：步骤自身 Conditions 与统计项内的 Conditions（StepCondition.StepUid）
        ///  - 弹窗步骤的"到达此步骤时关闭"目标：PopupStep.CloseOnStepUid
        ///  - 烧录配置步骤的"烧录完成步骤"：BurnInConfigStep.Config.BurnFinishStepUid
        ///  - 统计步骤自选范围：StatisticsItemConfig.CustomStepUids
        ///  - 多线程开始步骤的区域结束步骤：MultiThreadStartStep.RangeEndUid
        /// 说明：StepCondition.ParentStepUid 是"条件归属的步骤"，随所属步骤一起删除，不属于跨步骤引用。
        /// </summary>
        /// <param name="deletingUids">待删除步骤的 Uid 集合</param>
        /// <param name="clear">true=清空引用；false=只收集引用者（用于删除确认提示）</param>
        /// <returns>引用了待删除步骤的其他步骤描述（"行号 - 名称"）</returns>
        private List<string> ProcessReferencesTo(HashSet<Guid> deletingUids, bool clear)
        {
            var referencingSteps = new List<string>();
            if (deletingUids == null || deletingUids.Count == 0 || AutomationStepBases == null)
                return referencingSteps;

            bool IsDeleted(Guid? uid) => uid.HasValue && uid.Value != Guid.Empty && deletingUids.Contains(uid.Value);

            foreach (var step in AutomationStepBases)
            {
                if (deletingUids.Contains(step.Uid)) continue; // 跳过待删除的步骤本身

                bool references = false;

                // 跳转引用
                if (IsDeleted(step.TrueGotoUid))
                {
                    references = true;
                    if (clear) step.TrueGotoUid = null;
                }
                if (IsDeleted(step.FalseGotoUid))
                {
                    references = true;
                    if (clear) step.FalseGotoUid = null;
                }

                // 步骤自身的条件行引用
                void ProcessConditions(IEnumerable<StepCondition> conditions, ref bool referenced)
                {
                    if (conditions == null) return;
                    foreach (var cond in conditions)
                    {
                        if (cond == null || !IsDeleted(cond.StepUid)) continue;
                        referenced = true;
                        if (clear) cond.StepUid = null;
                    }
                }
                ProcessConditions(step.Conditions, ref references);

                // 弹窗步骤：到达目标步骤时关闭弹窗
                if (step is PopupStep popupStep && IsDeleted(popupStep.CloseOnStepUid))
                {
                    references = true;
                    if (clear) popupStep.CloseOnStepUid = null;
                }

                // 烧录配置步骤：烧录完成步骤
                if (step is BurnInConfigStep burnStep && burnStep.Config != null
                    && IsDeleted(burnStep.Config.BurnFinishStepUid))
                {
                    references = true;
                    if (clear) burnStep.Config.BurnFinishStepUid = null;
                }

                // 多线程开始步骤：区域结束步骤（被删后区域失效，清空避免解析到错误步骤）
                if (step is MultiThreadStartStep multiThreadStep && IsDeleted(multiThreadStep.RangeEndUid))
                {
                    references = true;
                    if (clear) multiThreadStep.RangeEndUid = null;
                }

                // 统计步骤：自选步骤 Uid 列表与统计项内的条件行
                if (step is StatisticsStep statsStep)
                {
                    void ProcessStatisticsItem(StatisticsItemConfig item)
                    {
                        if (item == null) return;
                        ProcessConditions(item.Conditions, ref references);
                        if (item.CustomStepUids == null || item.CustomStepUids.Count == 0) return;
                        for (int i = item.CustomStepUids.Count - 1; i >= 0; i--)
                        {
                            if (!deletingUids.Contains(item.CustomStepUids[i])) continue;
                            references = true;
                            if (clear) item.CustomStepUids.RemoveAt(i);
                        }
                    }
                    foreach (var item in statsStep.StatisticsItems)
                        ProcessStatisticsItem(item);
                    foreach (var item in statsStep.CustomConditions)
                        ProcessStatisticsItem(item);
                }

                if (references)
                    referencingSteps.Add($"{step.LineNo} - {step.Name}");
            }
            return referencingSteps;
        }

        private async Task RemoveStepsAsync(bool confirm)
        {
            try
            {
                if (SelectedSteps.Count == 0) return;

                // 检查待删除步骤是否被其他步骤引用：与下面的清理复用同一处遍历，
                // 保证"提示里列出的引用点"和"实际清掉的引用点"完全一致（M5）
                var deletingUids = SelectedSteps.Select(s => s.Uid).ToHashSet();
                var referencingSteps = ProcessReferencesTo(deletingUids, clear: false);

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

                // 删除前清空全部跨步骤引用（跳转 / 条件 / 弹窗关闭目标 / 烧录完成步骤 / 多线程区域 / 统计自选步骤）
                ProcessReferencesTo(deletingUids, clear: true);

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
            MoveSelectedSteps(StepsOrderMove.BuildUp);
        }

        private void DownStep()
        {
            if (!EnsureLoggedIn()) return;
            MoveSelectedSteps(StepsOrderMove.BuildDown);
        }

        /// <summary>
        /// 统一上移 / 下移选中的步骤（支持多选）。plan 为纯函数：选中索引集合 + 当前数量 → 本次点击的移动序列。
        /// 每次点击每个选中步骤最多移动一位；无选中、或本次点击无处可移时不改列表也不记录撤销快照（与原有单步行为一致）。
        /// </summary>
        private void MoveSelectedSteps(Func<IEnumerable<int>, int, IReadOnlyList<StepsOrderMove.MoveStep>> plan)
        {
            var selectedIndices = GetSelectedStepIndices();
            if (selectedIndices.Count == 0) return;
            var moves = plan(selectedIndices, AutomationStepBases.Count);
            if (moves.Count == 0) return;
            PushListUndoSnapshot();
            foreach (var move in moves)
            {
                AutomationStepBases.Move(move.From, move.To);
            }
        }

        /// <summary>当前选中的步骤在 AutomationStepBases 中的索引（多选为多条；已不在列表中的忽略）。</summary>
        private HashSet<int> GetSelectedStepIndices()
        {
            var indices = new HashSet<int>();
            if (SelectedSteps != null)
            {
                foreach (var step in SelectedSteps)
                {
                    if (step == null) continue;
                    int index = AutomationStepBases.IndexOf(step);
                    if (index >= 0) indices.Add(index);
                }
            }
            if (SelectedStep != null)
            {
                int index = AutomationStepBases.IndexOf(SelectedStep);
                if (index >= 0) indices.Add(index);
            }
            return indices;
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
            var finalSteps = cloneSteps ? toInsert.Select(CloneStepWithNewUid).ToList() : toInsert;

            if (index >= 0 && index < AutomationStepBases.Count)
            {
                for (int i = 0; i < finalSteps.Count; i++)
                {
                    var step = finalSteps[i];
                    if (!cloneSteps && AutomationStepBases.Contains(step))
                        step = CloneStepWithNewUid(step);
                    AutomationStepBases.Insert(index + i, step);
                }
            }
            else
            {
                foreach (var step in finalSteps)
                {
                    var target = (!cloneSteps && AutomationStepBases.Contains(step)) ? CloneStepWithNewUid(step) : step;
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
                PasteSteps.Add(CloneStepWithNewUid(step));

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
            // 只发出"停止请求"：运行态（IsRunning）必须等旧循环真正退出后，
            // 在 Run() 的 finally 里复位，否则刚点停止就能再点开始，叠加出第二套循环（S8）
            StopSignal = true;
            _cts?.Cancel();
        }

        private bool CanRun()
        {
            // 只看运行态。StopSignal 只表示"停止请求"，不能当作可运行依据（S8）
            return AutomationStepBases != null && AutomationStepBases.Count > 0
                && !_isRunning
                && Volatile.Read(ref _runActive) == 0;
        }

        private void PreRun()
        {
            // 运行前把未提交的步骤编辑合并成一条撤销记录（M22）
            CommitPendingStepChange();
            // 重置停止信号和暂停状态
            StopSignal = false;
            IsPaused = false;
            // 重置跳转链（Q3 环形检测用，只统计本次运行）
            _jumpVisits.Clear();
            // 初始化自动化引擎
            Autogui.StartAuto();
            // 清空执行上下文并启动总计时
            _executionContext.Clear();
            _executionContext.StartTimer();
            // 多线程：清空上一轮登记的子线程上下文，避免残留数据混入统计
            StepExecutionContext.ClearChildContexts();
            // 多线程：清空信号中心与待处理中断，并注册主线程的中断处理器
            SignalCenter.Reset();
            lock (_interruptLock)
            {
                _pendingInterruptUid = null;
            }
            MultiThreadInterruptBridge.RegisterHandler(RequestInterrupt);
            // 多线程：清空上一次运行留下的子线程记录
            ClearSubThreadRunners();
            if (_stepSettings.MinimizeOnRun)
                Application.Current.MainWindow.WindowState = WindowState.Minimized;
            foreach (var step in AutomationStepBases)
            {
                // 重置自引用计数器
                step.SelfReferenceCount = 0;
                step.ErrorType = StepErrorType.None;
                step.LastResult = null;
                // 多线程：重置已建线程数
                if (step is MultiThreadStartStep multiThreadStart)
                    multiThreadStart.CreatedThreads = 0;
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
        /// 运行前确认：可等待的确认窗。确认期间禁用主窗口（等效模态，
        /// 但消息循环不被阻塞，界面照常重绘）。
        /// 确认期间 IsRunning 已为 true，因此再点"开始"会被 CanRun 拦下（S8）。
        /// </summary>
        private async Task<bool> ConfirmRunAsync()
        {
            var mainWindow = Application.Current?.MainWindow;
            var (popupWindow, confirmTask) = WindowAsyncPopup.Show(
                LanguageService.GetLocalizedString("Msg_ConfirmRun"), LanguageService.GetLocalizedString("Question"),
                PopupButtons.YesNo, MessageBoxImage.Question);

            bool ownerDisabled = false;
            try
            {
                if (mainWindow != null && popupWindow != null)
                {
                    // 确认窗口非模态，这里用"禁用主窗口"达到模态效果（不阻塞 WPF 线程）
                    mainWindow.IsEnabled = false;
                    ownerDisabled = true;
                }
                var confirmResult = await confirmTask;
                return confirmResult == PopupButton.YesValue;
            }
            finally
            {
                if (ownerDisabled && mainWindow != null)
                    mainWindow.IsEnabled = true;
            }
        }

        /// <summary>
        /// 启动自动化运行，将耗时任务移至后台线程
        /// </summary>
        public async void Run()
        {
            // S8：运行态在入口处原子置位（不再依赖 StopSignal 反向清理）。
            // 已有一次运行在跑（含正在停止的旧循环）时直接忽略，防止重入出第二套循环。
            if (Interlocked.Exchange(ref _runActive, 1) == 1)
            {
                logger.Warn("Run ignored: previous run is still active");
                return;
            }
            IsRunning = true;

            // 烧录完成判定的运行时状态（声明于 try 外，供 finally 访问）：
            // burnConfigStep = 烧录配置步骤；burnFinishStepUid = 配置的烧录完成步骤；
            // burnCycleStart = 本轮烧录起始时间；burnJudged = 本次运行是否至少成功判定并记录过一次
            BurnInConfigStep burnConfigStep = null;
            Guid? burnFinishStepUid = null;
            DateTime? burnCycleStart = null;
            bool burnJudged = false;
            // 是否走到过 PreRun（取消确认时不复位主窗口，避免把用户的最大化窗口还原掉）
            bool preRunStarted = false;
            // 多线程：本次运行是否正常跑完（未停止、未取消、未异常）——正常跑完时子线程应被等待而不是取消
            bool runEndedNormally = false;
            // 本次运行的取消源（局部持有，避免被下一次运行替换的 _cts 影响收尾逻辑）
            CancellationTokenSource cts = null;

            try
            {
                // 烧录配置在运行开始时读取一次，供本次运行与 finally 的补记判定使用
                burnConfigStep = AutomationStepBases?.FirstOrDefault(s => s is BurnInConfigStep) as BurnInConfigStep;
                burnFinishStepUid = burnConfigStep?.Config?.BurnFinishStepUid;

                // 运行前确认（可等待，不阻塞 WPF 线程）
                if (_stepSettings.ConfirmBeforeRun && !await ConfirmRunAsync())
                    return;

                PreRun();
                preRunStarted = true;
                _cts = new CancellationTokenSource();
                cts = _cts;
                var token = cts.Token;

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

                    // 待处理中断：子线程打断主线程时，主线程从目标步骤开始执行
                    if (TryConsumeInterrupt(out Guid interruptTargetUid))
                    {
                        int interruptIndex = FindIndexByUid(interruptTargetUid);
                        if (interruptIndex < 0)
                        {
                            logger.Warn("Interrupt: target step not found: {0}", interruptTargetUid);
                        }
                        else
                        {
                            logger.Info("Interrupt: main flow restarts from step index {0}", interruptIndex);
                            i = interruptIndex - 1; // -1 因为 for 循环会 i++
                            continue;
                        }
                    }

                    var step = AutomationStepBases[i];

                    // StepReached 模式：检查是否有弹窗需要在到达此步骤时关闭
                    CheckAndCloseStepReachedPopups(step.Uid);

                    if (!step.IsNeed)
                        continue;

                    // 多线程开始步骤解析出的区域末尾（-1 表示不跳过区域）
                    int multiThreadSkipEndIndex = -1;

                    // 计时器放在 try 之外，异常路径也要用它记录耗时
                    var sw = Stopwatch.StartNew();

                    try
                    {
                        SelectedStep = step;

                        // 记录当前正在执行的步骤（供统计窗口展示）
                        _executionContext.RunningStepUid = step.Uid;

                        await step.RunAsync(token);
                        sw.Stop();

                        step.IsError = false;
                        step.ErrorType = StepErrorType.None;

                        // 多线程开始步骤：解析区域、按需创建子线程，并让主线程跳过区域继续执行
                        if (step is MultiThreadStartStep multiThreadStart)
                        {
                            if (MultiThreadRange.TryResolve(AutomationStepBases, i, multiThreadStart.RangeMode,
                                    multiThreadStart.RangeEndUid, out int rangeEnd, out string rangeError))
                            {
                                if (multiThreadStart.CreatedThreads < multiThreadStart.ThreadCount)
                                {
                                    multiThreadStart.CreatedThreads++;
                                    StartSubThread(AutomationStepBases, i + 1, rangeEnd, multiThreadStart.CreatedThreads, token);
                                    logger.Info("MultiThread: thread {0}/{1} started for region [{2},{3}]",
                                        multiThreadStart.CreatedThreads, multiThreadStart.ThreadCount, i + 1, rangeEnd);
                                }
                                multiThreadSkipEndIndex = rangeEnd;
                            }
                            else
                            {
                                // 解析失败：该步骤记为失败，且不跳过区域（安全退化）
                                step.IsTrue = false;
                                step.IsError = true;
                                step.ErrorMessage = rangeError;
                                step.ErrorType = StepErrorType.Unknown;
                                logger.Warn("MultiThread: range resolve failed: {0}", rangeError);
                            }
                        }

                        // 构建执行结果
                        var result = step.LastResult ?? new StepExecutionResult();
                        result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
                        result.IsTrue = step.IsTrue;
                        result.ExecutedAt = DateTime.Now;
                        // 先标记"刚刚执行完成的步骤"：条件里 Step_Triggered 的语义与改动前保持一致
                        _executionContext.CurrentStepUid = step.Uid;

                        // S7：先评估自定义条件得出"最终 IsTrue"，再回写结果与执行上下文。
                        // 条件里的 Self_* 变量读的是步骤自身结果（即上面写入的 result.IsTrue），
                        // 因此这里传入尚未回写最终值的 result；评估完成后把最终结果写回 result。
                        // （统计步骤不参与条件判断）
                        if (step is not StatisticsStep
                            && step.ConditionMode == ConditionMode.Custom && step.Conditions.Count > 0)
                        {
                            step.IsTrue = ConditionEvaluator.Evaluate(step.Conditions, _executionContext, result);
                        }
                        result.IsTrue = step.IsTrue;

                        // LastResult 保存的是最终结果（已含自定义条件判定后的值）
                        step.LastResult = result;

                        // 存入执行上下文：后续步骤引用 Step_IsTrue、统计步骤累计时读到的都是最终结果
                        _executionContext.SetResult(step.LineNo, result);
                        _executionContext.SetResultByUid(step.Uid, result);

                        // 统计步骤的条件计数累计（读取上面已写入上下文的最终结果）
                        foreach (var s in AutomationStepBases)
                        {
                            if (s is StatisticsStep statsStep)
                                statsStep.UpdateConditionCounts(_executionContext, result);
                        }

                        // 通用日志记录（T4 契约：Log 返回是否落库成功，失败只告警、不静默）
                        if (step.EnableLog)
                        {
                            string fileName = Path.GetFileNameWithoutExtension(SingletonLocator.Main.StepFilePath ?? "unsaved");
                            string logContent = $"[Result:{step.IsTrue}] [Time:{result.ExecutionTimeMs:F0}ms]";
                            if (result.Similarity >= 0)
                                logContent += $" [Similarity:{result.Similarity:F3}]";
                            // 结构化参数一并入库，供执行记录窗口渲染“结果 / 执行时间 / 线程”
                            if (!ExecutionLogService.Log(step.Uid, fileName, step.Name, logContent, result.OCRText,
                                executionTimeMs: result.ExecutionTimeMs,
                                isTrue: step.IsTrue,
                                similarity: result.Similarity,
                                threadName: "主线程",
                                textSource: result.TextSource,
                                errorMessage: step.IsError ? step.ErrorMessage : null))
                            {
                                logger.Warn("执行日志未落库：{0}", step.Name);
                            }
                        }

                        // 烧录完成判定：当前步骤为配置的烧录完成步骤时立即判定并记录
                        // （循环流程中每轮都会触发，每轮独立计时）
                        if (burnFinishStepUid.HasValue && step.Uid == burnFinishStepUid.Value
                            && BurnInService.CurrentSession != null)
                        {
                            burnCycleStart ??= BurnInService.CurrentSession.StartedAt;
                            try
                            {
                                // T4 契约：只有记录真正落库成功才算"已判定"；
                                // 失败时保持 burnJudged=false，交给运行结束时的补记（M9）
                                if (JudgeAndRecordBurnIn(BurnInService.CurrentSession, step, burnCycleStart.Value))
                                {
                                    burnJudged = true;
                                    burnCycleStart = DateTime.Now; // 下一轮烧录的起始时间
                                }
                                else
                                {
                                    logger.Warn("烧录记录未落库，等待运行结束补记：{0}", step.Name);
                                }
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
                        sw.Stop();
                        RecordFailureResult(step, _executionContext, sw.Elapsed.TotalMilliseconds, ex.Message);
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

                    // 确定下一个执行索引（通过 Uid 查找目标步骤，含自引用上限处理）
                    int nextIndex = ResolveNextIndex(step, i, AutomationStepBases);
                    if (nextIndex >= 0)
                    {
                        // Q3：记录跳转路径，检测 A→B→A 这类环（仅告警）
                        if (nextIndex < AutomationStepBases.Count)
                            DetectJumpCycle(AutomationStepBases[nextIndex].Uid);
                        i = nextIndex - 1; // -1 因为 for 循环会 i++
                    }
                    else if (multiThreadSkipEndIndex >= 0)
                    {
                        // 多线程开始步骤：主线程跳过被圈入的区域，继续执行区域之后的步骤
                        i = multiThreadSkipEndIndex;
                    }
                    if (nextIndex < 0)
                    {
                        // 本次没有跳转：正常进入下一步，跳转链结束
                        _jumpVisits.Clear();
                    }
                    // 未跳转时不修改 i，for 循环 i++ 自然进入下一步
                }

                // 多线程：脚本正常跑完（未停止、未取消、未异常）时先等子线程自然结束，再收尾
                runEndedNormally = !StopSignal && !token.IsCancellationRequested;
                if (runEndedNormally)
                    await WaitSubThreadsToEndAsync(cts, token);
            }
            catch (OperationCanceledException)
            {
                logger.Info("Operation Canceled");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Run Error: ");
            }
            finally
            {
                // S9：正常结束 / 用户取消 / 外层异常，都在这里统一收尾，
                // 避免异常路径不复位运行状态、统计窗关不掉、主窗卡在最小化。
                // 放在子线程等待之前：用户点停止时界面立即恢复（与多线程功能改造前一致）。
                if (preRunStarted)
                {
                    try
                    {
                        StopSignal = true;
                        _executionContext.StopTimer();

                        // Q5：流程结束时统一关闭"到达步骤关闭"的弹窗，避免残留窗口永久停留
                        CloseAllStepReachedPopups();

                        Views.WindowStatistics.CloseAllOnStop();
                        var mainWindow = Application.Current?.MainWindow;
                        if (mainWindow != null)
                        {
                            mainWindow.WindowState = WindowState.Normal;
                            Application.Current.Dispatcher.Invoke(() => mainWindow.Activate());
                        }
                        logger.Info("Auto Finished");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "Reset run state failed");
                    }

                    // 烧录统计补记（M20）：只有"配置了烧录完成步骤 + 本轮确实进入过烧录会话 + 从未判定成功"
                    // 才补记 NotFinished；只有配置步骤却没配完成步骤属于"未配置"，只告警、不写失败记录
                    try
                    {
                        if (burnConfigStep != null && !burnFinishStepUid.HasValue)
                        {
                            logger.Warn("烧录流程未配置烧录完成步骤：本次运行不补记 NotFinished 记录（仅告警）");
                        }
                        else if (burnFinishStepUid.HasValue && BurnInService.CurrentSession != null && !burnJudged)
                        {
                            RecordBurnInNotFinished(BurnInService.CurrentSession,
                                burnCycleStart ?? BurnInService.CurrentSession.StartedAt);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, "RecordBurnInNotFinished failed");
                    }
                }

                // 多线程：只有用户停止/取消/未处理异常（或仍有子线程在跑）时才取消子线程并等其收尾；
                // 正常跑完时子线程已在上面的等待中自然结束，不再取消它们
                try
                {
                    if (!runEndedNormally || HasRunningSubThreads())
                    {
                        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
                        await WaitSubThreadsAfterCancelAsync();
                    }
                    else
                    {
                        ClearSubThreadRunners();
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "MultiThread: sub thread cleanup failed");
                }
                MultiThreadInterruptBridge.RegisterHandler(null);

                try
                {
                    BurnInService.EndSession();
                }
                finally
                {
                    // S8：运行态最后复位——旧循环（含子线程收尾）真正退出后才允许下一次开始。
                    // 先清 _runActive 再置 IsRunning：IsRunning 的 setter 会重算 CanRun，
                    // 这样"开始"按钮在结束时立即恢复可用（两份状态同处 UI 线程，无竞态）。
                    Interlocked.Exchange(ref _runActive, 0);
                    IsRunning = false;
                }
            }
        }

        #region 多线程子线程收尾

        /// <summary>取消后等待子线程收尾的上限（毫秒），避免个别子线程不响应取消时界面永远卡住</summary>
        private const int SubThreadCancelWaitMs = 5000;

        /// <summary>
        /// 多线程：脚本正常跑完时等待所有子线程自然结束，不主动取消。
        /// 等待期间用户点停止（token 被取消）则按取消处理：取消 token 后继续等子线程收尾。
        /// </summary>
        private async Task WaitSubThreadsToEndAsync(CancellationTokenSource cts, CancellationToken token)
        {
            var runners = SnapshotSubThreadRunners();
            if (runners.Length == 0) return;

            logger.Info("MultiThread: main flow finished, waiting for {0} sub thread(s) to end", runners.Length);
            var all = Task.WhenAll(runners.Select(r => r.Completion));

            // 等待期间用户点停止：子线程按取消收尾（而不是无限等待）
            if (await Task.WhenAny(all, Task.Delay(Timeout.Infinite, token)) != all)
            {
                logger.Info("MultiThread: stop requested while waiting, {0} sub thread(s) will be cancelled",
                    runners.Count(r => !r.IsCompleted));
                try { cts?.Cancel(); } catch (ObjectDisposedException) { }
            }

            await all;
            logger.Info("MultiThread: all {0} sub thread(s) ended", runners.Length);
            ClearSubThreadRunners();
        }

        /// <summary>
        /// 多线程：取消后等待子线程收尾（有界等待；个别子线程不响应取消时只告警，不无限等待）
        /// </summary>
        private async Task WaitSubThreadsAfterCancelAsync()
        {
            var runners = SnapshotSubThreadRunners();
            if (runners.Length == 0) return;

            logger.Info("MultiThread: waiting for {0} cancelled sub thread(s) to unwind", runners.Length);
            var all = Task.WhenAll(runners.Select(r => r.Completion));
            if (await Task.WhenAny(all, Task.Delay(SubThreadCancelWaitMs)) == all)
            {
                logger.Info("MultiThread: all {0} sub thread(s) ended", runners.Length);
            }
            else
            {
                logger.Warn("MultiThread: {0} sub thread(s) still running after {1} ms, stop waiting",
                    runners.Count(r => !r.IsCompleted), SubThreadCancelWaitMs);
            }

            int abandoned = runners.Count(r => r.IsAbandoned);
            if (abandoned > 0)
                logger.Warn("MultiThread: {0} sub thread(s) aborted by cancel before their steps finished", abandoned);

            ClearSubThreadRunners();
        }

        private SubThreadRunner[] SnapshotSubThreadRunners()
        {
            lock (_subThreadLock)
            {
                return _subThreadRunners.ToArray();
            }
        }

        private bool HasRunningSubThreads()
        {
            lock (_subThreadLock)
            {
                return _subThreadRunners.Any(r => !r.IsCompleted);
            }
        }

        private void ClearSubThreadRunners()
        {
            lock (_subThreadLock)
            {
                _subThreadRunners.Clear();
            }
        }

        #endregion

        /// <summary>
        /// 记录一次失败的执行结果：失败路径同样要写入执行上下文，
        /// 否则统计窗口看不到失败步骤的结果、次数与耗时（子线程同理）
        /// </summary>
        private static StepExecutionResult RecordFailureResult(AutomationStepBase step, StepExecutionContext context, double elapsedMs, string errorMessage)
        {
            var result = new StepExecutionResult
            {
                IsTrue = false,
                ExecutionTimeMs = elapsedMs,
                ExecutedAt = DateTime.Now,
                ErrorMessage = errorMessage,
            };
            context.SetResult(step.LineNo, result);
            context.SetResultByUid(step.Uid, result);
            context.CurrentStepUid = step.Uid;
            return result;
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
        /// 返回 true 表示记录已成功落库（T4 契约）；返回 false 时调用方不得视为"已判定"（M9）。
        /// </summary>
        private bool JudgeAndRecordBurnIn(BurnInSession session, AutomationStepBase finishStep, DateTime cycleStart)
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
                bool recordedNotConfigured = BurnInService.Record(new BurnInRecord
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
                if (!recordedNotConfigured)
                    logger.Warn("烧录记录（NotConfigured）未落库：{0}", stepFileName);
                return recordedNotConfigured;
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
                goodImageHit = MatchTemplate(config.GoodTemplateName, config.SimilarityThreshold, config.HighPrecision, config.ColorThreshold);
                badImageHit = MatchTemplate(config.BadTemplateName, config.SimilarityThreshold, config.HighPrecision, config.ColorThreshold);
                failImageHit = MatchTemplate(config.FailTemplateName, config.SimilarityThreshold, config.HighPrecision, config.ColorThreshold);
            }

            var (result, goodText, badText, remark, screenshotPath) =
                BurnInService.EvaluateAndCapture(config, ocrText, goodImageHit, badImageHit, failImageHit, session.OrderNo);

            bool recorded = BurnInService.Record(new BurnInRecord
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
            if (!recorded)
                logger.Warn("烧录记录未落库（{0}）：{1}", result, stepFileName);
            return recorded;
        }

        /// <summary>
        /// 运行结束（正常/取消/异常）但从未成功判定记录过烧录时，补记一条未完成烧录供追溯。
        /// M20：只有"配置了烧录完成步骤"且"本轮确实进入过烧录会话"才补记；
        /// "未配置完成步骤"属于配置问题，由调用方告警，不写失败记录。
        /// </summary>
        private void RecordBurnInNotFinished(BurnInSession session, DateTime cycleStart)
        {
            if (session == null) return;
            var configStep = AutomationStepBases?.FirstOrDefault(s => s is BurnInConfigStep) as BurnInConfigStep;
            if (configStep?.Config?.BurnFinishStepUid == null) return;

            var finishedAt = DateTime.Now;
            bool recorded = BurnInService.Record(new BurnInRecord
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
            if (!recorded)
                logger.Warn("未完成烧录的补记记录未落库（NotFinished）");
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
        private static bool MatchTemplate(string templateName, double threshold, bool highPrecision, double colorThreshold)
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
                    var rect = Autogui.FindImageOnScreen(template, threshold, gaptime: 0, timeout: 0, highPrecision: highPrecision, colorThreshold: colorThreshold);
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
        /// StepReached 模式：到达目标步骤时关闭弹窗。
        /// Q5：目标步骤已被删除、或目标为空（未设置）时同样关闭，
        /// 否则弹窗会因为没有"到达时刻"而永久停留。
        /// </summary>
        private void CheckAndCloseStepReachedPopups(Guid currentStepUid)
        {
            foreach (var s in AutomationStepBases)
            {
                if (s is not PopupStep popupStep) continue;
                if (!popupStep.CloseMode.HasFlag(PopupCloseMode.StepReached)) continue;
                var popupWindow = popupStep.ActivePopupWindow;
                if (popupWindow == null) continue;

                bool reached = popupStep.CloseOnStepUid == currentStepUid;
                bool targetMissing = !popupStep.CloseOnStepUid.HasValue
                    || popupStep.CloseOnStepUid.Value == Guid.Empty
                    || FindIndexByUid(popupStep.CloseOnStepUid.Value) < 0;
                if (!reached && !targetMissing) continue;

                var defaultResult = popupStep.PopupButtons?.DefaultButton?.Value ?? string.Empty;
                popupWindow.CloseWithResult(defaultResult);
                popupStep.ActivePopupWindow = null;
                if (targetMissing && !reached)
                    logger.Warn("StepReached 弹窗的目标步骤不存在（可能已被删除），已关闭：{0}", popupStep.Name);
            }
        }

        /// <summary>
        /// Q5：流程结束（正常/取消/异常）时统一关闭所有 StepReached 模式的弹窗，
        /// 避免弹窗在运行结束后残留、用户关不掉。
        /// </summary>
        private void CloseAllStepReachedPopups()
        {
            if (AutomationStepBases == null) return;
            foreach (var s in AutomationStepBases)
            {
                if (s is not PopupStep popupStep) continue;
                if (!popupStep.CloseMode.HasFlag(PopupCloseMode.StepReached)) continue;
                var popupWindow = popupStep.ActivePopupWindow;
                if (popupWindow == null) continue;

                var defaultResult = popupStep.PopupButtons?.DefaultButton?.Value ?? string.Empty;
                popupWindow.CloseWithResult(defaultResult);
                popupStep.ActivePopupWindow = null;
            }
        }

        /// <summary>
        /// Q3：检测跳转链里的环（A→B→A 这类两段跳转环）。
        /// 只做诊断告警、不中断流程：直接自跳已有 SelfReferenceLimit 兜底，
        /// 而两段跳转环常被用作"轮询直到满足条件"的合法循环，强制中断会破坏现有流程。
        /// 跳转链在遇到"不跳转的步骤"时清空，避免把正常流程的重复访问误判成环。
        /// </summary>
        private void DetectJumpCycle(Guid targetUid)
        {
            if (!_jumpVisits.TryGetValue(targetUid, out int count))
                count = 0;
            count++;
            _jumpVisits[targetUid] = count;
            // 同一跳转链内第 3 次回到同一步骤才告警，避免首次重复访问就误报
            if (count == 3)
                logger.Warn("检测到跳转环：步骤 {0} 在连续跳转链中第 {1} 次出现，若流程无法结束请检查跳转设置",
                    targetUid, count);
        }

        #endregion

        #region 多线程

        /// <summary>登记一个待处理中断（子线程调用，多次中断覆盖为最新）</summary>
        private void RequestInterrupt(Guid targetUid)
        {
            lock (_interruptLock)
            {
                _pendingInterruptUid = targetUid;
            }
        }

        /// <summary>取走待处理中断（消费）</summary>
        private bool TryConsumeInterrupt(out Guid targetUid)
        {
            lock (_interruptLock)
            {
                if (_pendingInterruptUid.HasValue)
                {
                    targetUid = _pendingInterruptUid.Value;
                    _pendingInterruptUid = null;
                    return true;
                }
            }
            targetUid = Guid.Empty;
            return false;
        }

        /// <summary>
        /// 启动一个子线程执行 [regionStart, regionEnd] 区域内的步骤。
        /// 子线程是专用 STA 线程 + Dispatcher 消息泵（见 SubThreadRunner），因此区域内可以创建并操作 WPF 窗口；
        /// 子线程使用独立的执行上下文、不阻塞主线程，结束时由主流程负责等待或取消。
        /// token 只用于步骤内的协作式取消，不能交给线程调度（token 已取消时委托根本不会执行）。
        /// </summary>
        private void StartSubThread(IList<AutomationStepBase> steps, int regionStart, int regionEnd, int threadIndex, CancellationToken token)
        {
            if (steps == null) return;
            var snapshot = steps.ToList();
            var runner = SubThreadRunner.Start($"ShaoLu-SubThread-[{regionStart},{regionEnd}]", async ct =>
            {
                // 标记当前执行流位于子线程：中断步骤据此判断发出者身份
                MultiThreadInterruptBridge.InChildThread = true;
                try
                {
                    await RunSubThreadAsync(snapshot, regionStart, regionEnd, threadIndex, ct);
                }
                catch (OperationCanceledException)
                {
                    logger.Info("MultiThread: sub thread cancelled");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "MultiThread: sub thread failed");
                }
            }, token);

            lock (_subThreadLock)
            {
                _subThreadRunners.Add(runner);
            }
        }

        /// <summary>
        /// 子线程执行器：顺序执行区域内步骤，结果只写入自己的执行上下文。
        /// 步骤报错只记录并继续；跳转指向区域之外时结束该子线程。
        /// 只随 token 取消而中止：主流程正常跑完时会等它自然结束（见 WaitSubThreadsToEndAsync），
        /// 用户点停止时 Stop() 已取消 token，子线程据此按取消收尾。
        /// </summary>
        private async Task RunSubThreadAsync(List<AutomationStepBase> steps, int regionStart, int regionEnd, int threadIndex, CancellationToken token)
        {
            var context = new StepExecutionContext();
            // 登记到全局上下文表并标注线程名，统计面板据此聚合数据并区分线程
            StepExecutionContext.RegisterContext(context, $"子线程{threadIndex}");
            context.StartTimer();
            try
            {
                for (int i = regionStart; i <= regionEnd; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if (i < 0) break;

                    // 暂停等待：只看 token（主流程正常跑完不影响子线程；用户停止时 token 已取消）
                    while (IsPaused)
                    {
                        token.ThrowIfCancellationRequested();
                        await Task.Delay(100, token);
                    }

                    var step = steps[i];
                    if (!step.IsNeed) continue;

                    // 计时器放在 try 之外，异常路径也要用它记录耗时
                    var sw = Stopwatch.StartNew();

                    try
                    {
                        await step.RunAsync(token);
                        sw.Stop();

                        step.IsError = false;
                        step.ErrorType = StepErrorType.None;

                        // 子线程不修改 SelectedStep / RunningStepUid，不更新统计计数，不判定烧录
                        var result = step.LastResult ?? new StepExecutionResult();
                        result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
                        result.IsTrue = step.IsTrue;
                        result.ExecutedAt = DateTime.Now;
                        // 先标记"刚刚执行完成的步骤"：条件里 Step_Triggered 的语义与主流程一致
                        context.CurrentStepUid = step.Uid;

                        // S7：与主流程同样的顺序——先评估自定义条件得最终 IsTrue，再回写结果与上下文，
                        // 保证子线程后续步骤引用 Step_IsTrue 时读到的是条件判定后的最终结果
                        if (step is not StatisticsStep
                            && step.ConditionMode == ConditionMode.Custom && step.Conditions.Count > 0)
                        {
                            step.IsTrue = ConditionEvaluator.Evaluate(step.Conditions, context, result);
                        }
                        result.IsTrue = step.IsTrue;
                        step.LastResult = result;

                        context.SetResult(step.LineNo, result);
                        context.SetResultByUid(step.Uid, result);

                        if (step.EnableLog)
                        {
                            string fileName = Path.GetFileNameWithoutExtension(SingletonLocator.Main.StepFilePath ?? "unsaved");
                            string logContent = $"[SubThread] [Result:{step.IsTrue}] [Time:{result.ExecutionTimeMs:F0}ms]";
                            if (result.Similarity >= 0)
                                logContent += $" [Similarity:{result.Similarity:F3}]";
                            if (!ExecutionLogService.Log(step.Uid, fileName, step.Name, logContent, result.OCRText,
                                executionTimeMs: result.ExecutionTimeMs,
                                isTrue: step.IsTrue,
                                similarity: result.Similarity,
                                threadName: context.ThreadName,
                                textSource: result.TextSource,
                                errorMessage: step.IsError ? step.ErrorMessage : null))
                            {
                                logger.Warn("执行日志未落库（子线程）：{0}", step.Name);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        step.ErrorType = StepErrorType.CancelledByUser;
                        // 取消路径不会经过 try 内的成功日志，这里补一条，避免子线程步骤“没有日志”
                        if (step.EnableLog)
                        {
                            string cancelFileName = Path.GetFileNameWithoutExtension(SingletonLocator.Main.StepFilePath ?? "unsaved");
                            if (!ExecutionLogService.Log(step.Uid, cancelFileName, step.Name,
                                "[SubThread] [Result:False] [Cancelled]", null,
                                executionTimeMs: step.LastResult?.ExecutionTimeMs,
                                isTrue: false,
                                similarity: step.LastResult?.Similarity,
                                threadName: context.ThreadName,
                                textSource: step.LastResult?.TextSource,
                                errorMessage: "Cancelled"))
                            {
                                logger.Warn("取消日志未落库（子线程）：{0}", step.Name);
                            }
                        }
                        break;
                    }
                    catch (Exception ex)
                    {
                        // 子线程内步骤报错：只记录错误与日志，不影响主流程，继续执行后续步骤
                        logger.Warn(ex, "MultiThread: step '{0}' failed in sub thread", step.Name);
                        step.IsError = true;
                        step.ErrorMessage = ex.Message;
                        step.ErrorType = InferErrorType(ex);
                        // 异常即失败：不置 false 会沿用上次的 IsTrue，导致 FalseGoto 不生效而继续往下执行
                        step.IsTrue = false;
                        // 失败也要写入执行上下文，统计窗口才能看到子线程步骤的结果/次数/耗时
                        sw.Stop();
                        var failResult = RecordFailureResult(step, context, sw.Elapsed.TotalMilliseconds, ex.Message);
                        // 异常路径同样补一条日志，内容带上异常消息
                        if (step.EnableLog)
                        {
                            string errorFileName = Path.GetFileNameWithoutExtension(SingletonLocator.Main.StepFilePath ?? "unsaved");
                            if (!ExecutionLogService.Log(step.Uid, errorFileName, step.Name,
                                $"[SubThread] [Result:False] [Error:{ex.Message}]", null,
                                executionTimeMs: failResult.ExecutionTimeMs,
                                isTrue: false,
                                similarity: failResult.Similarity,
                                threadName: context.ThreadName,
                                textSource: failResult.TextSource,
                                errorMessage: ex.Message))
                            {
                                logger.Warn("错误日志未落库（子线程）：{0}", step.Name);
                            }
                        }
                    }

                    // 区域内的跳转；指向区域之外时结束该子线程并记日志
                    int nextIndex = ResolveNextIndex(step, i, steps);
                    if (nextIndex < 0) continue;

                    if (nextIndex < regionStart || nextIndex > regionEnd)
                    {
                        logger.Info("MultiThread: step '{0}' jumps outside region [{1},{2}], sub thread ends",
                            step.Name, regionStart, regionEnd);
                        break;
                    }
                    i = nextIndex - 1; // -1 因为 for 循环会 i++
                }
            }
            finally
            {
                context.StopTimer();
            }
        }

        /// <summary>
        /// 按跳转配置解析下一个执行索引（含自引用上限处理）。
        /// 返回 -1 表示不跳转（继续下一步）；否则返回目标步骤索引。
        /// </summary>
        private static int ResolveNextIndex(AutomationStepBase step, int currentIndex, IList<AutomationStepBase> steps)
        {
            Guid? targetUid = step.IsTrue ? step.TrueGotoUid : step.FalseGotoUid;
            if (!targetUid.HasValue || targetUid.Value == Guid.Empty)
                return -1;

            int nextIndex = FindIndexByUidIn(steps, targetUid.Value);
            if (nextIndex < 0)
            {
                // 目标步骤不存在（可能被删除），继续下一步
                step.SelfReferenceCount = 0;
                return -1;
            }
            if (nextIndex != currentIndex)
            {
                step.SelfReferenceCount = 0; // 非自引用时重置
                return nextIndex;
            }

            // 自引用检测
            step.SelfReferenceCount++;
            // SelfReferenceLimit: -1=无限制, 0=禁止自引用, >0=限制次数
            if (step.SelfReferenceLimit < 0 || step.SelfReferenceCount < step.SelfReferenceLimit)
                return nextIndex;

            // 达到自引用上限：自身结果强制为 false
            step.IsTrue = false;
            step.ErrorMessage = string.Format(LanguageService.GetLocalizedString("Msg_SelfReferenceLimit"), step.Name, step.SelfReferenceLimit);
            step.SelfReferenceCount = 0;

            // 按 false 结果重新解析跳转目标；FalseGoto 为空或仍指向自身时不跳转
            if (step.FalseGotoUid.HasValue && step.FalseGotoUid.Value != Guid.Empty)
            {
                int falseIndex = FindIndexByUidIn(steps, step.FalseGotoUid.Value);
                if (falseIndex >= 0 && falseIndex != currentIndex)
                    return falseIndex;
            }
            return -1;
        }

        private static int FindIndexByUidIn(IList<AutomationStepBase> steps, Guid uid)
        {
            if (steps == null) return -1;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Uid == uid) return i;
            }
            return -1;
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
