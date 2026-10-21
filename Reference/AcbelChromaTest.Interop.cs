// -----------------------------------------------------------------------------
//  Acbel_Chroma_Test.Interop.cs
//  Reference\Acbel_Chroma_Test.dll 的 P/Invoke 声明（32 位原生 Delphi DLL）
//
//  ⚠ 使用前提
//   1. 本 DLL 是 32 位原生 DLL：宿主进程必须是 x86，否则运行时抛
//      BadImageFormatException (0x8007000B)。64 位进程连 LoadLibrary 都会失败
//      (GetLastError = 193 ERROR_BAD_EXE_FORMAT)。
//   2. DLL 必须位于 exe 同级目录（见 Reference\Acbel_Chroma_Test-接入说明.md 第 4 节）。
//   3. 调用约定：除 ACBEL_DATABASE_OPEN/CLOSE、GET_ALL_INFO 为无栈参数外，
//      其余全部是 stdcall。
//   4. **返回值是 ANSI 字符串指针（PChar），不是 int**（厂商文档 鐳雕DLL說明.docx 明确写
//      `: PCHAR`，反汇编亦证实 EAX 指向 "1"/"0}B00x-…"/"database" 之类的常量串）→
//      声明为 IntPtr，用 AcbelChromaTest.ToText() 转成 string 再判断。
//   4. 参数"类型"是按二进制证据推断的（字符串参数为 ANSI char*），
//      未确认的参数统一声明为 IntPtr，验证后再改成 string/int。
//
//  完整分析见：Reference\Acbel_Chroma_Test-接入说明.md
// -----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace ShaoLu.Interop
{
    internal static class AcbelChromaTest
    {
        private const string Dll = "Acbel_Chroma_Test.dll";

        // stdcall + ANSI + 精确名称匹配，是本 DLL 的正确调用方式
        private const CallingConvention Conv = CallingConvention.StdCall;
        private const CharSet Chars = CharSet.Ansi;

        // =====================================================================
        // 1. 连接管理
        // =====================================================================

        /// <summary>连接 MES 数据库。无参数：连接信息取自 Chroma.ini / FAC_CODE.txt。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr ACBEL_DATABASE_OPEN();

        /// <summary>
        /// 关闭数据库连接。
        /// 注意：该导出与 ACBEL_DATABASE_OPEN 指向同一段代码（同一实现），
        /// 不保证真的释放连接。
        /// </summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr ACBEL_DATABASE_CLOSE();

        // =====================================================================
        // 2. 基础资料 / 只读查询（建议先用这些做联调验证）
        // =====================================================================

        /// <summary>
        /// 取测试程序名。SQL：SELECT MODEL_PROGRAM FROM apitech.sim547
        ///                      WHERE MODEL_NO=:MODEL_NO AND STATION=:STATION
        /// 参数顺序按 SQL 绑定名推断；第 3 个参数是否为输出缓冲区尚未确认。
        /// </summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr GET_TEST_PROGRAM(string modelNo, string station, IntPtr modelProgramOut);

        /// <summary>取规格上下限（SIM115_1/SIM115_2 的 DEPTH/ENERGY/… UCL、LCL）。入参为 MO_NO。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr CHKUCL_LCL(string moNo);

        /// <summary>模具/料号检查（SIM537_P01 / XTOOL_NO），入参为工装号。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr CHKML_NO(string toolNo);

        /// <summary>依据 MO_NO 校验料号；第 1 个参数含义未确认。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr CHECK_PART_NO(IntPtr unknown1, string moNo);

        /// <summary>读取全局信息（无栈参数，返回值经 EAX 传出，可能是字符串指针）。</summary>
        /// <remarks>
        /// 若返回的是 Delphi AnsiString（数据前 8 字节为引用计数/长度头），
        /// 直接 Marshal.PtrToStringAnsi 会读到错误的长度语义，需要先实测前 64 字节。
        /// </remarks>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr GET_ALL_INFO();

        // =====================================================================
        // 3. 条码相关
        // =====================================================================

        /// <summary>主条码检查（32 KB 的大函数：路由/前站 PASS/QC/时距等规则）。共 3 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr CHKBAR_CODE(IntPtr arg1, IntPtr arg2, string arg3);

        /// <summary>生成/写入条码组：INSERT INTO SIM117A(BARCODE1…BARCODE6)，
        /// 涉及 MO_NO / MODEL_NO / PART_NO / SCAN_SEQ / SN_*_RANGE_SEQ。共 5 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr GREATR_BAR_CODE(IntPtr arg1, string moNo, IntPtr arg3, IntPtr arg4, IntPtr arg5);

        /// <summary>DC 换条码（ls_amz_newpart / SIM117.PRE_FIXED_CD / SQ_BARCODE_NEW）。共 4 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr CHANGE_BARCODE_DC(IntPtr arg1, string arg2, string arg3, string arg4);

        // =====================================================================
        // 4. 站别 / 基础资料维护
        // =====================================================================

        /// <summary>基础资料校验：工单 SIM40、站别 SIM02、员工受训 SIM501、线别 SIM11。共 4 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr ACBEL_SET_BASIC_INFO(string arg1, string arg2, string arg3, string arg4);

        /// <summary>站别映射：GET_MO_MODEL_NO / SIM11 线别 / SIM02 站别 / SIM524 条码映射。共 7 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr ACBEL_MAPPING_STATION(
            IntPtr arg1, string moNo, IntPtr arg3, string lineName, string station, string internalSr, string period);

        /// <summary>Amazon(AMZ) 映射：HMM01_v1 在职校验、SIM53/SIM03 路由与 BARCODE_CTRL。共 8 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr ACBEL_AMZ_MAPPING(
            IntPtr arg1, IntPtr arg2, IntPtr arg3, IntPtr arg4, IntPtr arg5, IntPtr arg6, IntPtr arg7, IntPtr arg8);

        // =====================================================================
        // 5. 数据上传 / 入库（会写 MES 生产数据，验证阶段请勿盲调）
        // =====================================================================

        /// <summary>主力数据入库（29 KB 大函数：SIM11/SIM551/SIM53）。共 3 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr ACBEL_INSERT_DATA(IntPtr arg1, IntPtr arg2, IntPtr arg3);

        /// <summary>上传系统数据（MID_COMPOPAL 序列、LINE_NO/MO_NO/XSTATION/QUL_*）。共 7 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr ACBEL_SYS_DATA(
            IntPtr errMsg, string moNo, string station, string qulData, string qulStatus, string crtUser, string equidNo);

        /// <summary>上传功能测试值（MID_FUN_V 序列、QUL_data1/QUL_data2/QUL_*）。共 8 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr FUN_VALUE(
            IntPtr arg1, IntPtr arg2, IntPtr arg3, string qulData1, string qulData2,
            string qulStatus, string crtUser, string equidNo);

        /// <summary>上传设备数据（EQUIP_DATA_SPLIT / UL_DATA / UL_STATUS / EQUIP_NAME / CRT_USER / EQUID_NO）。共 5 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr ACBEL_UPLOAD_DATA(IntPtr arg1, IntPtr arg2, IntPtr arg3, IntPtr arg4, IntPtr arg5);

        /// <summary>ICT 数据入库（日志标识 ICT_DLL）。共 2 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr ICT_INSERT_DATA(IntPtr arg1, IntPtr arg2);

        /// <summary>AOI 数据入库（GET_MO_YN / SIM11 / SIM17 / SIM22）。共 3 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr AOI_INSERT_DATA(IntPtr arg1, IntPtr arg2, IntPtr arg3);

        /// <summary>AOI 上传（SIM11 SHIFT_CLASS/LINE_NO、SIM17 PERIOD、CUR_ROUTE_ID/INTERNAL_SR/TEST_RESULT/UPD_USER）。共 10 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr ACBEL_AOI_UP(
            IntPtr arg1, IntPtr arg2, IntPtr arg3, IntPtr arg4, IntPtr arg5,
            IntPtr arg6, IntPtr arg7, IntPtr arg8, IntPtr arg9, IntPtr arg10);

        // =====================================================================
        // 6. SMT 实验室（ACBEL_SMT_LAB_*；参数名为 SQL 绑定变量名推断）
        // =====================================================================

        /// <summary>ACBEL_SMT_LAB_CHECK(XMO, XNUM, XEMP, XSTA, XSEC) + 错误信息，共 6 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr ACBEL_SMT_LAB_CHK(
            IntPtr errMsg, string moNo, string num, string empNo, string station, string sec);

        /// <summary>ACBEL_SMT_LAB_INSERT(XMO, XBAR_A, XBAR_NEW, XLINE, XEMP, XSTA, XSEC) + 错误信息，共 8 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr ACBEL_SMT_LAB_INS(
            IntPtr errMsg, string moNo, string barA, string barNew, string line, string empNo, string station, string sec);

        /// <summary>ACBEL_SMT_LAB_LINK(A_SN, XBAR_B, XSTA, XSEC) + 错误信息，共 5 个参数。</summary>
        [DllImport(Dll, CallingConvention = Conv, CharSet = Chars, ExactSpelling = true)]
        public static extern IntPtr ACBEL_SMT_LAB_LK(
            IntPtr errMsg, string aSn, string barB, string station, string sec);

        // =====================================================================
        // 低层兜底：参数个数必须与上表一致，调用约定必须为 stdcall，
        // 否则被调用方清栈的字节数不匹配，会直接破坏栈。
        // =====================================================================
        // =====================================================================
        // 返回值处理：C 侧返回 ANSI 字符串指针（PChar）
        //   成功：ACBEL_DATABASE_OPEN -> "database"；其余多为 "1"
        //         CHANGE_BARCODE_DC  -> 直接返回新条码
        //   失败："0}XXXX-…"（业务卡控）/"2}調用函數…"（内部异常，需重试）
        // =====================================================================

        /// <summary>把返回的 ANSI 字符串指针转成 string（空指针返回 null）。</summary>
        public static string ToText(IntPtr p)
        {
            return p == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(p);
        }

        /// <summary>按厂商文档口径判断成功：ACBEL_DATABASE_OPEN 返回 "database"，其余返回 "1"。</summary>
        public static bool IsOk(IntPtr p)
        {
            string s = ToText(p);
            return s == "1" || string.Equals(s, "database", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>是否为厂商定义的异常（"2}…"），文档要求此时重新调用一次。</summary>
        public static bool IsRetryNeeded(IntPtr p)
        {
            string s = ToText(p);
            return s != null && s.StartsWith("2}", StringComparison.Ordinal);
        }

        /// <summary>业务卡控信息（"0}…"），首段为异常代码，例如 0}B006-線別錯誤。</summary>
        public static bool IsBusinessFail(IntPtr p)
        {
            string s = ToText(p);
            return s != null && s.StartsWith("0", StringComparison.Ordinal);
        }

        private static void EnsureX86()
        {
            if (Environment.Is64BitProcess)
                throw new PlatformNotSupportedException(
                    "Acbel_Chroma_Test.dll 是 32 位原生 DLL，必须运行在 x86 进程中。");
        }
    }
}
