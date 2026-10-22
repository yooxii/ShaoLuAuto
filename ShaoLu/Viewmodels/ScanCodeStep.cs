using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShaoLu.Viewmodels
{
    /// <summary>
    /// 扫码步骤：验证网络（ACBEL_DATABASE_OPEN）→ 弹窗扫码 → 验证条码（CHKBAR_CODE）→ 写入基础信息（ACBEL_SET_BASIC_INFO）。
    /// 扫码得到的条码作为本步骤的「文本」（OCRText）暴露，上传步骤的 INTERNAL_SR 等可直接引用。
    /// </summary>
    public class ScanCodeStep : AutomationStepBase
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

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

        private string _workOrderField = "工单号";
        /// <summary>取工单号所用的用户输入字段名</summary>
        public string WorkOrderField { get => _workOrderField; set => SetProperty(ref _workOrderField, value); }

        private string _stationField = "站别";
        /// <summary>取站别所用的用户输入字段名</summary>
        public string StationField { get => _stationField; set => SetProperty(ref _stationField, value); }

        private string _employeeField = "工号";
        /// <summary>取工号所用的用户输入字段名</summary>
        public string EmployeeField { get => _employeeField; set => SetProperty(ref _employeeField, value); }

        private string _lineField = "线别";
        /// <summary>取线别所用的用户输入字段名</summary>
        public string LineField { get => _lineField; set => SetProperty(ref _lineField, value); }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            string prefix = Loc("ScanCode_Failed", "扫码失败：");
            try
            {
                if (WaitTime > 0) await Task.Delay((int)(WaitTime * 1000), cancellationToken);

                string workOrder = ResolveField(WorkOrderField);
                string station = ResolveField(StationField);
                string employee = ResolveField(EmployeeField);
                string line = ResolveField(LineField);
                // 站别与工号按「直接拼接」合并成 DLL 需要的单值
                string stationEmp = (station ?? string.Empty) + (employee ?? string.Empty);

                if (string.IsNullOrWhiteSpace(workOrder) || string.IsNullOrWhiteSpace(stationEmp) || string.IsNullOrWhiteSpace(line))
                    return Fail(prefix + Loc("ScanCode_MissingInput", "缺少工单号/站别/工号/线别，请先在用户输入步骤中填写"));

                using var client = new AcbelBridgeClient();

                // 1) 验证网络：打开 MES 连接
                var open = client.OpenDatabase();
                if (!IsOkCall(open))
                    return Fail(prefix + Loc("ScanCode_NetworkFailed", "验证网络失败") + "：" + Describe(open));

                // 2) 扫码
                string barcode = ShaoLu.Views.WindowScanCode.ShowAndWait();
                if (string.IsNullOrWhiteSpace(barcode))
                    return Fail(prefix + Loc("ScanCode_Canceled", "未扫码或已取消"));

                // 3) 验证条码
                var check = client.CheckBarcode(barcode, stationEmp);
                if (!IsOkCall(check))
                    return Fail(prefix + Loc("ScanCode_CheckFailed", "验证条码失败") + "：" + Describe(check));

                // 4) 写入基础信息（工单/站别工号/线别）
                var basic = client.SetBasicInfo(workOrder, stationEmp, line);
                if (!IsOkCall(basic))
                    return Fail(prefix + Loc("ScanCode_BasicFailed", "写入基础信息失败") + "：" + Describe(basic));

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

        private static bool IsOkCall(AcbelCallResult result)
            => result != null && result.TransportOk && !result.TimedOut && AcbelBridgeClient.IsOk(result.Value);

        private static string Describe(AcbelCallResult result)
        {
            if (result == null) return Loc("ScanCode_NoResponse", "桥接无响应");
            if (!result.TransportOk) return result.Error ?? string.Empty;
            if (result.TimedOut) return Loc("ScanCode_Timeout", "调用超时");
            return string.IsNullOrEmpty(result.Value) ? (result.Error ?? string.Empty) : result.Value;
        }

        /// <summary>从「用户输入」步骤里取字段值（取不到按空串处理）。</summary>
        private static string ResolveField(string fieldName)
        {
            try
            {
                var steps = SingletonLocator.Steps?.AutomationStepBases;
                if (steps == null) return string.Empty;
                var userInput = steps.OfType<UserInputStep>().FirstOrDefault();
                if (userInput?.Fields == null) return string.Empty;
                var field = userInput.Fields.FirstOrDefault(f =>
                    string.Equals((f?.Name ?? string.Empty).Trim(), (fieldName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
                return field?.Value ?? string.Empty;
            }
            catch { return string.Empty; }
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
            clone.ConditionMode = ConditionMode;
            clone.Conditions = new(Conditions);
            clone.IsSave = IsSave;
            clone.WorkOrderField = WorkOrderField;
            clone.StationField = StationField;
            clone.EmployeeField = EmployeeField;
            clone.LineField = LineField;
            return clone;
        }
    }
}
