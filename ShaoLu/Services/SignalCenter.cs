using System;
using System.Collections.Concurrent;
using System.Threading;

namespace ShaoLu.Services
{
    /// <summary>信号值类型</summary>
    public enum SignalValueKind { Bool = 0, Int = 1, String = 2 }

    /// <summary>跨线程信号中心：按名字保存“最新一次发送的值”（未接收前覆盖式更新）</summary>
    public static class SignalCenter
    {
        /// <summary>单个信号名的槽位：有值时段位为真，接收即清空</summary>
        private sealed class SignalSlot
        {
            public readonly object Gate = new object();
            public bool HasValue;
            public SignalValueKind Kind;
            public string Value;
        }

        /// <summary>等待切片（毫秒）：分段等待以便及时响应取消</summary>
        private const int WaitSliceMs = 50;

        // 信号名区分大小写
        private static readonly ConcurrentDictionary<string, SignalSlot> _slots =
            new ConcurrentDictionary<string, SignalSlot>(StringComparer.Ordinal);

        private static int _waitingCount;

        /// <summary>发送/覆盖：name 为信号名，value 为值的字符串表示（bool 用 true/false）</summary>
        public static void Send(string name, SignalValueKind kind, string value)
        {
            if (IsInvalidName(name)) return;

            var slot = _slots.GetOrAdd(name, _ => new SignalSlot());
            lock (slot.Gate)
            {
                slot.Kind = kind;
                slot.Value = value;
                slot.HasValue = true;
                Monitor.PulseAll(slot.Gate);
            }
        }

        /// <summary>取走并清除（消费）：有值返回 true；无值返回 false（kind/value 置默认）</summary>
        public static bool TryReceive(string name, out SignalValueKind kind, out string value)
        {
            kind = SignalValueKind.Bool;
            value = null;
            if (IsInvalidName(name)) return false;

            var slot = _slots.GetOrAdd(name, _ => new SignalSlot());
            lock (slot.Gate)
            {
                return ConsumeLocked(slot, out kind, out value);
            }
        }

        /// <summary>阻塞等待直到有值、超时（timeoutMs&lt;=0 表示不限时）或 token 取消；返回是否取到（取到即消费）</summary>
        public static bool WaitForSignal(string name, int timeoutMs, CancellationToken token, out SignalValueKind kind, out string value)
        {
            kind = SignalValueKind.Bool;
            value = null;
            if (IsInvalidName(name)) return false;

            var slot = _slots.GetOrAdd(name, _ => new SignalSlot());
            lock (slot.Gate)
            {
                if (ConsumeLocked(slot, out kind, out value))
                    return true;

                Interlocked.Increment(ref _waitingCount);
                try
                {
                    int waited = 0;
                    while (!slot.HasValue)
                    {
                        if (token.IsCancellationRequested) return false;

                        int slice = WaitSliceMs;
                        if (timeoutMs > 0)
                        {
                            int remaining = timeoutMs - waited;
                            if (remaining <= 0) return false;
                            if (remaining < slice) slice = remaining;
                        }

                        Monitor.Wait(slot.Gate, slice);
                        waited += slice;
                    }

                    return ConsumeLocked(slot, out kind, out value);
                }
                finally
                {
                    Interlocked.Decrement(ref _waitingCount);
                }
            }
        }

        /// <summary>每次运行开始前清空所有信号（保留槽位，等待中的线程不会被孤立）</summary>
        public static void Reset()
        {
            foreach (var pair in _slots)
            {
                lock (pair.Value.Gate)
                {
                    pair.Value.HasValue = false;
                    pair.Value.Value = null;
                    pair.Value.Kind = SignalValueKind.Bool;
                }
            }
        }

        /// <summary>当前阻塞等待信号的线程数（诊断/测试用）</summary>
        public static int PendingCount => Volatile.Read(ref _waitingCount);

        private static bool ConsumeLocked(SignalSlot slot, out SignalValueKind kind, out string value)
        {
            if (!slot.HasValue)
            {
                kind = SignalValueKind.Bool;
                value = null;
                return false;
            }

            kind = slot.Kind;
            value = slot.Value;
            slot.HasValue = false;
            slot.Value = null;
            slot.Kind = SignalValueKind.Bool;
            return true;
        }

        private static bool IsInvalidName(string name) => string.IsNullOrWhiteSpace(name);
    }
}
