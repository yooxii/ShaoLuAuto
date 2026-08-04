using NUnit.Framework;
using ShaoLu.Models;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace NUnitTest
{
    /// <summary>
    /// StepsFile JSON 序列化/反序列化的单元测试
    /// </summary>
    [TestFixture]
    public class StepsFileTests
    {
        private string _testDir;

        [SetUp]
        public void Setup()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "ShaoLu_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }

        #region SaveStepsToJson / LoadStepsFromJson

#pragma warning disable CS0618 // 测试旧版 API 兼容性

        [Test]
        public void SaveAndLoad_TypeTextStep_RoundTrip()
        {
            var steps = new ObservableCollection<AutomationStepBase>
            {
                new TypeTextStep("输入步骤", "测试描述")
                {
                    TextToType = "Hello World",
                    DelayBetweenKeys = 0.08,
                    WaitTime = 0.5,
                    TrueGotoUid = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    FalseGotoUid = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                    IsNeed = true
                }
            };

            string filePath = Path.Combine(_testDir, "test_steps.json");

            StepsFile.SaveStepsToJson(steps, filePath);
            Assert.That(File.Exists(filePath), Is.True);

            var loaded = StepsFile.LoadStepsFromJson(filePath);
            Assert.That(loaded.Count, Is.EqualTo(1));

            var loadedStep = loaded[0] as TypeTextStep;
            Assert.That(loadedStep, Is.Not.Null);
            Assert.That(loadedStep.Name, Is.EqualTo("输入步骤"));
            Assert.That(loadedStep.Description, Is.EqualTo("测试描述"));
            Assert.That(loadedStep.TextToType, Is.EqualTo("Hello World"));
            Assert.That(loadedStep.DelayBetweenKeys, Is.EqualTo(0.08));
            Assert.That(loadedStep.WaitTime, Is.EqualTo(0.5));
            Assert.That(loadedStep.TrueGotoUid, Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000002")));
            Assert.That(loadedStep.FalseGotoUid, Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000003")));
            Assert.That(loadedStep.IsNeed, Is.True);
            Assert.That(loadedStep.Type, Is.EqualTo(StepType.TypeText));
        }

        [Test]
        public void SaveAndLoad_MultipleStepTypes_RoundTrip()
        {
            var steps = new ObservableCollection<AutomationStepBase>
            {
                new TypeTextStep("Step1") { TextToType = "ABC" },
                new EmptyStep("Step2"),
                new TypeTextMoreStep("Step3") { Prefix = "P", Infix = "I", Suffix = "S" }
            };

            string filePath = Path.Combine(_testDir, "multi_steps.json");

            StepsFile.SaveStepsToJson(steps, filePath);
            var loaded = StepsFile.LoadStepsFromJson(filePath);

            Assert.That(loaded.Count, Is.EqualTo(3));
            Assert.That(loaded[0], Is.InstanceOf<TypeTextStep>());
            Assert.That(loaded[1], Is.InstanceOf<EmptyStep>());
            Assert.That(loaded[2], Is.InstanceOf<TypeTextMoreStep>());

            var step3 = loaded[2] as TypeTextMoreStep;
            Assert.That(step3.Prefix, Is.EqualTo("P"));
            Assert.That(step3.Infix, Is.EqualTo("I"));
            Assert.That(step3.Suffix, Is.EqualTo("S"));
        }

        [Test]
        public void SaveAndLoad_EmptyList_RoundTrip()
        {
            var steps = new ObservableCollection<AutomationStepBase>();
            string filePath = Path.Combine(_testDir, "empty_steps.json");

            StepsFile.SaveStepsToJson(steps, filePath);
            var loaded = StepsFile.LoadStepsFromJson(filePath);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Count, Is.EqualTo(0));
        }

        [Test]
        public void SaveAndLoad_PopupStep_RoundTrip()
        {
            var steps = new ObservableCollection<AutomationStepBase>
            {
                new PopupStep("弹窗", "弹窗描述")
                {
                    Title = "提示",
                    PopupText = "确认继续？",
                    PopupType = "Question",
                    WaitTime = 1.0,
                    TrueGotoUid = Guid.Parse("00000000-0000-0000-0000-000000000005"),
                    FalseGotoUid = Guid.Parse("00000000-0000-0000-0000-000000000010")
                }
            };

            string filePath = Path.Combine(_testDir, "popup_steps.json");

            StepsFile.SaveStepsToJson(steps, filePath);
            var loaded = StepsFile.LoadStepsFromJson(filePath);

            Assert.That(loaded.Count, Is.EqualTo(1));
            var popup = loaded[0] as PopupStep;
            Assert.That(popup, Is.Not.Null);
            Assert.That(popup.Title, Is.EqualTo("提示"));
            Assert.That(popup.PopupText, Is.EqualTo("确认继续？"));
            Assert.That(popup.PopupType, Is.EqualTo("Question"));
            Assert.That(popup.TrueGotoUid, Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000005")));
            Assert.That(popup.FalseGotoUid, Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000010")));
        }

        [Test]
        public void SaveAndLoad_StepWithConditions_RoundTrip()
        {
            var step = new TypeTextStep("条件步骤")
            {
                TextToType = "Test",
                ConditionMode = ConditionMode.Custom
            };
            step.Conditions.Add(new StepCondition
            {
                Variable = ConditionVariable.Self_Similarity,
                Operator = ConditionOperator.GreaterThan,
                Value = "0.8",
                Connector = LogicConnector.And
            });
            step.Conditions.Add(new StepCondition
            {
                Variable = ConditionVariable.Self_IsTrue,
                Operator = ConditionOperator.Equal,
                Value = "true"
            });

            var steps = new ObservableCollection<AutomationStepBase> { step };
            string filePath = Path.Combine(_testDir, "condition_steps.json");

            StepsFile.SaveStepsToJson(steps, filePath);
            var loaded = StepsFile.LoadStepsFromJson(filePath);

            var loadedStep = loaded[0] as TypeTextStep;
            Assert.That(loadedStep.ConditionMode, Is.EqualTo(ConditionMode.Custom));
            Assert.That(loadedStep.Conditions.Count, Is.EqualTo(2));
            Assert.That(loadedStep.Conditions[0].Variable, Is.EqualTo(ConditionVariable.Self_Similarity));
            Assert.That(loadedStep.Conditions[0].Operator, Is.EqualTo(ConditionOperator.GreaterThan));
            Assert.That(loadedStep.Conditions[0].Value, Is.EqualTo("0.8"));
            Assert.That(loadedStep.Conditions[1].Variable, Is.EqualTo(ConditionVariable.Self_IsTrue));
        }

        [Test]
        public void SaveAndLoad_GotoReferences_UidRoundTrip()
        {
            // 验证跳转设置依赖的 Uid 在保存/重新打开后保持不变
            var step1 = new TypeTextStep("Step1") { TextToType = "A" };
            var step2 = new TypeTextStep("Step2") { TextToType = "B" };
            var step3 = new TypeTextStep("Step3") { TextToType = "C" };
            step1.TrueGotoUid = step3.Uid;
            step1.FalseGotoUid = step2.Uid;
            step2.TrueGotoUid = step1.Uid;

            var steps = new ObservableCollection<AutomationStepBase> { step1, step2, step3 };
            string filePath = Path.Combine(_testDir, "goto_uid_steps.json");

            StepsFile.SaveStepsToJson(steps, filePath);
            var loaded = StepsFile.LoadStepsFromJson(filePath);

            Assert.That(loaded.Count, Is.EqualTo(3));
            // Uid 必须保持一致，否则跳转引用全部失效
            Assert.That(loaded[0].Uid, Is.EqualTo(step1.Uid));
            Assert.That(loaded[1].Uid, Is.EqualTo(step2.Uid));
            Assert.That(loaded[2].Uid, Is.EqualTo(step3.Uid));
            // 跳转目标仍指向加载后的对应步骤
            Assert.That(loaded[0].TrueGotoUid, Is.EqualTo(loaded[2].Uid));
            Assert.That(loaded[0].FalseGotoUid, Is.EqualTo(loaded[1].Uid));
            Assert.That(loaded[1].TrueGotoUid, Is.EqualTo(loaded[0].Uid));
        }

        [Test]
        public void SaveAndLoad_ConditionTextExtraction_RoundTrip()
        {
            // 验证条件行的识别文本提取设置能往返保存
            var step = new TypeTextStep("提取步骤") { ConditionMode = ConditionMode.Custom };
            step.Conditions.Add(new StepCondition
            {
                Variable = ConditionVariable.Self_OCRText,
                TextExtractMode = TextExtractMode.FromSubstring,
                ExtractMarker = "SN:",
                ExtractLength = 8,
                Operator = ConditionOperator.Equal,
                Value = "ABC12345"
            });

            var steps = new ObservableCollection<AutomationStepBase> { step };
            string filePath = Path.Combine(_testDir, "extract_steps.json");

            StepsFile.SaveStepsToJson(steps, filePath);
            var loaded = StepsFile.LoadStepsFromJson(filePath);

            var cond = loaded[0].Conditions[0];
            Assert.That(cond.TextExtractMode, Is.EqualTo(TextExtractMode.FromSubstring));
            Assert.That(cond.ExtractMarker, Is.EqualTo("SN:"));
            Assert.That(cond.ExtractLength, Is.EqualTo(8));
        }

        [Test]
        public void SaveStepsToJson_NullSteps_ThrowsArgumentNullException()
        {
            string filePath = Path.Combine(_testDir, "null_steps.json");
            Assert.Throws<ArgumentNullException>((TestDelegate)(() => StepsFile.SaveStepsToJson(null, filePath)));
        }

        [Test]
        public void SaveStepsToJson_EmptyPath_ThrowsArgumentException()
        {
            var steps = new ObservableCollection<AutomationStepBase>();
            Assert.Throws<ArgumentException>((TestDelegate)(() => StepsFile.SaveStepsToJson(steps, "")));
        }

        [Test]
        public void LoadStepsFromJson_NonExistentFile_ThrowsFileNotFoundException()
        {
            string filePath = Path.Combine(_testDir, "nonexistent.json");
            Assert.Throws<FileNotFoundException>((TestDelegate)(() => StepsFile.LoadStepsFromJson(filePath)));
        }

        [Test]
        public void LoadStepsFromJson_EmptyPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>((TestDelegate)(() => StepsFile.LoadStepsFromJson("")));
        }

#pragma warning restore CS0618

        #endregion

        #region SaveAsAutoStepPackage 参数验证

        [Test]
        public void SaveAsAutoStepPackage_NullSteps_ThrowsArgumentNullException()
        {
            string path = Path.Combine(_testDir, "test.autostep");
            Assert.Throws<ArgumentNullException>((TestDelegate)(() => StepsFile.SaveAsAutoStepPackage(null, path)));
        }

        [Test]
        public void SaveAsAutoStepPackage_EmptyPath_ThrowsArgumentException()
        {
            var steps = new ObservableCollection<AutomationStepBase>();
            Assert.Throws<ArgumentException>((TestDelegate)(() => StepsFile.SaveAsAutoStepPackage(steps, "")));
        }

        [Test]
        public void LoadFromAutoStepPackage_EmptyPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>((TestDelegate)(() => StepsFile.LoadFromAutoStepPackage("")));
        }

        [Test]
        public void LoadFromAutoStepPackage_NonExistentFile_ThrowsFileNotFoundException()
        {
            string path = Path.Combine(_testDir, "nonexistent.autostep");
            Assert.Throws<FileNotFoundException>((TestDelegate)(() => StepsFile.LoadFromAutoStepPackage(path)));
        }

        #endregion

        #region GetWorkDirPath

        [Test]
        public void GetWorkDirPath_ReturnsCorrectPath()
        {
            string packagePath = @"C:\TestDir\MySteps.autostep";
            string expected = Path.Combine(@"C:\TestDir", "MySteps_images");
            string actual = StepsFile.GetWorkDirPath(packagePath);
            Assert.That(actual, Is.EqualTo(expected));
        }

        #endregion
    }
}
