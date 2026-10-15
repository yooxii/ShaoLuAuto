using AcbelMes;
using NUnit.Framework;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace NUnitTest
{
    /// <summary>
    /// 「上传Acbel」步骤（ACBEL_INSERT_DATA）测试。
    /// 全部离线：桥接通道由 AcbelUploadStep.ChannelFactory 注入替身，测试绝不真的发 ACBEL_INSERT_DATA。
    /// </summary>
    [TestFixture]
    public class AcbelUploadStepTests
    {
        private const string ProgramFile = "LASER_1.PRG";

        private FakeAcbelChannel _fake;

        [SetUp]
        public void SetUp()
        {
            AcbelUploadStep.ResetChannelCacheForTests();
            _fake = new FakeAcbelChannel();
            AcbelUploadStep.ChannelFactory = () => _fake;
            StepExecutionContext.Instance.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            AcbelUploadStep.ResetChannelCacheForTests();
            StepExecutionContext.Instance.Clear();
        }

        #region 替身

        private sealed class FakeAcbelChannel : IAcbelBridgeChannel
        {
            private readonly Queue<AcbelCallResult> _insertResults = new Queue<AcbelCallResult>();

            public readonly List<string> Payloads = new List<string>();
            public readonly List<string> ProgramFiles = new List<string>();
            public AcbelCallResult OpenResult = Ok("database");
            public int OpenCount;
            public int DisposeCount;

            public int InsertCount { get { return Payloads.Count; } }

            public AcbelCallResult OpenDatabase()
            {
                OpenCount++;
                return OpenResult;
            }

            public AcbelCallResult InsertData(string uploadData, string programFile)
            {
                Payloads.Add(uploadData);
                ProgramFiles.Add(programFile);
                if (_insertResults.Count > 0)
                    return _insertResults.Dequeue();
                return Ok("1");
            }

            public void EnqueueInsert(params AcbelCallResult[] results)
            {
                foreach (var r in results)
                    _insertResults.Enqueue(r);
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        private static AcbelCallResult Ok(string value)
        {
            return new AcbelCallResult { TransportOk = true, Value = value };
        }

        #endregion

        #region 组装与校验

        /// <summary>与 AcbelInsertDataValidationTests 相同的合法 27 段取值。</summary>
        private static string[] ValidSegments()
        {
            string payload = new AcbelInsertDataBuilder()
                .SetResult(true)
                .Set("INTERNAL_SR", "O4769455")
                .Set("MO_NO", "HE47H1G001H18163")
                .Set("STATION", "165")
                .Set("LINE_NAME", "H18")
                .Set("START_TIME", "140735")
                .Set("END_TIME", "140846")
                .Set("TEST_DATE", "20210520")
                .Set("UPD_USER", "21807605")
                .Set("TEST_DATA1", "0.1")
                .Set("TEST_DATA2", "0.2")
                .Set("TEST_DATA3", "0.3")
                .Set("TEST_DATA4", "0.4")
                .Set("OUT_BARCODE", "FSC007A047H1GB2116000136")
                .Set("TOOL_NO", "TOOL")
                .Set("TOOL_NO1", "TOOL1")
                .Set("TOOL_NO2", "TOOL2")
                .Set("TOOL_NO3", "TOOL3")
                .Set("TOOL_NO4", "TOOL4")
                .Set("TOOL_NO5", "TOOL5")
                .Set("TOOL_NO6", "TOOL6")
                .Set("test_data5", "0.5")
                .Set("Laser SN", "GMAU0001")
                .Set("設備編號", "D205")
                .Build();

            return payload.Split(',');
        }

        private static AcbelUploadStep CreateValidStep()
        {
            var step = new AcbelUploadStep("AcbelUpload_1") { WaitTime = 0 };
            FillValid(step);
            return step;
        }

        private static void FillValid(AcbelUploadStep step)
        {
            string[] values = ValidSegments();
            step.ProgramFileRow.Text = ProgramFile;
            foreach (var row in step.FieldRows())
            {
                if (row.Index >= 1 && row.Index <= values.Length)
                    row.Text = values[row.Index - 1];
            }
        }

        [Test]
        public void 组装_27段并通过字段规范校验()
        {
            var step = CreateValidStep();

            string payload = step.BuildUploadData();
            string[] values = payload.Split(',');

            Assert.That(AcbelInsertDataSpec.FieldCount, Is.EqualTo(27));
            Assert.That(values.Length, Is.EqualTo(AcbelInsertDataSpec.FieldCount));

            AcbelPayloadValidationResult validation = step.ValidateUploadData();
            Assert.That(validation.IsValid, Is.True, validation.ToErrorText());

            foreach (var field in AcbelInsertDataSpec.RequiredFields)
                Assert.That(values[field.Index - 1], Is.Not.Empty, "必填段 " + field.Name + " 不应为空");

            Assert.That(values[AcbelInsertDataSpec.ByName("INTERNAL_SR").Index - 1], Is.EqualTo("O4769455"));
            Assert.That(values[AcbelInsertDataSpec.ByName("UPD_USER").Index - 1], Is.EqualTo("21807605"));
        }

        [Test]
        public void 必填段为空时判失败且不调用insert()
        {
            var step = CreateValidStep();
            step.RequiredRows.First(r => r.Index == AcbelInsertDataSpec.ByName("INTERNAL_SR").Index).Text = string.Empty;

            bool ok = step.RunAsync().GetAwaiter().GetResult();

            Assert.That(ok, Is.False);
            Assert.That(step.IsTrue, Is.False);
            Assert.That(step.ErrorMessage, Does.Contain("必填"));
            Assert.That(step.LastResult, Is.Not.Null);
            Assert.That(step.LastResult.IsTrue, Is.False);
            Assert.That(_fake.OpenCount, Is.EqualTo(0));
            Assert.That(_fake.InsertCount, Is.EqualTo(0));
        }

        [Test]
        public void 选填段缺失不影响校验()
        {
            var step = CreateValidStep();
            step.OptionalRows.First(r => r.Index == AcbelInsertDataSpec.ByName("Error Code").Index).Text = string.Empty;

            Assert.That(step.ValidateUploadData().IsValid, Is.True);
            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(_fake.InsertCount, Is.EqualTo(1));
        }

        [Test]
        public void 引用值参与27段组装()
        {
            var step = CreateValidStep();
            var uid = Guid.NewGuid();
            StepExecutionContext.Instance.SetResultByUid(uid, new StepExecutionResult { IsTrue = true, ExecutedAt = DateTime.Now });

            AcbelUploadRow resultRow = step.RequiredRows.First(r => r.Index == 1);
            resultRow.SetReference(uid, AcbelReferenceKind.ExecutionResult);

            string[] values = step.BuildUploadData().Split(',');
            Assert.That(values[0], Is.EqualTo("PASS"));
            Assert.That(resultRow.Text, Is.EqualTo("PASS"), "引用期间固定文本保留");
            Assert.That(step.ValidateUploadData().IsValid, Is.True);
        }

        #endregion

        #region 执行：试运行 / 连接 / 返回值

        [Test]
        public void 试运行_只校验不写库也不连接()
        {
            var step = CreateValidStep();
            step.DryRun = true;

            bool ok = step.RunAsync().GetAwaiter().GetResult();

            Assert.That(ok, Is.True);
            Assert.That(step.IsTrue, Is.True);
            Assert.That(_fake.OpenCount, Is.EqualTo(0));
            Assert.That(_fake.InsertCount, Is.EqualTo(0));
        }

        [Test]
        public void 试运行_必填缺失依然判失败()
        {
            var step = CreateValidStep();
            step.DryRun = true;
            step.RequiredRows.First(r => r.Index == 4).Text = string.Empty;

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(_fake.InsertCount, Is.EqualTo(0));
        }

        [Test]
        public void 连接成功时只open一次并复用缓存()
        {
            var step = CreateValidStep();

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.True);

            Assert.That(_fake.OpenCount, Is.EqualTo(1));
            Assert.That(_fake.InsertCount, Is.EqualTo(2));
        }

        [Test]
        public void 镭雕程式文件名作为insert第3参数()
        {
            var step = CreateValidStep();

            step.RunAsync().GetAwaiter().GetResult();

            Assert.That(_fake.ProgramFiles.Single(), Is.EqualTo(ProgramFile));
            Assert.That(_fake.Payloads.Single(), Is.EqualTo(step.BuildUploadData()));
        }

        [Test]
        public void 数据库连接失败时判失败且不insert()
        {
            var step = CreateValidStep();
            _fake.OpenResult = Ok("0}數據庫無法連接");

            bool ok = step.RunAsync().GetAwaiter().GetResult();

            Assert.That(ok, Is.False);
            Assert.That(step.IsTrue, Is.False);
            Assert.That(step.ErrorMessage, Does.Contain("數據庫無法連接"));
            Assert.That(_fake.InsertCount, Is.EqualTo(0));
        }

        [Test]
        public void 业务卡控_0开头判失败并带错误说明()
        {
            var step = CreateValidStep();
            _fake.EnqueueInsert(Ok("0}B006-INTERNAL_SR 不存在"));

            bool ok = step.RunAsync().GetAwaiter().GetResult();

            Assert.That(ok, Is.False);
            Assert.That(step.IsTrue, Is.False);
            Assert.That(step.ErrorMessage, Does.Contain("B006"));
            Assert.That(step.LastResult.ErrorMessage, Does.Contain("B006"));
            Assert.That(_fake.InsertCount, Is.EqualTo(1));
        }

        [Test]
        public void 内部异常_2开头重试一次后成功()
        {
            var step = CreateValidStep();
            _fake.EnqueueInsert(Ok("2}內部異常"), Ok("1"));

            bool ok = step.RunAsync().GetAwaiter().GetResult();

            Assert.That(ok, Is.True);
            Assert.That(step.IsTrue, Is.True);
            Assert.That(_fake.InsertCount, Is.EqualTo(2));
            Assert.That(_fake.Payloads[0], Is.EqualTo(_fake.Payloads[1]));
        }

        [Test]
        public void 内部异常_2开头重试一次后仍失败()
        {
            var step = CreateValidStep();
            _fake.EnqueueInsert(Ok("2}內部異常"), Ok("2}仍然失敗"));

            bool ok = step.RunAsync().GetAwaiter().GetResult();

            Assert.That(ok, Is.False);
            Assert.That(_fake.InsertCount, Is.EqualTo(2));
            Assert.That(step.ErrorMessage, Does.Contain("2}"));
        }

        [Test]
        public void 桥接运输失败时判失败不抛异常()
        {
            var step = CreateValidStep();
            _fake.EnqueueInsert(new AcbelCallResult { TransportOk = false, Error = "桥接进程已退出" });

            bool ok = step.RunAsync().GetAwaiter().GetResult();

            Assert.That(ok, Is.False);
            Assert.That(step.ErrorMessage, Does.Contain("桥接进程已退出"));
        }

        #endregion

        #region 引用解析

        [Test]
        public void 引用_执行结果按PASS与FAIL()
        {
            var step = new AcbelUploadStep("t") { WaitTime = 0 };
            var row = new AcbelUploadRow { Index = 1 };
            var uid = Guid.NewGuid();
            row.SetReference(uid, AcbelReferenceKind.ExecutionResult);

            StepExecutionContext.Instance.SetResultByUid(uid, new StepExecutionResult { IsTrue = true, ExecutedAt = DateTime.Now });
            Assert.That(row.IsReferenced, Is.True);
            Assert.That(step.ResolveRowValue(row), Is.EqualTo("PASS"));

            StepExecutionContext.Instance.SetResultByUid(uid, new StepExecutionResult { IsTrue = false, ExecutedAt = DateTime.Now.AddSeconds(1) });
            Assert.That(step.ResolveRowValue(row), Is.EqualTo("FAIL"));
        }

        [Test]
        public void 引用_OCR文本与直接获取文本()
        {
            var step = new AcbelUploadStep("t") { WaitTime = 0 };
            var uid = Guid.NewGuid();

            var ocrRow = new AcbelUploadRow { Index = 4 };
            ocrRow.SetReference(uid, AcbelReferenceKind.OcrText);
            var directRow = new AcbelUploadRow { Index = 5 };
            directRow.SetReference(uid, AcbelReferenceKind.DirectText);

            StepExecutionContext.Instance.SetResultByUid(uid, new StepExecutionResult
            {
                IsTrue = true,
                OCRText = "ABC123",
                TextSource = "OCR",
                ExecutedAt = DateTime.Now
            });
            Assert.That(step.ResolveRowValue(ocrRow), Is.EqualTo("ABC123"));
            Assert.That(step.ResolveRowValue(directRow), Is.EqualTo(string.Empty));

            StepExecutionContext.Instance.SetResultByUid(uid, new StepExecutionResult
            {
                IsTrue = true,
                OCRText = "XYZ",
                TextSource = "Direct",
                ExecutedAt = DateTime.Now.AddSeconds(1)
            });
            Assert.That(step.ResolveRowValue(ocrRow), Is.EqualTo(string.Empty));
            Assert.That(step.ResolveRowValue(directRow), Is.EqualTo("XYZ"));

            StepExecutionContext.Instance.SetResultByUid(uid, new StepExecutionResult
            {
                IsTrue = true,
                OCRText = "LEGACY",
                TextSource = null,
                ExecutedAt = DateTime.Now.AddSeconds(2)
            });
            Assert.That(step.ResolveRowValue(ocrRow), Is.EqualTo("LEGACY"), "旧数据 TextSource 为空按 OCR 处理");
            Assert.That(step.ResolveRowValue(directRow), Is.EqualTo(string.Empty));
        }

        [Test]
        public void 引用_步骤未执行过时取空串()
        {
            var step = new AcbelUploadStep("t") { WaitTime = 0 };
            var row = new AcbelUploadRow { Index = 4 };
            row.SetReference(Guid.NewGuid(), AcbelReferenceKind.OcrText);

            Assert.That(step.ResolveRowValue(row), Is.EqualTo(string.Empty));
        }

        [Test]
        public void 引用_清除后恢复原来的固定文本()
        {
            var row = new AcbelUploadRow { Index = 1, Text = "固定值" };
            row.SetReference(Guid.NewGuid(), AcbelReferenceKind.OcrText);

            Assert.That(row.IsReferenced, Is.True);
            Assert.That(row.Text, Is.EqualTo("固定值"));

            row.ClearReferenceCommand.Execute(null);

            Assert.That(row.IsReferenced, Is.False);
            Assert.That(row.Text, Is.EqualTo("固定值"));
        }

        [Test]
        public void 字段行按规范生成_9必填_18选填()
        {
            var step = new AcbelUploadStep("t");

            Assert.That(step.RequiredRows.Count, Is.EqualTo(9));
            Assert.That(step.OptionalRows.Count, Is.EqualTo(18));
            Assert.That(step.RequiredRows.All(r => r.IsRequired), Is.True);
            Assert.That(step.OptionalRows.All(r => !r.IsRequired), Is.True);
            Assert.That(step.RequiredRows.Select(r => r.Name).SequenceEqual(AcbelInsertDataSpec.RequiredFields.Select(f => f.Name)), Is.True);
            Assert.That(step.RequiredRows.Select(r => r.Index).SequenceEqual(step.RequiredRows.Select(r => r.Index).OrderBy(i => i)), Is.True);
        }

        #endregion

        #region 克隆与序列化

        [Test]
        public void Clone_深拷贝且互相独立()
        {
            var step = CreateValidStep();
            step.DryRun = true;
            step.RequiredRows.First(r => r.Index == 1).SetReference(Guid.NewGuid(), AcbelReferenceKind.ExecutionResult);

            var clone = (AcbelUploadStep)step.Clone();

            Assert.That(clone, Is.Not.SameAs(step));
            Assert.That(clone.Name, Is.EqualTo(step.Name));
            Assert.That(clone.DryRun, Is.True);
            Assert.That(clone.Uid, Is.Not.EqualTo(step.Uid));
            Assert.That(clone.RequiredRows.Count, Is.EqualTo(9));
            Assert.That(clone.OptionalRows.Count, Is.EqualTo(18));
            Assert.That(clone.RequiredRows[0], Is.Not.SameAs(step.RequiredRows[0]));
            Assert.That(clone.RequiredRows[0].RefKind, Is.EqualTo(AcbelReferenceKind.ExecutionResult));
            Assert.That(clone.ProgramFileRow.Text, Is.EqualTo(ProgramFile));
            Assert.That(clone.RequiredRows[0].OwnerUid, Is.EqualTo(clone.Uid), "新步骤的行不得指向原步骤");

            string original = step.RequiredRows[1].Text;
            clone.RequiredRows[1].Text = "改过的值";
            Assert.That(step.RequiredRows[1].Text, Is.EqualTo(original));
        }

        [Test]
        public void steps_json_往返序列化()
        {
            var step = CreateValidStep();
            step.Description = "上传测试";
            step.DryRun = true;
            var refUid = Guid.NewGuid();
            step.RequiredRows.First(r => r.Index == 1).SetReference(refUid, AcbelReferenceKind.ExecutionResult);
            StepExecutionContext.Instance.SetResultByUid(refUid, new StepExecutionResult { IsTrue = true, ExecutedAt = DateTime.Now });

            var steps = new ObservableCollection<AutomationStepBase> { step };
            string filePath = Path.Combine(Path.GetTempPath(), "acbel_upload_step_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                StepsFile.SaveStepsToJson(steps, filePath);
                Assert.That(File.Exists(filePath), Is.True);

                ObservableCollection<AutomationStepBase> loaded = StepsFile.LoadStepsFromJson(filePath);
                Assert.That(loaded.Count, Is.EqualTo(1));

                var back = loaded[0] as AcbelUploadStep;
                Assert.That(back, Is.Not.Null);
                Assert.That(back.Type, Is.EqualTo(StepType.AcbelUpload));
                Assert.That(back.Name, Is.EqualTo(step.Name));
                Assert.That(back.Description, Is.EqualTo("上传测试"));
                Assert.That(back.DryRun, Is.True);
                Assert.That(back.RequiredRows.Count, Is.EqualTo(9));
                Assert.That(back.OptionalRows.Count, Is.EqualTo(18));
                Assert.That(back.ProgramFileRow, Is.Not.Null);
                Assert.That(back.ProgramFileRow.Text, Is.EqualTo(ProgramFile));
                Assert.That(back.RequiredRows[0].RefStepUid, Is.EqualTo(refUid));
                Assert.That(back.RequiredRows[0].RefKind, Is.EqualTo(AcbelReferenceKind.ExecutionResult));
                Assert.That(back.BuildUploadData(), Is.EqualTo(step.BuildUploadData()));
                Assert.That(back.ValidateUploadData().IsValid, Is.True);
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        #endregion
    }
}
