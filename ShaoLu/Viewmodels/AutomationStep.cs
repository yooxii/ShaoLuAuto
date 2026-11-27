using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Expression.Drawing.Core;
using NLog;
using OfficeOpenXml;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;

namespace ShaoLu.Viewmodels.AutomationStep
{
    // 基类
    public abstract class AutomationStepBase : ObservableObject
    {
        internal readonly Logger _logger = LogManager.GetCurrentClassLogger();

        #region 属性

        private bool _isNeed = true;
        private bool _isSave = false;
        private bool _isError = false;
        private string _errorMessage;
        private int _lineNo;
        private string _name;
        private string _description;
        private StepType _type;
        private bool _isTrue = false;
        private Guid? _trueGotoUid;
        private Guid? _falseGotoUid;
        private double _waitTime = 0.1;
        private StepErrorType _errorType = StepErrorType.None;

        public bool IsNeed
        {
            get => _isNeed;
            set => SetProperty(ref _isNeed, value);
        }

        private Guid _uid = Guid.NewGuid();
        /// <summary>
        /// 步骤的唯一uid
        /// 注意：setter 供 JSON 反序列化恢复原 Uid（跳转设置、条件引用均依赖 Uid），
        /// 忽略 Guid.Empty 赋值以防覆盖已有标识（占位项除外，占位项在构造函数中直接赋值）
        /// </summary>
        public Guid Uid
        {
            get => _uid;
            set
            {
                if (value != Guid.Empty) _uid = value;
            }
        }

        /// <summary>
        /// 用于创建占位项（如“无跳转”）的特殊构造
        /// </summary>
        private protected AutomationStepBase(bool isPlaceholder)
        {
            _uid = Guid.Empty;
        }

        public bool IsSave { get => _isSave; set => SetProperty(ref _isSave, value); }

        public bool IsError { get => _isError; set => SetProperty(ref _isError, value); }

        public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

        /// <summary>
        /// 步骤行号。
        /// 注意：此值应由包含该步骤的集合（如 ObservableCollection）在增删改时统一维护，
        /// 或者在 UI 绑定时通过 Index 计算。此处保留 SetProperty 以支持手动刷新。
        /// </summary>
        public int LineNo { get => _lineNo; set => SetProperty(ref _lineNo, value); }

        /// <summary>
        /// 步骤名称
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                // 简单的防御性编程，防止 Null 导致绑定崩溃
                if (value == null) { IsError = true; throw new ArgumentNullException(nameof(Name)); }
                SetProperty(ref _name, value);
            }
        }

        /// <summary>
        /// 步骤描述
        /// </summary>
        public string Description { get => _description; set => SetProperty(ref _description, value); }

        /// <summary>
        /// 步骤类型
        /// </summary>
        public StepType Type { get => _type; set => SetProperty(ref _type, value); }

        public bool IsTrue { get => _isTrue; set => SetProperty(ref _isTrue, value); }


        /// <summary>
        /// 如果真,跳转到的步骤 Uid
        /// </summary>
        [JsonPropertyName("TrueGoto")]
        public Guid? TrueGotoUid { get => _trueGotoUid; set { if (SetProperty(ref _trueGotoUid, value)) { OnPropertyChanged(nameof(TrueGotoLineNo)); } } }

        /// <summary>
        /// 如果假,跳转到的步骤 Uid
        /// </summary>
        [JsonPropertyName("FalseGoto")]
        public Guid? FalseGotoUid { get => _falseGotoUid; set { if (SetProperty(ref _falseGotoUid, value)) { OnPropertyChanged(nameof(FalseGotoLineNo)); } } }

        /// <summary>
        /// TrueGoto 对应的行号（只读，用于 UI 显示）
        /// </summary>
        [JsonIgnore]
        public int TrueGotoLineNo => ResolveLineNo(_trueGotoUid);

        /// <summary>
        /// FalseGoto 对应的行号（只读，用于 UI 显示）
        /// </summary>
        [JsonIgnore]
        public int FalseGotoLineNo => ResolveLineNo(_falseGotoUid);

        /// <summary>
        /// 根据 Uid 解析步骤行号，找不到返回 0
        /// </summary>
        private static int ResolveLineNo(Guid? uid)
        {
            if (!uid.HasValue || uid.Value == Guid.Empty) return 0;
            try
            {
                var steps = SingletonLocator.Steps.AutomationStepBases;
                if (steps == null) return 0;
                for (int i = 0; i < steps.Count; i++)
                {
                    if (steps[i].Uid == uid.Value) return i + 1;
                }
            }
            catch { /* DI 未初始化时忽略 */ }
            return 0;
        }

        /// <summary>
        /// 所有步骤集合（供 Goto ComboBox 绑定，包含“无跳转”占位项）
        /// </summary>
        [JsonIgnore]
        public ObservableCollection<AutomationStepBase> AllSteps
        {
            get
            {
                try
                {
                    var result = new ObservableCollection<AutomationStepBase> { GotoPlaceholder };
                    foreach (var s in SingletonLocator.Steps.AutomationStepBases)
                        result.Add(s);
                    return result;
                }
                catch { return null; }
            }
        }

        /// <summary>
        /// Goto 下拉框的“无跳转”占位项
        /// </summary>
        private static AutomationStepBase _gotoPlaceholder;
        [JsonIgnore]
        public static AutomationStepBase GotoPlaceholder
        {
            get
            {
                if (_gotoPlaceholder == null)
                {
                    _gotoPlaceholder = new EmptyStep(true);
                    _gotoPlaceholder.Name = "(无跳转)";
                }
                return _gotoPlaceholder;
            }
        }

        public double WaitTime { get => _waitTime; set => SetProperty(ref _waitTime, value); }

        /// <summary>
        /// 步骤错误类型
        /// </summary>
        public StepErrorType ErrorType { get => _errorType; set => SetProperty(ref _errorType, value); }

        /// <summary>
        /// 流程图里是否被「锚定」（钉住）：自动整理时该节点保持原位，只能手动拖动。
        /// 视图层状态，不进工程文件。
        /// </summary>
        [JsonIgnore]
        public bool FlowPinned
        {
            get => Services.FlowLayoutStore.IsPinned(Uid);
            set
            {
                if (Services.FlowLayoutStore.IsPinned(Uid) == value) return;
                Services.FlowLayoutStore.SetPinned(Uid, value);
                OnPropertyChanged();
            }
        }

        #region 循环跳转保护（跳出循环的次数 / 时间限制）

        private int _trueGotoMaxCount = -1;
        /// <summary>真分支跳转次数上限（-1 = 不限制，0 = 不跳转）</summary>
        public int TrueGotoMaxCount { get => _trueGotoMaxCount; set => SetProperty(ref _trueGotoMaxCount, value); }

        private int _falseGotoMaxCount = -1;
        /// <summary>假分支跳转次数上限（-1 = 不限制，0 = 不跳转）</summary>
        public int FalseGotoMaxCount { get => _falseGotoMaxCount; set => SetProperty(ref _falseGotoMaxCount, value); }

        private double _trueGotoTimeoutSeconds;
        /// <summary>真分支跳转超时（秒，0 = 不限制）：从该分支第一次跳转开始计时，超时后不再跳转</summary>
        public double TrueGotoTimeoutSeconds { get => _trueGotoTimeoutSeconds; set => SetProperty(ref _trueGotoTimeoutSeconds, value); }

        private double _falseGotoTimeoutSeconds;
        /// <summary>假分支跳转超时（秒，0 = 不限制）</summary>
        public double FalseGotoTimeoutSeconds { get => _falseGotoTimeoutSeconds; set => SetProperty(ref _falseGotoTimeoutSeconds, value); }

        private Guid? _gotoLimitGotoUid;
        /// <summary>
        /// 跳转次数 / 超时到达上限后跳到哪个步骤（空 = 按原来的处理：改走另一分支，另一分支也不跳就顺序执行下一步）。
        /// 真、假两个分支共用这个出口，自引用（跳回自己）也算在跳转次数里。
        /// </summary>
        public Guid? GotoLimitGotoUid { get => _gotoLimitGotoUid; set => SetProperty(ref _gotoLimitGotoUid, value); }

        /// <summary>运行时：真分支已跳转次数（不序列化）</summary>
        [JsonIgnore]
        public int TrueGotoCount { get; set; }

        /// <summary>运行时：假分支已跳转次数（不序列化）</summary>
        [JsonIgnore]
        public int FalseGotoCount { get; set; }

        /// <summary>运行时：真分支第一次跳转的时间（用于超时计时，不序列化）</summary>
        [JsonIgnore]
        public DateTime TrueGotoStartedAt { get; set; }

        /// <summary>运行时：假分支第一次跳转的时间（不序列化）</summary>
        [JsonIgnore]
        public DateTime FalseGotoStartedAt { get; set; }

        /// <summary>清空真/假分支的跳转计数与计时（跳出循环后重新计数）</summary>
        public void ResetGotoGuards()
        {
            TrueGotoCount = 0;
            FalseGotoCount = 0;
            TrueGotoStartedAt = default;
            FalseGotoStartedAt = default;
        }

        #endregion

        #region 条件判断

        private ConditionMode _conditionMode = ConditionMode.Default;
        /// <summary>
        /// 条件判断模式
        /// </summary>
        public ConditionMode ConditionMode
        {
            get => _conditionMode;
            set
            {
                if (SetProperty(ref _conditionMode, value))
                    OnPropertyChanged(nameof(ConditionSummary));
            }
        }

        private ObservableCollection<StepCondition> _conditions = [];
        /// <summary>
        /// 自定义条件规则行列表
        /// </summary>
        public ObservableCollection<StepCondition> Conditions
        {
            get => _conditions;
            set
            {
                if (_conditions != null)
                    _conditions.CollectionChanged -= OnConditionsCollectionChanged;
                if (SetProperty(ref _conditions, value))
                {
                    if (_conditions != null)
                        _conditions.CollectionChanged += OnConditionsCollectionChanged;
                    OnPropertyChanged(nameof(ConditionSummary));
                }
            }
        }

        /// <summary>结果判断编辑入口的摘要文本（默认 / 自定义规则条数）</summary>
        [JsonIgnore]
        public string ConditionSummary
        {
            get
            {
                if (ConditionMode != ConditionMode.Custom)
                    return LanguageService.GetLocalizedString("Condition_SummaryDefault", "Default (use step result)");
                return string.Format(
                    LanguageService.GetLocalizedString("Condition_SummaryCustom", "Custom ({0} rules)"),
                    Conditions?.Count ?? 0);
            }
        }

        private void OnConditionsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ConditionSummary));
        }

        [JsonIgnore]
        private ICommand editConditionsCommand;
        /// <summary>打开结果判断编辑窗口（原位置仅保留入口）</summary>
        [JsonIgnore]
        public ICommand EditConditionsCommand => editConditionsCommand ??= new RelayCommand(() => Views.WindowEditConditions.Edit(this));

        [JsonIgnore]
        private ICommand addConditionCommand;
        [JsonIgnore]
        public ICommand AddConditionCommand => addConditionCommand ??= new RelayCommand(AddCondition);

        [JsonIgnore]
        private ICommand removeConditionCommand;
        [JsonIgnore]
        public ICommand RemoveConditionCommand => removeConditionCommand ??= new RelayCommand(RemoveCondition);

        private void AddCondition()
        {
            Conditions.Add(new StepCondition { ParentStepUid = Uid });
        }

        private void RemoveCondition()
        {
            if (Conditions.Count > 0)
                Conditions.RemoveAt(Conditions.Count - 1);
        }

        #endregion

        #region 日志

        private bool _enableLog = false;
        /// <summary>
        /// 是否记录执行日志
        /// </summary>
        public bool EnableLog { get => _enableLog; set => SetProperty(ref _enableLog, value); }

        private bool _recordDetails;
        /// <summary>
        /// 是否记录「更多细节」（执行记录窗口的详情列）；默认关闭，避免高频执行时额外开销
        /// </summary>
        public bool RecordDetails { get => _recordDetails; set => SetProperty(ref _recordDetails, value); }

        #endregion

        #region 执行结果

        /// <summary>
        /// 最近一次执行结果（运行时，不序列化）
        /// </summary>
        [JsonIgnore]
        public StepExecutionResult LastResult { get; set; }

        #endregion

        #endregion

        /// <summary>
        /// 构造函数
        /// 确保创建时即分配唯一 ID 和默认值
        /// </summary>
        protected AutomationStepBase()
        {
            this.LineNo = 0;
            this.Name = string.Empty;
            this.Description = string.Empty;
            Conditions.CollectionChanged += OnConditionsCollectionChanged;
        }

        /// <summary>
        /// 带参构造函数（方便测试和初始化）
        /// </summary>
        protected AutomationStepBase(string name, StepType type) : this()
        {
            this.Name = name ?? throw new ArgumentNullException(nameof(name));
            this.Type = type;
        }

        public abstract AutomationStepBase Clone();

        public abstract Task<bool> RunAsync(CancellationToken cancellationToken = default);
        public bool Run()
        {
            return RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        // 其他公共属性...
    }


    // 输入文字步骤
    public class TypeTextStep : AutomationStepBase
    {
        private string _textToType;
        private double _delayBetweenKeys = 0.05;

        // ---- 引用其他步骤的结果（设置后本次键入用引用值，TextToType 保留原固定文本） ----
        private Guid? _refStepUid;
        private AcbelReferenceKind? _refKind;
        private string _refFieldName = string.Empty;
        private ICommand _clearReferenceCommand;

        /// <summary>
        /// 输入内容
        /// </summary>
        public string TextToType { get => _textToType; set => SetProperty(ref _textToType, value); }

        public double DelayBetweenKeys { get => _delayBetweenKeys; set => SetProperty(ref _delayBetweenKeys, value); }

        /// <summary>被引用的步骤 Uid（空表示直接键入 TextToType）。</summary>
        public Guid? RefStepUid
        {
            get => _refStepUid;
            set { if (SetProperty(ref _refStepUid, value)) RaiseReferenceChanged(); }
        }

        /// <summary>引用种类（执行结果 / OCR 文本 / 直接获取文本 / 扫码结果 / 用户输入字段）。</summary>
        public AcbelReferenceKind? RefKind
        {
            get => _refKind;
            set { if (SetProperty(ref _refKind, value)) RaiseReferenceChanged(); }
        }

        /// <summary>引用的「用户输入」字段名（仅 RefKind = UserInput 时有意义）。</summary>
        public string RefFieldName
        {
            get => _refFieldName;
            set { if (SetProperty(ref _refFieldName, value ?? string.Empty)) RaiseReferenceChanged(); }
        }

        /// <summary>已经引用了其他步骤的结果。</summary>
        [JsonIgnore]
        public bool IsReferenced
            => _refStepUid.HasValue && _refStepUid.Value != Guid.Empty
               && _refKind.HasValue && _refKind.Value != AcbelReferenceKind.None;

        /// <summary>引用标签，例如「3. ScanCode_1 · 扫码结果」或「用户输入 → 用户输入_1 / SN」。</summary>
        [JsonIgnore]
        public string ReferenceLabel
        {
            get
            {
                if (!IsReferenced) return string.Empty;

                int lineNo = 0;
                string stepName = null;
                foreach (var pair in AcbelUploadStep.OtherSteps(Guid.Empty))
                {
                    if (pair.Value.Uid != _refStepUid.Value) continue;
                    lineNo = pair.Key;
                    stepName = pair.Value.Name;
                    break;
                }

                if (_refKind.Value == AcbelReferenceKind.UserInput)
                {
                    string userInputKind = AcbelUploadStep.Loc("AcbelRefUserInput", "用户输入");
                    string fieldName = _refFieldName ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(stepName))
                        return string.IsNullOrWhiteSpace(fieldName)
                            ? userInputKind + " → " + stepName
                            : userInputKind + " → " + stepName + " / " + fieldName;
                    if (!string.IsNullOrWhiteSpace(fieldName))
                        return userInputKind + " → " + fieldName;
                    return userInputKind;
                }

                string kind;
                switch (_refKind.Value)
                {
                    case AcbelReferenceKind.ExecutionResult:
                        kind = AcbelUploadStep.Loc("AcbelRefExecutionResult", "执行结果");
                        break;
                    case AcbelReferenceKind.OcrText:
                        kind = AcbelUploadStep.Loc("AcbelRefOcrText", "OCR 文本");
                        break;
                    case AcbelReferenceKind.DirectText:
                        kind = AcbelUploadStep.Loc("AcbelRefDirectText", "直接获取文本");
                        break;
                    case AcbelReferenceKind.ScanResult:
                        kind = AcbelUploadStep.Loc("AcbelRefScanResult", "扫码结果");
                        break;
                    default:
                        kind = string.Empty;
                        break;
                }

                stepName = stepName ?? "?";
                return lineNo > 0 ? lineNo + ". " + stepName + " · " + kind : stepName + " · " + kind;
            }
        }

        /// <summary>引用菜单节点树（引用步骤 → 步骤 → 结果种类 / 用户输入字段，以及清除引用）。</summary>
        [JsonIgnore]
        public ObservableCollection<AcbelRefMenuNode> MenuNodes
        {
            get
            {
                var nodes = new ObservableCollection<AcbelRefMenuNode>();
                var root = new AcbelRefMenuNode { Header = AcbelUploadStep.Loc("AcbelRefSteps", "引用步骤") };

                foreach (var pair in AcbelUploadStep.OtherSteps(Uid))
                {
                    var step = pair.Value;
                    var stepNode = new AcbelRefMenuNode { Header = pair.Key + ". " + step.Name };

                    if (step is UserInputStep userInputStep)
                    {
                        foreach (string name in CollectUserInputFieldNames(userInputStep))
                        {
                            string fieldName = name;
                            stepNode.Children.Add(new AcbelRefMenuNode
                            {
                                Header = fieldName,
                                StepUid = step.Uid,
                                Kind = AcbelReferenceKind.UserInput,
                                FieldName = fieldName,
                                Command = new RelayCommand(() => SetReference(step.Uid, AcbelReferenceKind.UserInput, fieldName))
                            });
                        }

                        if (stepNode.Children.Count == 0)
                        {
                            stepNode.Children.Add(new AcbelRefMenuNode
                            {
                                Header = AcbelUploadStep.Loc("AcbelRefNoFields", "（未定义字段）")
                            });
                        }
                    }
                    else
                    {
                        stepNode.Children.Add(CreateKindNode("AcbelRefExecutionResult", "执行结果", step.Uid, AcbelReferenceKind.ExecutionResult));
                        stepNode.Children.Add(CreateKindNode("AcbelRefOcrText", "OCR 文本", step.Uid, AcbelReferenceKind.OcrText));
                        stepNode.Children.Add(CreateKindNode("AcbelRefDirectText", "直接获取文本", step.Uid, AcbelReferenceKind.DirectText));
                        if (step is ScanCodeStep)
                            stepNode.Children.Add(CreateKindNode("AcbelRefScanResult", "扫码结果", step.Uid, AcbelReferenceKind.ScanResult));
                    }

                    root.Children.Add(stepNode);
                }

                nodes.Add(root);
                nodes.Add(new AcbelRefMenuNode
                {
                    Header = AcbelUploadStep.Loc("AcbelRefClear", "清除引用"),
                    Command = ClearReferenceCommand
                });
                return nodes;
            }
        }

        /// <summary>选中某步骤的某种结果作为本次键入内容。</summary>
        public void SetReference(Guid stepUid, AcbelReferenceKind kind, string fieldName = null)
        {
            if (stepUid == Guid.Empty || kind == AcbelReferenceKind.None) return;
            if (kind == AcbelReferenceKind.UserInput && string.IsNullOrWhiteSpace(fieldName)) return;

            _refStepUid = stepUid;
            _refKind = kind;
            _refFieldName = kind == AcbelReferenceKind.UserInput ? fieldName.Trim() : string.Empty;
            RaiseReferenceChanged();
        }

        /// <summary>清除引用，恢复键入固定文本。</summary>
        public void ClearReference()
        {
            if (!IsReferenced) return;
            _refStepUid = null;
            _refKind = null;
            _refFieldName = string.Empty;
            RaiseReferenceChanged();
        }

        [JsonIgnore]
        public ICommand ClearReferenceCommand => _clearReferenceCommand ??= new RelayCommand(ClearReference);

        /// <summary>
        /// 解析引用值；未引用返回 null（调用方用 TextToType）。
        /// 引用的步骤本次还没执行过时返回空串（与上传步骤的引用口径一致）。
        /// </summary>
        public string ResolveReferencedText()
        {
            if (!IsReferenced) return null;

            try
            {
                if (_refKind.Value == AcbelReferenceKind.UserInput)
                {
                    return StepExecutionContext.TryGetUserInput(_refStepUid.Value, _refFieldName, out string userInput)
                        ? (userInput ?? string.Empty)
                        : string.Empty;
                }

                StepExecutionResult result;
                string threadName;
                if (!StepExecutionContext.TryGetLatestResult(_refStepUid.Value, out result, out threadName) || result == null)
                    return string.Empty;

                switch (_refKind.Value)
                {
                    case AcbelReferenceKind.ExecutionResult:
                        return result.IsTrue ? "PASS" : "FAIL";
                    case AcbelReferenceKind.ScanResult:
                        return result.OCRText ?? string.Empty;
                    case AcbelReferenceKind.OcrText:
                        return IsDirectTextSource(result.TextSource) ? string.Empty : (result.OCRText ?? string.Empty);
                    case AcbelReferenceKind.DirectText:
                        return IsDirectTextSource(result.TextSource) ? (result.OCRText ?? string.Empty) : string.Empty;
                    default:
                        return string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsDirectTextSource(string textSource)
            => string.Equals(textSource, "Direct", StringComparison.OrdinalIgnoreCase);

        private static List<string> CollectUserInputFieldNames(UserInputStep step)
        {
            var names = new List<string>();
            try
            {
                if (step.Fields != null)
                {
                    foreach (var field in step.Fields)
                    {
                        if (field == null || string.IsNullOrWhiteSpace(field.Name)) continue;
                        string name = field.Name.Trim();
                        if (!names.Contains(name)) names.Add(name);
                    }
                }

                foreach (string name in StepExecutionContext.GetUserInputFieldNamesAll(step.Uid))
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string trimmed = name.Trim();
                    if (!names.Contains(trimmed)) names.Add(trimmed);
                }
            }
            catch { }
            return names;
        }

        private AcbelRefMenuNode CreateKindNode(string key, string fallback, Guid stepUid, AcbelReferenceKind kind)
        {
            return new AcbelRefMenuNode
            {
                Header = AcbelUploadStep.Loc(key, fallback),
                StepUid = stepUid,
                Kind = kind,
                Command = new RelayCommand(() => SetReference(stepUid, kind))
            };
        }

        private void RaiseReferenceChanged()
        {
            OnPropertyChanged(nameof(IsReferenced));
            OnPropertyChanged(nameof(ReferenceLabel));
        }

        #region 构造
        public TypeTextStep() : base()
        {
            Type = StepType.TypeText;
        }
        public TypeTextStep(string name) : base()
        {
            Type = StepType.TypeText;
            Name = name;
        }
        public TypeTextStep(string name, string description) : base()
        {
            Type = StepType.TypeText;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new TypeTextStep(Name, Description)
            {
                TextToType = TextToType,
                DelayBetweenKeys = DelayBetweenKeys,
                RefStepUid = RefStepUid,
                RefKind = RefKind,
                RefFieldName = RefFieldName,
                WaitTime = WaitTime,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                GotoLimitGotoUid = GotoLimitGotoUid, TrueGotoMaxCount = TrueGotoMaxCount, FalseGotoMaxCount = FalseGotoMaxCount, TrueGotoTimeoutSeconds = TrueGotoTimeoutSeconds, FalseGotoTimeoutSeconds = FalseGotoTimeoutSeconds,
            };
        }
        #endregion

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            await Task.Delay((int)(WaitTime * 1000), cancellationToken);
            // 引用了其他步骤时，本次键入用引用值（TextToType 保留原固定文本，清除引用后恢复）
            string text = ResolveReferencedText() ?? TextToType;
            var res = await Task.Run(() =>
            {
                if (DelayBetweenKeys < 0.01)
                {
                    return Autogui.TypeTextSafe(text);
                }
                else
                {
                    return Autogui.TypeText(text, (int)(DelayBetweenKeys * 1000));
                }
            });
            IsTrue = res;
            IsError = false;
            ErrorType = StepErrorType.None;

            // 记下这次实际键入的文本，供其他步骤/结果判断引用
            LastResult ??= new StepExecutionResult();
            LastResult.TypedText = text;

            return res;
        }
    }

    public class TypeTextMoreStep : AutomationStepBase
    {
        private string _textToType;
        private double _delayBetweenKeys = 0.01;
        private string _prefix;
        private string _infix;
        private string _suffix;
        private bool _prefix_gen = false;
        private bool _infix_gen = false;
        private bool _suffix_gen = false;
        private bool _reloadText = false;
        private string _prefix_;
        private string _infix_;
        private string _suffix_;


        /// <summary>
        /// 输入内容
        /// </summary>
        public string TextToType { get => _textToType; set => SetProperty(ref _textToType, value); }

        public double DelayBetweenKeys { get => _delayBetweenKeys; set => SetProperty(ref _delayBetweenKeys, value); }

        public string Prefix
        {
            get => _prefix; set
            {
                if (SetProperty(ref _prefix, value))
                {
                    _prefix_ = value;
                    TextToType = _prefix_ + _infix_ + _suffix_;
                }
            }
        }

        public string Infix
        {
            get => _infix; set
            {
                if (SetProperty(ref _infix, value))
                {
                    _infix_ = value;
                    TextToType = _prefix_ + _infix_ + _suffix_;
                }
            }
        }

        public string Suffix
        {
            get => _suffix; set
            {
                if (SetProperty(ref _suffix, value))
                {
                    _suffix_ = value;
                    TextToType = _prefix_ + _infix_ + _suffix_;
                }
            }
        }

        public bool Prefix_gen { get => _prefix_gen; set => SetProperty(ref _prefix_gen, value); }

        public bool Infix_gen { get => _infix_gen; set => SetProperty(ref _infix_gen, value); }

        public bool Suffix_gen { get => _suffix_gen; set => SetProperty(ref _suffix_gen, value); }

        public bool ReloadText { get => _reloadText; set => SetProperty(ref _reloadText, value); }

        #region 构造
        public TypeTextMoreStep() : base()
        {
            Type = StepType.TypeTextMore;
        }
        public TypeTextMoreStep(string name) : base()
        {
            Type = StepType.TypeTextMore;
            Name = name;
        }
        public TypeTextMoreStep(string name, string description) : base()
        {
            Type = StepType.TypeTextMore;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new TypeTextMoreStep(Name, Description)
            {
                Prefix = Prefix,
                Infix = Infix,
                Suffix = Suffix,
                ReloadText = ReloadText,
                Prefix_gen = Prefix_gen,
                Infix_gen = Infix_gen,
                Suffix_gen = Suffix_gen,
                WaitTime = WaitTime,
                TextToType = TextToType,
                DelayBetweenKeys = DelayBetweenKeys,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                GotoLimitGotoUid = GotoLimitGotoUid, TrueGotoMaxCount = TrueGotoMaxCount, FalseGotoMaxCount = FalseGotoMaxCount, TrueGotoTimeoutSeconds = TrueGotoTimeoutSeconds, FalseGotoTimeoutSeconds = FalseGotoTimeoutSeconds,
            };
        }
        #endregion

        private void Increment()
        {
            if (Prefix_gen)
            {
                _prefix_ = Autogui.IncrementString(_prefix_);
            }
            if (Infix_gen)
            {
                _infix_ = Autogui.IncrementString(_infix_);
            }
            if (Suffix_gen)
            {
                _suffix_ = Autogui.IncrementString(_suffix_);
            }
            TextToType = _prefix_ + _infix_ + _suffix_;
        }

        public void Reload()
        {
            _prefix_ = _prefix;
            _infix_ = _infix;
            _suffix_ = _suffix;
            TextToType = _prefix_ + _infix_ + _suffix_;
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            await Task.Delay((int)(WaitTime * 1000), cancellationToken);
            var res = await Task.Run(() =>
            {
                if (DelayBetweenKeys >= 0.01)
                    return Autogui.TypeText(TextToType, (int)(DelayBetweenKeys * 1000));
                else
                    return Autogui.TypeTextSafe(TextToType);
            });

            Increment();
            IsTrue = res;
            IsError = false;
            ErrorType = StepErrorType.None;

            // 记下这次实际键入的文本，供其他步骤/结果判断引用
            LastResult ??= new StepExecutionResult();
            LastResult.TypedText = TextToType;

            return res;
        }
    }

    /// <summary>
    /// 文本文件逐条输入步骤：按 Index 顺序依次输入文件中的一条内容。
    /// 文本源耗尽（Index >= Contents.Count）的语义（Q6）：结束该输入源并报错，不重置 Index。
    /// 抛出 IndexOutOfRangeException（它是密封类，无法派生专用类型；该类型正是执行引擎
    /// InferErrorType 识别为 StepErrorType.IndexOutOfRange 的类型，错误类型不会被覆盖成 Unknown）：
    /// - 未配置 FalseGoto：执行引擎停止流程；
    /// - 配置了 FalseGoto：跳转到失败分支。
    /// 因为 Index 不重置，重复进入该步骤会再次报同样的错，不会静默从头循环形成死循环。
    /// </summary>
    public partial class TypeTextFromFileStep : AutomationStepBase
    {
        private FileServices _fileServices;
        private FileServices FileServices => _fileServices ?? (_fileServices = SingletonLocator.FileServices);

        private string _filePath;
        private string _textToType;
        private double _delayBetweenKeys = 0.01;
        private bool _reloadIndex = false;
        private int _index = 0;
        private ObservableCollection<ContentItem> _contents = [];
        private string[] _delimiter = ["\n", "\r", "\n\r", "\t", ",", ";", "|"];


        public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }

        /// <summary>
        /// 输入内容
        /// </summary>
        public string TextToType { get => _textToType; set => SetProperty(ref _textToType, value); }

        public double DelayBetweenKeys { get => _delayBetweenKeys; set => SetProperty(ref _delayBetweenKeys, value); }

        public int Index { get => _index; set => SetProperty(ref _index, value); }

        /// <summary>
        /// 待输入内容
        /// </summary>
        [JsonConverter(typeof(ContentItemCollectionConverter))]
        public ObservableCollection<ContentItem> Contents { get => _contents; set => SetProperty(ref _contents, value); }

        /// <summary>
        /// 分割符
        /// </summary>
        [JsonIgnore]
        public string[] Delimiter { get => _delimiter; set => SetProperty(ref _delimiter, value); }
        public bool ReloadIndex { get => _reloadIndex; set => SetProperty(ref _reloadIndex, value); }



        #region 构造
        public TypeTextFromFileStep() : base()
        {
            Type = StepType.TypeTextFromFile;
        }
        public TypeTextFromFileStep(string name) : base()
        {
            Type = StepType.TypeTextFromFile;
            Name = name;
        }
        public TypeTextFromFileStep(string name, string description) : base()
        {
            Type = StepType.TypeTextFromFile;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new TypeTextFromFileStep(Name, Description)
            {
                FilePath = FilePath,
                TextToType = TextToType,
                DelayBetweenKeys = DelayBetweenKeys,
                Contents = new ObservableCollection<ContentItem>(Contents.Select(c => new ContentItem(c.Text))),
                ReloadIndex = ReloadIndex,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                Index = Index,
                EnableLog = EnableLog,
                GotoLimitGotoUid = GotoLimitGotoUid, TrueGotoMaxCount = TrueGotoMaxCount, FalseGotoMaxCount = FalseGotoMaxCount, TrueGotoTimeoutSeconds = TrueGotoTimeoutSeconds, FalseGotoTimeoutSeconds = FalseGotoTimeoutSeconds,
            };
        }
        #endregion


        [RelayCommand]
        private void OpenFile()
        {
            var path = PathServices.OpenPathDialog(LanguageService.GetLocalizedString("OpenFile"), "Text|*.txt;*.csv|Xlsx|*.xlsx");
            if (path != null) FilePath = path;
            LoadFile();
        }

        [RelayCommand]
        private void AddContent()
        {
            Contents.Add(new ContentItem(""));
        }

        [RelayCommand]
        private void DelContent()
        {
            if (Contents.Count > 0)
                Contents.RemoveAt(Contents.Count - 1);
        }

        [RelayCommand]
        public void ResetIndex()
        {
            Index = 0;
        }

        private void LoadFile()
        {
            if (FilePath == null) return;
            if (new List<string> { ".txt", ".csv" }.Contains(Path.GetExtension(FilePath).ToLower()))
            {
                string res = FileServices.SmartReadTextFile(FilePath);
                Contents.Clear();
                foreach (var item in res.Split(Delimiter, StringSplitOptions.RemoveEmptyEntries))
                    Contents.Add(new ContentItem(item));
                // 重新载入后从头开始，避免残留旧索引一进来就被判定为“已耗尽”
                Index = 0;
            }
            else if (Path.GetExtension(FilePath).ToLower() == ".xlsx")
            {
                FileInfo fileInfo = new(FilePath);
                using ExcelPackage package = new(fileInfo);
                ExcelWorksheet ws = package.Workbook.Worksheets[0];

                Contents.Clear();
                // 空表（未使用任何单元格）时 ws.Dimension 为 null，直接访问会空引用，需防御
                var dimension = ws.Dimension;
                if (dimension == null)
                {
                    Index = 0;
                    return;
                }
                for (int r = 1; r <= dimension.End.Row; r++)
                {
                    Contents.Add(new ContentItem(ws.Cells[r, 1].Text));
                }
                Index = 0;
            }
            else
            {
                throw new Exception("No support file type.");
            }
        }


        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            // 文本源耗尽（Index >= Contents.Count）的语义见类注释：结束该输入源并报错，不重置 Index。
            if (Contents != null && Contents.Count > 0)
            {
                if (Index >= Contents.Count)
                {
                    IsTrue = false;
                    IsError = true;
                    ErrorType = StepErrorType.IndexOutOfRange;
                    ErrorMessage = string.Format(
                        LanguageService.GetLocalizedString("TypeTextFromFile_ContentsFinished", "{0}'s Contents is finished."), Name);
                    throw new IndexOutOfRangeException(ErrorMessage);
                }
                TextToType = Contents[Index].Text;
            }
            await Task.Delay((int)(WaitTime * 1000), cancellationToken);
            var res = await Task.Run(() =>
            {
                if (DelayBetweenKeys <= 0.01)
                    return Autogui.TypeTextSafe(TextToType, (int)(DelayBetweenKeys * 1000));
                else
                    return Autogui.TypeText(TextToType, (int)(DelayBetweenKeys * 1000));
            });
            IsTrue = res;
            IsError = false;
            ErrorType = StepErrorType.None;

            // 输入成功后才推进索引：输入失败不消耗一条内容
            if (res && Contents != null && Contents.Count > 0)
                Index++;

            // 记下这次实际键入的文本，供其他步骤/结果判断引用
            LastResult ??= new StepExecutionResult();
            LastResult.TypedText = TextToType;

            return res;
        }
    }


    // 弹出框步骤
    public class PopupStep : AutomationStepBase
    {
        private string _title;
        public string Title { get => _title; set => SetProperty(ref _title, value); }


        private string _popupText;
        public string PopupText { get => _popupText; set => SetProperty(ref _popupText, value); }

        private FontModel _popupFont = new()
        {
            FontFamily = "Arial",
            FontSize = 14,
            FontWeight = FontWeights.Regular,
            FontStyle = FontStyles.Normal,
            FontColor = 0x000000,
        };
        public FontModel PopupFont { get => _popupFont; set => SetProperty(ref _popupFont, value); }


        private string _popupType = "Information";
        public string PopupType { get => _popupType; set => SetProperty(ref _popupType, value); }

        private PopupWindowStyle _popupWindowStyle = PopupWindowStyle.Normal;
        /// <summary>窗口样式（正常/紧凑）</summary>
        public PopupWindowStyle PopupWindowStyle { get => _popupWindowStyle; set => SetProperty(ref _popupWindowStyle, value); }

        private PopupButtons _popupButtons = PopupButtons.OK;
        public PopupButtons PopupButtons { get => _popupButtons; set => SetProperty(ref _popupButtons, value); }

        private bool _pauseRunOnShow;
        /// <summary>弹出弹窗的同时暂停运行：弹窗出来后流程停下，关掉弹窗就自动继续</summary>
        public bool PauseRunOnShow { get => _pauseRunOnShow; set => SetProperty(ref _pauseRunOnShow, value); }

        #region 关闭模式

        private PopupCloseMode _closeMode = PopupCloseMode.ButtonClick;
        /// <summary>
        /// 关闭方式（Flags，可多选组合）：ButtonClick=点击按钮, Timeout=超时自动关闭, StepReached=到达步骤关闭。
        /// 组合后满足任一条件即关闭。
        /// </summary>
        [JsonConverter(typeof(ShaoLu.Converters.PopupCloseModeJsonConverter))]
        public PopupCloseMode CloseMode
        {
            get => _closeMode;
            set
            {
                if (SetProperty(ref _closeMode, value))
                {
                    OnPropertyChanged(nameof(CloseByButtonClick));
                    OnPropertyChanged(nameof(CloseByTimeout));
                    OnPropertyChanged(nameof(CloseByStepReached));
                }
            }
        }

        /// <summary>是否启用点击按钮关闭</summary>
        [JsonIgnore]
        public bool CloseByButtonClick
        {
            get => CloseMode.HasFlag(PopupCloseMode.ButtonClick);
            set => SetCloseFlag(PopupCloseMode.ButtonClick, value);
        }

        /// <summary>是否启用超时自动关闭</summary>
        [JsonIgnore]
        public bool CloseByTimeout
        {
            get => CloseMode.HasFlag(PopupCloseMode.Timeout);
            set => SetCloseFlag(PopupCloseMode.Timeout, value);
        }

        /// <summary>是否启用到达步骤关闭</summary>
        [JsonIgnore]
        public bool CloseByStepReached
        {
            get => CloseMode.HasFlag(PopupCloseMode.StepReached);
            set => SetCloseFlag(PopupCloseMode.StepReached, value);
        }

        /// <summary>按位设置/清除某个关闭方式</summary>
        private void SetCloseFlag(PopupCloseMode flag, bool enabled)
        {
            CloseMode = enabled ? (CloseMode | flag) : (CloseMode & ~flag);
        }

        private double _autoCloseSeconds = 5;
        /// <summary>Timeout 模式下的自动关闭时间(秒)</summary>
        public double AutoCloseSeconds { get => _autoCloseSeconds; set => SetProperty(ref _autoCloseSeconds, value); }

        private Guid? _closeOnStepUid;
        /// <summary>StepReached 模式下，到达此步骤时关闭弹窗</summary>
        public Guid? CloseOnStepUid { get => _closeOnStepUid; set => SetProperty(ref _closeOnStepUid, value); }

        #endregion

        #region 窗口外观

        private bool _windowPositionRelative;
        /// <summary>窗口位置是否为屏幕相对坐标（WindowX/WindowY 存占屏幕宽/高的百分比；切换时自动换算）</summary>
        public bool WindowPositionRelative
        {
            get => _windowPositionRelative;
            set
            {
                if (!SetProperty(ref _windowPositionRelative, value)) return;
                // -1 为"居中显示"的哨兵值，不参与换算
                if (WindowX < 0 && WindowY < 0) return;
                WindowX = ScreenCoordinates.ConvertX(WindowX, !value, value);
                WindowY = ScreenCoordinates.ConvertY(WindowY, !value, value);
            }
        }

        private double _windowX = -1;
        /// <summary>窗口 X 位置（-1 表示居中显示；相对模式下为占屏幕宽度的百分比）</summary>
        public double WindowX { get => _windowX; set => SetProperty(ref _windowX, value); }

        private double _windowY = -1;
        /// <summary>窗口 Y 位置（-1 表示居中显示；相对模式下为占屏幕高度的百分比）</summary>
        public double WindowY { get => _windowY; set => SetProperty(ref _windowY, value); }

        private double _windowOpacity = 100;
        /// <summary>窗口背景不透明度（百分比 0~100）</summary>
        public double WindowOpacity
        {
            get => _windowOpacity;
            set => SetProperty(ref _windowOpacity, Math.Min(100.0, Math.Max(1.0, value)));
        }

        private string _backgroundColor = "#FFFFFF";
        /// <summary>窗口背景颜色（十六进制）</summary>
        public string BackgroundColor { get => _backgroundColor; set => SetProperty(ref _backgroundColor, value); }

        [JsonIgnore]
        private ICommand selectPositionCommand;
        [JsonIgnore]
        public ICommand SelectPositionCommand => selectPositionCommand ??= new RelayCommand(SelectPosition);

        [JsonIgnore]
        private ICommand selectBackgroundColorCommand;
        [JsonIgnore]
        public ICommand SelectBackgroundColorCommand => selectBackgroundColorCommand ??= new RelayCommand(SelectBackgroundColor);

        private void SelectPosition()
        {
            var point = Views.WindowSelectPoint.ShowAndSelect();
            if (point.HasValue)
            {
                // 相对模式下存百分比，绝对模式下存逻辑像素
                WindowX = ScreenCoordinates.ConvertX(point.Value.X, false, WindowPositionRelative);
                WindowY = ScreenCoordinates.ConvertY(point.Value.Y, false, WindowPositionRelative);
            }
        }

        private void SelectBackgroundColor()
        {
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(BackgroundColor ?? "#FFFFFF");
                ColorDialog dialog = new() { Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B) };
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    BackgroundColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                }
            }
            catch
            {
                ColorDialog dialog = new() { Color = System.Drawing.Color.White };
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    BackgroundColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                }
            }
        }

        #endregion

        [JsonIgnore]
        public List<PopupCloseMode> CloseModes { get; } = [PopupCloseMode.ButtonClick, PopupCloseMode.Timeout, PopupCloseMode.StepReached];

        /// <summary>运行时引用：当前活跃的弹窗实例（供 StepReached 模式使用）</summary>
        [JsonIgnore]
        internal WindowAsyncPopup ActivePopupWindow { get; set; }

        /// <summary>运行时引用：本步骤当前还开着的所有弹窗（同时存在个数大于 1 时会有多个）</summary>
        [JsonIgnore]
        internal List<WindowAsyncPopup> ActivePopups { get; } = new List<WindowAsyncPopup>();

        private int _maxWindows = 1;
        /// <summary>
        /// 同时存在的窗口个数（默认 1）。已经开着的窗口达到该数量时，本次执行不再重复弹出。
        /// </summary>
        public int MaxWindows
        {
            get => _maxWindows;
            set => SetProperty(ref _maxWindows, Math.Max(1, Math.Min(20, value)));
        }

        #region 命令

        [JsonIgnore]
        public List<string> PopupTypes { get; set; } = ["None", "Information", "Warning", "Error", "Question"];

        [JsonIgnore]
        public List<PopupWindowStyle> PopupWindowStyles { get; } = [PopupWindowStyle.Normal, PopupWindowStyle.Compact];

        [JsonIgnore]
        private RelayCommand fontSelectCommand;
        [JsonIgnore]
        public RelayCommand FontSelectCommand => fontSelectCommand ??= new RelayCommand(FontSelect);

        [JsonIgnore]
        private RelayCommand colorSelectCommand;
        [JsonIgnore]
        public RelayCommand ColorSelectCommand => colorSelectCommand ??= new RelayCommand(ColorSelect);

        [JsonIgnore]
        private RelayCommand addButtonCommand;
        [JsonIgnore]
        public RelayCommand AddButtonCommand => addButtonCommand ??= new RelayCommand(AddButton);

        [JsonIgnore]
        private RelayCommand delButtonCommand;
        [JsonIgnore]
        public RelayCommand DelButtonCommand => delButtonCommand ??= new RelayCommand(DelButton);

        [JsonIgnore]
        private RelayCommand richTextEditCommand;
        [JsonIgnore]
        public RelayCommand RichTextEditCommand => richTextEditCommand ??= new RelayCommand(RichTextEdit);

        [JsonIgnore]
        private RelayCommand testPopupCommand;
        /// <summary>测试弹窗效果（按当前配置弹一次，不影响运行）</summary>
        [JsonIgnore]
        public RelayCommand TestPopupCommand => testPopupCommand ??= new RelayCommand(TestPopup);

        #endregion

        private void RichTextEdit()
        {
            var result = Views.WindowRichTextEditor.ShowDialog(PopupText);
            if (result != null)
                PopupText = result;
        }

        public PopupStep() : base()
        {
            this.Type = StepType.Popup;
        }
        public PopupStep(string name) : base()
        {
            Type = StepType.Popup;
            Name = name;
            Title = name;
        }
        public PopupStep(string name, string description) : base()
        {
            Type = StepType.Popup;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new PopupStep(Name, Description)
            {
                IsTrue = IsTrue,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                EnableLog = EnableLog,
                GotoLimitGotoUid = GotoLimitGotoUid, TrueGotoMaxCount = TrueGotoMaxCount, FalseGotoMaxCount = FalseGotoMaxCount, TrueGotoTimeoutSeconds = TrueGotoTimeoutSeconds, FalseGotoTimeoutSeconds = FalseGotoTimeoutSeconds,
                Title = Title,
                PopupText = PopupText,
                PopupFont = PopupFont?.Clone(),
                PopupType = PopupType,
                PopupWindowStyle = PopupWindowStyle,
                PopupButtons = new PopupButtons(PopupButtons),
                CloseMode = CloseMode,
                PauseRunOnShow = PauseRunOnShow,
                AutoCloseSeconds = AutoCloseSeconds,
                CloseOnStepUid = CloseOnStepUid,
                WindowPositionRelative = WindowPositionRelative,
                WindowX = WindowX,
                WindowY = WindowY,
                WindowOpacity = WindowOpacity,
                BackgroundColor = BackgroundColor,
                MaxWindows = MaxWindows,
                WaitTime = WaitTime,
                IsNeed = IsNeed,
            };
        }

        /// <summary>按当前弹窗类型取系统图标</summary>
        private MessageBoxImage IconTypeOf() => PopupType switch
        {
            "None" => MessageBoxImage.None,
            "Information" => MessageBoxImage.Information,
            "Warning" => MessageBoxImage.Warning,
            "Error" => MessageBoxImage.Error,
            "Question" => MessageBoxImage.Question,
            _ => MessageBoxImage.Information
        };

        /// <summary>按当前配置弹出一个窗口，并登记到活跃窗口列表</summary>
        private (Window Window, Task<string> Task) ShowPopupWindow(MessageBoxImage iconType)
        {
            double popupX = ScreenCoordinates.ToPixelX(WindowX, WindowPositionRelative);
            double popupY = ScreenCoordinates.ToPixelY(WindowY, WindowPositionRelative);
            System.Windows.Point? position = (popupX >= 0 && popupY >= 0) ? new System.Windows.Point(popupX, popupY) : null;

            var result = WindowAsyncPopup.Show(PopupText, Title, PopupFont, PopupButtons, iconType,
                position, WindowOpacity / 100.0, BackgroundColor, PopupWindowStyle);

            if (result.Window is WindowAsyncPopup created)
            {
                ActivePopups.Add(created);
                created.Closed += (s, e) => ActivePopups.Remove(created);
                ActivePopupWindow = created;
            }

            return result;
        }

        /// <summary>测试：按当前配置直接弹一次（不参与运行结果判定，可随便点按钮关掉）</summary>
        private void TestPopup()
        {
            try
            {
                var (window, _) = ShowPopupWindow(IconTypeOf());
                window?.Activate();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Test popup failed: {0}", ex.Message);
            }
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            // 清掉已经关掉的窗口；已经开着的达到「同时存在个数」上限时不再重复弹出
            ActivePopups.RemoveAll(w => w == null || !w.IsVisible);
            if (ActivePopups.Count >= MaxWindows)
            {
                IsTrue = true;
                LastResult = new StepExecutionResult
                {
                    IsTrue = true,
                    ExecutedAt = DateTime.Now,
                };
                return true;
            }

            var iconType = IconTypeOf();

            // 关闭方式可组合：未勾选任何方式时按点击按钮处理，避免无限等待
            var closeMode = CloseMode;
            if ((closeMode & (PopupCloseMode.ButtonClick | PopupCloseMode.Timeout | PopupCloseMode.StepReached)) == 0)
                closeMode |= PopupCloseMode.ButtonClick;

            bool closeByStepReached = closeMode.HasFlag(PopupCloseMode.StepReached);
            bool closeByTimeout = closeMode.HasFlag(PopupCloseMode.Timeout) && AutoCloseSeconds > 0;
            string defaultResult = PopupButtons.DefaultButton?.Value ?? string.Empty;

            // 1. 启动异步弹窗任务（应用自定义位置、背景颜色、背景不透明度与窗口样式）
            var (popupWindow, popupTask) = ShowPopupWindow(iconType);

            // 弹出时暂停运行：弹窗一出来就置暂停，关掉弹窗后自动继续（本来就在暂停的，保持暂停不动）
            if (PauseRunOnShow)
            {
                bool pausedByPopup = false;
                try
                {
                    var steps = SingletonLocator.Steps;
                    if (!steps.IsPaused)
                    {
                        steps.IsPaused = true;
                        pausedByPopup = true;
                    }
                }
                catch (Exception ex) { NLog.LogManager.GetCurrentClassLogger().Warn(ex, "Popup: pause run failed"); }

                if (pausedByPopup && popupWindow != null)
                {
                    popupWindow.Closed += (s, e) =>
                    {
                        try
                        {
                            var steps = SingletonLocator.Steps;
                            if (steps.IsPaused) steps.IsPaused = false;
                        }
                        catch (Exception ex) { NLog.LogManager.GetCurrentClassLogger().Warn(ex, "Popup: resume run failed"); }
                    };
                }
            }

            // StepReached 模式：不等待窗口关闭，直接继续执行；到达目标步骤时由执行引擎关闭弹窗。
            // 若同时勾选了超时，则后台计时到点后自动关闭；停止运行时也关闭。
            if (closeByStepReached)
            {
                var stopRegistration = cancellationToken.Register(() => ClosePopupWindow(popupWindow));
                if (popupWindow != null)
                {
                    popupWindow.Closed += (s, e) =>
                    {
                        stopRegistration.Dispose();
                        ActivePopupWindow = null;
                    };
                }

                if (closeByTimeout)
                {
                    // 这里不跟随步骤令牌：步骤执行结束后引擎会释放该步骤的取消源，
                    // 若计时用的令牌随之失效，弹窗就再也不会按超时自动关闭
                    _ = ClosePopupAfterTimeoutAsync(popupWindow, TimeSpan.FromSeconds(AutoCloseSeconds), defaultResult, CancellationToken.None);
                }

                IsTrue = true;
                LastResult = new StepExecutionResult
                {
                    IsTrue = true,
                    ExecutedAt = DateTime.Now,
                };
                return IsTrue;
            }

            // 2. 等待用户点击按钮超时自动关闭停止运行中的任意一个条件先满足
            try
            {
                using (cancellationToken.Register(() => ClosePopupWindow(popupWindow)))
                {
                    var cancelTask = new TaskCompletionSource<bool>();
                    using (cancellationToken.Register(() => cancelTask.TrySetResult(true)))
                    {
                        var waitTasks = new List<Task> { popupTask, cancelTask.Task };
                        Task timeoutTask = null;
                        if (closeByTimeout)
                        {
                            timeoutTask = Task.Delay(TimeSpan.FromSeconds(AutoCloseSeconds), cancellationToken);
                            waitTasks.Add(timeoutTask);
                        }

                        var completedTask = await Task.WhenAny(waitTasks);

                        if (timeoutTask != null && completedTask == timeoutTask)
                        {
                            // 超时自动关闭：返回默认按钮值，不等待窗口关闭
                            if (popupWindow is WindowAsyncPopup popup)
                                popup.CloseWithResult(defaultResult);
                            IsTrue = PopupResultToBool(defaultResult);
                            LastResult = new StepExecutionResult
                            {
                                IsTrue = IsTrue,
                                PopupResult = defaultResult,
                                ExecutedAt = DateTime.Now,
                            };
                            return IsTrue;
                        }

                        if (completedTask == cancelTask.Task)
                        {
                            ClosePopupWindow(popupWindow);
                            IsTrue = false;
                            return false;
                        }

                        // 用户点击了弹窗按钮（或被外部关闭）
                        var popupResult = await popupTask;
                        IsTrue = PopupResultToBool(popupResult);
                        LastResult = new StepExecutionResult
                        {
                            IsTrue = IsTrue,
                            PopupResult = popupResult,
                            ExecutedAt = DateTime.Now,
                        };
                        return IsTrue;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Popup error: {ex.Message}");
                ClosePopupWindow(popupWindow);
                IsTrue = false;
                return IsTrue;
            }
            finally
            {
                ActivePopupWindow = null;
            }
        }

        /// <summary>在 UI 线程上关闭弹窗（已关闭或调度器停止时静默忽略）</summary>
        private static void ClosePopupWindow(Window window)
        {
            if (window == null) return;
            try
            {
                if (window.Dispatcher.HasShutdownStarted) return;
                window.Dispatcher.InvokeAsync(() =>
                {
                    if (window.IsVisible)
                        window.Close();
                });
            }
            catch { }
        }

        /// <summary>StepReached 同时勾选超时时：计时到达后关闭弹窗</summary>
        private async Task ClosePopupAfterTimeoutAsync(Window window, TimeSpan timeout, string result, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(timeout, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (window is WindowAsyncPopup popup)
                popup.CloseWithResult(result);
            else
                ClosePopupWindow(window);
            ActivePopupWindow = null;
        }
        /// <summary>
        /// 弹窗返回值转布尔：点击按钮 OK/Yes 为 true，No/Cancel 为 false；
        /// 右上角叉关闭时返回空字符串，同样为 false
        /// </summary>
        private static bool PopupResultToBool(string result) =>
            result == PopupButton.OKValue || result == PopupButton.YesValue;

        private void FontSelect()
        {
            PopupFont ??= new();
            var fontDialog = new FontDialog()
            {
                Font = PopupFont?.Font ?? new System.Drawing.Font("Arial", 12)
            };

            if (fontDialog.ShowDialog() == DialogResult.OK)
            {
                // 获取选择的字体信息
                System.Drawing.Font selectedFont = fontDialog.Font;

                // 将 Windows Forms 的字体转换为 WPF 的字体属性
                FontStyle fontStyle = selectedFont.Italic ? FontStyles.Italic : FontStyles.Normal;
                FontWeight fontWeight = selectedFont.Bold ? FontWeights.Bold : FontWeights.Normal;

                // 将选择的字体应用到 WPF 控件上（例如名为 TextBlockSample 的 TextBlock）
                PopupFont.FontFamily = selectedFont.FontFamily.Name;
                PopupFont.FontSize = selectedFont.Size;
                PopupFont.FontStyle = fontStyle;
                PopupFont.FontWeight = fontWeight;

                PopupFont.Style = selectedFont.Style;
                PopupFont.Unit = selectedFont.Unit;
            }
        }

        private void ColorSelect()
        {
            PopupFont ??= new();
            ColorDialog dialog = new()
            {
                Color = System.Drawing.Color.FromArgb(PopupFont.FontColor)
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                PopupFont.FontColor = dialog.Color.ToArgb();
            }
        }

        private void AddButton()
        {
            PopupButtons.Buttons.Add(new PopupButton());
        }

        private void DelButton()
        {
            if (PopupButtons.Buttons.Count > 0)
            {
                PopupButtons.Buttons.RemoveAt(PopupButtons.Buttons.Count - 1);
            }
        }
    }

    /// <summary>结束步骤：执行到这里立即结束本次运行（等同中断步骤的「结束本次运行」）</summary>
    public class EndStep : AutomationStepBase
    {
        public EndStep() : base()
        {
            IsTrue = true;
            Type = StepType.End;
        }

        public EndStep(string name) : base()
        {
            IsTrue = true;
            Type = StepType.End;
            Name = name;
        }

        public EndStep(string name, string description) : base()
        {
            IsTrue = true;
            Type = StepType.End;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new EndStep(Name, Description)
            {
                WaitTime = WaitTime,
                IsTrue = IsTrue,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
            };
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            await Task.Delay((int)(WaitTime * 1000), cancellationToken);

            // 记一个「结束本次运行」请求：主循环处理下一步之前会看到它并跳出
            try { SingletonLocator.Steps.RequestEndRun(); }
            catch (Exception ex) { NLog.LogManager.GetCurrentClassLogger().Warn(ex, "End step: request end run failed"); }

            IsTrue = true;
            LastResult = new StepExecutionResult { IsTrue = true, ExecutedAt = DateTime.Now };
            return true;
        }
    }
    public class EmptyStep : AutomationStepBase
    {
        /// <summary>
        /// 内部占位构造（Uid=Guid.Empty）
        /// </summary>
        internal EmptyStep(bool placeholder) : base(placeholder)
        {
            IsTrue = true;
            Type = StepType.Empty;
        }
        public EmptyStep() : base()
        {
            IsTrue = true;
            Type = StepType.Empty;
        }
        public EmptyStep(string name) : base()
        {
            IsTrue = true;
            Type = StepType.Empty;
            Name = name;
        }
        public EmptyStep(string name, string description) : base()
        {
            IsTrue = true;
            Type = StepType.Empty;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new EmptyStep(Name, Description)
            {
                WaitTime = WaitTime,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                GotoLimitGotoUid = GotoLimitGotoUid, TrueGotoMaxCount = TrueGotoMaxCount, FalseGotoMaxCount = FalseGotoMaxCount, TrueGotoTimeoutSeconds = TrueGotoTimeoutSeconds, FalseGotoTimeoutSeconds = FalseGotoTimeoutSeconds,
            };
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            await Task.Delay((int)(WaitTime * 1000), cancellationToken);
            return IsTrue;
        }
    }

}
