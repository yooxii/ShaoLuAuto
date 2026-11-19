using NLog;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShaoLu.Viewmodels
{
    /// <summary>
    /// 扫码步骤：只做扫码本身——弹窗等扫码枪/键盘输入，把条码作为本步骤的「文本」（OCRText）暴露，
    /// 「上传 Acbel」步骤按「扫码结果」引用即可。
    /// 与 Acbel MES 的交互（验证网络 / 验证条码 / 写入基础资料 / 上传）全部在「上传 Acbel」步骤里，
    /// 本步骤不加载、不调用 Acbel DLL。
    /// </summary>
    public class ScanCodeStep : AutomationStepBase
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// 测试注入点：替代扫码弹窗，返回条码（null 或空表示取消）。为 null 时弹出真实扫码窗口。
        /// </summary>
        internal static Func<string> ScanPromptOverride { get; set; }

        public ScanCodeStep() : base()
        {
            Type = StepType.ScanCode;
        }

        public ScanCodeStep(string name) : base()
        {
            Type = StepType.ScanCode;
            Name = name;
        }

        public ScanCodeStep(string name, string description) : base()
        {
            Type = StepType.ScanCode;
            Name = name;
            Description = description;
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            string prefix = Loc("ScanCode_Failed", "扫码失败：");
            try
            {
                if (WaitTime > 0) await Task.Delay((int)(WaitTime * 1000), cancellationToken);

                var prompt = ScanPromptOverride;
                string barcode = prompt != null ? prompt() : ShaoLu.Views.WindowScanCode.ShowAndWait();
                if (string.IsNullOrWhiteSpace(barcode))
                    return Fail(prefix + Loc("ScanCode_Canceled", "未扫码或已取消"));

                barcode = barcode.Trim();
                IsTrue = true;
                IsError = false;
                ErrorType = StepErrorType.None;
                ErrorMessage = string.Empty;
                LastResult = new StepExecutionResult { IsTrue = true, OCRText = barcode, ExecutedAt = DateTime.Now };
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "ScanCode: 执行失败");
                return Fail(prefix + ex.Message);
            }
        }

        private bool Fail(string message)
        {
            IsTrue = false;
            IsError = true;
            ErrorType = StepErrorType.Unknown;
            ErrorMessage = message;
            LastResult = new StepExecutionResult { IsTrue = false, ErrorMessage = message, ExecutedAt = DateTime.Now };
            Logger.Warn("ScanCode: {0}", message);
            return false;
        }

        private static string Loc(string key, string fallback = null)
        {
            try { return LanguageService.GetLocalizedString(key, fallback); }
            catch { return fallback ?? key; }
        }

        public override AutomationStepBase Clone()
        {
            var clone = new ScanCodeStep(Name, Description);
            clone.WaitTime = WaitTime;
            clone.TrueGotoUid = TrueGotoUid;
            clone.FalseGotoUid = FalseGotoUid;
            clone.IsNeed = IsNeed;
            clone.EnableLog = EnableLog;
            clone.SelfReferenceLimit = SelfReferenceLimit;
            clone.TrueGotoMaxCount = TrueGotoMaxCount;
            clone.FalseGotoMaxCount = FalseGotoMaxCount;
            clone.TrueGotoTimeoutSeconds = TrueGotoTimeoutSeconds;
            clone.FalseGotoTimeoutSeconds = FalseGotoTimeoutSeconds;
            clone.ConditionMode = ConditionMode;
            clone.Conditions = new(Conditions);
            clone.IsSave = IsSave;
            return clone;
        }
    }
}
