using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Models;
using ShaoLu.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ShaoLu.Viewmodels.AutomationStep
{
    /// <summary>
    /// 多线程区域划定方式（执行时二者只能选其一，由步骤详情里的选择决定）
    /// </summary>
    public enum MultiThreadRangeMode
    {
        /// <summary>按行号：由指定的「结束步骤」Uid 圈定区域末尾（null = 到脚本末尾）</summary>
        ByLineNo = 0,

        /// <summary>按结束标记：由最近的「多线程区域结束」步骤圈定区域（标记本身不执行）</summary>
        ByEndStep = 1,
    }

    /// <summary>
    /// 子线程 → 主线程的中断登记桥。
    /// 引擎在每次运行开始时注册处理器（主线程负责处理「待处理中断」）；
    /// 子线程执行器把 InChildThread 置位，中断步骤据此判断自己是否运行在子线程中。
    /// </summary>
    public static class MultiThreadInterruptBridge
    {
        private static readonly object _handlerLock = new object();
        private static Action<Guid> _handler;
        private static readonly AsyncLocal<bool> _inChildThread = new AsyncLocal<bool>();

        /// <summary>当前执行流是否位于子线程中（由子线程执行器设置）</summary>
        public static bool InChildThread
        {
            get => _inChildThread.Value;
            set => _inChildThread.Value = value;
        }

        /// <summary>注册主线程的中断处理器（运行开始时调用，null 表示注销）</summary>
        public static void RegisterHandler(Action<Guid> handler)
        {
            lock (_handlerLock)
            {
                _handler = handler;
            }
        }

        /// <summary>登记一个待处理中断（多次中断覆盖为最新）；未注册处理器时返回 false</summary>
        public static bool RequestInterrupt(Guid targetUid)
        {
            Action<Guid> handler;
            lock (_handlerLock)
            {
                handler = _handler;
            }
            if (handler == null) return false;

            handler(targetUid);
            return true;
        }
    }

    /// <summary>
    /// 多线程区域折叠状态的变更通知器（纯界面状态）。
    /// 步骤列表的每一行都绑定它的 Revision 以重新计算可见性；
    /// 折叠只影响列表显示，不改变任何运行语义。
    /// </summary>
    public sealed class RegionCollapseNotifier : ObservableObject
    {
        /// <summary>全局单例（步骤列表 XAML 以 x:Static 绑定其 Revision）</summary>
        public static RegionCollapseNotifier Instance { get; } = new RegionCollapseNotifier();

        private int _revision;

        /// <summary>折叠状态版本号：每次折叠/展开自增，用于触发所有行重算可见性</summary>
        public int Revision
        {
            get => _revision;
            private set => SetProperty(ref _revision, value);
        }

        /// <summary>通知步骤列表重新计算各行可见性</summary>
        public void Refresh() => Revision++;
    }

    /// <summary>
    /// 多线程开始步骤：到达时新建一个子线程执行被圈入的区域（配置值为线程总数上限）；
    /// 主线程跳过区域，继续执行区域之后的步骤。
    /// </summary>
    public class MultiThreadStartStep : AutomationStepBase
    {
        private int _threadCount = 1;
        private MultiThreadRangeMode _rangeMode = MultiThreadRangeMode.ByLineNo;
        private Guid? _rangeEndUid;

        /// <summary>线程总数上限（每次到达本步骤各新建 1 个，到达上限后不再新建）</summary>
        public int ThreadCount { get => _threadCount; set => SetProperty(ref _threadCount, value); }

        /// <summary>区域划定方式</summary>
        public MultiThreadRangeMode RangeMode { get => _rangeMode; set => SetProperty(ref _rangeMode, value); }

        /// <summary>ByLineNo 方式下的结束步骤 Uid（null/空 = 区域到脚本末尾）</summary>
        [JsonPropertyName("RangeEndUid")]
        public Guid? RangeEndUid { get => _rangeEndUid; set => SetProperty(ref _rangeEndUid, value); }

        /// <summary>运行时已创建的线程数（每次运行开始时归零）</summary>
        [JsonIgnore]
        public int CreatedThreads { get; set; }

        private bool _isRegionCollapsed;

        /// <summary>本区域在步骤列表中是否已折叠（纯界面状态：不落盘、不影响运行语义）</summary>
        [JsonIgnore]
        public bool IsRegionCollapsed
        {
            get => _isRegionCollapsed;
            set
            {
                if (SetProperty(ref _isRegionCollapsed, value))
                {
                    RegionCollapseNotifier.Instance.Refresh();
                }
            }
        }

        /// <summary>区域划定方式下拉候选（纯追加，仅界面绑定使用）</summary>
        public static readonly IReadOnlyList<MultiThreadRangeMode> RangeModes = new[]
        {
            MultiThreadRangeMode.ByLineNo,
            MultiThreadRangeMode.ByEndStep,
        };

        #region 构造

        public MultiThreadStartStep() : base()
        {
            Type = StepType.MultiThread;
        }

        public MultiThreadStartStep(string name) : base()
        {
            Type = StepType.MultiThread;
            Name = name;
        }

        public MultiThreadStartStep(string name, string description) : base()
        {
            Type = StepType.MultiThread;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new MultiThreadStartStep(Name, Description)
            {
                ThreadCount = ThreadCount,
                RangeMode = RangeMode,
                RangeEndUid = RangeEndUid,
                WaitTime = WaitTime,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
            };
        }

        #endregion

        public override Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            IsTrue = true;
            IsError = false;
            ErrorType = StepErrorType.None;
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 多线程区域结束步骤：纯区域标记（ByEndStep 方式使用），本身不执行任何动作。
    /// </summary>
    public class MultiThreadEndStep : AutomationStepBase
    {
        #region 构造

        public MultiThreadEndStep() : base()
        {
            Type = StepType.MultiThreadEnd;
        }

        public MultiThreadEndStep(string name) : base()
        {
            Type = StepType.MultiThreadEnd;
            Name = name;
        }

        public MultiThreadEndStep(string name, string description) : base()
        {
            Type = StepType.MultiThreadEnd;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new MultiThreadEndStep(Name, Description)
            {
                WaitTime = WaitTime,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
            };
        }

        #endregion

        public override Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            IsTrue = true;
            IsError = false;
            ErrorType = StepErrorType.None;
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 发出信号步骤：把配置的值发送到信号中心（同名信号覆盖为最新值）。
    /// </summary>
    public class SendSignalStep : AutomationStepBase
    {
        private string _signalName = string.Empty;
        private SignalValueKind _signalKind = SignalValueKind.Bool;
        private bool _signalBool;
        private int _signalInt;
        private string _signalString = string.Empty;

        /// <summary>信号名（区分大小写）</summary>
        public string SignalName { get => _signalName; set => SetProperty(ref _signalName, value); }

        /// <summary>信号值类型</summary>
        public SignalValueKind SignalKind { get => _signalKind; set => SetProperty(ref _signalKind, value); }

        /// <summary>布尔值（SignalKind = Bool 时使用）</summary>
        public bool SignalBool { get => _signalBool; set => SetProperty(ref _signalBool, value); }

        /// <summary>整数值（SignalKind = Int 时使用）</summary>
        public int SignalInt { get => _signalInt; set => SetProperty(ref _signalInt, value); }

        /// <summary>字符串值（SignalKind = String 时使用）</summary>
        public string SignalString { get => _signalString; set => SetProperty(ref _signalString, value); }

        /// <summary>信号类型下拉候选（纯追加，仅界面绑定使用）</summary>
        public static readonly IReadOnlyList<SignalValueKind> SignalKinds = new[]
        {
            SignalValueKind.Bool,
            SignalValueKind.Int,
            SignalValueKind.String,
        };

        #region 构造

        public SendSignalStep() : base()
        {
            Type = StepType.SendSignal;
        }

        public SendSignalStep(string name) : base()
        {
            Type = StepType.SendSignal;
            Name = name;
        }

        public SendSignalStep(string name, string description) : base()
        {
            Type = StepType.SendSignal;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new SendSignalStep(Name, Description)
            {
                SignalName = SignalName,
                SignalKind = SignalKind,
                SignalBool = SignalBool,
                SignalInt = SignalInt,
                SignalString = SignalString,
                WaitTime = WaitTime,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
            };
        }

        #endregion

        public override Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(SignalName))
            {
                IsTrue = false;
                ErrorMessage = LanguageService.GetLocalizedString("Msg_Signal_NameRequired", "信号名不能为空");
                return Task.FromResult(false);
            }

            SignalCenter.Send(SignalName, SignalKind, GetValueText());
            IsTrue = true;
            ErrorMessage = null;
            return Task.FromResult(true);
        }

        /// <summary>按配置的类型取值的字符串表示（bool 用 true/false，int 用不变文化）</summary>
        private string GetValueText()
        {
            switch (SignalKind)
            {
                case SignalValueKind.Int:
                    return SignalInt.ToString(CultureInfo.InvariantCulture);
                case SignalValueKind.String:
                    return SignalString ?? string.Empty;
                default:
                    return SignalBool ? "true" : "false";
            }
        }
    }

    /// <summary>
    /// 接收信号步骤：阻塞等待或立即读取指定信号；取到即消费（清除），并把内容写入 LastResult.SignalValue。
    /// </summary>
    public class ReceiveSignalStep : AutomationStepBase
    {
        private string _signalName = string.Empty;
        private SignalValueKind _signalKind = SignalValueKind.Bool;
        private bool _blocking = true;
        private double _signalTimeout = 10;

        /// <summary>信号名（区分大小写）</summary>
        public string SignalName { get => _signalName; set => SetProperty(ref _signalName, value); }

        /// <summary>信号值类型（用于校验收到的值）</summary>
        public SignalValueKind SignalKind { get => _signalKind; set => SetProperty(ref _signalKind, value); }

        /// <summary>是否阻塞等待（false = 立刻读取）</summary>
        public bool Blocking { get => _blocking; set => SetProperty(ref _blocking, value); }

        /// <summary>阻塞等待上限（秒；&lt;=0 表示不限时）</summary>
        public double SignalTimeout { get => _signalTimeout; set => SetProperty(ref _signalTimeout, value); }

        /// <summary>信号类型下拉候选（纯追加，仅界面绑定使用）</summary>
        public static readonly IReadOnlyList<SignalValueKind> SignalKinds = new[]
        {
            SignalValueKind.Bool,
            SignalValueKind.Int,
            SignalValueKind.String,
        };

        #region 构造

        public ReceiveSignalStep() : base()
        {
            Type = StepType.ReceiveSignal;
        }

        public ReceiveSignalStep(string name) : base()
        {
            Type = StepType.ReceiveSignal;
            Name = name;
        }

        public ReceiveSignalStep(string name, string description) : base()
        {
            Type = StepType.ReceiveSignal;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new ReceiveSignalStep(Name, Description)
            {
                SignalName = SignalName,
                SignalKind = SignalKind,
                Blocking = Blocking,
                SignalTimeout = SignalTimeout,
                WaitTime = WaitTime,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
            };
        }

        #endregion

        public override Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            LastResult ??= new StepExecutionResult();
            LastResult.SignalValue = string.Empty;

            if (string.IsNullOrWhiteSpace(SignalName))
            {
                IsTrue = false;
                ErrorMessage = LanguageService.GetLocalizedString("Msg_Signal_NameRequired", "信号名不能为空");
                return Task.FromResult(false);
            }

            SignalValueKind receivedKind;
            string rawValue;
            bool received;
            if (Blocking)
            {
                int timeoutMs = SignalTimeout <= 0 ? 0 : (int)(SignalTimeout * 1000);
                received = SignalCenter.WaitForSignal(SignalName, timeoutMs, cancellationToken, out receivedKind, out rawValue);
            }
            else
            {
                received = SignalCenter.TryReceive(SignalName, out receivedKind, out rawValue);
            }

            if (!received)
            {
                IsTrue = false;
                ErrorMessage = Blocking
                    ? LanguageService.GetLocalizedString("Msg_Signal_WaitFailed", "等待信号超时或被取消")
                    : LanguageService.GetLocalizedString("Msg_Signal_NotFound", "未收到信号");
                return Task.FromResult(false);
            }

            // 按本步骤配置的类型校验收到的值
            string text;
            switch (SignalKind)
            {
                case SignalValueKind.Bool:
                    if (string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase))
                        text = "true";
                    else if (string.Equals(rawValue, "false", StringComparison.OrdinalIgnoreCase))
                        text = "false";
                    else
                    {
                        IsTrue = false;
                        ErrorMessage = LanguageService.GetLocalizedString("Msg_Signal_TypeMismatch", "信号值类型不匹配：期望布尔值 true/false");
                        return Task.FromResult(false);
                    }
                    break;

                case SignalValueKind.Int:
                    if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                    {
                        IsTrue = false;
                        ErrorMessage = LanguageService.GetLocalizedString("Msg_Signal_TypeMismatch", "信号值类型不匹配：期望整数");
                        return Task.FromResult(false);
                    }
                    text = intValue.ToString(CultureInfo.InvariantCulture);
                    break;

                default:
                    text = rawValue ?? string.Empty;
                    break;
            }

            IsTrue = true;
            ErrorMessage = null;
            LastResult.SignalValue = text;
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 中断步骤：只能由子线程发出，登记主线程的待处理中断（打断主线程并从目标步骤开始执行）。
    /// </summary>
    public class InterruptStep : AutomationStepBase
    {
        private Guid? _interruptTargetUid;

        /// <summary>主线程从此步骤开始执行</summary>
        [JsonPropertyName("InterruptTargetUid")]
        public Guid? InterruptTargetUid { get => _interruptTargetUid; set => SetProperty(ref _interruptTargetUid, value); }

        #region 构造

        public InterruptStep() : base()
        {
            Type = StepType.Interrupt;
        }

        public InterruptStep(string name) : base()
        {
            Type = StepType.Interrupt;
            Name = name;
        }

        public InterruptStep(string name, string description) : base()
        {
            Type = StepType.Interrupt;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new InterruptStep(Name, Description)
            {
                InterruptTargetUid = InterruptTargetUid,
                WaitTime = WaitTime,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
            };
        }

        #endregion

        public override Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            if (!MultiThreadInterruptBridge.InChildThread)
            {
                IsTrue = false;
                ErrorMessage = LanguageService.GetLocalizedString("Msg_Interrupt_OnlyFromSubThread", "中断步骤只能由子线程发出");
                return Task.FromResult(false);
            }

            if (!InterruptTargetUid.HasValue || InterruptTargetUid.Value == Guid.Empty)
            {
                IsTrue = false;
                ErrorMessage = LanguageService.GetLocalizedString("Msg_Interrupt_TargetRequired", "中断步骤未指定目标步骤");
                return Task.FromResult(false);
            }

            if (!MultiThreadInterruptBridge.RequestInterrupt(InterruptTargetUid.Value))
            {
                IsTrue = false;
                ErrorMessage = LanguageService.GetLocalizedString("Msg_Interrupt_NotRunning", "中断步骤只能在自动化运行中发出");
                return Task.FromResult(false);
            }

            IsTrue = true;
            ErrorMessage = null;
            return Task.FromResult(true);
        }
    }
}
