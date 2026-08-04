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
        Self_ClickX,
        Self_ClickY,
        Self_OCRText,
        Self_PopupResult,

        // 引用其他步骤（通过 StepLineNo 指定）
        Step_IsTrue,
        Step_Similarity,
        Step_ExecutionTimeMs,
        Step_ClickX,
        Step_ClickY,
        Step_OCRText,
        Step_PopupResult,
        /// <summary>引用步骤是否在本次评估前刚刚被执行（用于统计执行次数）</summary>
        Step_Triggered,
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
        private ConditionVariable _variable = ConditionVariable.Self_IsTrue;
        private Guid? _stepUid;
        private ConditionOperator _operator = ConditionOperator.Equal;
        private string _value = string.Empty;
        private LogicConnector _connector = LogicConnector.And;
        private TextExtractMode _textExtractMode = TextExtractMode.Whole;
        private string _extractLines = string.Empty;
        private string _extractMarker = string.Empty;
        private int _extractLength = 0;
        private ExtractUnit _extractUnit = ExtractUnit.Character;

        /// <summary>
        /// 条件变量
        /// </summary>
        public ConditionVariable Variable
        {
            get => _variable;
            set
            {
                if (SetProperty(ref _variable, value))
                {
                    OnPropertyChanged(nameof(ShowTextExtractSettings));
                    RefreshExtractVisibility();
                }
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
                    OnPropertyChanged(nameof(AvailableVariables));
            }
        }

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
        /// 根据选中步骤类型返回可用的条件变量列表
        /// </summary>
        [JsonIgnore]
        public List<ConditionVariable> AvailableVariables
        {
            get
            {
                var stepType = ResolveStepType();
                var vars = new List<ConditionVariable>
                {
                    ConditionVariable.ConstantTrue,
                    ConditionVariable.ConstantFalse,
                };

                switch (stepType)
                {
                    case StepType.ClickImage:
                    case StepType.FindImage:
                    case StepType.ClickImages:
                    case StepType.FindImages:
                        vars.AddRange(new[]
                        {
                            ConditionVariable.Self_IsTrue,
                            ConditionVariable.Self_Similarity,
                            ConditionVariable.Self_ExecutionTimeMs,
                            ConditionVariable.Self_ClickX,
                            ConditionVariable.Self_ClickY,
                            ConditionVariable.Self_OCRText,
                            ConditionVariable.Self_PopupResult,
                            ConditionVariable.Step_IsTrue,
                            ConditionVariable.Step_Similarity,
                            ConditionVariable.Step_ExecutionTimeMs,
                            ConditionVariable.Step_ClickX,
                            ConditionVariable.Step_ClickY,
                            ConditionVariable.Step_OCRText,
                            ConditionVariable.Step_PopupResult,
                            ConditionVariable.Step_Triggered,
                        });
                        break;
                    case StepType.Popup:
                        vars.AddRange(new[]
                        {
                            ConditionVariable.Self_IsTrue,
                            ConditionVariable.Self_PopupResult,
                            ConditionVariable.Self_ExecutionTimeMs,
                            ConditionVariable.Step_IsTrue,
                            ConditionVariable.Step_PopupResult,
                            ConditionVariable.Step_ExecutionTimeMs,
                            ConditionVariable.Step_Triggered,
                        });
                        break;
                    case StepType.GetInput:
                        vars.AddRange(new[]
                        {
                            ConditionVariable.Self_IsTrue,
                            ConditionVariable.Self_OCRText,
                            ConditionVariable.Self_ExecutionTimeMs,
                            ConditionVariable.Step_IsTrue,
                            ConditionVariable.Step_OCRText,
                            ConditionVariable.Step_ExecutionTimeMs,
                            ConditionVariable.Step_Triggered,
                        });
                        break;
                    default: // 文本步骤及其他
                        vars.AddRange(new[]
                        {
                            ConditionVariable.Self_IsTrue,
                            ConditionVariable.Self_ExecutionTimeMs,
                            ConditionVariable.Self_OCRText,
                            ConditionVariable.Self_PopupResult,
                            ConditionVariable.Step_IsTrue,
                            ConditionVariable.Step_ExecutionTimeMs,
                            ConditionVariable.Step_OCRText,
                            ConditionVariable.Step_PopupResult,
                            ConditionVariable.Step_Triggered,
                        });
                        break;
                }
                return vars;
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
        public bool IsTextVariable => Variable == ConditionVariable.Self_OCRText || Variable == ConditionVariable.Step_OCRText;

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
                Variable = Variable,
                StepUid = StepUid,
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
