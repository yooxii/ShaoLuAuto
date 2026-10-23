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
        [TearDown]
        public void TearDown()
        {
            try { StepExecutionContext.Instance.Clear(); } catch { }
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new ShaoLu.Converters.AutomationStepBaseJsonConverter());
            return options;
        }

        [Test]
        public void ScanCodeStep_JsonRoundTrip_PreservesTypeAndFields()
        {
            var step = new ScanCodeStep("ScanCode_1")
            {
                WorkOrderField = "工单号X",
                StationField = "站别X",
                EmployeeField = "工号X",
                LineField = "线别X"
            };

            string json = JsonSerializer.Serialize<AutomationStepBase>(step, CreateOptions());
            var restored = JsonSerializer.Deserialize<AutomationStepBase>(json, CreateOptions());

            Assert.That(restored, Is.InstanceOf<ScanCodeStep>(), "重新打开工程后必须还是扫码步骤，不能降级成空步骤");
            var scan = (ScanCodeStep)restored;
            Assert.That(scan.Type, Is.EqualTo(StepType.ScanCode));
            Assert.That(scan.WorkOrderField, Is.EqualTo("工单号X"));
            Assert.That(scan.LineField, Is.EqualTo("线别X"));
        }

        [Test]
        public void ScanCodeStep_Clone_PreservesFieldNames()
        {
            var step = new ScanCodeStep("ScanCode_1") { WorkOrderField = "MO", StationField = "ST", EmployeeField = "EMP", LineField = "LN" };
            var clone = (ScanCodeStep)step.Clone();
            Assert.That(clone.WorkOrderField, Is.EqualTo("MO"));
            Assert.That(clone.EmployeeField, Is.EqualTo("EMP"));
            Assert.That(clone.Type, Is.EqualTo(StepType.ScanCode));
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
        public void ScanCodeStep_RunAsync_WithoutUserInput_FailsGracefully()
        {
            var step = new ScanCodeStep("ScanCode_1");
            bool result = step.RunAsync(default).GetAwaiter().GetResult();
            Assert.That(result, Is.False, "没有用户输入时应直接失败而不是抛异常");
            Assert.That(step.IsError, Is.True);
            Assert.That(step.ErrorMessage, Is.Not.Empty);
        }
    }
}
