using NUnit.Framework;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Viewmodels;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Linq;
using System.Text.Json;

namespace NUnitTest
{
    /// <summary>
    /// 交付前冒烟测试：覆盖最近改动的关键链路（扫码步骤、扫码结果引用、执行细节兜底）。
    /// 只验证「改动不会把基本功能带崩」，不追求穷尽。
    /// </summary>
    [TestFixture]
    public class SmokeTests
    {
        [SetUp]
        public void SetUp()
        {
            ScanCodeStep.ScanPromptOverride = null;
        }

        [TearDown]
        public void TearDown()
        {
            ScanCodeStep.ScanPromptOverride = null;
            try { StepExecutionContext.Instance.Clear(); } catch { }
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new ShaoLu.Converters.AutomationStepBaseJsonConverter());
            return options;
        }

        [Test]
        public void ScanCodeStep_JsonRoundTrip_PreservesTypeAndName()
        {
            var step = new ScanCodeStep("ScanCode_1") { WaitTime = 1.5, IsNeed = true };

            string json = JsonSerializer.Serialize<AutomationStepBase>(step, CreateOptions());
            var restored = JsonSerializer.Deserialize<AutomationStepBase>(json, CreateOptions());

            Assert.That(restored, Is.InstanceOf<ScanCodeStep>(), "重新打开工程后必须还是扫码步骤，不能降级成空步骤");
            var scan = (ScanCodeStep)restored;
            Assert.That(scan.Type, Is.EqualTo(StepType.ScanCode));
            Assert.That(scan.Name, Is.EqualTo("ScanCode_1"));
            Assert.That(scan.WaitTime, Is.EqualTo(1.5));
        }

        [Test]
        public void ScanCodeStep_旧工程里的字段名配置不影响打开()
        {
            // 旧版本的扫码步骤会存工单号/站别/工号/线别四个字段名（现在这些函数归上传步骤，参数也没了）。
            // 旧工程文件里留着这四个属性时必须能照常打开，不能报错。
            var step = new ScanCodeStep("ScanCode_1");
            string json = JsonSerializer.Serialize<AutomationStepBase>(step, CreateOptions());
            string legacy = json.Insert(json.IndexOf('{') + 1,
                "\"WorkOrderField\":\"工单号X\",\"StationField\":\"站别X\",\"EmployeeField\":\"工号X\",\"LineField\":\"线别X\",");

            var restored = JsonSerializer.Deserialize<AutomationStepBase>(legacy, CreateOptions());

            Assert.That(restored, Is.InstanceOf<ScanCodeStep>());
            Assert.That(restored.Type, Is.EqualTo(StepType.ScanCode));
        }

        [Test]
        public void ScanCodeStep_Clone_PreservesNameAndSettings()
        {
            var step = new ScanCodeStep("ScanCode_1") { WaitTime = 2, IsNeed = true };
            var clone = (ScanCodeStep)step.Clone();

            Assert.That(clone.Uid, Is.Not.EqualTo(step.Uid));
            Assert.That(clone.Name, Is.EqualTo("ScanCode_1"));
            Assert.That(clone.WaitTime, Is.EqualTo(2));
            Assert.That(clone.IsNeed, Is.True);
            Assert.That(clone.Type, Is.EqualTo(StepType.ScanCode));
        }

        [Test]
        public void ScanCodeStep_RunAsync_把扫到的条码作为步骤文本()
        {
            ScanCodeStep.ScanPromptOverride = () => "  SN123456  ";
            var step = new ScanCodeStep("ScanCode_1");

            bool result = step.RunAsync(default).GetAwaiter().GetResult();

            Assert.That(result, Is.True);
            Assert.That(step.IsError, Is.False);
            Assert.That(step.LastResult, Is.Not.Null);
            Assert.That(step.LastResult.IsTrue, Is.True);
            Assert.That(step.LastResult.OCRText, Is.EqualTo("SN123456"), "条码去掉首尾空白后作为本步骤文本");
        }

        [Test]
        public void ScanCodeStep_RunAsync_取消扫码时判失败()
        {
            ScanCodeStep.ScanPromptOverride = () => null;
            var step = new ScanCodeStep("ScanCode_1");

            bool result = step.RunAsync(default).GetAwaiter().GetResult();

            Assert.That(result, Is.False);
            Assert.That(step.IsError, Is.True);
            Assert.That(step.ErrorMessage, Is.Not.Empty);
            Assert.That(step.LastResult.IsTrue, Is.False);
        }

        [Test]
        public void ScanResultReference_ReturnsScannedBarcode()
        {
            var scan = new ScanCodeStep("ScanCode_1");
            StepExecutionContext.Instance.SetResultByUid(scan.Uid,
                new StepExecutionResult { IsTrue = true, OCRText = "SN123456", ExecutedAt = DateTime.Now });

            var upload = new AcbelUploadStep("AcbelUpload_1");
            var internalSr = upload.RequiredRows.First(r => r.Index == 4);
            internalSr.RefStepUid = scan.Uid;
            internalSr.RefKind = AcbelReferenceKind.ScanResult;

            Assert.That(upload.ResolveRowValue(internalSr), Is.EqualTo("SN123456"), "单体条码应引用扫码步骤的扫码结果");
        }

        [Test]
        public void FocusWindowStep_JsonRoundTrip_PreservesOperation()
        {
            var step = new FocusWindowStep("WindowControl_1")
            {
                SelectedWindowTitle = "Notepad",
                Operation = WindowOperation.Minimize,
            };

            string json = JsonSerializer.Serialize<AutomationStepBase>(step, CreateOptions());
            var restored = JsonSerializer.Deserialize<AutomationStepBase>(json, CreateOptions());

            Assert.That(restored, Is.InstanceOf<FocusWindowStep>(), "重新打开工程后必须还是窗口操作步骤");
            Assert.That(((FocusWindowStep)restored).Operation, Is.EqualTo(WindowOperation.Minimize));
        }

        [Test]
        public void FocusWindowStep_旧工程没有操作字段时按聚焦处理()
        {
            // 旧版本只有“聚焦窗口”一种行为，存量工程里没有 Operation 字段（Type 按枚举数字存）
            string legacy = "{ \"Type\": 204, \"Name\": \"FocusWindow_1\", \"SelectedWindowTitle\": \"Notepad\" }";

            var restored = JsonSerializer.Deserialize<AutomationStepBase>(legacy, CreateOptions());

            Assert.That(restored, Is.InstanceOf<FocusWindowStep>());
            Assert.That(((FocusWindowStep)restored).Operation, Is.EqualTo(WindowOperation.Activate));
        }
    }
}
