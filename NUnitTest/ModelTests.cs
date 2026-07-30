using NUnit.Framework;
using ShaoLu.Models;

namespace NUnitTest
{
    /// <summary>
    /// 模型类的单元测试
    /// </summary>
    [TestFixture]
    public class ModelTests
    {
        #region StepCondition

        [TestFixture]
        public class StepConditionTests
        {
            [Test]
            public void DefaultValues_AreCorrect()
            {
                var condition = new StepCondition();

                Assert.That(condition.Variable, Is.EqualTo(ConditionVariable.Self_IsTrue));
                Assert.That(condition.StepLineNo, Is.EqualTo(0));
                Assert.That(condition.Operator, Is.EqualTo(ConditionOperator.Equal));
                Assert.That(condition.Value, Is.EqualTo(string.Empty));
                Assert.That(condition.Connector, Is.EqualTo(LogicConnector.And));
            }

            [Test]
            public void Clone_CreatesIndependentCopy()
            {
                var original = new StepCondition
                {
                    Variable = ConditionVariable.Step_Similarity,
                    StepLineNo = 5,
                    Operator = ConditionOperator.GreaterThan,
                    Value = "0.8",
                    Connector = LogicConnector.Or
                };

                var clone = original.Clone();

                Assert.That(clone.Variable, Is.EqualTo(ConditionVariable.Step_Similarity));
                Assert.That(clone.StepLineNo, Is.EqualTo(5));
                Assert.That(clone.Operator, Is.EqualTo(ConditionOperator.GreaterThan));
                Assert.That(clone.Value, Is.EqualTo("0.8"));
                Assert.That(clone.Connector, Is.EqualTo(LogicConnector.Or));
            }

            [Test]
            public void Clone_ModifyingClone_DoesNotAffectOriginal()
            {
                var original = new StepCondition
                {
                    Variable = ConditionVariable.Self_IsTrue,
                    Value = "true"
                };

                var clone = original.Clone();
                clone.Variable = ConditionVariable.ConstantFalse;
                clone.Value = "false";

                Assert.That(original.Variable, Is.EqualTo(ConditionVariable.Self_IsTrue));
                Assert.That(original.Value, Is.EqualTo("true"));
            }

            [Test]
            public void SetProperty_RaisesPropertyChanged()
            {
                var condition = new StepCondition();
                bool propertyChanged = false;
                condition.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(StepCondition.Value))
                        propertyChanged = true;
                };

                condition.Value = "new value";

                Assert.That(propertyChanged, Is.True);
            }
        }

        #endregion

        #region StepExecutionResult

        [TestFixture]
        public class StepExecutionResultTests
        {
            [Test]
            public void DefaultValues_AreCorrect()
            {
                var result = new StepExecutionResult();

                Assert.That(result.IsTrue, Is.False);
                Assert.That(result.ExecutionTimeMs, Is.EqualTo(0));
                Assert.That(result.Similarity, Is.EqualTo(-1));
                Assert.That(result.ClickPosition, Is.Null);
                Assert.That(result.OCRText, Is.Null);
                Assert.That(result.ErrorMessage, Is.Null);
                Assert.That(result.ExecutedAt, Is.Not.Null);
            }

            [Test]
            public void CanSetAllProperties()
            {
                var result = new StepExecutionResult
                {
                    IsTrue = true,
                    ExecutionTimeMs = 250.5,
                    Similarity = 0.95,
                    OCRText = "Test",
                    ErrorMessage = "None"
                };

                Assert.That(result.IsTrue, Is.True);
                Assert.That(result.ExecutionTimeMs, Is.EqualTo(250.5));
                Assert.That(result.Similarity, Is.EqualTo(0.95));
                Assert.That(result.OCRText, Is.EqualTo("Test"));
                Assert.That(result.ErrorMessage, Is.EqualTo("None"));
            }
        }

        #endregion

        #region AppSettings

        [TestFixture]
        public class AppSettingsTests
        {
            [Test]
            public void DefaultSettings_HaveCorrectDefaults()
            {
                var settings = new AppSettings();

                Assert.That(settings.App, Is.Not.Null);
                Assert.That(settings.Step, Is.Not.Null);
                Assert.That(settings.UserSettings, Is.Not.Null);
            }

            [Test]
            public void AppSettingsModel_Defaults()
            {
                var app = new AppSettingsModel();

                Assert.That(app.ThemeLight, Is.True);
                Assert.That(app.WindowWidth, Is.EqualTo(1000));
                Assert.That(app.WindowHeight, Is.EqualTo(600));
                Assert.That(app.LogRetentionDays, Is.EqualTo(0));
                Assert.That(app.WindowFont, Is.Not.Null);
            }

            [Test]
            public void StepSettingsModel_Defaults()
            {
                var step = new StepSettingsModel();

                Assert.That(step.ShowErrorPopup, Is.False);
                Assert.That(step.MinimizeOnRun, Is.True);
                Assert.That(step.DefaultSelfReferenceLimit, Is.EqualTo(10));
                Assert.That(step.ConfirmBeforeRun, Is.False);
                Assert.That(step.DefaultSimilarityThreshold, Is.EqualTo(0.85));
                Assert.That(step.DefaultWaitTime, Is.EqualTo(0.1));
                Assert.That(step.DefaultTimeout, Is.EqualTo(3));
                Assert.That(step.DefaultClicks, Is.EqualTo(1));
            }

            [Test]
            public void UserSettingsModel_Defaults()
            {
                var user = new UserSettingsModel();

                Assert.That(user.RememberUser, Is.False);
                Assert.That(user.LastUsername, Is.EqualTo(string.Empty));
            }

            [Test]
            public void HotKeySetting_Defaults()
            {
                var step = new StepSettingsModel();

                Assert.That(step.StartHotKey, Is.Not.Null);
                Assert.That(step.StopHotKey, Is.Not.Null);
                Assert.That(step.StartHotKey.Key, Is.EqualTo(System.Windows.Input.Key.F9));
                Assert.That(step.StopHotKey.Key, Is.EqualTo(System.Windows.Input.Key.F10));
            }
        }

        #endregion

        #region FontModel

        [TestFixture]
        public class FontModelTests
        {
            [Test]
            public void Clone_CreatesIndependentCopy()
            {
                var original = new FontModel
                {
                    FontFamily = "Arial",
                    FontSize = 14,
                    FontColor = 0xFF0000,
                    FontBackgroundColor = "#FFFFFF",
                    FontBorderColor = "#000000",
                    FontBorderWidth = "1"
                };

                var clone = original.Clone();

                Assert.That(clone.FontFamily, Is.EqualTo("Arial"));
                Assert.That(clone.FontSize, Is.EqualTo(14));
                Assert.That(clone.FontColor, Is.EqualTo(0xFF0000));
                Assert.That(clone.FontBackgroundColor, Is.EqualTo("#FFFFFF"));
                Assert.That(clone.FontBorderColor, Is.EqualTo("#000000"));
                Assert.That(clone.FontBorderWidth, Is.EqualTo("1"));
            }

            [Test]
            public void Clone_ModifyingClone_DoesNotAffectOriginal()
            {
                var original = new FontModel
                {
                    FontFamily = "Arial",
                    FontSize = 14
                };

                var clone = original.Clone();
                clone.FontFamily = "Times New Roman";
                clone.FontSize = 20;

                Assert.That(original.FontFamily, Is.EqualTo("Arial"));
                Assert.That(original.FontSize, Is.EqualTo(14));
            }

            [Test]
            public void DefaultConstructor_SetsSystemDefaults()
            {
                var font = new FontModel();

                Assert.That(font.FontFamily, Is.Not.Null.And.Not.Empty);
                Assert.That(font.FontSize, Is.GreaterThan(0));
            }
        }

        #endregion

        #region StepType 枚举

        [TestFixture]
        public class StepTypeTests
        {
            [Test]
            public void StepType_HasExpectedValues()
            {
                Assert.That((int)StepType.Empty, Is.EqualTo(-1));
                Assert.That((int)StepType.ClickImage, Is.EqualTo(0));
                Assert.That((int)StepType.TypeText, Is.EqualTo(1));
                Assert.That((int)StepType.FindImage, Is.EqualTo(2));
                Assert.That((int)StepType.ClickImages, Is.EqualTo(100));
                Assert.That((int)StepType.FindImages, Is.EqualTo(101));
                Assert.That((int)StepType.TypeTextMore, Is.EqualTo(102));
                Assert.That((int)StepType.TypeTextFromFile, Is.EqualTo(103));
                Assert.That((int)StepType.TextOCR, Is.EqualTo(200));
                Assert.That((int)StepType.Popup, Is.EqualTo(1000));
            }
        }

        #endregion

        #region StepExecutionLog

        [TestFixture]
        public class StepExecutionLogTests
        {
            [Test]
            public void CanSetProperties()
            {
                var log = new StepExecutionLog
                {
                    Id = 1,
                    StepUid = System.Guid.NewGuid(),
                    StepFileName = "test_file",
                    StepName = "Test Step",
                    InputText = "Hello"
                };

                Assert.That(log.Id, Is.EqualTo(1));
                Assert.That(log.StepUid, Is.Not.EqualTo(System.Guid.Empty));
                Assert.That(log.StepFileName, Is.EqualTo("test_file"));
                Assert.That(log.StepName, Is.EqualTo("Test Step"));
                Assert.That(log.InputText, Is.EqualTo("Hello"));
            }
        }

        #endregion
    }
}
