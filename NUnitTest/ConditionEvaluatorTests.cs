using NUnit.Framework;
using ShaoLu.Models;
using ShaoLu.Services;
using System.Collections.Generic;

namespace NUnitTest
{
    /// <summary>
    /// ConditionEvaluator 条件评估器的单元测试
    /// </summary>
    [TestFixture]
    public class ConditionEvaluatorTests
    {
        private StepExecutionContext _context;
        private StepExecutionResult _currentResult;

        [SetUp]
        public void Setup()
        {
            _context = new StepExecutionContext();
            _currentResult = new StepExecutionResult
            {
                IsTrue = true,
                Similarity = 0.95,
                ExecutionTimeMs = 150,
                OCRText = "Hello World"
            };
        }

        #region 空条件 / 默认行为

        [Test]
        public void Evaluate_NullConditions_ReturnsCurrentResult()
        {
            var result = ConditionEvaluator.Evaluate(null, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_EmptyConditions_ReturnsCurrentResult()
        {
            var conditions = new List<StepCondition>();
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_NullConditions_NullCurrentResult_ReturnsFalse()
        {
            var result = ConditionEvaluator.Evaluate(null, _context, null);
            Assert.That(result, Is.False);
        }

        [Test]
        public void Evaluate_EmptyConditions_CurrentResultFalse_ReturnsFalse()
        {
            _currentResult.IsTrue = false;
            var conditions = new List<StepCondition>();
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.False);
        }

        #endregion

        #region 常量条件

        [Test]
        public void Evaluate_ConstantTrue_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition { Variable = ConditionVariable.ConstantTrue }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_ConstantFalse_ReturnsFalse()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition { Variable = ConditionVariable.ConstantFalse }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.False);
        }

        #endregion

        #region 布尔比较

        [Test]
        public void Evaluate_SelfIsTrue_EqualTrue_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_IsTrue,
                    Operator = ConditionOperator.Equal,
                    Value = "true"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_SelfIsTrue_EqualFalse_ReturnsFalse()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_IsTrue,
                    Operator = ConditionOperator.Equal,
                    Value = "false"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.False);
        }

        [Test]
        public void Evaluate_SelfIsTrue_NotEqualFalse_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_IsTrue,
                    Operator = ConditionOperator.NotEqual,
                    Value = "false"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_BoolCompare_WithNumericString_1()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_IsTrue,
                    Operator = ConditionOperator.Equal,
                    Value = "1"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        #endregion

        #region 数字比较

        [Test]
        public void Evaluate_Similarity_GreaterThan_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_Similarity,
                    Operator = ConditionOperator.GreaterThan,
                    Value = "0.9"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_Similarity_LessThan_ReturnsFalse()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_Similarity,
                    Operator = ConditionOperator.LessThan,
                    Value = "0.9"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.False);
        }

        [Test]
        public void Evaluate_ExecutionTime_GreaterOrEqual_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_ExecutionTimeMs,
                    Operator = ConditionOperator.GreaterOrEqual,
                    Value = "150"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_ExecutionTime_LessOrEqual_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_ExecutionTimeMs,
                    Operator = ConditionOperator.LessOrEqual,
                    Value = "200"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_NumericEqual_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_ExecutionTimeMs,
                    Operator = ConditionOperator.Equal,
                    Value = "150"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_NumericNotEqual_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_ExecutionTimeMs,
                    Operator = ConditionOperator.NotEqual,
                    Value = "999"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        #endregion

        #region 字符串比较

        [Test]
        public void Evaluate_OCRText_Equal_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_OCRText,
                    Operator = ConditionOperator.Equal,
                    Value = "hello world"  // 不区分大小写
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_OCRText_Contains_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_OCRText,
                    Operator = ConditionOperator.Contains,
                    Value = "World"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_OCRText_NotContains_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_OCRText,
                    Operator = ConditionOperator.NotContains,
                    Value = "Foo"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_OCRText_NotEqual_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_OCRText,
                    Operator = ConditionOperator.NotEqual,
                    Value = "Goodbye"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        #endregion

        #region IsEmpty / IsNotEmpty

        [Test]
        public void Evaluate_OCRText_IsNotEmpty_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_OCRText,
                    Operator = ConditionOperator.IsNotEmpty
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_OCRText_IsEmpty_ReturnsFalse()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_OCRText,
                    Operator = ConditionOperator.IsEmpty
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.False);
        }

        [Test]
        public void Evaluate_EmptyOCRText_IsEmpty_ReturnsTrue()
        {
            _currentResult.OCRText = "";
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_OCRText,
                    Operator = ConditionOperator.IsEmpty
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        #endregion

        #region AND / OR 逻辑组合

        [Test]
        public void Evaluate_AndConnector_BothTrue_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_IsTrue,
                    Operator = ConditionOperator.Equal,
                    Value = "true",
                    Connector = LogicConnector.And
                },
                new StepCondition
                {
                    Variable = ConditionVariable.Self_Similarity,
                    Operator = ConditionOperator.GreaterThan,
                    Value = "0.9"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_AndConnector_OneFalse_ReturnsFalse()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_IsTrue,
                    Operator = ConditionOperator.Equal,
                    Value = "true",
                    Connector = LogicConnector.And
                },
                new StepCondition
                {
                    Variable = ConditionVariable.Self_Similarity,
                    Operator = ConditionOperator.LessThan,
                    Value = "0.5"  // 0.95 < 0.5 为 false
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.False);
        }

        [Test]
        public void Evaluate_OrConnector_OneFalse_ReturnsTrue()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_Similarity,
                    Operator = ConditionOperator.LessThan,
                    Value = "0.5",  // false
                    Connector = LogicConnector.Or
                },
                new StepCondition
                {
                    Variable = ConditionVariable.Self_IsTrue,
                    Operator = ConditionOperator.Equal,
                    Value = "true"  // true
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_OrConnector_BothFalse_ReturnsFalse()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_Similarity,
                    Operator = ConditionOperator.LessThan,
                    Value = "0.5",  // false
                    Connector = LogicConnector.Or
                },
                new StepCondition
                {
                    Variable = ConditionVariable.Self_IsTrue,
                    Operator = ConditionOperator.Equal,
                    Value = "false"  // false
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.False);
        }

        [Test]
        public void Evaluate_ThreeConditions_MixedConnectors()
        {
            // true AND false OR true => (true && false) || true => true
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_IsTrue,
                    Operator = ConditionOperator.Equal,
                    Value = "true",
                    Connector = LogicConnector.And  // 与下一行用 AND
                },
                new StepCondition
                {
                    Variable = ConditionVariable.Self_Similarity,
                    Operator = ConditionOperator.LessThan,
                    Value = "0.5",  // false
                    Connector = LogicConnector.Or  // 与下一行用 OR
                },
                new StepCondition
                {
                    Variable = ConditionVariable.ConstantTrue,
                    Operator = ConditionOperator.Equal,
                    Value = ""
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        #endregion

        #region 引用其他步骤

        [Test]
        public void Evaluate_StepReference_IsTrue()
        {
            var uid = System.Guid.Parse("00000000-0000-0000-0000-000000000005");
            _context.SetResultByUid(uid, new StepExecutionResult { IsTrue = true });

            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Step_IsTrue,
                    StepUid = uid,
                    Operator = ConditionOperator.Equal,
                    Value = "true"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_StepReference_Similarity()
        {
            var uid = System.Guid.Parse("00000000-0000-0000-0000-000000000003");
            _context.SetResultByUid(uid, new StepExecutionResult { Similarity = 0.88 });

            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Step_Similarity,
                    StepUid = uid,
                    Operator = ConditionOperator.GreaterThan,
                    Value = "0.8"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        [Test]
        public void Evaluate_StepReference_NonExistentStep_DefaultsToFalse()
        {
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Step_IsTrue,
                    StepUid = System.Guid.Parse("00000000-0000-0000-0000-000000000099"),
                    Operator = ConditionOperator.Equal,
                    Value = "false"
                }
            };
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.True);
        }

        #endregion

        #region Null 值处理

        [Test]
        public void Evaluate_NullLeftValue_NotEqual_ReturnsTrue()
        {
            // 当 currentResult 为 null 时，Self_OCRText 解析为 string.Empty
            // 这里用一个不存在的步骤引用来获取 null
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Step_OCRText,
                    StepUid = System.Guid.Parse("00000000-0000-0000-0000-000000000999"),
                    Operator = ConditionOperator.IsNotEmpty
                }
            };
            // Step_OCRText 对不存在的步骤返回 string.Empty，IsEmpty => true, IsNotEmpty => false
            var result = ConditionEvaluator.Evaluate(conditions, _context, _currentResult);
            Assert.That(result, Is.False);
        }

        #endregion
    }
}
