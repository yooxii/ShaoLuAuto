using NUnit.Framework;
using ShaoLu.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;

namespace NUnitTest
{
    /// <summary>
    /// 子线程运行器：专用 STA 线程 + Dispatcher 消息泵 + 取消收尾
    /// </summary>
    [TestFixture]
    public class SubThreadRunnerTests
    {
        private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
                Thread.Sleep(10);
            return condition();
        }

        [Test]
        public void Start_RunsBodyOnDedicatedStaThread()
        {
            int bodyThreadId = 0;
            ApartmentState bodyApartment = ApartmentState.Unknown;
            bool isThreadPoolThread = true;
            bool isBackground = false;
            string threadName = null;

            var runner = SubThreadRunner.Start("sta-check", ct =>
            {
                // 线程属性只在线程内读取（线程结束后读 GetApartmentState 会抛 ThreadStateException）
                bodyThreadId = Thread.CurrentThread.ManagedThreadId;
                bodyApartment = Thread.CurrentThread.GetApartmentState();
                isThreadPoolThread = Thread.CurrentThread.IsThreadPoolThread;
                isBackground = Thread.CurrentThread.IsBackground;
                threadName = Thread.CurrentThread.Name;
                return Task.CompletedTask;
            }, CancellationToken.None);

            Assert.That(runner.Join(5000), Is.True, "子线程未结束");
            Assert.That(bodyApartment, Is.EqualTo(ApartmentState.STA));
            Assert.That(isThreadPoolThread, Is.False, "子线程不应是线程池线程");
            Assert.That(isBackground, Is.True, "子线程应为后台线程，进程退出时不应被它挂住");
            Assert.That(threadName, Is.EqualTo("sta-check"), "子线程应带上用于日志的名称");
            Assert.That(bodyThreadId, Is.Not.EqualTo(Thread.CurrentThread.ManagedThreadId));
            Assert.That(runner.Name, Is.EqualTo("sta-check"));
            Assert.That(runner.Error, Is.Null);
        }

        [Test]
        public void Start_BodyCanCreateAndAccessWpfObjectsAcrossAwait()
        {
            Exception failure = null;
            bool verified = false;
            int threadIdBeforeAwait = 0;
            int threadIdAfterAwait = 0;
            ApartmentState apartmentAfterAwait = ApartmentState.Unknown;

            var runner = SubThreadRunner.Start("wpf-objects", async ct =>
            {
                try
                {
                    // 线程池 MTA 线程上创建这些对象会抛跨线程/STA 异常
                    // （Button 与 SolidColorBrush 都是 DispatcherObject 的子类，具有线程亲和性）
                    var button = new System.Windows.Controls.Button { Content = "子线程" };
                    var brush = new SolidColorBrush(Colors.Red);

                    threadIdBeforeAwait = Thread.CurrentThread.ManagedThreadId;

                    // await 之后必须回到同一个 STA 线程，否则下面的访问会抛跨线程异常
                    await Task.Delay(50, ct);

                    apartmentAfterAwait = Thread.CurrentThread.GetApartmentState();
                    threadIdAfterAwait = Thread.CurrentThread.ManagedThreadId;

                    button.VerifyAccess();
                    brush.VerifyAccess();
                    var dispatcherMatches = ReferenceEquals(button.Dispatcher, Dispatcher.CurrentDispatcher);
                    button.Content = "ok";
                    verified = dispatcherMatches
                        && brush.Color == Colors.Red
                        && (string)button.Content == "ok"
                        && button.Dispatcher.Thread.ManagedThreadId == Thread.CurrentThread.ManagedThreadId;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            }, CancellationToken.None);

            Assert.That(runner.Join(10000), Is.True, "子线程未结束");
            Assert.That(failure, Is.Null, "子线程内创建/访问 WPF 对象失败：" + failure);
            Assert.That(apartmentAfterAwait, Is.EqualTo(ApartmentState.STA), "await 之后必须仍处于 STA 线程");
            Assert.That(threadIdAfterAwait, Is.EqualTo(threadIdBeforeAwait), "await 之后必须回到同一子线程");
            Assert.That(verified, Is.True);
        }

        [Test]
        public void Completion_WaitsForBodyToFinish()
        {
            int finishedCount = 0;
            var entered = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);

            var runner = SubThreadRunner.Start("wait-body", ct =>
            {
                entered.Set();
                release.Wait(10000);
                Interlocked.Increment(ref finishedCount);
                return Task.CompletedTask;
            }, CancellationToken.None);

            Assert.That(entered.Wait(5000), Is.True, "子线程未开始执行委托");
            Assert.That(runner.IsCompleted, Is.False, "委托还在执行，运行器不应视为已完成");
            Assert.That(runner.Join(200), Is.False, "委托还在执行，等待不应提前返回");
            Assert.That(finishedCount, Is.EqualTo(0));

            release.Set();

            Assert.That(runner.Join(5000), Is.True, "委托结束后等待应返回");
            Assert.That(runner.IsCompleted, Is.True);
            Assert.That(finishedCount, Is.EqualTo(1), "等待返回时委托必须已经跑完");
        }

        [Test]
        public void Completion_WaitsForAsyncBodyContinuations()
        {
            int counter = 0;
            var runner = SubThreadRunner.Start("wait-async", async ct =>
            {
                await Task.Delay(100, ct);
                Interlocked.Increment(ref counter);
            }, CancellationToken.None);

            Assert.That(runner.Completion.Wait(10000), Is.True, "子线程未结束");
            Assert.That(counter, Is.EqualTo(1), "异步续体必须在等待返回前执行完");
        }

        [Test]
        public void Cancel_InterruptsBodyAndEndsThreadQuickly()
        {
            var cts = new CancellationTokenSource();
            int entered = 0;
            Exception failure = null;

            var runner = SubThreadRunner.Start("cancel-body", async ct =>
            {
                Interlocked.Increment(ref entered);
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { failure = ex; }
            }, cts.Token);

            Assert.That(WaitUntil(() => Volatile.Read(ref entered) == 1), Is.True, "子线程未开始执行委托");

            var sw = Stopwatch.StartNew();
            cts.Cancel();
            Assert.That(runner.Join(5000), Is.True, "取消后子线程未及时退出");
            sw.Stop();

            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(3000), "取消后子线程退出过慢");
            Assert.That(failure, Is.Null, "取消不应产生未处理异常：" + failure);
            Assert.That(runner.Error, Is.Null, "取消不应被记作异常");
            Assert.That(runner.IsAbandoned, Is.False, "委托响应了取消，不应被判定为丢弃");
            Assert.That(runner.Thread.IsAlive, Is.False, "取消后不应留下悬挂线程");
        }

        [Test]
        public void Cancel_EndsPump_WhenBodyIgnoresCancellation()
        {
            var cts = new CancellationTokenSource();
            int entered = 0;

            // 委托不响应取消：消息泵仍应在取消时结束，不能留下悬挂线程
            var runner = SubThreadRunner.Start("stubborn-body", ct =>
            {
                Interlocked.Increment(ref entered);
                return Task.Delay(Timeout.Infinite);
            }, cts.Token);

            Assert.That(WaitUntil(() => Volatile.Read(ref entered) == 1), Is.True, "子线程未开始执行委托");
            cts.Cancel();

            Assert.That(runner.Join(5000), Is.True, "委托不响应取消时子线程仍应退出");
            Assert.That(runner.IsAbandoned, Is.True, "应标记为取消后丢弃");
            Assert.That(WaitUntil(() => !runner.Thread.IsAlive), Is.True, "线程应已退出");
        }

        [Test]
        public void Start_SynchronousBody_CompletesWithoutHanging()
        {
            int calls = 0;
            var runner = SubThreadRunner.Start("sync-body", ct =>
            {
                calls++;
                return Task.CompletedTask;
            }, CancellationToken.None);

            Assert.That(runner.Join(5000), Is.True, "同步完成的委托不应挂住线程");
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(runner.Completion.IsCompleted, Is.True);
        }

        [Test]
        public void Start_BodyException_IsCapturedNotThrownOnThread()
        {
            var runner = SubThreadRunner.Start("throwing-body", ct => throw new InvalidOperationException("boom"),
                CancellationToken.None);

            Assert.That(runner.Join(5000), Is.True, "委托抛异常后线程仍应正常结束");
            Assert.That(runner.Error, Is.Not.Null);
            Assert.That(runner.Error.Message, Is.EqualTo("boom"));
            Assert.That(runner.Completion.IsFaulted, Is.False, "运行器自身不应以异常结束（否则会变成未处理异常）");
        }

        [Test]
        public void Start_FaultedBodyTask_IsCaptured()
        {
            var runner = SubThreadRunner.Start("faulted-body",
                ct => Task.FromException(new InvalidOperationException("async-boom")), CancellationToken.None);

            Assert.That(runner.Join(5000), Is.True, "失败的异步委托结束后线程应正常退出");
            Assert.That(runner.Error, Is.Not.Null);
            Assert.That(runner.Error.Message, Is.EqualTo("async-boom"));
        }

        [Test]
        public void Start_ThreeRunners_RunConcurrentlyWithoutInterference()
        {
            const int count = 3;
            int counter = 0;
            var gate = new ManualResetEventSlim(false);
            var started = new ManualResetEventSlim[count];
            var runners = new List<SubThreadRunner>();

            for (int i = 0; i < count; i++)
            {
                started[i] = new ManualResetEventSlim(false);
                int index = i;
                runners.Add(SubThreadRunner.Start("concurrent-" + index, async ct =>
                {
                    started[index].Set();
                    // 三个线程都卡在这里：只有真正并发（各自独立线程）才能全部走到下一步
                    gate.Wait(10000);
                    await Task.Delay(10, ct);
                    Interlocked.Increment(ref counter);
                }, CancellationToken.None));
            }

            foreach (var signal in started)
                Assert.That(signal.Wait(5000), Is.True, "子线程未并发启动");

            gate.Set();

            var all = Task.WhenAll(runners.Select(r => r.Completion));
            Assert.That(all.Wait(15000), Is.True, "并发子线程未全部结束");

            Assert.That(counter, Is.EqualTo(count));
            Assert.That(runners.Select(r => r.Thread.ManagedThreadId).Distinct().Count(), Is.EqualTo(count),
                "每个子线程必须使用各自的线程");
            Assert.That(runners.All(r => r.Error == null), Is.True);
            Assert.That(runners.All(r => r.IsCompleted), Is.True);
        }
    }
}
