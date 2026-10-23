using NUnit.Framework;
using ShaoLu.Models;
using ShaoLu.Services;
using System;
using System.Collections.Generic;

namespace NUnitTest
{
    /// <summary>
    /// 执行记录展示层单元测试：结果/执行时间/线程渲染与 10 秒内相同结果归并
    /// </summary>
    [TestFixture]
    public class ExecutionLogDisplayTests
    {
        private static readonly DateTime BaseTime = new DateTime(2026, 1, 1, 10, 0, 0);

        private static StepExecutionLog MakeLog(Guid uid, double seconds, bool isTrue, string resultSource = null)
        {
            return new StepExecutionLog
            {
                StepUid = uid,
                StepFileName = "demo.steps",
                StepName = "步骤A",
                InputText = resultSource ?? "[Result:" + isTrue + "]",
                IsTrue = isTrue,
                ExecutionTimeMs = 100,
                ThreadName = "主线程",
                ExecutedAt = BaseTime.AddSeconds(seconds),
            };
        }

        private static void Build(List<StepExecutionLog> logs)
        {
            ExecutionLogDisplay.Build(logs);
        }

        /// <summary>统计列表中实际会显示的行数（被归并行的记录不显示）</summary>
        private static int CountVisible(List<StepExecutionLog> logs)
        {
            int count = 0;
            foreach (var log in logs)
            {
                if (!log.IsMergedAway)
                    count++;
            }

            return count;
        }

        [Test]
        public void Build_SameResultWithinTenSeconds_MergesAndAppendsRepeatCount()
        {
            var uid = Guid.NewGuid();
            var logs = new List<StepExecutionLog>
            {
                MakeLog(uid, 0, true),
                MakeLog(uid, 3, true),
                MakeLog(uid, 6, true),
            };

            Build(logs);

            Assert.That(logs[0].RepeatCount, Is.EqualTo(3));
            Assert.That(logs[1].RepeatCount, Is.EqualTo(1));
            Assert.That(logs[2].RepeatCount, Is.EqualTo(1));
            Assert.That(logs[0].ResultDisplay, Is.EqualTo("[Result:True] ×3"));
            Assert.That(logs[1].ResultDisplay, Is.EqualTo("[Result:True]"));
        }

        [Test]
        public void Build_MergeKeepsFirstRecordAndOrder()
        {
            var uid = Guid.NewGuid();
            var first = MakeLog(uid, 5, true);
            var second = MakeLog(uid, 8, true);
            var logs = new List<StepExecutionLog> { first, second };

            Build(logs);

            Assert.That(logs[0], Is.SameAs(first));
            Assert.That(logs[0].RepeatCount, Is.EqualTo(2));
            Assert.That(logs[0].ExecutedAt, Is.EqualTo(first.ExecutedAt));
            Assert.That(logs[1].RepeatCount, Is.EqualTo(1));
        }

        [Test]
        public void Build_IntervalExactlyTenSeconds_Merges()
        {
            var uid = Guid.NewGuid();
            var logs = new List<StepExecutionLog> { MakeLog(uid, 0, true), MakeLog(uid, 10, true) };

            Build(logs);

            Assert.That(logs[0].RepeatCount, Is.EqualTo(2));
        }

        [Test]
        public void Build_IntervalOverTenSeconds_DoesNotMerge()
        {
            var uid = Guid.NewGuid();
            var logs = new List<StepExecutionLog> { MakeLog(uid, 0, true), MakeLog(uid, 11, true), MakeLog(uid, 30, true) };

            Build(logs);

            Assert.That(logs[0].RepeatCount, Is.EqualTo(1));
            Assert.That(logs[1].RepeatCount, Is.EqualTo(1));
            Assert.That(logs[0].ResultDisplay, Is.EqualTo("[Result:True]"));
            Assert.That(logs[0].ResultDisplay.Contains("×"), Is.False);
        }

        [Test]
        public void Build_DifferentResultSameStep_DoesNotMerge()
        {
            var uid = Guid.NewGuid();
            var logs = new List<StepExecutionLog> { MakeLog(uid, 0, true), MakeLog(uid, 2, false) };

            Build(logs);

            Assert.That(logs[0].RepeatCount, Is.EqualTo(1));
            Assert.That(logs[1].RepeatCount, Is.EqualTo(1));
        }

        [Test]
        public void Build_DifferentStepSameResult_DoesNotMerge()
        {
            var logs = new List<StepExecutionLog>
            {
                MakeLog(Guid.NewGuid(), 0, true),
                MakeLog(Guid.NewGuid(), 2, true),
            };

            Build(logs);

            Assert.That(logs[0].RepeatCount, Is.EqualTo(1));
            Assert.That(logs[1].RepeatCount, Is.EqualTo(1));
        }

        [Test]
        public void Build_SingleRecord_RepeatCountOneWithoutSuffix()
        {
            var logs = new List<StepExecutionLog> { MakeLog(Guid.NewGuid(), 0, true) };

            Build(logs);

            Assert.That(logs[0].RepeatCount, Is.EqualTo(1));
            Assert.That(logs[0].ResultDisplay, Is.EqualTo("[Result:True]"));
        }

        [Test]
        public void Build_NullOrEmptyList_DoesNotThrow()
        {
            Assert.DoesNotThrow((TestDelegate)(() => ExecutionLogDisplay.Build(null)));
            Assert.DoesNotThrow((TestDelegate)(() => ExecutionLogDisplay.Build(new List<StepExecutionLog>())));
        }

        [Test]
        public void ExecutionTimeDisplay_FromStructuredField_UsesNumberAndUnit()
        {
            var log = MakeLog(Guid.NewGuid(), 0, true);
            log.ExecutionTimeMs = 732.4;
            var logs = new List<StepExecutionLog> { log };

            Build(logs);

            Assert.That(logs[0].ExecutionTimeDisplay, Is.EqualTo("732 ms"));
        }

        [Test]
        public void ExecutionTimeDisplay_FromInputTextFallback()
        {
            var log = MakeLog(Guid.NewGuid(), 0, true);
            log.ExecutionTimeMs = null;
            log.InputText = "[Result:True] [Time:1234ms]";
            var logs = new List<StepExecutionLog> { log };

            Build(logs);

            Assert.That(logs[0].ExecutionTimeDisplay, Is.EqualTo("1234 ms"));
        }

        [Test]
        public void ExecutionTimeDisplay_NoData_ReturnsDash()
        {
            var log = MakeLog(Guid.NewGuid(), 0, true);
            log.ExecutionTimeMs = null;
            log.InputText = "[Result:True]";
            var logs = new List<StepExecutionLog> { log };

            Build(logs);

            Assert.That(logs[0].ExecutionTimeDisplay, Is.EqualTo("-"));
        }

        [Test]
        public void ResultDisplay_OcrText_UsesOcrPrefix()
        {
            var log = MakeLog(Guid.NewGuid(), 0, true);
            log.OCRText = "ABC123";
            log.TextSource = "OCR";
            var logs = new List<StepExecutionLog> { log };

            Build(logs);

            Assert.That(logs[0].ResultDisplay, Is.EqualTo("[Result:True] [OCR:ABC123]"));
        }

        [Test]
        public void ResultDisplay_DirectTextSource_UsesDirectPrefix()
        {
            var log = MakeLog(Guid.NewGuid(), 0, true);
            log.OCRText = "ABC123";
            log.TextSource = "Direct";
            var logs = new List<StepExecutionLog> { log };

            Build(logs);

            Assert.That(logs[0].ResultDisplay, Is.EqualTo("[Result:True] [直接获取:ABC123]"));
        }

        [Test]
        public void ResultDisplay_EmptyTextSource_FallsBackToOcrPrefix()
        {
            var log = MakeLog(Guid.NewGuid(), 0, true);
            log.OCRText = "ABC123";
            log.TextSource = null;
            var logs = new List<StepExecutionLog> { log };

            Build(logs);

            Assert.That(logs[0].ResultDisplay, Is.EqualTo("[Result:True] [OCR:ABC123]"));
        }

        [Test]
        public void ResultDisplay_NoOcrText_KeepsResultOnly()
        {
            var logs = new List<StepExecutionLog> { MakeLog(Guid.NewGuid(), 0, false) };

            Build(logs);

            Assert.That(logs[0].ResultDisplay, Is.EqualTo("[Result:False]"));
        }

        [Test]
        public void ResultDisplay_SimilarityAndError_AreAppended()
        {
            var log = MakeLog(Guid.NewGuid(), 0, true);
            log.Similarity = 0.98765;
            log.ErrorMessage = "boom";
            var logs = new List<StepExecutionLog> { log };

            Build(logs);

            Assert.That(logs[0].ResultDisplay, Is.EqualTo("[Result:True] [Similarity:0.988] [FailResult:boom]"));
        }

        [Test]
        public void ResultDisplay_NegativeSimilarity_IsSkipped()
        {
            var log = MakeLog(Guid.NewGuid(), 0, true);
            log.Similarity = -1;
            var logs = new List<StepExecutionLog> { log };

            Build(logs);

            Assert.That(logs[0].ResultDisplay, Is.EqualTo("[Result:True]"));
        }

        [Test]
        public void ResultDisplay_LegacyRowWithoutStructuredFields_FallsBackToInputText()
        {
            var log = new StepExecutionLog
            {
                StepUid = Guid.NewGuid(),
                StepName = "旧步骤",
                InputText = "[SubThread] [Result:True] [Time:15ms]",
                OCRText = null,
                ExecutedAt = BaseTime,
            };
            var logs = new List<StepExecutionLog> { log };

            Build(logs);

            Assert.That(logs[0].ResultDisplay, Is.EqualTo("[SubThread] [Result:True] [Time:15ms]"));
            Assert.That(logs[0].ExecutionTimeDisplay, Is.EqualTo("15 ms"));
            Assert.That(logs[0].ThreadDisplay, Is.EqualTo("子线程"), "线程列按 [SubThread] 前缀判定为子线程");
        }

        [Test]
        public void ThreadDisplay_StructuredThreadName_IsUsed()
        {
            var log = MakeLog(Guid.NewGuid(), 0, true);
            log.ThreadName = "子线程2";
            var logs = new List<StepExecutionLog> { log };

            Build(logs);

            Assert.That(logs[0].ThreadDisplay, Is.EqualTo("子线程2"));
        }

        [Test]
        public void ThreadDisplay_LegacySubThreadInputText_ShowsSubThreadLabel()
        {
            var log = MakeLog(Guid.NewGuid(), 0, true);
            log.ThreadName = null;
            log.InputText = "[SubThread] [Result:True]";
            var logs = new List<StepExecutionLog> { log };

            Build(logs);

            Assert.That(logs[0].ThreadDisplay, Is.EqualTo("子线程"));
        }

        [Test]
        public void ThreadDisplay_LegacyMainThreadInputText_ShowsMainThreadLabel()
        {
            var log = MakeLog(Guid.NewGuid(), 0, true);
            log.ThreadName = null;
            var logs = new List<StepExecutionLog> { log };

            Build(logs);

            Assert.That(logs[0].ThreadDisplay, Is.EqualTo("主线程"));
        }

        [Test]
        public void Build_MergedRows_AreMarkedMergedAwayAndHidden()
        {
            var uid = Guid.NewGuid();
            var logs = new List<StepExecutionLog>
            {
                MakeLog(uid, 0, true),
                MakeLog(uid, 3, true),
                MakeLog(uid, 6, true),
            };

            Build(logs);

            Assert.That(logs[0].IsMergedAway, Is.False, "组首记录仍然显示");
            Assert.That(logs[1].IsMergedAway, Is.True, "第二条已并入组首，不再显示");
            Assert.That(logs[2].IsMergedAway, Is.True, "第三条已并入组首，不再显示");
            Assert.That(CountVisible(logs), Is.EqualTo(1), "三条重复记录归并后列表里只剩一行");
        }

        [Test]
        public void Build_IntervalOverTenSeconds_AllRowsVisible()
        {
            var uid = Guid.NewGuid();
            var logs = new List<StepExecutionLog>
            {
                MakeLog(uid, 0, true),
                MakeLog(uid, 11, true),
                MakeLog(uid, 30, true),
            };

            Build(logs);

            Assert.That(logs[0].IsMergedAway, Is.False);
            Assert.That(logs[1].IsMergedAway, Is.False, "超过 10 秒不归并，仍显示");
            Assert.That(logs[2].IsMergedAway, Is.False, "超过 10 秒不归并，仍显示");
            Assert.That(CountVisible(logs), Is.EqualTo(3));
        }

        [Test]
        public void Build_SingleRecord_IsVisible()
        {
            var logs = new List<StepExecutionLog> { MakeLog(Guid.NewGuid(), 0, true) };

            Build(logs);

            Assert.That(logs[0].IsMergedAway, Is.False);
            Assert.That(CountVisible(logs), Is.EqualTo(1));
        }

        [Test]
        public void Build_DifferentStepOrResult_AllRowsVisible()
        {
            var uidA = Guid.NewGuid();
            var uidB = Guid.NewGuid();
            var logs = new List<StepExecutionLog>
            {
                MakeLog(uidA, 0, true),
                MakeLog(uidB, 3, true),
                MakeLog(uidA, 6, false),
            };

            Build(logs);

            Assert.That(logs[0].IsMergedAway, Is.False);
            Assert.That(logs[1].IsMergedAway, Is.False, "步骤不同不归并");
            Assert.That(logs[2].IsMergedAway, Is.False, "结果不同不归并");
            Assert.That(CountVisible(logs), Is.EqualTo(3));
        }

        [Test]
        public void Build_MergedGroupHead_KeepsRepeatCountAndSuffix()
        {
            var uid = Guid.NewGuid();
            var logs = new List<StepExecutionLog>
            {
                MakeLog(uid, 0, true),
                MakeLog(uid, 2, true),
            };

            Build(logs);

            Assert.That(logs[0].IsMergedAway, Is.False);
            Assert.That(logs[0].RepeatCount, Is.EqualTo(2));
            Assert.That(logs[0].ResultDisplay, Is.EqualTo("[Result:True] ×2"));
        }
    }
}
