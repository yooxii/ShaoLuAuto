using CommunityToolkit.Mvvm.ComponentModel;

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

        // 引用其他步骤（通过 StepLineNo 指定）
        Step_IsTrue,
        Step_Similarity,
        Step_ExecutionTimeMs,
        Step_ClickX,
        Step_ClickY,
        Step_OCRText,
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
        IsNotEmpty
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
    /// 步骤条件规则行
    /// </summary>
    public class StepCondition : ObservableObject
    {
        private ConditionVariable _variable = ConditionVariable.Self_IsTrue;
        private int _stepLineNo;
        private ConditionOperator _operator = ConditionOperator.Equal;
        private string _value = string.Empty;
        private LogicConnector _connector = LogicConnector.And;

        /// <summary>
        /// 条件变量
        /// </summary>
        public ConditionVariable Variable { get => _variable; set => SetProperty(ref _variable, value); }

        /// <summary>
        /// 当 Variable 为 Step_* 时引用目标步骤行号
        /// </summary>
        public int StepLineNo { get => _stepLineNo; set => SetProperty(ref _stepLineNo, value); }

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

        public StepCondition Clone()
        {
            return new StepCondition
            {
                Variable = Variable,
                StepLineNo = StepLineNo,
                Operator = Operator,
                Value = Value,
                Connector = Connector,
            };
        }
    }
}
