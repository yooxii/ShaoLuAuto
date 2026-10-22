// -----------------------------------------------------------------------------
//  AcbelBridge —— x86 桥接进程
//
//  为什么需要它：Reference\Acbel_Chroma_Test.dll 是 32 位原生 Delphi DLL，
//  64 位的 ShaoLu 主程序无法加载它（LoadLibrary 会返回 ERROR_BAD_EXE_FORMAT 193）。
//  本进程用 x86 编译，独占加载该 DLL，并通过 stdin/stdout 协议对外提供调用。
//
//  当前只暴露两个函数（按需求）：
//     open    -> ACBEL_DATABASE_OPEN()                        验证与数据库是否连通
//     insert  -> ACBEL_INSERT_DATA(db, 上传资料, 镭雕程式文件名)  ★会写入 MES 生产库
//
//  协议（UTF-8，一行一请求，一行一响应；参数一律 Base64(UTF-8) 以免分隔符/编码问题）
//     请求:  <命令>[\t<base64 arg1>[\t<base64 arg2>]]
//            ping
//            open
//            validate\t<base64 上传资料>                     （只做字段校验，不碰数据库/DLL 调用）
//            insert\t<base64 上传资料>\t<base64 镭雕程式文件名>
//            quit
//     响应:  OK\t<base64 负载>      负载是文本（可能是多行 key=value）
//            FAIL\t<base64 说明>
//
//  返回值口径（见 Reference\镭雕DLL說明-文本.md）：
//      ACBEL_DATABASE_OPEN 成功返回 "database"，失败返回 "0}數據庫無法連接" / "連接DB Connection ERROR"
//      ACBEL_INSERT_DATA   成功返回 "1"，失败返回 "0}…"（业务卡控）或 "2}…"（内部异常，需重试）
//  两者都是 ANSI 字符串指针（PChar），不是整数。
//
//  运行环境（DLL 里硬编码的绝对路径，实测行为）：
//      D:\CHROMA\FAC_CODE.txt   缺失 → OPEN 内部读取异常被吞掉，返回 "0}數據庫無法連接"（看着像连不上库）
//                               内容第一行第一个字符决定 BDE 别名：H→BSFC / P→BSFC2 / E→ESFC / M→MMRP2，
//                               其它字符→AMDB。文件必须是**无 BOM** 的纯文本，否则首字符不是 H。
//      D:\AMZ_LOG\              缺失 → OPEN 一开始写 D:\AMZ_LOG\DB_LOG.txt 就抛 Delphi 异常（0xEEDFADE）冲出 DLL
//      D:\TEST_DATA1\           缺失 → ACBEL_INSERT_DATA 写 D:\TEST_DATA1\LOG1.txt 时同样抛异常
//      以上三项启动时自动补齐（只补缺失的，已存在不动）；--no-prepare-env 可关闭，
//      --fac-code 可指定写入 FAC_CODE.txt 的厂区代码（默认 H）。
//
//  安全约束：
//      * insert 必须先用 open 成功拿到 database 串，否则直接拒绝，**不会调用 DLL**；
//      * insert 的第 2 个参数（上传资料）按 27 段字段规范二次校验（共享源码 Shared\AcbelInsertDataSpec.cs），
//        不通过同样拒绝且不写库；--no-validate 可关闭，--allow-extra-fields 允许字段数多于 27；
//      * 任何一次调用都带超时（默认 60s），超时返回 FAIL，避免数据库不可达时永久挂住；
//      * stdout 只输出协议行，诊断信息走 stderr。
//
//  命令行：
//      AcbelBridge.exe [--dll <Acbel_Chroma_Test.dll 路径>] [--call-timeout <秒>]
//                      [--no-validate] [--allow-extra-fields]
//                      [--no-prepare-env] [--fac-code <厂区代码>] [--version]
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using AcbelMes;

namespace AcbelBridge
{
    internal static class Program
    {
        private const string DefaultDllName = "Acbel_Chroma_Test.dll";

        // ---- 只声明当前需要的两个导出；stdcall + ANSI + 精确名称 ----
        [DllImport(DefaultDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ACBEL_DATABASE_OPEN();

        [DllImport(DefaultDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ACBEL_INSERT_DATA(string database, string uploadData, string programFile);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr LoadLibraryA(string path);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern bool SetDllDirectoryA(string path);

        private static string _dllPath;
        private static int _callTimeoutMs = 60000;
        private static bool _validate = true;
        private static bool _allowExtraFields;
        private static bool _prepareEnv = true;
        private static string _facCode = "H";
        private static string _envError;
        private static StreamWriter _out;
        private static StreamReader _in;

        // ---- DLL 硬编码依赖的 D 盘运行环境（缺哪个会出什么事，见文件头注释） ----
        private static readonly string[] EnvDirectories =
        {
            @"D:\AMZ_LOG",
            @"D:\TEST_DATA1",
            @"D:\CHROMA"
        };

        private const string FacCodePath = @"D:\CHROMA\FAC_CODE.txt";

        /// <summary>上一次 ACBEL_DATABASE_OPEN 成功返回的库名，作为 ACBEL_INSERT_DATA 的第 1 个参数。</summary>
        private static string _database;

        private static int Main(string[] args)
        {
            try
            {
                var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                stdout.AutoFlush = true;
                _out = stdout;
                _in = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
                Console.OutputEncoding = new UTF8Encoding(false);

                if (!ParseArgs(args)) return 2;
                PrepareEnvironment();

                string line;
                while ((line = _in.ReadLine()) != null)
                {
                    line = line.TrimEnd('\r', '\n');
                    if (line.Length == 0) continue;

                    string[] parts = line.Split('\t');
                    string cmd = parts[0].Trim().ToLowerInvariant();

                    if (cmd == "quit") return 0;
                    if (cmd == "ping") { ReplyOk(PingReport()); continue; }
                    if (cmd == "validate")
                    {
                        if (parts.Length < 2) { ReplyFail("validate 需要 1 个参数：上传资料"); continue; }
                        string payload;
                        try { payload = FromB64(parts[1]); }
                        catch (Exception ex) { ReplyFail("参数不是合法的 Base64：" + ex.Message); continue; }
                        AcbelPayloadValidationResult v = AcbelInsertDataValidator.Validate(payload, _allowExtraFields);
                        if (!v.IsValid) { ReplyFail(v.ToErrorText()); continue; }
                        ReplyOk(v.Warnings.Count == 0
                            ? "校验通过：字段数 " + AcbelInsertDataSpec.FieldCount + " 段齐全"
                            : v.ToErrorText());
                        continue;
                    }
                    if (cmd == "open") { ReplyOkOrFail(CallOpen); continue; }
                    if (cmd == "insert")
                    {
                        if (parts.Length < 3) { ReplyFail("insert 需要 2 个参数：上传资料、镭雕程式文件名"); continue; }
                        string payload, program;
                        try
                        {
                            payload = FromB64(parts[1]);
                            program = FromB64(parts[2]);
                        }
                        catch (Exception ex) { ReplyFail("参数不是合法的 Base64：" + ex.Message); continue; }
                        ReplyOkOrFail(() => CallInsert(payload, program));
                        continue;
                    }
                    ReplyFail("未知命令：" + cmd + "（可用：ping / open / insert / quit）");
                }
                return 0;
            }
            catch (Exception ex)
            {
                try { ReplyFail("桥接进程异常：" + ex.GetType().Name + ": " + ex.Message); } catch { }
                return 1;
            }
        }

        // ------------------------------------------------------------------ 参数
        private static bool ParseArgs(string[] args)
        {
            _dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultDllName);
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--help":
                    case "-h":
                        Console.Error.WriteLine("AcbelBridge [--dll <path>] [--call-timeout <seconds>] [--no-validate] [--allow-extra-fields] [--no-prepare-env] [--fac-code <code>] [--version]");
                        return false;
                    case "--version":
                        Console.Error.WriteLine("AcbelBridge 1.0.0 (x86 bridge for Acbel_Chroma_Test.dll)");
                        return false;
                    case "--dll":
                        if (i + 1 >= args.Length) { Console.Error.WriteLine("--dll 缺少参数"); return false; }
                        _dllPath = Path.GetFullPath(args[++i]);
                        break;
                    case "--call-timeout":
                        if (i + 1 >= args.Length) { Console.Error.WriteLine("--call-timeout 缺少参数"); return false; }
                        int sec;
                        if (!int.TryParse(args[++i], out sec) || sec <= 0) { Console.Error.WriteLine("--call-timeout 需要正整数秒"); return false; }
                        _callTimeoutMs = sec * 1000;
                        break;
                    case "--no-validate":
                        _validate = false;
                        break;
                    case "--allow-extra-fields":
                        _allowExtraFields = true;
                        break;
                    case "--no-prepare-env":
                        _prepareEnv = false;
                        break;
                    case "--fac-code":
                        if (i + 1 >= args.Length) { Console.Error.WriteLine("--fac-code 缺少参数"); return false; }
                        string code = args[++i];
                        if (string.IsNullOrWhiteSpace(code) || code.IndexOf('\r') >= 0 || code.IndexOf('\n') >= 0)
                        {
                            Console.Error.WriteLine("--fac-code 需要一行非空文本（如 H）");
                            return false;
                        }
                        _facCode = code.Trim();
                        break;
                    default:
                        Console.Error.WriteLine("未知参数：" + args[i]);
                        return false;
                }
            }
            // 让 DllImport 的 "Acbel_Chroma_Test.dll" 从指定目录加载
            string dir = Path.GetDirectoryName(_dllPath);
            if (!string.IsNullOrEmpty(dir)) SetDllDirectoryA(dir);
            return true;
        }

        // ------------------------------------------------------------------ 运行环境
        /// <summary>
        /// 补齐 DLL 硬编码依赖的 D 盘目录与 FAC_CODE.txt。只补缺失项，已存在的一律不动。
        /// 失败时记录 _envError，由 open/insert 在调用 DLL 之前直接拒绝，避免把
        /// “环境没准备好”伪装成 “0}數據庫無法連接”。
        /// </summary>
        private static void PrepareEnvironment()
        {
            if (!_prepareEnv) return;
            try
            {
                foreach (string dir in EnvDirectories)
                {
                    if (Directory.Exists(dir)) continue;
                    Directory.CreateDirectory(dir);
                    Console.Error.WriteLine("[AcbelBridge] 已创建目录 " + dir);
                }

                if (!File.Exists(FacCodePath))
                {
                    // 必须写成无 BOM 的纯文本：DLL 取第一行第一个字符当厂区代码。
                    File.WriteAllText(FacCodePath, _facCode, new UTF8Encoding(false));
                    Console.Error.WriteLine("[AcbelBridge] 已创建 " + FacCodePath + "，内容=" + _facCode);
                }
            }
            catch (Exception ex)
            {
                _envError = ex.GetType().Name + ": " + ex.Message;
                Console.Error.WriteLine("[AcbelBridge] 运行环境准备失败：" + _envError);
            }
        }

        /// <summary>调用 DLL 之前检查运行环境是否准备就绪。</summary>
        private static void EnsureEnvironmentReady()
        {
            if (_envError == null) return;
            throw new InvalidOperationException(
                "DLL 依赖的 D 盘运行环境准备失败（D:\\AMZ_LOG、D:\\TEST_DATA1、D:\\CHROMA\\FAC_CODE.txt）："
                + _envError + "。已拒绝调用 DLL。");
        }

        // ------------------------------------------------------------------ ping
        /// <summary>只做 LoadLibrary/GetProcAddress，不调用任何 DLL 函数。</summary>
        private static string PingReport()
        {
            var sb = new StringBuilder();
            bool x86 = !Environment.Is64BitProcess;
            sb.AppendLine("bitness=" + (x86 ? "x86" : "x64"));
            sb.AppendLine("dll=" + _dllPath);
            sb.AppendLine("dllExists=" + File.Exists(_dllPath).ToString().ToLowerInvariant());
            sb.AppendLine("callTimeoutSeconds=" + (_callTimeoutMs / 1000));
            sb.AppendLine("validateInsertData=" + _validate.ToString().ToLowerInvariant());
            sb.AppendLine("allowExtraFields=" + _allowExtraFields.ToString().ToLowerInvariant());
            sb.AppendLine("prepareEnvironment=" + _prepareEnv.ToString().ToLowerInvariant());
            sb.AppendLine("env.amzLog=" + Directory.Exists(@"D:\AMZ_LOG").ToString().ToLowerInvariant());
            sb.AppendLine("env.testData1=" + Directory.Exists(@"D:\TEST_DATA1").ToString().ToLowerInvariant());
            sb.AppendLine("env.facCodeFile=" + File.Exists(FacCodePath).ToString().ToLowerInvariant());
            sb.AppendLine("env.facCode=" + _facCode);
            if (_envError != null) sb.AppendLine("env.error=" + _envError);
            sb.AppendLine("insertFieldCount=" + AcbelInsertDataSpec.FieldCount);
            sb.AppendLine("insertRequiredFields=" + string.Join("/", System.Linq.Enumerable.Select(AcbelInsertDataSpec.RequiredFields, f => f.Name)));

            IntPtr h = IntPtr.Zero;
            try
            {
                h = LoadLibraryA(_dllPath);
                int err = Marshal.GetLastWin32Error();
                sb.AppendLine("loadLibrary=" + (h != IntPtr.Zero ? "ok" : "failed"));
                if (h == IntPtr.Zero) sb.AppendLine("loadLibraryError=" + err);
                foreach (string name in new[] { "ACBEL_DATABASE_OPEN", "ACBEL_INSERT_DATA" })
                {
                    IntPtr p = h == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(h, name);
                    sb.AppendLine("export." + name + "=" + (p != IntPtr.Zero).ToString().ToLowerInvariant());
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("loadLibrary=exception");
                sb.AppendLine("loadLibraryError=" + ex.GetType().Name + ": " + ex.Message);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ 调用
        private static string CallOpen()
        {
            EnsureEnvironmentReady();
            LogNetworkCheck("open");
            string result = RunGuarded(ACBEL_DATABASE_OPEN);
            // 成功时返回库名（厂商文档：正确返回 database）；失败是 "0}…" / "2}…"
            if (!string.IsNullOrEmpty(result) && result[0] != '0' && result[0] != '2')
                _database = result;
            else
                _database = null;
            return result;
        }

        /// <summary>
        /// 调用 DLL 前打印一次网络/环境检查（一律走 stderr，主程序会落进日志）。
        /// DLL 内部的「验证网络」步骤由厂商 DLL 自己输出，这里补充我们这一侧能看到的信息。
        /// </summary>
        private static void LogNetworkCheck(string phase)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("[AcbelBridge] 网络检查(" + phase + ")：time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                try
                {
                    sb.Append("; networkAvailable=" + System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable());
                    int up = 0;
                    foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up) up++;
                    }
                    sb.Append("; nicsUp=" + up);
                }
                catch (Exception ex)
                {
                    sb.Append("; networkCheckError=" + ex.Message);
                }
                sb.Append("; dll=" + _dllPath);
                sb.Append("; db=" + (_database ?? "（未 open）"));
                Console.Error.WriteLine(sb.ToString());
            }
            catch { }
        }

        private static string CallInsert(string uploadData, string programFile)
        {
            EnsureEnvironmentReady();
            LogNetworkCheck("insert");
            if (string.IsNullOrEmpty(_database))
                throw new InvalidOperationException(
                    "尚未成功执行 open：ACBEL_INSERT_DATA 的第 1 个参数必须是 ACBEL_DATABASE_OPEN 返回的库名。已拒绝调用（未写库）。");
            if (string.IsNullOrEmpty(uploadData))
                throw new InvalidOperationException("上传资料为空，已拒绝调用（未写库）。");

            // 第 2 道闸门：按 27 段字段规范校验上传资料（客户端已校验过，这里防绕过）
            if (_validate)
            {
                AcbelPayloadValidationResult v = AcbelInsertDataValidator.Validate(uploadData, _allowExtraFields);
                if (!v.IsValid)
                    throw new InvalidOperationException("上传资料校验未通过，已拒绝调用（未写库）：\n" + v.ToErrorText());
                if (v.Warnings.Count > 0)
                    Console.Error.WriteLine("[AcbelBridge] 上传资料校验提示：" + v.ToErrorText());
            }

            string db = _database;
            return RunGuarded(() => ACBEL_INSERT_DATA(db, uploadData, programFile ?? string.Empty));
        }

        /// <summary>把 ANSI 字符串指针转成 string。</summary>
        private static string PtrToString(IntPtr p)
        {
            return p == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(p);
        }

        /// <summary>带超时地执行一次 DLL 调用；超时抛 TimeoutException（不阻塞桥接进程）。</summary>
        private static string RunGuarded(Func<IntPtr> call)
        {
            string result = null;
            Exception error = null;
            var done = new ManualResetEventSlim(false);

            var t = new Thread(() =>
            {
                try { result = PtrToString(call()); }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            t.IsBackground = true;
            t.Name = "AcbelCall";
            t.Start();

            if (!done.Wait(_callTimeoutMs))
                throw new TimeoutException("调用超过 " + (_callTimeoutMs / 1000) + " 秒未返回（数据库不可达或 DLL 弹出对话框）；桥接进程仍可用，但该调用线程已放弃。");
            if (error != null) throw error;
            return result;
        }

        // ------------------------------------------------------------------ 协议
        private static void ReplyOk(string text) { WriteLine("OK\t" + ToB64(text ?? string.Empty)); }

        private static void ReplyOkOrFail(Func<string> call)
        {
            try { ReplyOk(call()); }
            catch (TimeoutException ex) { ReplyFail("TIMEOUT: " + ex.Message); }
            catch (Exception ex) { ReplyFail(ex.GetType().Name + ": " + ex.Message); }
        }

        private static void ReplyFail(string message) { WriteLine("FAIL\t" + ToB64(message ?? string.Empty)); }

        private static void WriteLine(string s)
        {
            _out.Write(s);
            _out.Write('\n');
            _out.Flush();
        }

        private static string ToB64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? string.Empty)); }

        private static string FromB64(string s) { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
    }
}
