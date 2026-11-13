// -----------------------------------------------------------------------------
//  ConsoleSession —— AcbelBridge 的控制台命令集（--console）
//
//  用途：不经过主程序，直接手工试 Acbel_Chroma_Test.dll 的任意函数。
//  参数直接写文本（不用 Base64），支持引号包住含空格的参数：
//      AcbelBridge.exe --console [--allow-write] [--call-timeout 60]
//
//  约定：
//      * 命令名 = 导出名（大小写不敏感），另有 open/close/insert/basic 等简写；
//      * db 参数可用 @db，自动替换成上一次 open 成功的库名；
//      * 空参数写 "" 或 -；
//      * 会写 MES 生产库的命令必须带 --allow-write，否则拒绝执行；
//      * help 看全部命令，help <命令> 看单条用法。
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;

namespace AcbelBridge
{
    internal static class ConsoleSession
    {
        /// <summary>控制台主循环；返回进程退出码。</summary>
        public static int Run()
        {
            PrintBanner();

            bool interactive = !Console.IsInputRedirected;
            while (true)
            {
                if (interactive) Console.Write("AcbelBridge> ");

                string line = Console.ReadLine();
                if (line == null) break;

                line = line.Trim();
                if (line.Length == 0) continue;

                string lower = line.ToLowerInvariant();
                if (lower == "quit" || lower == "exit" || lower == "q") break;

                string output;
                try
                {
                    output = Execute(line);
                }
                catch (Exception ex)
                {
                    output = "执行异常：" + ex.GetType().Name + ": " + ex.Message;
                }

                if (!string.IsNullOrEmpty(output)) Console.WriteLine(output);
            }

            return 0;
        }

        /// <summary>执行一行命令并返回要显示的文字（单元/脚本可复用）。</summary>
        public static string Execute(string line)
        {
            List<string> tokens = Tokenize(line);
            if (tokens.Count == 0) return string.Empty;

            string command = tokens[0];
            string lower = command.ToLowerInvariant();

            // ---- 内置命令 ----
            switch (lower)
            {
                case "help":
                case "?":
                case "h":
                    return tokens.Count > 1 ? DescribeCommand(tokens[1]) : BuildHelp();
                case "ping":
                    return Program.PingReport();
                case "env":
                    return EnvironmentReport();
                case "db":
                    if (tokens.Count > 1)
                    {
                        Program.Database = tokens[1];
                        return "已手动设置库名：" + Program.Database;
                    }
                    return string.IsNullOrEmpty(Program.Database)
                        ? "当前未连接（先执行 open，或用 db <库名> 手动设置）"
                        : "当前库名：" + Program.Database;
                case "open":
                    return RunOpen();
            }

            // ---- DLL 导出命令 ----
            AcbelFunction fn = DllFunctions.Find(command);
            if (fn == null)
                return "未知命令：" + command + "（输入 help 查看全部命令）";

            string[] args = tokens.GetRange(1, tokens.Count - 1).ToArray();
            string[] prepared;
            string error;
            if (!TryPrepareArgs(fn, args, out prepared, out error)) return error;

            if (fn.Writes && !Program.AllowWrite)
            {
                return "已拒绝：" + fn.Name + " 会写入 MES 生产库。确认要执行请用 --allow-write 启动。";
            }

            try
            {
                if (fn.Name == "ACBEL_DATABASE_OPEN")
                {
                    return RunOpen();
                }

                Program.EnsureEnvironmentReady();
                Program.LogNetworkCheck(fn.Name);

                List<string> changed;
                string result = DllFunctions.Invoke(fn, prepared, out changed);

                var sb = new StringBuilder();
                sb.AppendLine("返回：" + (string.IsNullOrEmpty(result) ? "(空)" : result));
                if (changed.Count > 0) sb.AppendLine("写回的参数：" + string.Join("；", changed.ToArray()));
                sb.Append("判定：" + Judge(result));
                return sb.ToString();
            }
            catch (TimeoutException ex)
            {
                return "调用超时：" + ex.Message;
            }
            catch (Exception ex)
            {
                return "调用失败：" + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static string RunOpen()
        {
            try
            {
                Program.EnsureEnvironmentReady();
                Program.LogNetworkCheck("open");
                string result = Program.CallOpen();
                var sb = new StringBuilder();
                sb.AppendLine("返回：" + (string.IsNullOrEmpty(result) ? "(空)" : result));
                sb.Append("判定：" + Judge(result));
                if (!string.IsNullOrEmpty(Program.Database))
                    sb.AppendLine().Append("已记住库名 " + Program.Database + "，后续参数可用 @db");
                return sb.ToString();
            }
            catch (TimeoutException ex)
            {
                return "调用超时：" + ex.Message;
            }
            catch (Exception ex)
            {
                return "调用失败：" + ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>把 @db 替换成当前库名，并校验参数个数。</summary>
        private static bool TryPrepareArgs(AcbelFunction fn, string[] input, out string[] args, out string error)
        {
            error = null;
            args = input;

            if (input.Length != fn.Args.Length)
            {
                error = "参数个数不对：" + fn.Name + " 需要 " + fn.Args.Length + " 个参数（" +
                        string.Join(", ", fn.Args) + "），收到 " + input.Length + " 个。\r\n用法： " + Usage(fn);
                return false;
            }

            string[] prepared = new string[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                string value = input[i];
                if (value == "-") value = string.Empty;
                if (value == "@db" || value == "@database")
                {
                    if (string.IsNullOrEmpty(Program.Database))
                    {
                        error = "还没有可用库名：先执行 open，或用 db <库名> 手动设置。";
                        return false;
                    }
                    value = Program.Database;
                }
                prepared[i] = value;
            }

            args = prepared;
            return true;
        }

        private static string Judge(string result)
        {
            if (string.IsNullOrEmpty(result)) return "无返回值";
            if (result == "1" || string.Equals(result, "database", StringComparison.OrdinalIgnoreCase))
                return "成功";
            if (result[0] == '0') return "业务失败（0 类别，卡控）";
            if (result[0] == '2') return "内部异常（2 类别，文档要求重调一次）";
            return "见返回内容";
        }

        // ---------------------------------------------------------------- 帮助
        public static string BuildHelp()
        {
            var sb = new StringBuilder();
            sb.AppendLine("AcbelBridge 控制台 — Acbel_Chroma_Test.dll 函数控制");
            sb.AppendLine("用法：AcbelBridge.exe --console [--allow-write] [--call-timeout 60] [--dll <路径>]");
            sb.AppendLine();
            sb.AppendLine("内置命令：");
            sb.AppendLine("  help [命令]   查看全部命令 / 单条命令用法（也可用 ? 或 h）");
            sb.AppendLine("  open          连接数据库并记住库名（等同 ACBEL_DATABASE_OPEN）");
            sb.AppendLine("  db [库名]     查看当前库名；带参数则手动设置（供 @db 使用）");
            sb.AppendLine("  env           查看 DLL/运行环境（目录、FAC_CODE、是否允许写库）");
            sb.AppendLine("  ping          自检：x86、DLL 是否可加载、导出是否可解析");
            sb.AppendLine("  quit / exit   退出");
            sb.AppendLine();
            sb.AppendLine("约定：");
            sb.AppendLine("  * 命令名就是导出名（大小写不敏感），也支持 open/insert/basic/chkbar 等简写；");
            sb.AppendLine("  * 参数按顺序写文本，含空格的参数用双引号包起来，空参数写 \"\" 或 -；");
            sb.AppendLine("  * db 参数可以写 @db，自动用上一次 open 得到的库名；");
            sb.AppendLine("  * 标 ★ 的命令会写 MES 生产库，必须加 --allow-write 才会执行；");
            sb.AppendLine("  * 输出参数（如「错误信息(out)」「MODEL_PROGRAM(out)」）调用后会打印写回的内容。");
            sb.AppendLine();
            sb.AppendLine("连接（0 参数）：");
            AppendGroup(sb, IsConnection);
            sb.AppendLine();
            sb.AppendLine("只读 / 校验类：");
            AppendGroup(sb, f => !f.Writes && !IsConnection(f));
            sb.AppendLine();
            sb.AppendLine("写入类（需要 --allow-write）：");
            AppendGroup(sb, f => f.Writes);
            sb.AppendLine();
            sb.Append("当前状态：库名=" + (string.IsNullOrEmpty(Program.Database) ? "(未连接)" : Program.Database)
                      + "；允许写库=" + (Program.AllowWrite ? "是" : "否"));
            return sb.ToString();
        }

        private static bool IsConnection(AcbelFunction f)
        {
            return f.Name == "ACBEL_DATABASE_OPEN" || f.Name == "ACBEL_DATABASE_CLOSE";
        }

        private static void AppendGroup(StringBuilder sb, Func<AcbelFunction, bool> filter)
        {
            foreach (var f in DllFunctions.All)
            {
                if (!filter(f)) continue;

                sb.Append("  ").Append(f.Writes ? "★ " : "  ").Append(f.Name.PadRight(24));
                sb.Append(' ').Append(f.Args.Length == 0 ? "(无参数)" : "参数: " + string.Join(", ", f.Args));
                sb.AppendLine();
                sb.AppendLine("        " + f.Summary);
            }
        }

        public static string DescribeCommand(string name)
        {
            AcbelFunction fn = DllFunctions.Find(name);
            if (fn == null) return "没有这个命令：" + name + "（输入 help 查看全部命令）";

            var sb = new StringBuilder();
            sb.AppendLine("命令：" + fn.Name + (fn.Writes ? "   ★ 会写 MES 生产库（需 --allow-write）" : string.Empty));
            sb.AppendLine("说明：" + fn.Summary);
            sb.AppendLine("用法：" + Usage(fn));
            sb.AppendLine("参数：");
            if (fn.Args.Length == 0) sb.AppendLine("  (无)");
            for (int i = 0; i < fn.Args.Length; i++)
                sb.AppendLine("  " + (i + 1) + ") " + fn.Args[i]);
            sb.AppendLine("返回：" + fn.ResultHint);
            sb.AppendLine("示例：" + fn.Example);
            return sb.ToString().TrimEnd();
        }

        private static string Usage(AcbelFunction fn)
        {
            if (fn.Args.Length == 0) return fn.Name;
            var parts = new List<string>();
            foreach (string a in fn.Args) parts.Add(a.IndexOf(' ') >= 0 ? "\"" + a + "\"" : a);
            return fn.Name + " " + string.Join(" ", parts.ToArray());
        }

        // ---------------------------------------------------------------- 杂项
        private static string EnvironmentReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("DLL：" + Program.DllPath + "（存在=" + System.IO.File.Exists(Program.DllPath) + "）");
            sb.AppendLine("调用超时：" + (Program.CallTimeoutMs / 1000) + " 秒");
            sb.AppendLine("库名：" + (string.IsNullOrEmpty(Program.Database) ? "(未连接)" : Program.Database));
            sb.AppendLine("允许写库：" + (Program.AllowWrite ? "是" : "否（加 --allow-write）"));
            sb.AppendLine("D:\\AMZ_LOG：" + System.IO.Directory.Exists(@"D:\AMZ_LOG"));
            sb.AppendLine("D:\\TEST_DATA1：" + System.IO.Directory.Exists(@"D:\TEST_DATA1"));
            sb.AppendLine("FAC_CODE.txt：" + System.IO.File.Exists(@"D:\CHROMA\FAC_CODE.txt"));
            sb.Append("命令数：" + DllFunctions.All.Count);
            return sb.ToString();
        }

        private static void PrintBanner()
        {
            Console.WriteLine("AcbelBridge 控制台 — Acbel_Chroma_Test.dll（" + DllFunctions.All.Count + " 个导出）");
            Console.WriteLine("DLL：" + Program.DllPath);
            Console.WriteLine("允许写 MES：" + (Program.AllowWrite ? "是" : "否（写入类命令需 --allow-write）"));
            Console.WriteLine("输入 help 查看命令，help <命令> 看用法，quit 退出。");
            Console.WriteLine();
        }

        /// <summary>把一行拆成参数：支持双引号与单引号，引号内可含空格。</summary>
        public static List<string> Tokenize(string line)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(line)) return tokens;

            var current = new StringBuilder();
            bool inQuotes = false;
            char quoteChar = '"';

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == quoteChar)
                    {
                        inQuotes = false;
                        continue;
                    }
                    current.Append(c);
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inQuotes = true;
                    quoteChar = c;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Length = 0;
                    }
                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0) tokens.Add(current.ToString());
            return tokens;
        }
    }
}
