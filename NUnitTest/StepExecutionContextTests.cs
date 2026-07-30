using NUnit.Framework;
using ShaoLu.Models;
using ShaoLu.Services;

namespace NUnitTest
{
    /// <summary>
    /// StepExecutionContext 执行上下文的单元测试
    /// </summary>
    [TestFixture]
    public class StepExecutionContextTests
    {
        private StepExecutionContext _context;

        [SetUp]
        public void Setup()
        {
            _context = new StepExecutionContext();
        }

        #region SetResult / GetResult

        [Test]
        public void SetResult_ThenGetResult_ReturnsSameResult()
        {
            var result = new StepExecutionResult { IsTrue = true, Similarity = 0.9 };
            _context.SetResult(1, result);

            var retrieved = _context.GetResult(1);
            Assert.That(retrieved, Is.SameAs(result));
        }

        [Test]
        public void GetResult_NonExistentLine_ReturnsNull()
        {
            var retrieved = _context.GetResult(999);
            Assert.That(retrieved, Is.Null);
        }

        [Test]
        public void SetResult_OverwriteExisting_ReturnsNewResult()
        {
            var result1 = new StepExecutionResult { IsTrue = true };
            var result2 = new StepExecutionResult { IsTrue = false };

            _context.SetResult(1, result1);
            _context.SetResult(1, result2);

            var retrieved = _context.GetResult(1);
            Assert.That(retrieved, Is.SameAs(result2));
            Assert.That(retrieved.IsTrue, Is.False);
        }

        [Test]
        public void SetResult_MultipleLines_AllRetrievable()
        {
            var r1 = new StepExecutionResult { IsTrue = true };
            var r2 = new StepExecutionResult { IsTrue = false };
            var r3 = new StepExecutionResult { Similarity = 0.75 };

            _context.SetResult(1, r1);
            _context.SetResult(2, r2);
            _context.SetResult(3, r3);

            Assert.That(_context.GetResult(1).IsTrue, Is.True);
            Assert.That(_context.GetResult(2).IsTrue, Is.False);
            Assert.That(_context.GetResult(3).Similarity, Is.EqualTo(0.75));
        }

        #endregion

        #region Clear

        [Test]
        public void Clear_RemovesAllResults()
        {
            _context.SetResult(1, new StepExecutionResult { IsTrue = true });
            _context.SetResult(2, new StepExecutionResult { IsTrue = false });

            _context.Clear();

            Assert.That(_context.GetResult(1), Is.Null);
            Assert.That(_context.GetResult(2), Is.Null);
        }

        [Test]
        public void Clear_OnEmptyContext_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _context.Clear());
        }

        #endregion

        #region ResolveVariable - 常量

        [Test]
        public void ResolveVariable_ConstantTrue_ReturnsTrue()
        {
            var value = _context.ResolveVariable(ConditionVariable.ConstantTrue, 0, null);
            Assert.That(value, Is.EqualTo(true));
        }

        [Test]
        public void ResolveVariable_ConstantFalse_ReturnsFalse()
        {
            var value = _context.ResolveVariable(ConditionVariable.ConstantFalse, 0, null);
            Assert.That(value, Is.EqualTo(false));
        }

        #endregion

        #region ResolveVariable - Self 变量

        [Test]
        public void ResolveVariable_SelfIsTrue_WithResult()
        {
            var currentResult = new StepExecutionResult { IsTrue = true };
            var value = _context.ResolveVariable(ConditionVariable.Self_IsTrue, 0, currentResult);
            Assert.That(value, Is.EqualTo(true));
        }

        [Test]
        public void ResolveVariable_SelfIsTrue_NullResult_ReturnsFalse()
        {
            var value = _context.ResolveVariable(ConditionVariable.Self_IsTrue, 0, null);
            Assert.That(value, Is.EqualTo(false));
        }

        [Test]
        public void ResolveVariable_SelfSimilarity_WithResult()
        {
            var currentResult = new StepExecutionResult { Similarity = 0.87 };
            var value = _context.ResolveVariable(ConditionVariable.Self_Similarity, 0, currentResult);
            Assert.That(value, Is.EqualTo(0.87));
        }

        [Test]
        public void ResolveVariable_SelfSimilarity_NullResult_ReturnsMinusOne()
        {
            var value = _context.ResolveVariable(ConditionVariable.Self_Similarity, 0, null);
            Assert.That(value, Is.EqualTo(-1.0));
        }

        [Test]
        public void ResolveVariable_SelfExecutionTime_WithResult()
        {
            var currentResult = new StepExecutionResult { ExecutionTimeMs = 250 };
            var value = _context.ResolveVariable(ConditionVariable.Self_ExecutionTimeMs, 0, currentResult);
            Assert.That(value, Is.EqualTo(250.0));
        }

        [Test]
        public void ResolveVariable_SelfExecutionTime_NullResult_ReturnsZero()
        {
            var value = _context.ResolveVariable(ConditionVariable.Self_ExecutionTimeMs, 0, null);
            Assert.That(value, Is.EqualTo(0.0));
        }

        [Test]
        public void ResolveVariable_SelfOCRText_WithResult()
        {
            var currentResult = new StepExecutionResult { OCRText = "Test123" };
            var value = _context.ResolveVariable(ConditionVariable.Self_OCRText, 0, currentResult);
            Assert.That(value, Is.EqualTo("Test123"));
        }

        [Test]
        public void ResolveVariable_SelfOCRText_NullResult_ReturnsEmpty()
        {
            var value = _context.ResolveVariable(ConditionVariable.Self_OCRText, 0, null);
            Assert.That(value, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ResolveVariable_SelfClickX_NullResult_ReturnsZero()
        {
            var value = _context.ResolveVariable(ConditionVariable.Self_ClickX, 0, null);
            Assert.That(value, Is.EqualTo(0.0));
        }

        [Test]
        public void ResolveVariable_SelfClickY_NullResult_ReturnsZero()
        {
            var value = _context.ResolveVariable(ConditionVariable.Self_ClickY, 0, null);
            Assert.That(value, Is.EqualTo(0.0));
        }

        #endregion

        #region ResolveVariable - Step 引用变量

        [Test]
        public void ResolveVariable_StepIsTrue_ExistingStep()
        {
            _context.SetResult(5, new StepExecutionResult { IsTrue = true });
            var value = _context.ResolveVariable(ConditionVariable.Step_IsTrue, 5, null);
            Assert.That(value, Is.EqualTo(true));
        }

        [Test]
        public void ResolveVariable_StepIsTrue_NonExistentStep_ReturnsFalse()
        {
            var value = _context.ResolveVariable(ConditionVariable.Step_IsTrue, 99, null);
            Assert.That(value, Is.EqualTo(false));
        }

        [Test]
        public void ResolveVariable_StepSimilarity_ExistingStep()
        {
            _context.SetResult(3, new StepExecutionResult { Similarity = 0.92 });
            var value = _context.ResolveVariable(ConditionVariable.Step_Similarity, 3, null);
            Assert.That(value, Is.EqualTo(0.92));
        }

        [Test]
        public void ResolveVariable_StepSimilarity_NonExistentStep_ReturnsMinusOne()
        {
            var value = _context.ResolveVariable(ConditionVariable.Step_Similarity, 99, null);
            Assert.That(value, Is.EqualTo(-1.0));
        }

        [Test]
        public void ResolveVariable_StepOCRText_ExistingStep()
        {
            _context.SetResult(2, new StepExecutionResult { OCRText = "ABC" });
            var value = _context.ResolveVariable(ConditionVariable.Step_OCRText, 2, null);
            Assert.That(value, Is.EqualTo("ABC"));
        }

        [Test]
        public void ResolveVariable_StepOCRText_NonExistentStep_ReturnsEmpty()
        {
            var value = _context.ResolveVariable(ConditionVariable.Step_OCRText, 99, null);
            Assert.That(value, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ResolveVariable_StepExecutionTime_ExistingStep()
        {
            _context.SetResult(7, new StepExecutionResult { ExecutionTimeMs = 500 });
            var value = _context.ResolveVariable(ConditionVariable.Step_ExecutionTimeMs, 7, null);
            Assert.That(value, Is.EqualTo(500.0));
        }

        #endregion
    }
}
