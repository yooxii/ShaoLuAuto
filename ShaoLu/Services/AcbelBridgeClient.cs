// -----------------------------------------------------------------------------
//  AcbelBridgeClient —— x64 主程序侧访问 x86 桥接进程的客户端
//
//  背景：Reference\Acbel_Chroma_Test.dll 是 32 位原生 DLL，x64 的 ShaoLu 无法在
//  进程内调用，因此由 AcbelBridge.exe（x86）独占加载，主程序只跟它说话。
//
//  协议：一行一请求/一行一响应，参数 Base64(UTF-8)，详见 AcbelBridge\Program.cs。
//
//  当前只暴露两个业务方法（按需求）：
//      OpenDatabase()  -> ACBEL_DATABASE_OPEN()：验证与数据库是否连通
//      InsertData()    -> ACBEL_INSERT_DATA(db, 上传资料, 镭雕程式文件名)：★会写 MES 生产库
//
//  返回值都是 ANSI 字符串（PChar）：
//      成功  "database"（OPEN）/ "1"（INSERT）；失败 "0}…"（业务卡控）/"2}…"（需重试）
//      （见 Reference\镭雕DLL說明-文本.md）
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AcbelMes;

namespace ShaoLu.Services
{
    /// <summary>一次桥接调用的结果。</summary>
    public sealed class AcbelCallResult
    {
        public AcbelCallResult()
        {
            ValidationErrors = new List<string>();
            ValidationWarnings = new List<string>();
        }

        /// <summary>桥接进程是否正常应答（false = 进程没起来/崩了/超时被杀/本地校验未通过）。</summary>
        public bool TransportOk { get; set; }

        /// <summary>本地（调用前）数据校验的错误项；非空表示根本没发给桥接进程。</summary>
        public List<string> ValidationErrors { get; private set; }

        /// <summary>本地数据校验的提示项（不阻断）。</summary>
        public List<string> ValidationWarnings { get; private set; }

        /// <summary>是否因为超时被中止（数据库不可达或 DLL 弹了模态框）。</summary>
        public bool TimedOut { get; set; }

        /// <summary>DLL 返回的字符串（如 "database" / "1" / "0}B006-線別錯誤…"）。</summary>
        public string Value { get; set; }

        /// <summary>失败说明（桥接层错误，或 "0}…"/"2}…" 的业务说明）。</summary>
        public string Error { get; set; }

        /// <summary>桥接是否报告业务成功（Value = "1" 或 "database"）。</summary>
        public bool IsOk { get { return TransportOk && !TimedOut && AcbelBridgeClient.IsOk(Value); } }

        public override string ToString()
        {
            if (!TransportOk) return LanguageService.GetLocalizedString("Msg_BridgeFailed", "桥接失败：") + (Error ?? LanguageService.GetLocalizedString("Msg_NoDescription", "(无说明)"));
            if (TimedOut) return LanguageService.GetLocalizedString("Msg_BridgeTimeout", "调用超时：") + (Error ?? LanguageService.GetLocalizedString("Msg_NoDescription", "(无说明)"));
            return Value ?? LanguageService.GetLocalizedString("Msg_EmptyValue", "(空)");
        }
    }

    /// <summary>x86 桥接进程的客户端。线程安全：内部串行化每次请求。</summary>
    public sealed class AcbelBridgeClient : IDisposable
    {
        private const string EnvVarName = "ACBEL_BRIDGE_EXE";

        private readonly object _gate = new object();
        private readonly string _bridgeExe;
        private readonly int _callTimeoutSeconds;
        private readonly StringBuilder _stderr = new StringBuilder();
        private static readonly NLog.Logger _log = NLog.LogManager.GetCurrentClassLogger();

        private Process _proc;
        private StreamWriter _stdin;
        private StreamReader _stdout;
        private bool _disposed;

        /// <summary>桥接进程可执行文件路径；null 表示按 <see cref="ResolveBridgeExe"/> 自动定位。</summary>
        public static string BridgeExeOverride { get; set; }

        public AcbelBridgeClient(string bridgeExe = null, int callTimeoutSeconds = 60)
        {
            if (callTimeoutSeconds <= 0) callTimeoutSeconds = 60;
            _callTimeoutSeconds = callTimeoutSeconds;
            _bridgeExe = ResolveBridgeExe(bridgeExe ?? BridgeExeOverride);
        }

        /// <summary>桥接进程路径。</summary>
        public string BridgeExe { get { return _bridgeExe; } }

        /// <summary>桥接进程的 stderr（诊断用）。</summary>
        public string StandardError { get { lock (_gate) return _stderr.ToString(); } }

        /// <summary>
        /// 定位 AcbelBridge.exe：显式路径 → 环境变量 ACBEL_BRIDGE_EXE → 程序目录 →
        /// 逐级向上找 AcbelBridge\bin\{Debug,Release}\AcbelBridge.exe（开发期）。
        /// </summary>
        public static string ResolveBridgeExe(string explicitPath = null)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                string p = Path.GetFullPath(explicitPath);
                if (File.Exists(p)) return p;
                throw new FileNotFoundException(LanguageService.GetLocalizedString("Msg_BridgeExeNotFoundPrefix", "指定的桥接程序不存在：") + p);
            }

            string env = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return Path.GetFullPath(env);

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string beside = Path.Combine(appDir, "AcbelBridge.exe");
            if (File.Exists(beside)) return beside;

            var dir = new DirectoryInfo(appDir);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                foreach (string cfg in new[] { "Debug", "Release" })
                {
                    string p = Path.Combine(dir.FullName, "AcbelBridge", "bin", cfg, "AcbelBridge.exe");
                    if (File.Exists(p)) return p;
                }
            }

            throw new FileNotFoundException(
                LanguageService.GetLocalizedString("Msg_BridgeExeMissingHint", "找不到 AcbelBridge.exe。请先构建 AcbelBridge 工程，或设置环境变量 {0}。", EnvVarName));
        }

        // ------------------------------------------------------------------ 生命周期
        /// <summary>启动桥接进程（幂等）。</summary>
        public bool Start()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_proc != null && !_proc.HasExited) return true;
                if (!File.Exists(_bridgeExe)) throw new FileNotFoundException(LanguageService.GetLocalizedString("Msg_BridgeExeNotFound", "找不到桥接程序：") + _bridgeExe);

                var psi = new ProcessStartInfo
                {
                    FileName = _bridgeExe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false),
                    WorkingDirectory = Path.GetDirectoryName(_bridgeExe)
                };
                psi.Arguments = "--call-timeout " + _callTimeoutSeconds;

                _proc = Process.Start(psi);
                _stdin = _proc.StandardInput;
                _stdout = _proc.StandardOutput;
                _stderr.Length = 0;
                var err = _proc.StandardError;
                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        string line;
                        while ((line = err.ReadLine()) != null)
                        {
                            lock (_gate) { _stderr.AppendLine(line); }
                            // 桥接进程（含 DLL 自身的输出）一律落进程序日志，便于现场排查网络/环境问题
                            try { _log.Warn("AcbelBridge: " + line); } catch { }
                        }
                    }
                    catch { /* 进程退出时的正常异常 */ }
                }, TaskCreationOptions.LongRunning);

                _stdin.AutoFlush = true;
                return true;
            }
        }

        /// <summary>停止桥接进程（调用 quit，不等待则强杀）。</summary>
        public void Stop()
        {
            lock (_gate)
            {
                if (_proc == null) return;
                try
                {
                    if (!_proc.HasExited && _stdin != null)
                    {
                        _stdin.Write("quit\n");
                        _stdin.Flush();
                        if (!_proc.WaitForExit(2000)) _proc.Kill();
                    }
                }
                catch { try { if (!_proc.HasExited) _proc.Kill(); } catch { } }
                finally
                {
                    try { _stdin?.Dispose(); } catch { }
                    try { _stdout?.Dispose(); } catch { }
                    try { _proc.Dispose(); } catch { }
                    _proc = null; _stdin = null; _stdout = null;
                }
            }
        }

        public void Dispose() { if (_disposed) return; _disposed = true; Stop(); }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(typeof(AcbelBridgeClient).Name);
        }

        // ------------------------------------------------------------------ 业务方法
        /// <summary>
        /// ping：只验证桥接进程 + DLL 能否加载、两个导出能否解析，**不调用任何 DLL 函数**。
        /// 返回多行 key=value 文本。
        /// </summary>
        public string Ping()
        {
            var r = Request("ping");
            if (!r.TransportOk) throw new InvalidOperationException(r.Error);
            return r.Value;
        }

        /// <summary>
        /// 调用 ACBEL_DATABASE_OPEN()：验证与 MES 数据库能否连通。
        /// 成功 → Value = "database"；失败 → Value = "0}數據庫無法連接" / "连接错误"。
        /// 注意：需要站点的 BDE 别名（BSFC/BSFC2/ESFC/MMRP2）与 Oracle 客户端齐备。
        /// </summary>
        public AcbelCallResult OpenDatabase()
        {
            return Request("open");
        }

        /// <summary>调用 ACBEL_SET_BASIC_INFO(db, 工单号, 站别/工号合并值, 线别)。</summary>
        public AcbelCallResult SetBasicInfo(string workOrder, string stationEmp, string line)
        {
            return Request("set_basic_info", workOrder ?? string.Empty, stationEmp ?? string.Empty, line ?? string.Empty);
        }

        /// <summary>调用 CHKBAR_CODE(条码, 站别/工号合并值, 站别/工号合并值)。</summary>
        public AcbelCallResult CheckBarcode(string barcode, string stationEmp)
        {
            return Request("chkbar_code", barcode ?? string.Empty, stationEmp ?? string.Empty);
        }

        /// <summary>
        /// 调用 ACBEL_INSERT_DATA(db, 上传资料, 镭雕程式文件名)。★ 会写入 MES 生产库。
        /// db 由桥接进程缓存（必须已成功执行过 OpenDatabase）；未成功 open 时桥接会拒绝且不写库。
        /// </summary>
        /// <param name="uploadData">厂商格式的定长逗号分隔资料（27 段，见 Reference\ACBEL_INSERT_DATA-字段与校验.md）。</param>
        /// <param name="programFile">镭雕的程式文件名。</param>
        /// <param name="validate">true（默认）先在本地按字段规范校验，不通过就直接返回、不发给桥接进程。</param>
        /// <param name="allowExtraFields">true 时字段数多于 27 段只告警不报错。</param>
        public AcbelCallResult InsertData(string uploadData, string programFile, bool validate = true, bool allowExtraFields = false)
        {
            if (uploadData == null) throw new ArgumentNullException("uploadData");

            if (validate)
            {
                AcbelPayloadValidationResult v = AcbelInsertDataValidator.Validate(uploadData, allowExtraFields);
                if (!v.IsValid)
                {
                    var bad = new AcbelCallResult { TransportOk = false, Value = null, Error = v.ToErrorText() };
                    bad.ValidationErrors.AddRange(v.Errors);
                    bad.ValidationWarnings.AddRange(v.Warnings);
                    return bad;   // 未发起桥接调用，更不会写库
                }
            }

            AcbelCallResult result = Request("insert", uploadData, programFile ?? string.Empty);
            return result;
        }

        /// <summary>只做上传资料的字段校验，不调用桥接、不连数据库。</summary>
        public static AcbelPayloadValidationResult ValidateInsertData(string uploadData, bool allowExtraFields = false)
        {
            return AcbelInsertDataValidator.Validate(uploadData, allowExtraFields);
        }

        // ------------------------------------------------------------------ 协议实现
        private AcbelCallResult Request(string command, params string[] args)
        {
            var result = new AcbelCallResult();
            lock (_gate)
            {
                ThrowIfDisposed();
                if (!StartInternal()) { result.Error = LanguageService.GetLocalizedString("Msg_BridgeStartFailed", "桥接进程启动失败"); return result; }

                var sb = new StringBuilder(command);
                foreach (string a in args) sb.Append('\t').Append(ToB64(a));
                sb.Append('\n');

                try
                {
                    _stdin.Write(sb.ToString());
                    _stdin.Flush();
                }
                catch (Exception ex)
                {
                    result.Error = LanguageService.GetLocalizedString("Msg_BridgeWriteFailed", "写入桥接进程失败：") + ex.Message;
                    KillProcess();
                    return result;
                }

                // 读一行，带超时（比桥接自身超时多 30s，留出进程调度余量）
                string line = null;
                var readTask = Task.Factory.StartNew(() =>
                {
                    try { return _stdout.ReadLine(); }
                    catch { return null; }
                }, TaskCreationOptions.LongRunning);

                int waitMs = (_callTimeoutSeconds + 30) * 1000;
                if (!readTask.Wait(waitMs))
                {
                    result.TimedOut = true;
                    result.Error = LanguageService.GetLocalizedString("Msg_BridgeNoResponse", "桥接进程 {0} 秒未响应（数据库不可达或 DLL 弹出对话框）；已终止该进程。", _callTimeoutSeconds + 30);
                    KillProcess();
                    return result;
                }

                line = readTask.Result;
                if (line == null)
                {
                    result.Error = LanguageService.GetLocalizedString("Msg_BridgeExited", "桥接进程已退出（stderr：{0}）", StandardError.Trim());
                    KillProcess();
                    return result;
                }

                result.TransportOk = true;
                string[] parts = line.Split('\t');
                if (parts.Length >= 2 && parts[0] == "OK")
                {
                    result.Value = FromB64(parts[1]);
                }
                else
                {
                    result.TransportOk = parts.Length >= 2 && parts[0] == "FAIL";
                    result.Error = parts.Length >= 2 ? FromB64(parts[1]) : line;
                    // 桥接报 FAIL 时，Value 里也可能是 DLL 的失败串（正常业务失败），这里不覆盖
                    if (result.Error != null && result.Error.StartsWith("TIMEOUT", StringComparison.Ordinal))
                    {
                        result.TimedOut = true;
                        KillProcess();
                    }
                }
                return result;
            }
        }

        private bool StartInternal()
        {
            if (_proc != null && !_proc.HasExited) return true;
            Stop();
            return Start();
        }

        private void KillProcess()
        {
            try { if (_proc != null && !_proc.HasExited) _proc.Kill(); } catch { }
            try { _proc?.Dispose(); } catch { }
            _proc = null;
        }

        private static string ToB64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? string.Empty)); }

        private static string FromB64(string s) { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }

        // ------------------------------------------------------------------ 返回值口径
        /// <summary>成功判定：OPEN 返回 "database"，其余返回 "1"（见厂商文档）。</summary>
        public static bool IsOk(string dllResult)
        {
            if (string.IsNullOrEmpty(dllResult)) return false;
            return dllResult == "1" || string.Equals(dllResult, "database", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>是否为厂商定义的内部异常（"2}…"），文档要求此时重新调用一次。</summary>
        public static bool IsRetryNeeded(string dllResult)
        {
            return !string.IsNullOrEmpty(dllResult) && dllResult.StartsWith("2}", StringComparison.Ordinal);
        }

        /// <summary>是否为业务卡控失败（"0}…"）。</summary>
        public static bool IsBusinessFail(string dllResult)
        {
            return !string.IsNullOrEmpty(dllResult) && dllResult.StartsWith("0", StringComparison.Ordinal);
        }

        /// <summary>取出 "0}B006-線別錯誤…" 里的异常码 B006；取不到返回 null。</summary>
        public static string ExtractErrorCode(string dllResult)
        {
            if (string.IsNullOrEmpty(dllResult) || dllResult[0] != '0') return null;
            int brace = dllResult.IndexOf('}');
            if (brace < 0 || brace + 1 >= dllResult.Length) return null;
            int end = brace + 1;
            while (end < dllResult.Length && !char.IsWhiteSpace(dllResult[end]) && dllResult[end] != '-' && dllResult[end] != '（')
                end++;
            return dllResult.Substring(brace + 1, end - brace - 1);
        }
    }
}
