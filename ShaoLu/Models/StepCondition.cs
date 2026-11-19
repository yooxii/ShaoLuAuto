using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace ShaoLu.Models
{
    /// <summary>
    /// 条件判断模式
    /// </summary>
    public enum ConditionMode
    {
        /// <summary>
        /// 默认模式：使用步骤自身的执行结果
        /// </summary>
        Default,

        /// <summary>
        /// 自定义模式：使用用户配置的规则行进行判断
        /// </summary>
        Custom
    }

    /// <summary>
    /// 条件变量类型
    /// </summary>
    public enum ConditionVariable
    {
        // 常量
        ConstantTrue,
        ConstantFalse,

        // 当前步骤自身
        Self_IsTrue,
        Self_Similarity,
        Self_ExecutionTimeMs,
        /// <summary>本步骤开始时间（以本次运行开始为 0 的毫秒数）</summary>
        Self_StartTimeMs,
        /// <summary>本步骤结束时间（以本次运行开始为 0 的毫秒数）</summary>
        Self_EndTimeMs,
        Self_ClickX,
        Self_ClickY,
        Self_OCRText,
        Self_PopupResult,
        /// <summary>本步骤实际键入的文本（键入文字类步骤）</summary>
        Self_TypedText,

        // 引用其他步骤（通过 StepLineNo 指定）
        Step_IsTrue,
        Step_Similarity,
        Step_ExecutionTimeMs,
        /// <summary>引用步骤开始时间（以本次运行开始为 0 的毫秒数）</summary>
        Step_StartTimeMs,
        /// <summary>引用步骤结束时间（以本次运行开始为 0 的毫秒数）</summary>
        Step_EndTimeMs,
        Step_ClickX,
        Step_ClickY,
        Step_OCRText,
        Step_PopupResult,
        /// <summary>引用步骤实际键入的文本</summary>
        Step_TypedText,
        /// <summary>引用步骤是否在本次评估前刚刚被执行（用于统计执行次数）</summary>
        Step_Triggered,

        // 信号内容（接收信号步骤收到的值的文本形式）
        /// <summary>本步骤收到的信号内容</summary>
        Self_SignalValue,
        /// <summary>引用步骤收到的信号内容</summary>
        Step_SignalValue,

        /// <summary>「用户输入」步骤里指定字段的值（按 FieldName 取）</summary>
        Step_UserInputField,
    }

    /// <summary>
    /// 条件取值来源：常量 / 本步骤 / 引用其他步骤
    /// </summary>
    public enum ConditionSource
    {
        /// <summary>常量：直接选真或假</summary>
        Constant,
        /// <summary>本步骤自身的执行结果等字段</summary>
        Self,
        /// <summary>引用其他步骤的字段</summary>
        Step,
    }

    /// <summary>
    /// 条件取值的具体字段（选定来源之后再选）
    /// </summary>
    public enum ConditionField
    {
        /// <summary>执行结果（成功/失败）</summary>
        Result,
        /// <summary>执行耗时(ms)</summary>
        ExecutionTimeMs,
        /// <summary>开始时间（以运行开始为 0 的毫秒）</summary>
        StartTimeMs,
        /// <summary>结束时间（以运行开始为 0 的毫秒）</summary>
        EndTimeMs,
        /// <summary>图像匹配相似度</summary>
        Similarity,
        /// <summary>点击 X 坐标</summary>
        ClickX,
        /// <summary>点击 Y 坐标</summary>
        ClickY,
        /// <summary>识别文本</summary>
        Text,
        /// <summary>实际键入的文本（键入文字类步骤）</summary>
        TypedText,
        /// <summary>弹窗选择结果</summary>
        PopupResult,
        /// <summary>接收到的信号内容</summary>
        SignalValue,
        /// <summary>是否刚执行过（仅引用步骤）</summary>
        Triggered,
        /// <summary>用户输入步骤的字段值（仅引用步骤）</summary>
        UserInputField,
    }

    /// <summary>
    /// 条件运算符
    /// </summary>
    public enum ConditionOperator
    {
        Equal,
        NotEqual,
        GreaterThan,
        LessThan,
        GreaterOrEqual,
        LessOrEqual,
        Contains,
        NotContains,
        IsEmpty,
        IsNotEmpty,
        /// <summary>正则表达式匹配（右值为正则模式）</summary>
        RegexMatch,
    }

    /// <summary>
    /// 逻辑连接器
    /// </summary>
    public enum LogicConnector
    {
        And,
        Or
    }

    /// <summary>
    /// 识别文本提取模式（条件变量为识别文本时的预处理方式）
    /// </summary>
    public enum TextExtractMode
    {
        /// <summary>不提取，使用完整文本</summary>
        Whole,
        /// <summary>读取指定行（按 ExtractLines 指定，1 基，支持 1,3,5-8 格式）</summary>
        Lines,
        /// <summary>从子字符串 ExtractMarker 之后读取 ExtractLength 个字符（0=到末尾）</summary>
        FromSubstring,
        /// <summary>从开头读取 ExtractLength 个字符</summary>
        FromStart,
        /// <summary>从末尾读取 ExtractLength 个字符</summary>
        FromEnd,
    }

    /// <summary>
    /// 提取长度的读取单位
    /// </summary>
    public enum ExtractUnit
    {
        /// <summary>按字符数读取</summary>
        Character,
        /// <summary>按行数读取</summary>
        Line,
    }

    /// <summary>
    /// 步骤条件规则行
    /// </summary>
    public class StepCondition : ObservableObject
    {
        private Guid? _stepUid;
        private ConditionOperator _operator = ConditionOperator.Equal;
        private string _value = string.Empty;
        private LogicConnector _connector = LogicConnector.And;
        private TextExtractMode _textExtractMode = TextExtractMode.Whole;
        private string _extractLines = string.Empty;
        private string _extractMarker = string.Empty;
        private int _extractLength = 0;
        private ExtractUnit _extractUnit = ExtractUnit.Character;

        private ConditionSource _source = ConditionSource.Self;
        private ConditionField _field = ConditionField.Result;
        private bool _constantValue = true;

        /// <summary>
        /// 取值来源（常量 / 本步骤 / 引用步骤）。界面第一项就是它，选定后再选具体字段。
        /// </summary>
        public ConditionSource Source
        {
            get => _source;
            set
            {
                if (!SetProperty(ref _source, value)) return;
                // 引用步骤才用得上字段名/引用步骤，切回来时补回默认字段
                if (value == ConditionSource.Step && _field == ConditionField.UserInputField)
                    OnPropertyChanged(nameof(ShowFieldName));
                if (value != ConditionSource.Step && (_field == ConditionField.UserInputField || _field == ConditionField.Triggered))
                    Field = ConditionField.Result;
                // 常量不需要运算符和右值：直接按真/假取值
                if (value == ConditionSource.Constant)
                {
                    Value = _constantValue ? "true" : "false";
                    Operator = ConditionOperator.Equal;
                }
                RefreshAvailability();
            }
        }

        /// <summary>
        /// 取值字段（执行结果 / 执行耗时 / 开始时间 / 结束时间 / 以及按步骤类型可用的字段）
        /// </summary>
        public ConditionField Field
        {
            get => _field;
            set
            {
                if (!SetProperty(ref _field, value)) return;
                RefreshAvailability();
            }
        }

        /// <summary>来源为常量时的取值（真 / 假）</summary>
        public bool ConstantValue
        {
            get => _constantValue;
            set
            {
                if (!SetProperty(ref _constantValue, value)) return;
                if (_source == ConditionSource.Constant)
                {
                    Value = value ? "true" : "false";
                    Operator = ConditionOperator.Equal;
                }
                OnPropertyChanged(nameof(Variable));
            }
        }

        /// <summary>
        /// 条件变量（由来源 + 字段推导；setter 仅用于兼容旧文件的 "Variable" 字段与外部赋值）
        /// </summary>
        public ConditionVariable Variable
        {
            get => ComputeVariable();
            set
            {
                ApplyVariable(value);
                OnPropertyChanged(nameof(Variable));
                OnPropertyChanged(nameof(ShowFieldName));
                OnPropertyChanged(nameof(ShowTextExtractSettings));
                OnPropertyChanged(nameof(ShowConstantValue));
                OnPropertyChanged(nameof(ShowFieldSelector));
                OnPropertyChanged(nameof(ShowStepSelector));
                OnPropertyChanged(nameof(ShowOperator));
                RefreshExtractVisibility();
            }
        }

        private ConditionVariable ComputeVariable()
        {
            if (_source == ConditionSource.Constant)
                return _constantValue ? ConditionVariable.ConstantTrue : ConditionVariable.ConstantFalse;

            bool self = _source == ConditionSource.Self;
            return _field switch
            {
                ConditionField.Result => self ? ConditionVariable.Self_IsTrue : ConditionVariable.Step_IsTrue,
                ConditionField.ExecutionTimeMs => self ? ConditionVariable.Self_ExecutionTimeMs : ConditionVariable.Step_ExecutionTimeMs,
                ConditionField.StartTimeMs => self ? ConditionVariable.Self_StartTimeMs : ConditionVariable.Step_StartTimeMs,
                ConditionField.EndTimeMs => self ? ConditionVariable.Self_EndTimeMs : ConditionVariable.Step_EndTimeMs,
                ConditionField.Similarity => self ? ConditionVariable.Self_Similarity : ConditionVariable.Step_Similarity,
                ConditionField.ClickX => self ? ConditionVariable.Self_ClickX : ConditionVariable.Step_ClickX,
                ConditionField.ClickY => self ? ConditionVariable.Self_ClickY : ConditionVariable.Step_ClickY,
                ConditionField.Text => self ? ConditionVariable.Self_OCRText : ConditionVariable.Step_OCRText,
                ConditionField.TypedText => self ? ConditionVariable.Self_TypedText : ConditionVariable.Step_TypedText,
                ConditionField.PopupResult => self ? ConditionVariable.Self_PopupResult : ConditionVariable.Step_PopupResult,
                ConditionField.SignalValue => self ? ConditionVariable.Self_SignalValue : ConditionVariable.Step_SignalValue,
                ConditionField.Triggered => ConditionVariable.Step_Triggered,
                ConditionField.UserInputField => ConditionVariable.Step_UserInputField,
                _ => ConditionVariable.Self_IsTrue,
            };
        }

        private void ApplyVariable(ConditionVariable variable)
        {
            switch (variable)
            {
                case ConditionVariable.ConstantTrue:
                    _source = ConditionSource.Constant;
                    _constantValue = true;
                    OnPropertyChanged(nameof(Source));
                    OnPropertyChanged(nameof(ConstantValue));
                    break;
                case ConditionVariable.ConstantFalse:
                    _source = ConditionSource.Constant;
                    _constantValue = false;
                    OnPropertyChanged(nameof(Source));
                    OnPropertyChanged(nameof(ConstantValue));
                    break;
                case ConditionVariable.Self_IsTrue:
                case ConditionVariable.Self_Similarity:
                case ConditionVariable.Self_ExecutionTimeMs:
                case ConditionVariable.Self_StartTimeMs:
                case ConditionVariable.Self_EndTimeMs:
                case ConditionVariable.Self_ClickX:
                case ConditionVariable.Self_ClickY:
                case ConditionVariable.Self_OCRText:
                case ConditionVariable.Self_PopupResult:
                case ConditionVariable.Self_SignalValue:
                case ConditionVariable.Self_TypedText:
                    _source = ConditionSource.Self;
                    _field = FieldOf(variable);
                    OnPropertyChanged(nameof(Source));
                    OnPropertyChanged(nameof(Field));
                    break;
                case ConditionVariable.Step_IsTrue:
                case ConditionVariable.Step_Similarity:
                case ConditionVariable.Step_ExecutionTimeMs:
                case ConditionVariable.Step_StartTimeMs:
                case ConditionVariable.Step_EndTimeMs:
                case ConditionVariable.Step_ClickX:
                case ConditionVariable.Step_ClickY:
                case ConditionVariable.Step_OCRText:
                case ConditionVariable.Step_PopupResult:
                case ConditionVariable.Step_SignalValue:
                case ConditionVariable.Step_TypedText:
                case ConditionVariable.Step_Triggered:
                case ConditionVariable.Step_UserInputField:
                    _source = ConditionSource.Step;
                    _field = FieldOf(variable);
                    OnPropertyChanged(nameof(Source));
                    OnPropertyChanged(nameof(Field));
                    break;
            }
        }

        private static ConditionField FieldOf(ConditionVariable variable) => variable switch
        {
            ConditionVariable.Self_IsTrue or ConditionVariable.Step_IsTrue => ConditionField.Result,
            ConditionVariable.Self_ExecutionTimeMs or ConditionVariable.Step_ExecutionTimeMs => ConditionField.ExecutionTimeMs,
            ConditionVariable.Self_StartTimeMs or ConditionVariable.Step_StartTimeMs => ConditionField.StartTimeMs,
            ConditionVariable.Self_EndTimeMs or ConditionVariable.Step_EndTimeMs => ConditionField.EndTimeMs,
            ConditionVariable.Self_Similarity or ConditionVariable.Step_Similarity => ConditionField.Similarity,
            ConditionVariable.Self_ClickX or ConditionVariable.Step_ClickX => ConditionField.ClickX,
            ConditionVariable.Self_ClickY or ConditionVariable.Step_ClickY => ConditionField.ClickY,
            ConditionVariable.Self_OCRText or ConditionVariable.Step_OCRText => ConditionField.Text,
            ConditionVariable.Self_TypedText or ConditionVariable.Step_TypedText => ConditionField.TypedText,
            ConditionVariable.Self_PopupResult or ConditionVariable.Step_PopupResult => ConditionField.PopupResult,
            ConditionVariable.Self_SignalValue or ConditionVariable.Step_SignalValue => ConditionField.SignalValue,
            ConditionVariable.Step_Triggered => ConditionField.Triggered,
            ConditionVariable.Step_UserInputField => ConditionField.UserInputField,
            _ => ConditionField.Result,
        };

        /// <summary>来源类型选项（界面第一项）</summary>
        [JsonIgnore]
        public List<ConditionSource> AvailableSources { get; } = new()
        {
            ConditionSource.Constant,
            ConditionSource.Self,
            ConditionSource.Step,
        };

        /// <summary>是否显示常量取值（真/假）</summary>
        [JsonIgnore]
        public bool ShowConstantValue => _source == ConditionSource.Constant;

        /// <summary>是否显示字段选择（非常量才有）</summary>
        [JsonIgnore]
        public bool ShowFieldSelector => _source != ConditionSource.Constant;

        /// <summary>是否显示引用步骤选择</summary>
        [JsonIgnore]
        public bool ShowStepSelector => _source == ConditionSource.Step;

        /// <summary>是否显示运算符与右值（常量不需要）</summary>
        [JsonIgnore]
        public bool ShowOperator => _source != ConditionSource.Constant;

        private void RefreshAvailability()
        {
            OnPropertyChanged(nameof(Variable));
            OnPropertyChanged(nameof(AvailableFields));
            OnPropertyChanged(nameof(ShowConstantValue));
            OnPropertyChanged(nameof(ShowFieldSelector));
            OnPropertyChanged(nameof(ShowStepSelector));
            OnPropertyChanged(nameof(ShowOperator));
            OnPropertyChanged(nameof(ShowFieldName));
            OnPropertyChanged(nameof(ShowTextExtractSettings));
            RefreshExtractVisibility();
        }

        /// <summary>
        /// 当前来源下可选的字段：执行结果 / 执行耗时 / 开始时间 / 结束时间，
        /// 再按步骤类型只补充该类型真正有的字段（键入文字就是键入的文本，识图才有相似度等）。
        /// </summary>
        [JsonIgnore]
        public List<ConditionField> AvailableFields
        {
            get
            {
                var fields = new List<ConditionField>
                {
                    ConditionField.Result,
                    ConditionField.ExecutionTimeMs,
                    ConditionField.StartTimeMs,
                    ConditionField.EndTimeMs,
                };

                var stepType = ResolveStepType();
                switch (stepType)
                {
                    case StepType.ClickImage:
                    case StepType.FindImage:
                    case StepType.ClickImages:
                    case StepType.FindImages:
                        fields.AddRange(new[]
                        {
                            ConditionField.Similarity,
                            ConditionField.ClickX,
                            ConditionField.ClickY,
                            ConditionField.Text,
                        });
                        break;
                    case StepType.TypeText:
                    case StepType.TypeTextMore:
                    case StepType.TypeTextFromFile:
                        fields.Add(ConditionField.TypedText);
                        break;
                    case StepType.GetInput:
                        fields.Add(ConditionField.Text);
                        break;
                    case StepType.Popup:
                        fields.Add(ConditionField.PopupResult);
                        break;
                    case StepType.ReceiveSignal:
                        fields.Add(ConditionField.SignalValue);
                        break;
                }

                // 「用户输入」字段只对用户输入步骤有意义
                if (_source == ConditionSource.Step && stepType == StepType.UserInput)
                    fields.Add(ConditionField.UserInputField);

                return fields;
            }
        }

        /// <summary>
        /// 当 Variable 为 Step_* 时引用目标步骤的 Uid
        /// </summary>
        [JsonPropertyName("StepUid")]
        public Guid? StepUid
        {
            get => _stepUid;
            set
            {
                if (SetProperty(ref _stepUid, value))
                    OnPropertyChanged(nameof(AvailableFields));
            }
        }

        private string _fieldName = string.Empty;

        /// <summary>
        /// 当 Variable 为 Step_UserInputField 时，引用的「用户输入」字段名
        /// </summary>
        public string FieldName
        {
            get => _fieldName;
            set
            {
                if (SetProperty(ref _fieldName, value))
                    OnPropertyChanged(nameof(ShowFieldName));
            }
        }

        /// <summary>是否显示字段名输入框（仅用户输入字段变量）</summary>
        [JsonIgnore]
        public bool ShowFieldName => _source == ConditionSource.Step && _field == ConditionField.UserInputField;

        /// <summary>
        /// 拥有此条件的步骤 Uid（不序列化，运行时设置）
        /// </summary>
        [JsonIgnore]
        public Guid ParentStepUid { get; set; }

        /// <summary>
        /// 旧版行号字段（仅用于反序列化兼容，不序列化）
        /// </summary>
        [JsonPropertyName("StepLineNo")]
        public int LegacyStepLineNo { get; set; }

        /// <summary>
        /// 所有步骤集合（供条件面板 ComboBox 绑定）
        /// </summary>
        [JsonIgnore]
        public ObservableCollection<Viewmodels.AutomationStep.AutomationStepBase> AllSteps
        {
            get
            {
                try { return Utils.SingletonLocator.Steps.AutomationStepBases; }
                catch { return null; }
            }
        }


        /// <summary>
        /// 解析当前选中步骤的类型（无选中时返回父步骤类型）
        /// </summary>
        private StepType ResolveStepType()
        {
            if (_stepUid.HasValue && _stepUid.Value != Guid.Empty)
            {
                try
                {
                    var steps = Utils.SingletonLocator.Steps.AutomationStepBases;
                    if (steps != null)
                    {
                        foreach (var s in steps)
                        {
                            if (s.Uid == _stepUid.Value) return s.Type;
                        }
                    }
                }
                catch { }
            }
            // 未选择步骤时，使用父步骤的类型
            if (ParentStepUid != Guid.Empty)
            {
                try
                {
                    var steps = Utils.SingletonLocator.Steps.AutomationStepBases;
                    if (steps != null)
                    {
                        foreach (var s in steps)
                        {
                            if (s.Uid == ParentStepUid) return s.Type;
                        }
                    }
                }
                catch { }
            }
            return StepType.ClickImage;
        }

        /// <summary>
        /// 运算符
        /// </summary>
        public ConditionOperator Operator { get => _operator; set => SetProperty(ref _operator, value); }

        /// <summary>
        /// 比较值（字符串形式，运行时按类型解析）
        /// </summary>
        public string Value { get => _value; set => SetProperty(ref _value, value); }

        /// <summary>
        /// 与下一行的逻辑连接方式
        /// </summary>
        public LogicConnector Connector { get => _connector; set => SetProperty(ref _connector, value); }

        #region 识别文本提取设置（仅对 *_OCRText 变量生效）

        /// <summary>
        /// 文本提取模式
        /// </summary>
        public TextExtractMode TextExtractMode
        {
            get => _textExtractMode;
            set
            {
                if (SetProperty(ref _textExtractMode, value))
                    RefreshExtractVisibility();
            }
        }

        /// <summary>
        /// 要读取的行（1 基，支持逗号与区间，如 "1,3,5-8"，多行结果按换行拼接）
        /// </summary>
        public string ExtractLines { get => _extractLines; set => SetProperty(ref _extractLines, value); }

        /// <summary>
        /// 起始子字符串（从该子字符串首次出现位置之后开始读取）
        /// </summary>
        public string ExtractMarker { get => _extractMarker; set => SetProperty(ref _extractMarker, value); }

        /// <summary>
        /// 读取长度（0 = 直到末尾，单位由 ExtractUnit 决定）
        /// </summary>
        public int ExtractLength { get => _extractLength; set => SetProperty(ref _extractLength, value); }

        /// <summary>
        /// 读取单位（按字符数 / 按行数）
        /// </summary>
        public ExtractUnit ExtractUnit { get => _extractUnit; set => SetProperty(ref _extractUnit, value); }

        /// <summary>所有读取单位选项（供 ComboBox 绑定）</summary>
        [JsonIgnore]
        public List<ExtractUnit> AllExtractUnits { get; } = new()
        {
            ExtractUnit.Character,
            ExtractUnit.Line,
        };

        /// <summary>
        /// 所有提取模式选项（供 ComboBox 绑定）
        /// </summary>
        [JsonIgnore]
        public List<TextExtractMode> AllTextExtractModes { get; } = new()
        {
            TextExtractMode.Whole,
            TextExtractMode.Lines,
            TextExtractMode.FromSubstring,
            TextExtractMode.FromStart,
            TextExtractMode.FromEnd,
        };

        /// <summary>
        /// 当前变量是否为识别文本类型（决定是否显示提取设置）
        /// </summary>
        [JsonIgnore]
        public bool IsTextVariable => Variable is ConditionVariable.Self_OCRText or ConditionVariable.Step_OCRText
            or ConditionVariable.Self_TypedText or ConditionVariable.Step_TypedText;

        /// <summary>
        /// 是否显示文本提取设置
        /// </summary>
        [JsonIgnore]
        public bool ShowTextExtractSettings => IsTextVariable;

        /// <summary>是否显示行号输入</summary>
        [JsonIgnore]
        public bool ShowExtractLines => IsTextVariable && TextExtractMode == TextExtractMode.Lines;

        /// <summary>是否显示子字符串输入</summary>
        [JsonIgnore]
        public bool ShowExtractMarker => IsTextVariable && TextExtractMode == TextExtractMode.FromSubstring;

        /// <summary>是否显示长度输入</summary>
        [JsonIgnore]
        public bool ShowExtractLength => IsTextVariable &&
            (TextExtractMode == TextExtractMode.FromSubstring || TextExtractMode == TextExtractMode.FromStart || TextExtractMode == TextExtractMode.FromEnd);

        /// <summary>是否显示读取单位选择（与长度输入同步显示）</summary>
        [JsonIgnore]
        public bool ShowExtractUnit => ShowExtractLength;

        /// <summary>
        /// 提取设置变更时刷新可见性
        /// </summary>
        public void RefreshExtractVisibility()
        {
            OnPropertyChanged(nameof(ShowExtractLines));
            OnPropertyChanged(nameof(ShowExtractMarker));
            OnPropertyChanged(nameof(ShowExtractLength));
            OnPropertyChanged(nameof(ShowExtractUnit));
        }

        #endregion

        public StepCondition Clone()
        {
            return new StepCondition
            {
                Source = Source,
                Field = Field,
                ConstantValue = ConstantValue,
                StepUid = StepUid,
                FieldName = FieldName,
                ParentStepUid = ParentStepUid,
                Operator = Operator,
                Value = Value,
                Connector = Connector,
                TextExtractMode = TextExtractMode,
                ExtractLines = ExtractLines,
                ExtractMarker = ExtractMarker,
                ExtractLength = ExtractLength,
                ExtractUnit = ExtractUnit,
            };
        }
    }
}
