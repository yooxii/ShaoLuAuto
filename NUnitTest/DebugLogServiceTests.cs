using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;

namespace NUnitTest
{
    /// <summary>
    /// 调试日志：调试模式关着不写文件；开着时一次运行一个文件，记步骤结果与跳转；
    /// 日志目录独立于程序日志与执行记录，清理只删调试日志。
    /// </summary>
    [TestFixture]
    public class DebugLogServiceTests
    {
        private string _dir;
        private bool _originalDebugMode;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "shaoLuDebugTests_" + Guid.NewGuid().ToString("N"));
            DebugLogService.DirectoryOverride = _dir;

            _originalDebugMode = SingletonLocator.Settings.Step.DebugMode;
            SingletonLocator.Settings.Step.DebugMode = false;
            DebugLogService.ClearLogs();
        }

        [TearDown]
        public void TearDown()
        {
            SingletonLocator.Settings.Step.DebugMode = _originalDebugMode;
            DebugLogService.DirectoryOverride = null;
            try
            {
                if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            }
            catch { }
        }

        private static EmptyStep NewStep(string name) => new EmptyStep { Name = name };

        [Test]
        public void Disabled_WritesNothing()
        {
            var step = NewStep("步骤一");
            Assert.That(DebugLogService.BeginSession("工程", 1), Is.Null);
            DebugLogService.StepStarted(0, step);
            DebugLogService.StepFinished(0, step, new StepExecutionResult { IsTrue = true }, "参数", -1, null);
            DebugLogService.EndSession("正常结束");

            Assert.That(Directory.Exists(_dir), Is.False);
        }

        [Test]
        public void Enabled_WritesStepResultAndJump()
        {
            SingletonLocator.Settings.Step.DebugMode = true;

            var first = NewStep("第一步");
            var second = NewStep("第二步");

            string path = DebugLogService.BeginSession("工程A", 2);
            Assert.That(path, Is.Not.Null);
            Assert.That(File.Exists(path), Is.True);

            DebugLogService.StepStarted(0, first);
            DebugLogService.StepFinished(0, first, new StepExecutionResult
            {
                IsTrue = true,
                ExecutionTimeMs = 120,
                Similarity = 0.93,
                OCRText = "ABC123",
            }, "耗时 120 ms; 相似度 0.93", 1, second.Name);
            DebugLogService.EndSession("正常结束");

            string text = File.ReadAllText(path);
            Assert.That(text, Does.Contain("工程A"));
            Assert.That(text, Does.Contain("第一步"));
            Assert.That(text, Does.Contain("结果=真"));
            Assert.That(text, Does.Contain("相似度=0.930"));
            Assert.That(text, Does.Contain("ABC123"));
            Assert.That(text, Does.Contain("跳转: 第 2 步 第二步"));
            Assert.That(text, Does.Contain("运行结束"));
        }

        [Test]
        public void Enabled_RecordsFailureAndError()
        {
            SingletonLocator.Settings.Step.DebugMode = true;
            var step = NewStep("出错步骤");

            string path = DebugLogService.BeginSession("工程B", 1);
            DebugLogService.StepFailed(0, step, "找不到图像", 88);

            Assert.That(File.ReadAllText(path), Does.Contain("找不到图像"));
        }

        [Test]
        public void Note_IsWrittenOnlyWhenEnabled()
        {
            SingletonLocator.Settings.Step.DebugMode = true;
            string path = DebugLogService.BeginSession("工程C", 0);
            DebugLogService.Note("用户暂停");
            Assert.That(File.ReadAllText(path), Does.Contain("用户暂停"));
        }

        [Test]
        public void ClearLogs_RemovesOnlyDebugLogs()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "debug_a.log"), "a");
            File.WriteAllText(Path.Combine(_dir, "debug_b.log"), "b");
            File.WriteAllText(Path.Combine(_dir, "keep.txt"), "keep");

            int removed = DebugLogService.ClearLogs();

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(File.Exists(Path.Combine(_dir, "debug_a.log")), Is.False);
            Assert.That(File.Exists(Path.Combine(_dir, "keep.txt")), Is.True);
        }

        [Test]
        public void LatestLogPath_ReturnsNewestFile()
        {
            Directory.CreateDirectory(_dir);
            string older = Path.Combine(_dir, "debug_old.log");
            string newer = Path.Combine(_dir, "debug_new.log");
            File.WriteAllText(older, "old");
            File.WriteAllText(newer, "new");
            File.SetLastWriteTime(older, DateTime.Now.AddMinutes(-10));
            File.SetLastWriteTime(newer, DateTime.Now);

            Assert.That(DebugLogService.LatestLogPath(), Is.EqualTo(newer));
        }

        [Test]
        public void LatestLogPath_WithoutDirectory_IsNull()
        {
            Assert.That(DebugLogService.LatestLogPath(), Is.Null);
        }
    }
}
