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

        private readonly Guid _uid = Guid.NewGuid();
        /// <summary>
        /// 步骤的唯一uid
        /// </summary>
        public Guid Uid => _uid;

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

        // 自引用次数上限（-1=无限制, 0=禁止自引用, >0=限制次数）
        private int _selfReferenceLimit = 10;
        public int SelfReferenceLimit { get => _selfReferenceLimit; set => SetProperty(ref _selfReferenceLimit, value); }

        // 运行时自引用计数器（不序列化）
        [JsonIgnore]
        public int SelfReferenceCount { get; set; }

        #region 条件判断

        private ConditionMode _conditionMode = ConditionMode.Default;
        /// <summary>
        /// 条件判断模式
        /// </summary>
        public ConditionMode ConditionMode { get => _conditionMode; set => SetProperty(ref _conditionMode, value); }

        private ObservableCollection<StepCondition> _conditions = [];
        /// <summary>
        /// 自定义条件规则行列表
        /// </summary>
        public ObservableCollection<StepCondition> Conditions { get => _conditions; set => SetProperty(ref _conditions, value); }

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

        /// <summary>
        /// 输入内容
        /// </summary>
        public string TextToType { get => _textToType; set => SetProperty(ref _textToType, value); }

        public double DelayBetweenKeys { get => _delayBetweenKeys; set => SetProperty(ref _delayBetweenKeys, value); }

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
                WaitTime = WaitTime,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
            };
        }
        #endregion

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            await Task.Delay((int)WaitTime * 1000, cancellationToken);
            var res = await Task.Run(() =>
            {
                if (DelayBetweenKeys < 0.01)
                {
                    return Autogui.TypeTextSafe(TextToType);
                }
                else
                {
                    return Autogui.TypeText(TextToType, (int)(DelayBetweenKeys * 1000));
                }
            });
            IsTrue = res;
            IsError = false;
            ErrorType = StepErrorType.None;

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
            await Task.Delay((int)WaitTime * 1000, cancellationToken);
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
            return res;
        }
    }

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
            }
            else if (Path.GetExtension(FilePath).ToLower() == ".xlsx")
            {
                FileInfo fileInfo = new(FilePath);
                using ExcelPackage package = new(fileInfo);
                ExcelWorksheet ws = package.Workbook.Worksheets[0];

                Contents.Clear();
                for (int r = 1; r <= ws.Dimension.End.Row; r++)
                {
                    Contents.Add(new ContentItem(ws.Cells[r, 1].Text));
                }
            }
            else
            {
                throw new Exception("No support file type.");
            }
        }


        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            if (Contents != null && Contents.Count > 0)
            {
                if (Index >= Contents.Count)
                {
                    IsTrue = false;
                    IsError = true;
                    ErrorType = StepErrorType.IndexOutOfRange;
                    Index = 0;
                    throw new InvalidOperationException($"{Name}'s Contents is Finished.");
                }
                TextToType = Contents[Index].Text;
                Index++;
            }
            await Task.Delay((int)WaitTime * 1000, cancellationToken);
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

        private PopupButtons _popupButtons = PopupButtons.OK;
        public PopupButtons PopupButtons { get => _popupButtons; set => SetProperty(ref _popupButtons, value); }

        #region 关闭模式

        private PopupCloseMode _closeMode = PopupCloseMode.ButtonClick;
        /// <summary>关闭模式：ButtonClick=点击按钮, Timeout=超时自动关闭, StepReached=到达步骤关闭</summary>
        public PopupCloseMode CloseMode { get => _closeMode; set => SetProperty(ref _closeMode, value); }

        private double _autoCloseSeconds = 5;
        /// <summary>Timeout 模式下的自动关闭时间(秒)</summary>
        public double AutoCloseSeconds { get => _autoCloseSeconds; set => SetProperty(ref _autoCloseSeconds, value); }

        private Guid? _closeOnStepUid;
        /// <summary>StepReached 模式下，到达此步骤时关闭弹窗</summary>
        public Guid? CloseOnStepUid { get => _closeOnStepUid; set => SetProperty(ref _closeOnStepUid, value); }

        [JsonIgnore]
        public List<PopupCloseMode> CloseModes { get; } = [PopupCloseMode.ButtonClick, PopupCloseMode.Timeout, PopupCloseMode.StepReached];

        /// <summary>运行时引用：当前活跃的弹窗实例（供 StepReached 模式使用）</summary>
        [JsonIgnore]
        internal WindowAsyncPopup ActivePopupWindow { get; set; }

        #endregion

        #region 命令

        [JsonIgnore]
        public List<string> PopupTypes { get; set; } = ["Information", "Warning", "Error", "Question"];

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
                Title = Title,
                PopupText = PopupText,
                PopupFont = PopupFont?.Clone(),
                PopupType = PopupType,
                PopupButtons = new PopupButtons(PopupButtons),
                CloseMode = CloseMode,
                AutoCloseSeconds = AutoCloseSeconds,
                CloseOnStepUid = CloseOnStepUid,
                WaitTime = WaitTime,
                IsNeed = IsNeed,
            };
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var iconType = PopupType switch
            {
                "Information" => MessageBoxImage.Information,
                "Warning" => MessageBoxImage.Warning,
                "Error" => MessageBoxImage.Error,
                "Question" => MessageBoxImage.Question,
                _ => MessageBoxImage.Information
            };

            // 1. 启动异步弹窗任务
            var (popupWindow, popupTask) = WindowAsyncPopup.Show(PopupText, Title, PopupFont, PopupButtons, iconType);

            // 保存活跃窗口引用（供 StepReached 模式使用）
            if (popupWindow is WindowAsyncPopup asyncPopup)
            {
                ActivePopupWindow = asyncPopup;
            }

            // 2. 根据关闭模式处理
            try
            {
                using (cancellationToken.Register(() =>
                {
                    if (popupWindow != null && !popupWindow.Dispatcher.HasShutdownStarted)
                    {
                        popupWindow.Dispatcher.InvokeAsync(() =>
                        {
                            if (popupWindow.IsVisible)
                                popupWindow.Close();
                        });
                    }
                }))
                {
                    var cancelTask = new TaskCompletionSource<bool>();
                    using (cancellationToken.Register(() => cancelTask.TrySetResult(true)))
                    {
                        Task completedTask;

                        if (CloseMode == PopupCloseMode.Timeout && AutoCloseSeconds > 0)
                        {
                            // 超时模式：等待弹窗任务、取消任务、超时任务三者之一完成
                            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(AutoCloseSeconds), cancellationToken);
                            completedTask = await Task.WhenAny(popupTask, cancelTask.Task, timeoutTask);

                            if (completedTask == timeoutTask)
                            {
                                // 超时自动关闭，返回默认按钮值，不等待窗口关闭
                                var defaultResult = PopupButtons.DefaultButton?.Value ?? string.Empty;
                                if (popupWindow is WindowAsyncPopup ap)
                                {
                                    ap.CloseWithResult(defaultResult);
                                }
                                IsTrue = (defaultResult == PopupButton.YesValue);
                                LastResult = new StepExecutionResult
                                {
                                    IsTrue = IsTrue,
                                    PopupResult = defaultResult,
                                    ExecutedAt = DateTime.Now,
                                };
                                return IsTrue;
                            }
                        }
                        else
                        {
                            // ButtonClick 或 StepReached 模式：等待弹窗任务或取消任务
                            completedTask = await Task.WhenAny(popupTask, cancelTask.Task);
                        }

                        if (completedTask == cancelTask.Task)
                        {
                            IsTrue = false;
                            return false;
                        }

                        // 用户点击了弹窗按钮（或被外部关闭）
                        var popupResult = await popupTask;
                        IsTrue = (popupResult == PopupButton.YesValue);
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
                IsTrue = false;
                return IsTrue;
            }
            finally
            {
                ActivePopupWindow = null;
            }
        }

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
            };
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            await Task.Delay((int)WaitTime * 1000, cancellationToken);
            return IsTrue;
        }
    }

}
