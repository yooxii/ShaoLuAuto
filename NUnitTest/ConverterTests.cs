using NUnit.Framework;
using ShaoLu.Converters;
using ShaoLu.Models;
using System;
using System.Globalization;
using System.Windows;

namespace NUnitTest
{
    /// <summary>
    /// WPF 值转换器的单元测试
    /// </summary>
    [TestFixture]
    public class ConverterTests
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        #region BoolToInverseBoolConverter

        [TestFixture]
        public class BoolToInverseBoolConverterTests
        {
            private BoolToInverseBoolConverter _converter;

            [SetUp]
            public void Setup()
            {
                _converter = new BoolToInverseBoolConverter();
            }

            [Test]
            public void Convert_True_ReturnsFalse()
            {
                var result = _converter.Convert(true, typeof(bool), null, Culture);
                Assert.That(result, Is.EqualTo(false));
            }

            [Test]
            public void Convert_False_ReturnsTrue()
            {
                var result = _converter.Convert(false, typeof(bool), null, Culture);
                Assert.That(result, Is.EqualTo(true));
            }

            [Test]
            public void Convert_NonBoolValue_ReturnsFalse()
            {
                var result = _converter.Convert("not a bool", typeof(bool), null, Culture);
                Assert.That(result, Is.EqualTo(false));
            }

            [Test]
            public void Convert_NullValue_ReturnsFalse()
            {
                var result = _converter.Convert(null, typeof(bool), null, Culture);
                Assert.That(result, Is.EqualTo(false));
            }

            [Test]
            public void ConvertBack_True_ReturnsFalse()
            {
                var result = _converter.ConvertBack(true, typeof(bool), null, Culture);
                Assert.That(result, Is.EqualTo(false));
            }

            [Test]
            public void ConvertBack_False_ReturnsTrue()
            {
                var result = _converter.ConvertBack(false, typeof(bool), null, Culture);
                Assert.That(result, Is.EqualTo(true));
            }

            [Test]
            public void ConvertBack_NonBoolValue_ReturnsFalse()
            {
                var result = _converter.ConvertBack(42, typeof(bool), null, Culture);
                Assert.That(result, Is.EqualTo(false));
            }
        }

        #endregion

        #region NullToVisibilityConverter

        [TestFixture]
        public class NullToVisibilityConverterTests
        {
            private NullToVisibilityConverter _converter;

            [SetUp]
            public void Setup()
            {
                _converter = new NullToVisibilityConverter();
            }

            [Test]
            public void Convert_NullValue_ReturnsCollapsed()
            {
                var result = _converter.Convert(null, typeof(Visibility), null, Culture);
                Assert.That(result, Is.EqualTo(Visibility.Collapsed));
            }

            [Test]
            public void Convert_NonNullValue_ReturnsVisible()
            {
                var result = _converter.Convert("some value", typeof(Visibility), null, Culture);
                Assert.That(result, Is.EqualTo(Visibility.Visible));
            }

            [Test]
            public void Convert_NullValue_InverseMode_ReturnsVisible()
            {
                var result = _converter.Convert(null, typeof(Visibility), "Inverse", Culture);
                Assert.That(result, Is.EqualTo(Visibility.Visible));
            }

            [Test]
            public void Convert_NonNullValue_InverseMode_ReturnsCollapsed()
            {
                var result = _converter.Convert("some value", typeof(Visibility), "Inverse", Culture);
                Assert.That(result, Is.EqualTo(Visibility.Collapsed));
            }

            [Test]
            public void Convert_ZeroValue_ReturnsVisible()
            {
                // 0 不是 null，应该返回 Visible
                var result = _converter.Convert(0, typeof(Visibility), null, Culture);
                Assert.That(result, Is.EqualTo(Visibility.Visible));
            }

            [Test]
            public void Convert_EmptyString_ReturnsVisible()
            {
                // 空字符串不是 null
                var result = _converter.Convert("", typeof(Visibility), null, Culture);
                Assert.That(result, Is.EqualTo(Visibility.Visible));
            }

            [Test]
            public void ConvertBack_ThrowsNotImplementedException()
            {
                Assert.Throws<NotImplementedException>((TestDelegate)(() =>
                    _converter.ConvertBack(Visibility.Visible, typeof(object), null, Culture)));
            }
        }

        #endregion

        #region ConditionModeToVisibilityConverter

        [TestFixture]
        public class ConditionModeToVisibilityConverterTests
        {
            private ConditionModeToVisibilityConverter _converter;

            [SetUp]
            public void Setup()
            {
                _converter = new ConditionModeToVisibilityConverter();
            }

            [Test]
            public void Convert_CustomMode_ReturnsVisible()
            {
                var result = _converter.Convert(ConditionMode.Custom, typeof(Visibility), null, Culture);
                Assert.That(result, Is.EqualTo(Visibility.Visible));
            }

            [Test]
            public void Convert_DefaultMode_ReturnsCollapsed()
            {
                var result = _converter.Convert(ConditionMode.Default, typeof(Visibility), null, Culture);
                Assert.That(result, Is.EqualTo(Visibility.Collapsed));
            }

            [Test]
            public void Convert_NonConditionModeValue_ReturnsCollapsed()
            {
                var result = _converter.Convert("invalid", typeof(Visibility), null, Culture);
                Assert.That(result, Is.EqualTo(Visibility.Collapsed));
            }

            [Test]
            public void Convert_NullValue_ReturnsCollapsed()
            {
                var result = _converter.Convert(null, typeof(Visibility), null, Culture);
                Assert.That(result, Is.EqualTo(Visibility.Collapsed));
            }

            [Test]
            public void ConvertBack_ThrowsNotImplementedException()
            {
                Assert.Throws<NotImplementedException>((TestDelegate)(() =>
                    _converter.ConvertBack(Visibility.Visible, typeof(ConditionMode), null, Culture)));
            }
        }

        #endregion

        #region ConditionEnumToStringConverter

        [TestFixture]
        public class ConditionEnumToStringConverterTests
        {
            private ConditionEnumToStringConverter _converter;

            [SetUp]
            public void Setup()
            {
                _converter = new ConditionEnumToStringConverter();
            }

            [Test]
            public void Convert_ConditionMode_Default()
            {
                var result = _converter.Convert(ConditionMode.Default, typeof(string), null, Culture);
                Assert.That(result, Is.EqualTo("默认（使用步骤自身结果）"));
            }

            [Test]
            public void Convert_ConditionMode_Custom()
            {
                var result = _converter.Convert(ConditionMode.Custom, typeof(string), null, Culture);
                Assert.That(result, Is.EqualTo("自定义（使用下方规则判断）"));
            }

            [Test]
            public void Convert_ConditionVariable_ConstantTrue()
            {
                var result = _converter.Convert(ConditionVariable.ConstantTrue, typeof(string), null, Culture);
                Assert.That(result, Is.EqualTo("常量：真"));
            }

            [Test]
            public void Convert_ConditionVariable_SelfIsTrue()
            {
                var result = _converter.Convert(ConditionVariable.Self_IsTrue, typeof(string), null, Culture);
                Assert.That(result, Is.EqualTo("本步骤 → 执行结果"));
            }

            [Test]
            public void Convert_ConditionVariable_StepOCRText()
            {
                var result = _converter.Convert(ConditionVariable.Step_OCRText, typeof(string), null, Culture);
                // 已改用本地化资源，且名称中不再包含 OCR；
                // 测试环境未初始化本地化时 LanguageService 回退返回资源键本身
                Assert.That(result, Is.AnyOf("引用步骤 → 识别文本", "ConditionVariable_Step_Text"));
            }

            [Test]
            public void Convert_ConditionOperator_Equal()
            {
                var result = _converter.Convert(ConditionOperator.Equal, typeof(string), null, Culture);
                Assert.That(result, Is.EqualTo("等于 (==)"));
            }

            [Test]
            public void Convert_ConditionOperator_Contains()
            {
                var result = _converter.Convert(ConditionOperator.Contains, typeof(string), null, Culture);
                Assert.That(result, Is.EqualTo("包含"));
            }

            [Test]
            public void Convert_ConditionOperator_IsEmpty()
            {
                var result = _converter.Convert(ConditionOperator.IsEmpty, typeof(string), null, Culture);
                Assert.That(result, Is.EqualTo("为空"));
            }

            [Test]
            public void Convert_LogicConnector_And()
            {
                var result = _converter.Convert(LogicConnector.And, typeof(string), null, Culture);
                Assert.That(result, Is.EqualTo("并且 (AND)"));
            }

            [Test]
            public void Convert_LogicConnector_Or()
            {
                var result = _converter.Convert(LogicConnector.Or, typeof(string), null, Culture);
                Assert.That(result, Is.EqualTo("或者 (OR)"));
            }

            [Test]
            public void Convert_NullValue_ReturnsEmpty()
            {
                var result = _converter.Convert(null, typeof(string), null, Culture);
                Assert.That(result, Is.EqualTo(string.Empty));
            }

            [Test]
            public void ConvertBack_ThrowsNotImplementedException()
            {
                Assert.Throws<NotImplementedException>((TestDelegate)(() =>
                    _converter.ConvertBack("test", typeof(ConditionOperator), null, Culture)));
            }

            [Test]
            public void GetVariableTooltip_ConstantTrue()
            {
                var tooltip = ConditionEnumToStringConverter.GetVariableTooltip(ConditionVariable.ConstantTrue);
                Assert.That(tooltip, Is.EqualTo("始终为真的常量值"));
            }

            [Test]
            public void GetVariableTooltip_SelfSimilarity()
            {
                var tooltip = ConditionEnumToStringConverter.GetVariableTooltip(ConditionVariable.Self_Similarity);
                Assert.That(tooltip, Is.EqualTo("当前步骤图像匹配的实际相似度（0~1）"));
            }
        }

        #endregion
    }
}
