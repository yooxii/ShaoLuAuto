using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ShaoLu.Services
{
    /// <summary>
    /// 子线程运行器：在专用 STA 线程上运行一个异步委托，并用 Dispatcher 消息泵驱动它的续体。
    /// WPF 对象（窗口、Freezable、ImageSource 等）具有线程亲和性，只有在 STA 线程 + 消息泵上
    /// 创建/访问才安全；线程池线程是 MTA 且没有消息泵，含窗口或图像的步骤在其上会失败。
    /// 线程为后台线程：委托结束后消息泵退出、线程结束；取消时也会结束消息泵，不留下悬挂线程。
    /// </summary>
    public sealed class SubThreadRunner
    {
        /// <summary>取消后留给委托响应取消的宽限期（毫秒），超过后才兜底结束消息泵</summary>
        private const int CancelPumpGraceMs = 1000;

        private readonly CancellationToken _token;
        private readonly TaskCompletionSource<bool> _completion =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private SubThreadRunner(string name, CancellationToken token)
        {
            Name = name ?? string.Empty;
            _token = token;
        }

        /// <summary>子线程名称（用于日志与诊断）</summary>
        public string Name { get; }

        /// <summary>承载委托的专用 STA 线程（已启动后不再变化）</summary>
        public Thread Thread { get; private set; }

        /// <summary>子线程完全结束（消息泵已退出）时完成；不会以异常结束，异常记录在 <see cref="Error"/></summary>
        public Task Completion => _completion.Task;

        /// <summary>子线程是否已经结束</summary>
        public bool IsCompleted => _completion.Task.IsCompleted;

        /// <summary>委托抛出的异常（取消不算异常）；未抛出时为 null</summary>
        public Exception Error { get; private set; }

        /// <summary>
        /// 是否因取消而提前结束：消息泵已退出但委托尚未结束（其续体不再有线程驱动）。
        /// 正常情况下委托响应取消后自行结束，此时为 false。
        /// </summary>
        public bool IsAbandoned { get; private set; }

        /// <summary>
        /// 启动一个专用 STA 子线程运行 <paramref name="body"/>。
        /// 注意：<paramref name="token"/> 只用于委托内部的协作式取消，不参与线程调度
        /// （把 token 交给 Task.Run 时，token 已取消会让委托根本不执行）。
        /// </summary>
        public static SubThreadRunner Start(string name, Func<CancellationToken, Task> body, CancellationToken token)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            var runner = new SubThreadRunner(name, token);
            var thread = new Thread(runner.ThreadMain) { IsBackground = true, Name = runner.Name };
            thread.SetApartmentState(ApartmentState.STA);
            runner.Thread = thread;
            thread.Start(body);
            return runner;
        }

        /// <summary>等待子线程结束，返回是否已经结束</summary>
        public bool Join(int millisecondsTimeout) => _completion.Task.Wait(millisecondsTimeout);

        private void ThreadMain(object state)
        {
            var body = (Func<CancellationToken, Task>)state;
            var dispatcher = Dispatcher.CurrentDispatcher;
            try
            {
                // 把续体交回本线程：默认 SynchronizationContext 为空，await 之后会落到线程池的 MTA 线程上
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

                Task work = null;
                try
                {
                    work = body(_token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Error = ex; }

                if (work == null)
                    return;

                if (work.IsCompleted)
                {
                    // 委托同步完成：无需消息泵，直接收尾
                    if (work.IsFaulted) Error = work.Exception?.GetBaseException();
                    return;
                }

                PumpUntilCompleted(dispatcher, work);
            }
            catch (Exception ex)
            {
                Error = ex;
            }
            finally
            {
                try { dispatcher.InvokeShutdown(); } catch (Exception) { }
                _completion.TrySetResult(true);
            }
        }

        /// <summary>
        /// 泵消息直到委托结束：委托完成时结束消息泵（较低优先级，保证委托自己的收尾续体先执行），
        /// 取消时也结束消息泵（避免委托不响应取消时线程永远悬挂）。
        /// </summary>
        private void PumpUntilCompleted(Dispatcher dispatcher, Task work)
        {
            var frame = new DispatcherFrame();
            Action endPump = () => frame.Continue = false;

            work.ContinueWith(_ =>
            {
                try { dispatcher.BeginInvoke(DispatcherPriority.Background, endPump); }
                catch (Exception) { endPump(); }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            // 取消：不是立刻结束消息泵（取消回调是同步触发的，早于委托自己的取消续体入队），
            // 而是留一个宽限期——委托响应取消就会自行结束（IsAbandoned=false）；
            // 宽限期内没结束才兜底结束消息泵，避免线程永久悬挂。
            using (_token.Register(() =>
            {
                Task.Delay(CancelPumpGraceMs).ContinueWith(_ => EndPump(dispatcher, endPump), TaskScheduler.Default);
            }))
            {
                Dispatcher.PushFrame(frame);
            }

            IsAbandoned = !work.IsCompleted;
            if (work.IsFaulted) Error = work.Exception?.GetBaseException();
        }

        /// <summary>结束消息泵（dispatcher 已关闭时直接置位，任意线程可调用）</summary>
        private static void EndPump(Dispatcher dispatcher, Action endPump)
        {
            try { dispatcher.BeginInvoke(DispatcherPriority.Background, endPump); }
            catch (Exception) { endPump(); }
        }
    }
}
