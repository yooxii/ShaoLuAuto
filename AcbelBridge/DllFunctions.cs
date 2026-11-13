// -----------------------------------------------------------------------------
//  DllFunctions —— Acbel_Chroma_Test.dll 全部 23 个导出的命令目录
//
//  控制台命令集用：每个导出对应一条命令，参数按厂商文档的顺序逐个给出。
//  参数一律用 IntPtr（PAnsiChar 语义）传入 8 KB 缓冲区：
//      * 输入参数：把文本写进缓冲区开头；
//      * 输出参数（例如“错误信息”“MODEL_PROGRAM”“条码”）：DLL 写回来的内容调用后可读回；
//  因此调用结束会附带打印「被写回的参数」，方便现场试函数。
//
//  注意：参数个数与顺序来自 Reference\Acbel_Chroma_Test-接入说明.md 的实测表 + 厂商文档
//  Reference\镭雕DLL說明-文本.md；少数函数厂商未给出参数名，这里标注为 argN 并在 help 里说明推断。
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AcbelBridge
{
    /// <summary>一个可调用导出的元数据。</summary>
    internal sealed class AcbelFunction
    {
        /// <summary>导出名（也是控制台命令名，大小写不敏感）。</summary>
        public string Name;
        /// <summary>一句话说明。</summary>
        public string Summary;
        /// <summary>参数名（按顺序，个数与 DLL 一致）。</summary>
        public string[] Args;
        /// <summary>是否会写 MES 生产库（需要 --allow-write）。</summary>
        public bool Writes;
        /// <summary>返回值的含义提示。</summary>
        public string ResultHint;
        /// <summary>示例（控制台里可直接改着用）。</summary>
        public string Example;
        /// <summary>按参数缓冲区调用 DLL。</summary>
        public Func<IntPtr[], IntPtr> Invoke;
    }

    /// <summary>DLL 导出清单 + 参数缓冲区调用封装。</summary>
    internal static class DllFunctions
    {
        /// <summary>每个参数的缓冲区大小（字节）。输出参数超过这个长度会被截断。</summary>
        public const int BufferSize = 8192;

        // ---------------------------------------------------------------- 导出声明
        // 全部是 stdcall + ANSI + 精确名称；参数一律 PAnsiChar（IntPtr 缓冲区）。
        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ACBEL_DATABASE_OPEN();

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ACBEL_DATABASE_CLOSE();

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ACBEL_SET_BASIC_INFO(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr CHKBAR_CODE(IntPtr a1, IntPtr a2, IntPtr a3);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GET_ALL_INFO();

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GREATR_BAR_CODE(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr a5);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ACBEL_MAPPING_STATION(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr a5, IntPtr a6, IntPtr a7);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr CHECK_PART_NO(IntPtr a1, IntPtr a2);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr CHKUCL_LCL(IntPtr a1);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr CHKML_NO(IntPtr a1);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ACBEL_SYS_DATA(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr a5, IntPtr a6, IntPtr a7);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ACBEL_INSERT_DATA(IntPtr a1, IntPtr a2, IntPtr a3);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr CHANGE_BARCODE_DC(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr AOI_INSERT_DATA(IntPtr a1, IntPtr a2, IntPtr a3);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ACBEL_AMZ_MAPPING(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr a5, IntPtr a6, IntPtr a7, IntPtr a8);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ACBEL_UPLOAD_DATA(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr a5);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ICT_INSERT_DATA(IntPtr a1, IntPtr a2);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr FUN_VALUE(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr a5, IntPtr a6, IntPtr a7, IntPtr a8);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ACBEL_SMT_LAB_CHK(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr a5, IntPtr a6);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ACBEL_SMT_LAB_INS(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr a5, IntPtr a6, IntPtr a7, IntPtr a8);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ACBEL_SMT_LAB_LK(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr a5);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr ACBEL_AOI_UP(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr a5, IntPtr a6, IntPtr a7, IntPtr a8, IntPtr a9, IntPtr a10);

        [DllImport("Acbel_Chroma_Test.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GET_TEST_PROGRAM(IntPtr a1, IntPtr a2, IntPtr a3);

        // ---------------------------------------------------------------- 清单
        /// <summary>全部 23 个导出（按「连接 / 只读 / 写入」分组排列，供 help 使用）。</summary>
        public static readonly List<AcbelFunction> All = new List<AcbelFunction>
        {
            new AcbelFunction
            {
                Name = "ACBEL_DATABASE_OPEN",
                Summary = "连接 MES 数据库（读 D:\\CHROMA\\FAC_CODE.txt 选 BDE 别名）；成功后记住库名",
                Args = new string[0],
                ResultHint = "成功返回库名（通常 database）；失败 0}數據庫無法連接 / 0}連接DB Connection ERROR",
                Example = "open",
                Invoke = b => ACBEL_DATABASE_OPEN(),
            },
            new AcbelFunction
            {
                Name = "ACBEL_DATABASE_CLOSE",
                Summary = "与 OPEN 是同一段实现（调用它等于重新连接一次，不会真正断开）",
                Args = new string[0],
                ResultHint = "同 OPEN",
                Example = "close",
                Invoke = b => ACBEL_DATABASE_CLOSE(),
            },
            new AcbelFunction
            {
                Name = "ACBEL_SET_BASIC_INFO",
                Summary = "设置基础资料（工单/站别工号/线别）；ACBEL_INSERT_DATA 之前必须先调它（四项只做校验，不写库）",
                Args = new[] { "db", "工单号(工令)", "站别+工号", "线别" },
                ResultHint = "成功 1；失败 0}B001~B006（B006=線別錯誤）；2} 需重调一次",
                Example = "set_basic_info @db HE47H1G001H18163 16521807605 H18",
                Invoke = b => ACBEL_SET_BASIC_INFO(b[0], b[1], b[2], b[3]),
            },
            new AcbelFunction
            {
                Name = "CHKBAR_CODE",
                Summary = "主条码检查（前站 PASS、站间时距、SAMPLE 等规则）",
                Args = new[] { "db", "站别+工号", "条码" },
                ResultHint = "成功 1；失败 0}C0xx-…；2} 需重调一次",
                Example = "chkbar_code @db 16521807605 O4769455",
                Invoke = b => CHKBAR_CODE(b[0], b[1], b[2]),
            },
            new AcbelFunction
            {
                Name = "GET_ALL_INFO",
                Summary = "无参，返回 DLL 内部全局信息字符串",
                Args = new string[0],
                ResultHint = "返回字符串（内容由 DLL 决定）",
                Example = "get_all_info",
                Invoke = b => GET_ALL_INFO(),
            },
            new AcbelFunction
            {
                Name = "GREATR_BAR_CODE",
                Summary = "生成/写入条码组（INSERT INTO SIM117A）★写生产库",
                Args = new[] { "arg1", "arg2", "arg3", "arg4", "arg5" },
                Writes = true,
                ResultHint = "成功 1；失败 0}…；参数顺序厂商未给全，见 help 说明",
                Example = "greatr_bar_code HE47H1G001H18163 PART_NO 1 \"\" \"\"",
                Invoke = b => GREATR_BAR_CODE(b[0], b[1], b[2], b[3], b[4]),
            },
            new AcbelFunction
            {
                Name = "ACBEL_MAPPING_STATION",
                Summary = "站别映射（SIM524 换条码等）★写生产库",
                Args = new[] { "arg1", "arg2", "arg3", "arg4", "arg5", "arg6", "arg7" },
                Writes = true,
                ResultHint = "成功 1；失败 0}…",
                Example = "mapping_station ...（7 个参数，见 help）",
                Invoke = b => ACBEL_MAPPING_STATION(b[0], b[1], b[2], b[3], b[4], b[5], b[6]),
            },
            new AcbelFunction
            {
                Name = "CHECK_PART_NO",
                Summary = "按工单校验料号（SIM40/SIM43）",
                Args = new[] { "MO_NO", "arg2" },
                ResultHint = "成功 1；失败 0}…",
                Example = "check_part_no HE47H1G001H18163 \"\"",
                Invoke = b => CHECK_PART_NO(b[0], b[1]),
            },
            new AcbelFunction
            {
                Name = "CHKUCL_LCL",
                Summary = "取规格上下限（SIM115_2/SIM115_1）",
                Args = new[] { "MO_NO" },
                ResultHint = "成功返回 1；失败 0}…",
                Example = "chkucl_lcl HE47H1G001H18163",
                Invoke = b => CHKUCL_LCL(b[0]),
            },
            new AcbelFunction
            {
                Name = "CHKML_NO",
                Summary = "模具/料号检查（调用 SIM537_P01，写 D:\\TEST_DATA1\\LOG1.txt）",
                Args = new[] { "XTOOL_NO" },
                ResultHint = "成功 1；失败 0}…",
                Example = "chkml_no TOOL",
                Invoke = b => CHKML_NO(b[0]),
            },
            new AcbelFunction
            {
                Name = "ACBEL_SYS_DATA",
                Summary = "上传系统数据（LINE_NO/MO_NO/XSTATION/QUL_*）",
                Args = new[] { "错误信息(out)", "MO_NO", "XSTATION", "QUL_data", "QUL_status", "Qcrt_user", "QEQUID_NO" },
                Writes = true,
                ResultHint = "成功 1；失败 0}…；2} 需重调一次",
                Example = "sys_data \"\" HE47H1G001H18163 165 ...",
                Invoke = b => ACBEL_SYS_DATA(b[0], b[1], b[2], b[3], b[4], b[5], b[6]),
            },
            new AcbelFunction
            {
                Name = "ACBEL_INSERT_DATA",
                Summary = "主力数据入库（27 段上传资料 → SIM53）★写生产库",
                Args = new[] { "db", "上传资料(27 段)", "镭雕程式文件名" },
                Writes = true,
                ResultHint = "成功 1；业务卡控 0}…；内部异常 2}（重试一次）",
                Example = "insert @db \"PASS,,,O4769455,...\" LASER_1.PRG",
                Invoke = b => ACBEL_INSERT_DATA(b[0], b[1], b[2]),
            },
            new AcbelFunction
            {
                Name = "CHANGE_BARCODE_DC",
                Summary = "DC 换新条码（返回新条码；日志写 D:\\LOG.txt）",
                Args = new[] { "db", "OLD_BAR_CODE", "MO_NO", "STATION" },
                Writes = true,
                ResultHint = "成功返回新条码；失败 2}…；周期码错误等 0}…",
                Example = "change_barcode_dc @db OLD123 HE47H1G001H18163 165",
                Invoke = b => CHANGE_BARCODE_DC(b[0], b[1], b[2], b[3]),
            },
            new AcbelFunction
            {
                Name = "AOI_INSERT_DATA",
                Summary = "AOI 数据入库",
                Args = new[] { "arg1", "arg2", "arg3" },
                Writes = true,
                ResultHint = "成功 1；失败 0}…",
                Example = "aoi_insert ...（3 个参数）",
                Invoke = b => AOI_INSERT_DATA(b[0], b[1], b[2]),
            },
            new AcbelFunction
            {
                Name = "ACBEL_AMZ_MAPPING",
                Summary = "Amazon(AMZ) 映射（员工在职校验、路由、BARCODE_CTRL）",
                Args = new[] { "arg1", "arg2", "arg3", "arg4", "arg5", "arg6", "arg7", "arg8" },
                Writes = true,
                ResultHint = "成功 1；失败 0}…；2} 需重调一次",
                Example = "amz_mapping ...（8 个参数）",
                Invoke = b => ACBEL_AMZ_MAPPING(b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7]),
            },
            new AcbelFunction
            {
                Name = "ACBEL_UPLOAD_DATA",
                Summary = "上传设备数据（EQUIP_DATA_SPLIT / UL_DATA / UL_STATUS …）",
                Args = new[] { "arg1", "arg2", "arg3", "arg4", "arg5" },
                Writes = true,
                ResultHint = "成功 1；失败 0}…",
                Example = "upload_data ...（5 个参数）",
                Invoke = b => ACBEL_UPLOAD_DATA(b[0], b[1], b[2], b[3], b[4]),
            },
            new AcbelFunction
            {
                Name = "ICT_INSERT_DATA",
                Summary = "ICT 数据入库（日志 ICT_DLL）",
                Args = new[] { "arg1", "arg2" },
                Writes = true,
                ResultHint = "成功 1；失败 0}…",
                Example = "ict_insert ...（2 个参数）",
                Invoke = b => ICT_INSERT_DATA(b[0], b[1]),
            },
            new AcbelFunction
            {
                Name = "FUN_VALUE",
                Summary = "功能测试值上传（MID_FUN_V.NEXTVAL）",
                Args = new[] { "arg1", "arg2", "arg3", "QUL_data1", "QUL_data2", "QUL_status", "Qcrt_user", "QEQUID_NO" },
                Writes = true,
                ResultHint = "成功 1；失败 0}…",
                Example = "fun_value ...（8 个参数）",
                Invoke = b => FUN_VALUE(b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7]),
            },
            new AcbelFunction
            {
                Name = "ACBEL_SMT_LAB_CHK",
                Summary = "SMT 实验室检查（ACBEL_SMT_LAB_CHECK）",
                Args = new[] { "错误信息(out)", "XMO", "XNUM", "XEMP", "XSTA", "XSEC" },
                ResultHint = "成功 1；失败 0}…（错误信息参数里可能有详情）",
                Example = "smt_lab_chk \"\" XMO 1 21104689 060 1",
                Invoke = b => ACBEL_SMT_LAB_CHK(b[0], b[1], b[2], b[3], b[4], b[5]),
            },
            new AcbelFunction
            {
                Name = "ACBEL_SMT_LAB_INS",
                Summary = "SMT 实验室登录（ACBEL_SMT_LAB_INSERT）",
                Args = new[] { "错误信息(out)", "XMO", "XBAR_A", "XBAR_NEW", "XLINE", "XEMP", "XSTA", "XSEC" },
                Writes = true,
                ResultHint = "成功 1；失败 0}…（错误信息参数里可能有详情）",
                Example = "smt_lab_ins \"\" XMO BAR_A BAR_NEW H18 21104689 060 1",
                Invoke = b => ACBEL_SMT_LAB_INS(b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7]),
            },
            new AcbelFunction
            {
                Name = "ACBEL_SMT_LAB_LK",
                Summary = "SMT 实验室关联（ACBEL_SMT_LAB_LINK）",
                Args = new[] { "错误信息(out)", "A_SN", "XBAR_B", "XSTA", "XSEC" },
                Writes = true,
                ResultHint = "成功 1；失败 0}…",
                Example = "smt_lab_lk \"\" A_SN BAR_B 060 1",
                Invoke = b => ACBEL_SMT_LAB_LK(b[0], b[1], b[2], b[3], b[4]),
            },
            new AcbelFunction
            {
                Name = "ACBEL_AOI_UP",
                Summary = "AOI 上传（SIM11/SIM17/CUR_ROUTE_ID/INTERNAL_SR …）",
                Args = new[] { "arg1", "arg2", "arg3", "arg4", "arg5", "arg6", "arg7", "arg8", "arg9", "arg10" },
                Writes = true,
                ResultHint = "成功 1；失败 0}…；2} 需重调一次",
                Example = "aoi_up ...（10 个参数）",
                Invoke = b => ACBEL_AOI_UP(b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7], b[8], b[9]),
            },
            new AcbelFunction
            {
                Name = "GET_TEST_PROGRAM",
                Summary = "按机种+站别取测试程序（SIM547）",
                Args = new[] { "MODEL_NO", "STATION", "MODEL_PROGRAM(out)" },
                ResultHint = "成功 1（程序名写回 arg3）；失败 0}…",
                Example = "get_program HE47H1G001H18163 165 \"\"",
                Invoke = b => GET_TEST_PROGRAM(b[0], b[1], b[2]),
            },
        };

        /// <summary>按导出名或别名查命令（大小写不敏感）。</summary>
        public static AcbelFunction Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string key = name.Trim();
            foreach (var f in All)
            {
                if (string.Equals(f.Name, key, StringComparison.OrdinalIgnoreCase)) return f;
            }
            string alias = ResolveAlias(key);
            if (alias == null) return null;
            foreach (var f in All)
            {
                if (string.Equals(f.Name, alias, StringComparison.OrdinalIgnoreCase)) return f;
            }
            return null;
        }

        /// <summary>常用简写 → 导出名。</summary>
        public static string ResolveAlias(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "open": return "ACBEL_DATABASE_OPEN";
                case "close": return "ACBEL_DATABASE_CLOSE";
                case "basic":
                case "set_basic_info": return "ACBEL_SET_BASIC_INFO";
                case "chkbar":
                case "chkbar_code": return "CHKBAR_CODE";
                case "get_info": return "GET_ALL_INFO";
                case "greatr_barcode": return "GREATR_BAR_CODE";
                case "mapping_station": return "ACBEL_MAPPING_STATION";
                case "check_part_no": return "CHECK_PART_NO";
                case "chkucl":
                case "chkucl_lcl": return "CHKUCL_LCL";
                case "chkml":
                case "chkml_no": return "CHKML_NO";
                case "sys_data": return "ACBEL_SYS_DATA";
                case "insert": return "ACBEL_INSERT_DATA";
                case "change_barcode":
                case "change_barcode_dc": return "CHANGE_BARCODE_DC";
                case "aoi_insert": return "AOI_INSERT_DATA";
                case "amz_mapping": return "ACBEL_AMZ_MAPPING";
                case "upload_data": return "ACBEL_UPLOAD_DATA";
                case "ict_insert": return "ICT_INSERT_DATA";
                case "fun_value": return "FUN_VALUE";
                case "smt_lab_chk": return "ACBEL_SMT_LAB_CHK";
                case "smt_lab_ins": return "ACBEL_SMT_LAB_INS";
                case "smt_lab_lk": return "ACBEL_SMT_LAB_LK";
                case "aoi_up": return "ACBEL_AOI_UP";
                case "get_program": return "GET_TEST_PROGRAM";
                default: return null;
            }
        }

        /// <summary>
        /// 用参数缓冲区调用一个导出并返回 DLL 的字符串返回值。
        /// changedArgs 输出被 DLL 改写过的参数（输出参数），供调用方展示。
        /// </summary>
        public static string Invoke(AcbelFunction fn, string[] args, out List<string> changedArgs)
        {
            changedArgs = new List<string>();
            int n = fn.Args.Length;
            IntPtr[] buffers = new IntPtr[n];
            string[] before = new string[n];
            try
            {
                for (int i = 0; i < n; i++)
                {
                    buffers[i] = Marshal.AllocHGlobal(BufferSize);
                    string value = i < args.Length ? (args[i] ?? string.Empty) : string.Empty;
                    before[i] = value;
                    WriteBuffer(buffers[i], value);
                }

                IntPtr ret = Program.RunGuardedPtr(() => fn.Invoke(buffers));

                for (int i = 0; i < n; i++)
                {
                    string after = Marshal.PtrToStringAnsi(buffers[i]) ?? string.Empty;
                    if (!string.Equals(after, before[i], StringComparison.Ordinal))
                        changedArgs.Add(fn.Args[i] + "=" + after);
                }

                return ret == IntPtr.Zero ? string.Empty : (Marshal.PtrToStringAnsi(ret) ?? string.Empty);
            }
            finally
            {
                for (int i = 0; i < buffers.Length; i++)
                {
                    if (buffers[i] != IntPtr.Zero) Marshal.FreeHGlobal(buffers[i]);
                }
            }
        }

        private static void WriteBuffer(IntPtr buffer, string text)
        {
            byte[] zeros = new byte[BufferSize];
            Marshal.Copy(zeros, 0, buffer, BufferSize);
            if (string.IsNullOrEmpty(text)) return;

            byte[] bytes = Encoding.Default.GetBytes(text);
            int length = Math.Min(bytes.Length, BufferSize - 1);
            Marshal.Copy(bytes, 0, buffer, length);
        }
    }
}
