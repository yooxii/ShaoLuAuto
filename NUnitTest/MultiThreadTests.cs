using NUnit.Framework;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NUnitTest
{
    /// <summary>多线程测试的共用辅助</summary>
    internal static class MultiThreadTestHelper
    {
        /// <summary>等待信号中心的阻塞等待者数量达到期望值（超时返回 false）</summary>
        internal static bool WaitForPendingCount(int expected, int timeoutMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (SignalCenter.PendingCount != expected && sw.ElapsedMilliseconds < timeoutMs)
            {
                Thread.Sleep(10);
            }
            return SignalCenter.PendingCount == expected;
        }

        internal static string IntText(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>信号中心：覆盖式最新值、消费语义、名字隔离、阻塞等待与并发收发</summary>
    [TestFixture]
    public class SignalCenterTests
    {
        [SetUp]
        public void Setup() => SignalCenter.Reset();

        [TearDown]
        public void TearDown() => SignalCenter.Reset();

        [Test]
        public void Send_OverwritesWithLatestValue()
        {
            SignalCenter.Send("s1", SignalValueKind.Int, "1");
            SignalCenter.Send("s1", SignalValueKind.Int, "2");

            Assert.That(SignalCenter.TryReceive("s1", out var kind, out var value), Is.True);
            Assert.That(value, Is.EqualTo("2"));
            Assert.That(kind, Is.EqualTo(SignalValueKind.Int));
        }

        [Test]
        public void TryReceive_ConsumesValue_SecondReceiveFails()
        {
            SignalCenter.Send("s2", SignalValueKind.Bool, "true");

            Assert.That(SignalCenter.TryReceive("s2", out var kind1, out var value1), Is.True);
            Assert.That(value1, Is.EqualTo("true"));
            Assert.That(kind1, Is.EqualTo(SignalValueKind.Bool));

            Assert.That(SignalCenter.TryReceive("s2", out var kind2, out var value2), Is.False);
            Assert.That(value2, Is.Null);
            Assert.That(kind2, Is.EqualTo(SignalValueKind.Bool));
        }

        [Test]
        public void DifferentNames_AreIsolated()
        {
            SignalCenter.Send("a", SignalValueKind.String, "A");
            SignalCenter.Send("b", SignalValueKind.String, "B");

            Assert.That(SignalCenter.TryReceive("a", out _, out var valueA), Is.True);
            Assert.That(valueA, Is.EqualTo("A"));
            Assert.That(SignalCenter.TryReceive("b", out _, out var valueB), Is.True);
            Assert.That(valueB, Is.EqualTo("B"));
        }

        [Test]
        public void SignalName_IsCaseSensitive()
        {
            SignalCenter.Send("Case", SignalValueKind.String, "1");

            Assert.That(SignalCenter.TryReceive("case", out _, out _), Is.False);
            Assert.That(SignalCenter.TryReceive("Case", out _, out _), Is.True);
        }

        [Test]
        public void BlankNames_AreIgnored()
        {
            SignalCenter.Send("", SignalValueKind.Bool, "true");
            SignalCenter.Send("   ", SignalValueKind.Bool, "true");
            SignalCenter.Send(null, SignalValueKind.Bool, "true");

            Assert.That(SignalCenter.TryReceive("", out _, out _), Is.False);
            Assert.That(SignalCenter.TryReceive("   ", out _, out _), Is.False);
            Assert.That(SignalCenter.TryReceive(null, out _, out _), Is.False);
            Assert.That(SignalCenter.WaitForSignal("", 10, CancellationToken.None, out _, out _), Is.False);
        }

        [Test]
        public void Reset_ClearsAllSignals()
        {
            SignalCenter.Send("r1", SignalValueKind.Bool, "true");
            SignalCenter.Send("r2", SignalValueKind.String, "x");

            SignalCenter.Reset();

            Assert.That(SignalCenter.TryReceive("r1", out _, out _), Is.False);
            Assert.That(SignalCenter.TryReceive("r2", out _, out _), Is.False);
        }

        [Test]
        public void WaitForSignal_ReturnsExistingValueImmediately()
        {
            SignalCenter.Send("w1", SignalValueKind.String, "hello");

            bool got = SignalCenter.WaitForSignal("w1", 500, CancellationToken.None, out var kind, out var value);

            Assert.That(got, Is.True);
            Assert.That(value, Is.EqualTo("hello"));
            Assert.That(kind, Is.EqualTo(SignalValueKind.String));
            Assert.That(SignalCenter.TryReceive("w1", out _, out _), Is.False, "取到即消费");
        }

        [Test]
        public void WaitForSignal_TimesOut_AndReturnsFalse()
        {
            var sw = Stopwatch.StartNew();
            bool got = SignalCenter.WaitForSignal("never-sent", 200, CancellationToken.None, out _, out _);
            sw.Stop();

            Assert.That(got, Is.False);
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(150));
            Assert.That(SignalCenter.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void WaitForSignal_CancelledWhileWaiting_ReturnsFalse()
        {
            var cts = new CancellationTokenSource();
            bool got = true;
            long elapsed = 0;

            var task = Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                got = SignalCenter.WaitForSignal("cancel-wait", 10000, cts.Token, out _, out _);
                sw.Stop();
                elapsed = sw.ElapsedMilliseconds;
            });

            Assert.That(MultiThreadTestHelper.WaitForPendingCount(1), Is.True, "等待者未进入阻塞等待");
            cts.Cancel();
            Assert.That(task.Wait(5000), Is.True, "取消后未及时返回");

            Assert.That(got, Is.False);
            Assert.That(elapsed, Is.LessThan(3000));
            Assert.That(MultiThreadTestHelper.WaitForPendingCount(0), Is.True);
        }

        [Test]
        public void WaitForSignal_WakesOnSend_AndTracksPendingCount()
        {
            SignalValueKind kind = SignalValueKind.Bool;
            string value = null;
            bool got = false;

            var task = Task.Run(() =>
            {
                got = SignalCenter.WaitForSignal("wake", 10000, CancellationToken.None, out kind, out value);
            });

            Assert.That(MultiThreadTestHelper.WaitForPendingCount(1), Is.True, "等待者未进入阻塞等待");
            SignalCenter.Send("wake", SignalValueKind.Int, "42");
            Assert.That(task.Wait(5000), Is.True, "发送后等待者未被唤醒");

            Assert.That(got, Is.True);
            Assert.That(value, Is.EqualTo("42"));
            Assert.That(kind, Is.EqualTo(SignalValueKind.Int));
            Assert.That(MultiThreadTestHelper.WaitForPendingCount(0), Is.True);
        }

        [Test]
        public void ConcurrentSendReceive_NoLossNoCrosstalk()
        {
            const int channels = 4;
            const int rounds = 200;
            var received = new List<string>[channels];
            for (int c = 0; c < channels; c++)
                received[c] = new List<string>();

            var tasks = new List<Task>();
            for (int c = 0; c < channels; c++)
            {
                int id = c;
                string dataName = "data-" + id;
                string ackName = "ack-" + id;

                tasks.Add(Task.Run(() =>
                {
                    for (int v = 0; v < rounds; v++)
                    {
                        SignalCenter.Send(dataName, SignalValueKind.Int, MultiThreadTestHelper.IntText(v));
                        // 握手：等接收端确认，避免“同名覆盖”吞掉中间值
                        if (!SignalCenter.WaitForSignal(ackName, 10000, CancellationToken.None, out _, out _))
                            return;
                    }
                }));

                tasks.Add(Task.Run(() =>
                {
                    for (int v = 0; v < rounds; v++)
                    {
                        if (!SignalCenter.WaitForSignal(dataName, 10000, CancellationToken.None, out _, out var value))
                            return;
                        received[id].Add(value);
                        SignalCenter.Send(ackName, SignalValueKind.Bool, "true");
                    }
                }));
            }

            Assert.That(Task.WaitAll(tasks.ToArray(), 60000), Is.True, "并发收发超时");

            var expected = Enumerable.Range(0, rounds).Select(MultiThreadTestHelper.IntText).ToList();
            for (int c = 0; c < channels; c++)
            {
                Assert.That(received[c].Count, Is.EqualTo(rounds), "通道 " + c + " 丢值");
                Assert.That(received[c], Is.EqualTo(expected), "通道 " + c + " 串扰或乱序");
            }
        }
    }

    /// <summary>多线程区域解析器</summary>
    [TestFixture]
    public class MultiThreadRangeTests
    {
        [Test]
        public void ByLineNo_WithEndStepUid_ResolvesToThatIndex()
        {
            var endStep = new EmptyStep("end");
            var steps = new List<AutomationStepBase>
            {
                new MultiThreadStartStep("start"),
                new EmptyStep("a"),
                new EmptyStep("b"),
                endStep,
                new EmptyStep("after"),
            };

            bool ok = MultiThreadRange.TryResolve(steps, 0, MultiThreadRangeMode.ByLineNo, endStep.Uid, out int endIndex, out string error);

            Assert.That(ok, Is.True, error);
            Assert.That(endIndex, Is.EqualTo(3));
        }

        [Test]
        public void ByLineNo_NullEndUid_ResolvesToScriptEnd()
        {
            var steps = new List<AutomationStepBase>
            {
                new MultiThreadStartStep("start"),
                new EmptyStep("a"),
                new EmptyStep("b"),
            };

            bool ok = MultiThreadRange.TryResolve(steps, 0, MultiThreadRangeMode.ByLineNo, null, out int endIndex, out string error);

            Assert.That(ok, Is.True, error);
            Assert.That(endIndex, Is.EqualTo(2));
        }

        [Test]
        public void ByLineNo_EndBeforeStart_Fails()
        {
            var endStep = new EmptyStep("end");
            var steps = new List<AutomationStepBase>
            {
                endStep,
                new MultiThreadStartStep("start"),
                new EmptyStep("a"),
            };

            bool ok = MultiThreadRange.TryResolve(steps, 1, MultiThreadRangeMode.ByLineNo, endStep.Uid, out int endIndex, out string error);

            Assert.That(ok, Is.False);
            Assert.That(endIndex, Is.EqualTo(-1));
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ByLineNo_EndIsStartItself_Fails()
        {
            var start = new MultiThreadStartStep("start");
            var steps = new List<AutomationStepBase> { start, new EmptyStep("a") };

            bool ok = MultiThreadRange.TryResolve(steps, 0, MultiThreadRangeMode.ByLineNo, start.Uid, out _, out string error);

            Assert.That(ok, Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ByLineNo_UnknownEndUid_Fails()
        {
            var steps = new List<AutomationStepBase> { new MultiThreadStartStep("start"), new EmptyStep("a") };

            bool ok = MultiThreadRange.TryResolve(steps, 0, MultiThreadRangeMode.ByLineNo, Guid.NewGuid(), out _, out string error);

            Assert.That(ok, Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ByEndStep_FindsNearestMarker()
        {
            var steps = new List<AutomationStepBase>
            {
                new MultiThreadStartStep("start"),
                new EmptyStep("a"),
                new MultiThreadEndStep("marker1"),
                new MultiThreadEndStep("marker2"),
                new EmptyStep("after"),
            };

            bool ok = MultiThreadRange.TryResolve(steps, 0, MultiThreadRangeMode.ByEndStep, null, out int endIndex, out string error);

            Assert.That(ok, Is.True, error);
            Assert.That(endIndex, Is.EqualTo(1), "应取最近的标记，且标记本身不执行");
        }

        [Test]
        public void ByEndStep_MarkerRightAfterStart_EmptyRegion()
        {
            var steps = new List<AutomationStepBase>
            {
                new MultiThreadStartStep("start"),
                new MultiThreadEndStep("marker"),
                new EmptyStep("after"),
            };

            bool ok = MultiThreadRange.TryResolve(steps, 0, MultiThreadRangeMode.ByEndStep, null, out int endIndex, out string error);

            Assert.That(ok, Is.True, error);
            Assert.That(endIndex, Is.EqualTo(0));
        }

        [Test]
        public void ByEndStep_MissingMarker_Fails()
        {
            var steps = new List<AutomationStepBase> { new MultiThreadStartStep("start"), new EmptyStep("a") };

            bool ok = MultiThreadRange.TryResolve(steps, 0, MultiThreadRangeMode.ByEndStep, null, out _, out string error);

            Assert.That(ok, Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ByEndStep_IgnoresRangeEndUid_AndIgnoresEarlierMarkers()
        {
            var steps = new List<AutomationStepBase>
            {
                new EmptyStep("before"),
                new MultiThreadEndStep("orphan-marker"),
                new MultiThreadStartStep("start"),
                new EmptyStep("a"),
                new MultiThreadEndStep("marker"),
                new EmptyStep("after"),
            };

            bool ok = MultiThreadRange.TryResolve(steps, 2, MultiThreadRangeMode.ByEndStep, Guid.NewGuid(), out int endIndex, out string error);

            Assert.That(ok, Is.True, error);
            Assert.That(endIndex, Is.EqualTo(3), "ByEndStep 忽略 RangeEndUid，且只取开始步骤之后的标记");
        }

        [Test]
        public void ByLineNo_IgnoresEndMarker()
        {
            var endStep = new EmptyStep("end");
            var steps = new List<AutomationStepBase>
            {
                new MultiThreadStartStep("start"),
                new MultiThreadEndStep("marker"),
                endStep,
                new EmptyStep("after"),
            };

            bool ok = MultiThreadRange.TryResolve(steps, 0, MultiThreadRangeMode.ByLineNo, endStep.Uid, out int endIndex, out string error);

            Assert.That(ok, Is.True, error);
            Assert.That(endIndex, Is.EqualTo(2), "ByLineNo 忽略结束标记");
        }

        [Test]
        public void StartIndexOutOfRange_Fails()
        {
            var steps = new List<AutomationStepBase> { new MultiThreadStartStep("start"), new EmptyStep("a") };

            Assert.That(MultiThreadRange.TryResolve(steps, 2, MultiThreadRangeMode.ByLineNo, null, out int endIndex, out string error), Is.False);
            Assert.That(endIndex, Is.EqualTo(-1));
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(MultiThreadRange.TryResolve(steps, -1, MultiThreadRangeMode.ByEndStep, null, out _, out _), Is.False);
        }

        [Test]
        public void NullSteps_Fails()
        {
            Assert.That(MultiThreadRange.TryResolve(null, 0, MultiThreadRangeMode.ByLineNo, null, out int endIndex, out string error), Is.False);
            Assert.That(endIndex, Is.EqualTo(-1));
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }
    }

    /// <summary>五个多线程步骤：默认值、RunAsync 语义、Clone 独立性、序列化往返</summary>
    [TestFixture]
    public class MultiThreadStepTests
    {
        private string _testDir;

        [SetUp]
        public void Setup()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "ShaoLu_MT_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            SignalCenter.Reset();
            MultiThreadInterruptBridge.RegisterHandler(null);
            MultiThreadInterruptBridge.InChildThread = false;
        }

        [TearDown]
        public void TearDown()
        {
            SignalCenter.Reset();
            MultiThreadInterruptBridge.RegisterHandler(null);
            MultiThreadInterruptBridge.InChildThread = false;
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }

        #region StepType 新取值

        [Test]
        public void NewStepTypeValues_AreFixed()
        {
            Assert.That((int)StepType.MultiThread, Is.EqualTo(300));
            Assert.That((int)StepType.MultiThreadEnd, Is.EqualTo(301));
            Assert.That((int)StepType.SendSignal, Is.EqualTo(302));
            Assert.That((int)StepType.ReceiveSignal, Is.EqualTo(303));
            Assert.That((int)StepType.Interrupt, Is.EqualTo(304));
        }

        [Test]
        public void NewStepTypeValues_DoNotConflictWithExisting()
        {
            var newValues = new[] { StepType.MultiThread, StepType.MultiThreadEnd, StepType.SendSignal, StepType.ReceiveSignal, StepType.Interrupt };
            var existing = new[] { StepType.Empty, StepType.ClickImage, StepType.TypeText, StepType.FindImage,
                StepType.ClickImages, StepType.FindImages, StepType.TypeTextMore, StepType.TypeTextFromFile,
                StepType.GetInput, StepType.MouseAction, StepType.Statistics, StepType.BurnInConfig,
                StepType.FocusWindow, StepType.Popup };

            var newInts = newValues.Select(v => (int)v).ToList();
            Assert.That(newInts.Distinct().Count(), Is.EqualTo(newValues.Length));
            Assert.That(newInts.Intersect(existing.Select(v => (int)v)), Is.Empty);
        }

        #endregion

        #region 默认值与类型

        [Test]
        public void DefaultConstructors_SetType()
        {
            Assert.That(new MultiThreadStartStep().Type, Is.EqualTo(StepType.MultiThread));
            Assert.That(new MultiThreadEndStep().Type, Is.EqualTo(StepType.MultiThreadEnd));
            Assert.That(new SendSignalStep().Type, Is.EqualTo(StepType.SendSignal));
            Assert.That(new ReceiveSignalStep().Type, Is.EqualTo(StepType.ReceiveSignal));
            Assert.That(new InterruptStep().Type, Is.EqualTo(StepType.Interrupt));
        }

        [Test]
        public void MultiThreadStartStep_Defaults()
        {
            var step = new MultiThreadStartStep("mt", "desc");

            Assert.That(step.Type, Is.EqualTo(StepType.MultiThread));
            Assert.That(step.Name, Is.EqualTo("mt"));
            Assert.That(step.Description, Is.EqualTo("desc"));
            Assert.That(step.ThreadCount, Is.EqualTo(1));
            Assert.That(step.RangeMode, Is.EqualTo(MultiThreadRangeMode.ByLineNo));
            Assert.That(step.RangeEndUid, Is.Null);
            Assert.That(step.CreatedThreads, Is.EqualTo(0));
            Assert.That(step.IsNeed, Is.True);
            Assert.That(step.WaitTime, Is.EqualTo(0.1));
        }

        [Test]
        public void SendSignalStep_Defaults()
        {
            var step = new SendSignalStep("s");

            Assert.That(step.SignalName, Is.EqualTo(string.Empty));
            Assert.That(step.SignalKind, Is.EqualTo(SignalValueKind.Bool));
            Assert.That(step.SignalBool, Is.False);
            Assert.That(step.SignalInt, Is.EqualTo(0));
            Assert.That(step.SignalString, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ReceiveSignalStep_Defaults()
        {
            var step = new ReceiveSignalStep("r");

            Assert.That(step.SignalName, Is.EqualTo(string.Empty));
            Assert.That(step.SignalKind, Is.EqualTo(SignalValueKind.Bool));
            Assert.That(step.Blocking, Is.True);
            Assert.That(step.SignalTimeout, Is.EqualTo(10));
        }

        [Test]
        public void InterruptStep_Defaults()
        {
            var step = new InterruptStep("i");

            Assert.That(step.InterruptTargetUid, Is.Null);
        }

        [Test]
        public void MultiThreadEndStep_RunAsync_IsNoOp()
        {
            var step = new MultiThreadEndStep("marker");

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(step.IsTrue, Is.True);
            Assert.That(step.IsError, Is.False);
        }

        [Test]
        public void MultiThreadStartStep_RunAsync_ReturnsTrue()
        {
            var step = new MultiThreadStartStep("mt", null);

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(step.IsTrue, Is.True);
        }

        #endregion

        #region 发送信号

        [Test]
        public void SendSignalStep_SendsBoolValue()
        {
            var step = new SendSignalStep("s") { SignalName = "flag", SignalKind = SignalValueKind.Bool, SignalBool = true };

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(SignalCenter.TryReceive("flag", out var kind, out var value), Is.True);
            Assert.That(kind, Is.EqualTo(SignalValueKind.Bool));
            Assert.That(value, Is.EqualTo("true"));

            step.SignalBool = false;
            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(SignalCenter.TryReceive("flag", out _, out var value2), Is.True);
            Assert.That(value2, Is.EqualTo("false"));
        }

        [Test]
        public void SendSignalStep_SendsIntValue_Invariant()
        {
            var step = new SendSignalStep("s") { SignalName = "num", SignalKind = SignalValueKind.Int, SignalInt = -1234 };

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(SignalCenter.TryReceive("num", out var kind, out var value), Is.True);
            Assert.That(kind, Is.EqualTo(SignalValueKind.Int));
            Assert.That(value, Is.EqualTo("-1234"));
        }

        [Test]
        public void SendSignalStep_SendsStringValue()
        {
            var step = new SendSignalStep("s") { SignalName = "text", SignalKind = SignalValueKind.String, SignalString = "你好abc" };

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(SignalCenter.TryReceive("text", out _, out var value), Is.True);
            Assert.That(value, Is.EqualTo("你好abc"));
        }

        [Test]
        public void SendSignalStep_BlankName_Fails()
        {
            var step = new SendSignalStep("s") { SignalName = "  ", SignalKind = SignalValueKind.Bool, SignalBool = true };

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(step.IsTrue, Is.False);
            Assert.That(step.ErrorMessage, Is.Not.Null.And.Not.Empty);
        }

        #endregion

        #region 接收信号

        [Test]
        public void ReceiveSignalStep_NonBlocking_ReceivesAndStoresSignalValue()
        {
            SignalCenter.Send("recv", SignalValueKind.Int, "123");
            var step = new ReceiveSignalStep("r") { SignalName = "recv", SignalKind = SignalValueKind.Int, Blocking = false };

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(step.IsTrue, Is.True);
            Assert.That(step.LastResult, Is.Not.Null);
            Assert.That(step.LastResult.SignalValue, Is.EqualTo("123"));
            Assert.That(SignalCenter.TryReceive("recv", out _, out _), Is.False, "取到即消费");
        }

        [Test]
        public void ReceiveSignalStep_NonBlocking_NoSignal_Fails()
        {
            var step = new ReceiveSignalStep("r") { SignalName = "absent", Blocking = false };

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(step.IsTrue, Is.False);
            Assert.That(step.ErrorMessage, Is.Not.Null.And.Not.Empty);
            Assert.That(step.LastResult.SignalValue, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ReceiveSignalStep_BoolValue_IsCaseInsensitive()
        {
            SignalCenter.Send("b", SignalValueKind.Bool, "TRUE");
            var step = new ReceiveSignalStep("r") { SignalName = "b", SignalKind = SignalValueKind.Bool, Blocking = false };

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(step.LastResult.SignalValue, Is.EqualTo("true"));
        }

        [Test]
        public void ReceiveSignalStep_TypeMismatch_Fails()
        {
            SignalCenter.Send("mm", SignalValueKind.String, "not-a-number");
            var step = new ReceiveSignalStep("r") { SignalName = "mm", SignalKind = SignalValueKind.Int, Blocking = false };

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(step.IsTrue, Is.False);
            Assert.That(step.ErrorMessage, Is.Not.Null.And.Not.Empty);
            Assert.That(SignalCenter.TryReceive("mm", out _, out _), Is.False, "类型不匹配也已消费");
        }

        [Test]
        public void ReceiveSignalStep_BlankName_Fails()
        {
            var step = new ReceiveSignalStep("r") { SignalName = "", Blocking = false };

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(step.ErrorMessage, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ReceiveSignalStep_Blocking_TimesOut_Fails()
        {
            var step = new ReceiveSignalStep("r") { SignalName = "never", Blocking = true, SignalTimeout = 0.2 };

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(step.IsTrue, Is.False);
            Assert.That(step.ErrorMessage, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ReceiveSignalStep_Blocking_WakesOnSignalFromOtherThread()
        {
            var step = new ReceiveSignalStep("r") { SignalName = "wake2", SignalKind = SignalValueKind.String, Blocking = true, SignalTimeout = 5 };

            var task = Task.Run(() => step.RunAsync().GetAwaiter().GetResult());
            Assert.That(MultiThreadTestHelper.WaitForPendingCount(1), Is.True, "接收步骤未进入阻塞等待");
            SignalCenter.Send("wake2", SignalValueKind.String, "hello");

            Assert.That(task.Wait(5000), Is.True, "发送后接收步骤未被唤醒");
            Assert.That(task.Result, Is.True);
            Assert.That(step.LastResult.SignalValue, Is.EqualTo("hello"));
        }

        #endregion

        #region 中断

        [Test]
        public void InChildThread_IsIsolatedPerAsyncFlow()
        {
            MultiThreadInterruptBridge.InChildThread = false;
            bool childSaw = false;

            var task = Task.Run(() =>
            {
                MultiThreadInterruptBridge.InChildThread = true;
                childSaw = MultiThreadInterruptBridge.InChildThread;
            });
            task.Wait();

            Assert.That(childSaw, Is.True);
            Assert.That(MultiThreadInterruptBridge.InChildThread, Is.False, "子线程的标记不应泄漏到主线程");
        }

        [Test]
        public void InterruptStep_OutsideSubThread_Fails()
        {
            MultiThreadInterruptBridge.InChildThread = false;
            var step = new InterruptStep("i") { InterruptTargetUid = Guid.NewGuid() };

            Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(step.IsTrue, Is.False);
            Assert.That(step.ErrorMessage, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void InterruptStep_InSubThread_RegistersPendingInterrupt()
        {
            Guid captured = Guid.Empty;
            MultiThreadInterruptBridge.RegisterHandler(uid => captured = uid);
            try
            {
                var target = Guid.NewGuid();
                MultiThreadInterruptBridge.InChildThread = true;
                var step = new InterruptStep("i") { InterruptTargetUid = target };

                Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.True);
                Assert.That(step.IsTrue, Is.True);
                Assert.That(captured, Is.EqualTo(target));
            }
            finally
            {
                MultiThreadInterruptBridge.InChildThread = false;
                MultiThreadInterruptBridge.RegisterHandler(null);
            }
        }

        [Test]
        public void InterruptStep_InSubThreadWithoutTarget_Fails()
        {
            MultiThreadInterruptBridge.RegisterHandler(uid => { });
            try
            {
                MultiThreadInterruptBridge.InChildThread = true;
                var step = new InterruptStep("i");

                Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.False);
                Assert.That(step.ErrorMessage, Is.Not.Null.And.Not.Empty);
            }
            finally
            {
                MultiThreadInterruptBridge.InChildThread = false;
                MultiThreadInterruptBridge.RegisterHandler(null);
            }
        }

        [Test]
        public void InterruptStep_InSubThreadWithoutHandler_Fails()
        {
            MultiThreadInterruptBridge.RegisterHandler(null);
            MultiThreadInterruptBridge.InChildThread = true;
            try
            {
                var step = new InterruptStep("i") { InterruptTargetUid = Guid.NewGuid() };

                Assert.That(step.RunAsync().GetAwaiter().GetResult(), Is.False);
                Assert.That(step.ErrorMessage, Is.Not.Null.And.Not.Empty);
            }
            finally
            {
                MultiThreadInterruptBridge.InChildThread = false;
            }
        }

        #endregion

        #region Clone 独立性

        [Test]
        public void Clone_MultiThreadStartStep_CopiesKeyFieldsIndependently()
        {
            var rangeEnd = Guid.NewGuid();
            var trueGoto = Guid.NewGuid();
            var original = new MultiThreadStartStep("mt", "d")
            {
                ThreadCount = 5,
                RangeMode = MultiThreadRangeMode.ByEndStep,
                RangeEndUid = rangeEnd,
                WaitTime = 0.5,
                EnableLog = true,
                IsNeed = false,
                SelfReferenceLimit = 3,
                TrueGotoUid = trueGoto,
            };

            var clone = (MultiThreadStartStep)original.Clone();

            Assert.That(clone.ThreadCount, Is.EqualTo(5));
            Assert.That(clone.RangeMode, Is.EqualTo(MultiThreadRangeMode.ByEndStep));
            Assert.That(clone.RangeEndUid, Is.EqualTo(rangeEnd));
            Assert.That(clone.WaitTime, Is.EqualTo(0.5));
            Assert.That(clone.EnableLog, Is.True);
            Assert.That(clone.IsNeed, Is.False);
            Assert.That(clone.SelfReferenceLimit, Is.EqualTo(3));
            Assert.That(clone.TrueGotoUid, Is.EqualTo(trueGoto));
            Assert.That(clone.Type, Is.EqualTo(StepType.MultiThread));

            clone.ThreadCount = 9;
            clone.RangeEndUid = Guid.NewGuid();
            clone.CreatedThreads = 4;

            Assert.That(original.ThreadCount, Is.EqualTo(5), "Clone 与原始步骤不应共享属性");
            Assert.That(original.RangeEndUid, Is.EqualTo(rangeEnd));
            Assert.That(original.CreatedThreads, Is.EqualTo(0));
        }

        [Test]
        public void Clone_SendSignalStep_DoesNotShareValues()
        {
            var original = new SendSignalStep("s")
            {
                SignalName = "n",
                SignalKind = SignalValueKind.String,
                SignalString = "v",
                SignalInt = 7,
                SignalBool = true,
            };

            var clone = (SendSignalStep)original.Clone();

            Assert.That(clone.SignalName, Is.EqualTo("n"));
            Assert.That(clone.SignalKind, Is.EqualTo(SignalValueKind.String));
            Assert.That(clone.SignalString, Is.EqualTo("v"));
            Assert.That(clone.SignalInt, Is.EqualTo(7));
            Assert.That(clone.SignalBool, Is.True);

            clone.SignalString = "other";
            clone.SignalInt = 8;
            clone.SignalKind = SignalValueKind.Int;

            Assert.That(original.SignalString, Is.EqualTo("v"));
            Assert.That(original.SignalInt, Is.EqualTo(7));
            Assert.That(original.SignalKind, Is.EqualTo(SignalValueKind.String));
        }

        [Test]
        public void Clone_ReceiveSignalStep_And_InterruptStep_AreIndependent()
        {
            var target = Guid.NewGuid();
            var receive = new ReceiveSignalStep("r") { SignalName = "sig", SignalKind = SignalValueKind.Int, Blocking = false, SignalTimeout = 2.5 };
            var interrupt = new InterruptStep("i") { InterruptTargetUid = target };

            var receiveClone = (ReceiveSignalStep)receive.Clone();
            var interruptClone = (InterruptStep)interrupt.Clone();

            Assert.That(receiveClone.SignalName, Is.EqualTo("sig"));
            Assert.That(receiveClone.SignalKind, Is.EqualTo(SignalValueKind.Int));
            Assert.That(receiveClone.Blocking, Is.False);
            Assert.That(receiveClone.SignalTimeout, Is.EqualTo(2.5));
            Assert.That(interruptClone.InterruptTargetUid, Is.EqualTo(target));

            receiveClone.Blocking = true;
            interruptClone.InterruptTargetUid = Guid.NewGuid();

            Assert.That(receive.Blocking, Is.False);
            Assert.That(interrupt.InterruptTargetUid, Is.EqualTo(target));
        }

        [Test]
        public void Clone_MultiThreadEndStep_KeepsTypeAndName()
        {
            var clone = (MultiThreadEndStep)new MultiThreadEndStep("marker", "d").Clone();

            Assert.That(clone.Type, Is.EqualTo(StepType.MultiThreadEnd));
            Assert.That(clone.Name, Is.EqualTo("marker"));
            Assert.That(clone.Description, Is.EqualTo("d"));
        }

        #endregion

        #region 序列化往返

#pragma warning disable CS0618 // 测试旧版 JSON 持久化 API

        [Test]
        public void SaveAndLoad_MultiThreadSteps_RoundTrip()
        {
            var rangeEnd = Guid.NewGuid();
            var interruptTarget = Guid.NewGuid();
            var steps = new ObservableCollection<AutomationStepBase>
            {
                new MultiThreadStartStep("多线程开始", "描述")
                {
                    ThreadCount = 3,
                    RangeMode = MultiThreadRangeMode.ByEndStep,
                    RangeEndUid = rangeEnd,
                },
                new SendSignalStep("发信号")
                {
                    SignalName = "sig",
                    SignalKind = SignalValueKind.String,
                    SignalString = "你好",
                    SignalBool = true,
                    SignalInt = 9,
                },
                new ReceiveSignalStep("收信号")
                {
                    SignalName = "sig",
                    SignalKind = SignalValueKind.Int,
                    Blocking = false,
                    SignalTimeout = 2.5,
                },
                new InterruptStep("中断") { InterruptTargetUid = interruptTarget },
                new MultiThreadEndStep("区域结束"),
            };

            string filePath = Path.Combine(_testDir, "mt_steps.json");
            StepsFile.SaveStepsToJson(steps, filePath);
            var loaded = StepsFile.LoadStepsFromJson(filePath);

            Assert.That(loaded.Count, Is.EqualTo(5));

            var start = loaded[0] as MultiThreadStartStep;
            Assert.That(start, Is.Not.Null);
            Assert.That(start.Name, Is.EqualTo("多线程开始"));
            Assert.That(start.ThreadCount, Is.EqualTo(3));
            Assert.That(start.RangeMode, Is.EqualTo(MultiThreadRangeMode.ByEndStep));
            Assert.That(start.RangeEndUid, Is.EqualTo(rangeEnd));
            Assert.That(start.CreatedThreads, Is.EqualTo(0), "运行时字段不参与序列化");

            var send = loaded[1] as SendSignalStep;
            Assert.That(send, Is.Not.Null);
            Assert.That(send.SignalName, Is.EqualTo("sig"));
            Assert.That(send.SignalKind, Is.EqualTo(SignalValueKind.String));
            Assert.That(send.SignalString, Is.EqualTo("你好"));
            Assert.That(send.SignalBool, Is.True);
            Assert.That(send.SignalInt, Is.EqualTo(9));

            var receive = loaded[2] as ReceiveSignalStep;
            Assert.That(receive, Is.Not.Null);
            Assert.That(receive.SignalName, Is.EqualTo("sig"));
            Assert.That(receive.SignalKind, Is.EqualTo(SignalValueKind.Int));
            Assert.That(receive.Blocking, Is.False);
            Assert.That(receive.SignalTimeout, Is.EqualTo(2.5));

            var interrupt = loaded[3] as InterruptStep;
            Assert.That(interrupt, Is.Not.Null);
            Assert.That(interrupt.InterruptTargetUid, Is.EqualTo(interruptTarget));

            var end = loaded[4] as MultiThreadEndStep;
            Assert.That(end, Is.Not.Null);
            Assert.That(end.Type, Is.EqualTo(StepType.MultiThreadEnd));
        }

        [Test]
        public void Load_LegacyFileWithoutNewFields_UsesDefaults()
        {
            var uids = new string[5];
            for (int i = 0; i < uids.Length; i++)
                uids[i] = Guid.NewGuid().ToString();

            // 旧版文件：只有 Type/Uid/Name，没有任何多线程新字段
            string json = "[" +
                "{\"Type\":300,\"Uid\":\"" + uids[0] + "\",\"Name\":\"mt\"}," +
                "{\"Type\":301,\"Uid\":\"" + uids[1] + "\",\"Name\":\"end\"}," +
                "{\"Type\":302,\"Uid\":\"" + uids[2] + "\",\"Name\":\"send\"}," +
                "{\"Type\":303,\"Uid\":\"" + uids[3] + "\",\"Name\":\"recv\"}," +
                "{\"Type\":304,\"Uid\":\"" + uids[4] + "\",\"Name\":\"int\"}" +
                "]";

            string filePath = Path.Combine(_testDir, "legacy_steps.json");
            File.WriteAllText(filePath, json, Encoding.UTF8);

            var loaded = StepsFile.LoadStepsFromJson(filePath);
            Assert.That(loaded.Count, Is.EqualTo(5));

            var start = loaded[0] as MultiThreadStartStep;
            Assert.That(start, Is.Not.Null);
            Assert.That(start.ThreadCount, Is.EqualTo(1));
            Assert.That(start.RangeMode, Is.EqualTo(MultiThreadRangeMode.ByLineNo));
            Assert.That(start.RangeEndUid, Is.Null);

            var send = loaded[2] as SendSignalStep;
            Assert.That(send, Is.Not.Null);
            Assert.That(send.SignalName, Is.EqualTo(string.Empty));
            Assert.That(send.SignalKind, Is.EqualTo(SignalValueKind.Bool));
            Assert.That(send.SignalString, Is.EqualTo(string.Empty));

            var receive = loaded[3] as ReceiveSignalStep;
            Assert.That(receive, Is.Not.Null);
            Assert.That(receive.Blocking, Is.True);
            Assert.That(receive.SignalTimeout, Is.EqualTo(10));

            var interrupt = loaded[4] as InterruptStep;
            Assert.That(interrupt, Is.Not.Null);
            Assert.That(interrupt.InterruptTargetUid, Is.Null);
        }

#pragma warning restore CS0618

        #endregion

        #region 条件变量

        [Test]
        public void ResolveVariable_ReturnsSignalValue()
        {
            var context = new StepExecutionContext();
            var uid = Guid.NewGuid();
            context.SetResultByUid(uid, new StepExecutionResult { SignalValue = "abc", IsTrue = true });

            Assert.That(context.ResolveVariable(ConditionVariable.Self_SignalValue, null, new StepExecutionResult { SignalValue = "abc" }), Is.EqualTo("abc"));
            Assert.That(context.ResolveVariable(ConditionVariable.Step_SignalValue, uid, null), Is.EqualTo("abc"));
        }

        [Test]
        public void ResolveVariable_SignalValue_EmptyWhenMissingOrNull()
        {
            var context = new StepExecutionContext();

            Assert.That(context.ResolveVariable(ConditionVariable.Self_SignalValue, null, new StepExecutionResult()), Is.EqualTo(string.Empty));
            Assert.That(context.ResolveVariable(ConditionVariable.Self_SignalValue, null, null), Is.EqualTo(string.Empty));
            Assert.That(context.ResolveVariable(ConditionVariable.Step_SignalValue, Guid.NewGuid(), null), Is.EqualTo(string.Empty));
            Assert.That(context.ResolveVariable(ConditionVariable.Step_SignalValue, null, null), Is.EqualTo(string.Empty));
        }

        [Test]
        public void ConditionEvaluator_ComparesSignalValue()
        {
            var context = new StepExecutionContext();
            var result = new StepExecutionResult { SignalValue = "OK", IsTrue = true };
            var conditions = new List<StepCondition>
            {
                new StepCondition
                {
                    Variable = ConditionVariable.Self_SignalValue,
                    Operator = ConditionOperator.Equal,
                    Value = "OK",
                },
            };

            Assert.That(ConditionEvaluator.Evaluate(conditions, context, result), Is.True);

            conditions[0].Value = "NG";
            Assert.That(ConditionEvaluator.Evaluate(conditions, context, result), Is.False);
        }

        #endregion
    }
}
