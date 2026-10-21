# Acbel_Chroma_Test.dll 接入说明

> 结论基于对 `Reference\Acbel_Chroma_Test.dll` 的静态分析（PE 头 / 导出表 / 反汇编）与本机实测（64 位、32 位、.NET Framework 4.8 三种宿主）。
> 分析环境：`dumpbin`（VS 18 Community，`D:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231\bin\Hostx64\x86\dumpbin.exe`）、32 位 Python 3.13、MSBuild 18。
> 复现脚本留在 `.analysis\`（未纳入版本库，可随时删除）。

---

## 1. 一句话结论

**它不是 .NET 程序集，而是一个 32 位原生（Win32 / Delphi VCL）DLL。**

因此：

1. 在 VS 里「添加引用」是加不进去的（会报“不是有效的程序集或 COM 组件”）；只能按 **P/Invoke（DllImport）** 使用。
2. **当前 ShaoLu 工程是纯 x64，64 位进程根本无法加载它**（实测 `LoadLibraryA` 返回 0，`GetLastError = 193 ERROR_BAD_EXE_FORMAT`）。
   所以「怎么加进工程」之前，必须先决定位数方案（见第 3 节），否则加进去也只能 `BadImageFormatException`。
3. 它导出 **23 个函数**，调用约定绝大多数是 `stdcall`，返回值放在 `EAX`（Delphi `Integer`），字符串参数是 **ANSI（PAnsiChar）**。

---

## 2. 已核实的二进制事实（含实测记录）

| 项目 | 结果 |
|------|------|
| 文件类型 | PE32（32 位），`Machine = 0x014C` |
| 是否有 CLR 头 | 无（不是 .NET 程序集） |
| 编译器 | Borland/Embarcadero Delphi（RTL 字符串、`Software\Borland\Delphi\RTL`、VCL 窗体资源） |
| 导出 | 23 个具名导出，`DllName = Acbel_Chroma_Test.dll`，ImageBase `0x400000` |
| 静态导入 | 仅 kernel32/user32/advapi32/gdi32/ole32/oleaut32/comctl32 —— **没有任何 Oracle/ODBC/BDE 客户端**（数据库客户端是运行时 `LoadLibraryA` 动态加载的） |
| 资源 | 含 VCL 窗体（DFM）与字符串表；`MessageBoxA` 被导入 → **运行中可能弹窗** |

实测记录：

```text
# 1) 64 位宿主加载 → 失败
PowerShell(x64) LoadLibraryA("Acbel_Chroma_Test.dll") = 0，GetLastError = 193 (ERROR_BAD_EXE_FORMAT)

# 2) 32 位宿主加载 → 成功，导出可解析
Python(x86) ctypes.WinDLL → handle=0x400000，ACBEL_DATABASE_OPEN@0x47A9A0 …

# 3) .NET Framework 4.8 + x86（与 ShaoLu 同运行时）→ 成功
BindProbe.exe: process bitness: x86
               LoadLibraryA = 0x51F0000，resolved 23/23 exports
```

---

## 3. 三条路线（先选这个，再谈代码）

### 路线 A：x86 桥接进程（推荐）

主程序保持 x64 不动；另建一个 **x86** 小程序/服务，由它独占加载并调用该 DLL，两边用
stdin/stdout、命名管道或本地 socket 通信。

- 优点：不动现有 x64 构建与发布链路；DLL 崩溃/弹窗不会拖垮主程序；最容易回退。
- 缺点：多一个进程与一层协议；需要自己定义请求/响应格式。
- 适用：调用频率不高（开机初始化、单板测试前后各调几次）。

### 路线 B：整个程序改成 x86

把 `ShaoLu.csproj` 与 `NUnitTest.csproj` 的 `PlatformTarget` 全部改成 `x86`，解决方案平台映射同步改。

- **前提**：当前 `OpenCvSharp4.runtime.win.slim 4.13.0.20260627` **只带 `runtimes/win-x64/native/OpenCvSharpExtern.dll`，没有 x86**。
  改 x86 后图像识别会在运行时抛 `DllNotFoundException: OpenCvSharpExtern`，必须换成带 win-x86 原生库的运行时包
  （如完整的 `OpenCvSharp4.runtime.win`）或自备 x86 版 `OpenCvSharpExtern.dll`。
- 已确认 x86 可用的现有原生依赖：`bin\Debug\x86\SQLite.Interop.dll`、`tesseract55.dll`、`leptonica-1.85.0.dll`、WebView2、Tesseract 均齐备。
- 优点：进程内直调，最简单直接。缺点：等于重做一次位数切换与回归测试。

### 路线 C：向厂商（Chroma / Acbel TE）索取 x64 版本或官方头文件

拿到 `Acbel_Chroma_Test.h/.pas` + 文档是**最省事也最安全**的，第 4 节的参数类型就不必靠猜。

> 建议：**先按路线 A 落地**（能立刻验证 DLL 在你站机上是否可用），同时向厂商开口要文档/64 位版本。

---

## 4. 工程接入的写法（实测过）

### 4.1 把 DLL 复制到输出目录（推荐写法）

在 `ShaoLu\ShaoLu.csproj` 的 `<ItemGroup>`（建议加在第 549 行附近那个 `<ItemGroup>` 里）加：

```xml
<ItemGroup>
  <!-- 32 位原生 DLL：只作为内容文件复制到输出目录，不做程序集引用 -->
  <None Include="..\Reference\Acbel_Chroma_Test.dll">
    <Link>Acbel_Chroma_Test.dll</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

**不要**写成 `<Reference Include="Acbel_Chroma_Test"><HintPath>…</HintPath></Reference>`。
实测（`.analysis\refprobe`）：`<Reference>` 虽然也能把文件复制到输出目录，但每次生成都会报

```text
warning MSB3246: 解析的文件包含错误图像、无元数据或不可访问。
                未能加载文件或程序集"Acbel_Chroma_Test.dll"…该模块应包含一个程序集清单。
```

同理，DLL 应放在 exe 同级目录，`DllImport` 用文件名即可；否则要写绝对路径。

### 4.2 路线 A 的桥接工程骨架（x86 控制台）

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
  <PlatformTarget>x86</PlatformTarget>   <!-- 关键 -->
  <Prefer32Bit>true</Prefer32Bit>
</PropertyGroup>
<ItemGroup>
  <None Include="..\Reference\Acbel_Chroma_Test.dll">
    <Link>Acbel_Chroma_Test.dll</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

### 4.3 路线 B 的改动点

- `ShaoLu.csproj`：`Debug|AnyCPU`、`Release|AnyCPU`、`Debug|x64`、`Release|x64` 四个配置里的 `<PlatformTarget>x64</PlatformTarget>` → `x86`。
- `NUnitTest.csproj`：两处 `<PlatformTarget>x64</PlatformTarget>` → `x86`。
- `ShaoLu.slnx`：`<Platform Name="x64" />` 与 `<Platform Solution="*|x64" Project="x64" />` 需要相应调整（或直接用 Any CPU）。
- 换掉 OpenCvSharp 的运行时包（见路线 B 的前提）。

---

## 5. 导出函数清单（调用约定与参数已实测）

- `args` 列的字节数来自函数结尾的 `ret imm16`（stdcall 自清栈字节数），**并已用 `[ebp+8]…` 参数读取交叉验证一致**。
- 返回类型：~~所有函数结尾都是 `mov eax, ebx`（或 `[ebp-4]`）→ 返回 32 位整数~~ → **更正（见第 10 节）：返回值是 ANSI 字符串指针（PChar）**，厂商文档写 `: PCHAR`，EAX 指向 `"1"` / `"0}B00x-…"` / `"database"` 这类常量串。
- 「用途」由函数内嵌的 SQL、绑定变量名与日志路径推断，非厂商文档。

| 序数 | 函数 | RVA | 调用约定 | 栈参数 | 推断用途 |
|---:|------|---:|------|---:|------|
| 22 | `ACBEL_DATABASE_OPEN` | 0x7A9A0 | 无栈参数（`ret`） | 0 | 读 `C:\Program Files\Chroma\PowerPro\Chroma.ini`、`D:\CHROMA\FAC_CODE.txt` 组装连接串（`USER NAME=APITECH`…）并连接 MES 库 |
| 23 | `ACBEL_DATABASE_CLOSE` | 0x7A9A0 | 同上 | 0 | **与 OPEN 指向同一段代码（同一实现）**，调用它等于再调一次 OPEN |
| 21 | `ACBEL_SET_BASIC_INFO` | 0x7AE6C | stdcall | 4 | 基础资料校验：工单 `SIM40`、站别 `SIM02`、员工受训 `SIM501`、线别 `SIM11` |
| 20 | `CHKBAR_CODE` | 0x7C744 | stdcall | 3 | 主条码检查（32 KB 大函数）：`SIM53/SIM03/SIM125_QC/SIM203`、前站 PASS 检查、站间时距等 |
| 19 | `GET_ALL_INFO` | 0x8C768 | 无栈参数（`ret`） | 0 | 无参，读全局对象字段（`+0x308`）经 `EAX` 返回（很可能是字符串） |
| 17 | `GREATR_BAR_CODE` | 0x8DB84 | stdcall | 5 | 生成/写入条码组：`INSERT INTO SIM117A(BARCODE1…6)`，涉及 `MO_NO/MODEL_NO/PART_NO/SCAN_SEQ/SN_*_RANGE_SEQ` |
| 16 | `ACBEL_MAPPING_STATION` | 0x907AC | stdcall | 7 | 站别映射：`GET_MO_MODEL_NO`、`SIM11` 线别、`SIM02` 站别、`SIM524(barcode_old/barcode_new)` |
| 15 | `CHECK_PART_NO` | 0x92504 | stdcall | 2 | 依据 `MO_NO` 校验料号（`PART_NO/MODEL_NO`） |
| 14 | `CHKUCL_LCL` | 0x927DC | stdcall | 1 | 取规格上下限：`SIM115_2/SIM115_1` 的 `DEPTH/ENERGY/SOUNDWAVE_TIME/PRESSURE/RIVETING` 的 `UCL/LCL`，入参为 `MO_NO` |
| 13 | `CHKML_NO` | 0x92D6C | stdcall | 1 | 模具/料号检查（`SIM537_P01`、`XTOOL_NO`、`XERROR_MSG`，写 `D:\TEST_DATA1\LOG1.txt`） |
| 11 | `ACBEL_SYS_DATA` | 0x93038 | stdcall | 7 | 上传系统数据：`MID_COMPOPAL.NEXTVAL`、`LINE_NO/MO_NO/XSTATION/QUL_data/QUL_status/Qcrt_user/QEQUID_NO` |
| 12 | `ACBEL_INSERT_DATA` | 0x937CC | stdcall | 3 | 主力数据入库（29 KB 大函数）：`SIM11/SIM551/SIM53`，涉及 `boardType/line_name/period` |
| 10 | `CHANGE_BARCODE_DC` | 0x9E474 | stdcall | 4 | DC 换条码：`ls_amz_newpart`、`SIM117.PRE_FIXED_CD`、`SQ_BARCODE_NEW`、`YIWD/XTIME` |
| 9 | `AOI_INSERT_DATA` | 0xA1630 | stdcall | 3 | AOI 数据入库：`GET_MO_YN`、`SIM11(XLINE_NAME)`、`SIM17(PERIOD)`、`SIM22` 计数 |
| 8 | `ACBEL_AMZ_MAPPING` | 0xA29A4 | stdcall | 8 | Amazon(AMZ) 映射：`HMM01_v1` 员工在职校验、`SIM53/SIM03` 路由与 `BARCODE_CTRL` |
| 7 | `ACBEL_UPLOAD_DATA` | 0xAAD70 | stdcall | 5 | 上传设备数据：`EQUIP_DATA_SPLIT`、`UL_DATA/UL_STATUS/EQUIP_NAME/CRT_USER/EQUID_NO` |
| 6 | `ICT_INSERT_DATA` | 0xAB448 | stdcall | 2 | ICT 数据入库（日志 `ICT_DLL`） |
| 5 | `FUN_VALUE` | 0xABD40 | stdcall | 8 | 功能测试值上传：`MID_FUN_V.NEXTVAL`、`QUL_data1/QUL_data2/QUL_status/Qcrt_user/QEQUID_NO` |
| 4 | `ACBEL_SMT_LAB_CHK` | 0xAD240 | stdcall | 6 | SMT 实验室检查：`ACBEL_SMT_LAB_CHECK(XMO, XNUM, XEMP, XSTA, XSEC)` + 错误信息 |
| 3 | `ACBEL_SMT_LAB_INS` | 0xAD4C4 | stdcall | 8 | SMT 实验室登录：`ACBEL_SMT_LAB_INSERT(XMO, XBAR_A, XBAR_NEW, XLINE, XEMP, XSTA, XSEC)` + 错误信息 |
| 2 | `ACBEL_SMT_LAB_LK` | 0xAD8CC | stdcall | 5 | SMT 实验室关联：`ACBEL_SMT_LAB_LINK(A_SN, XBAR_B, XSTA, XSEC)` + 错误信息 |
| 1 | `ACBEL_AOI_UP` | 0xADDD4 | stdcall | 10 | AOI 上传：`SIM11(SHIFT_CLASS/LINE_NO)`、`SIM17(PERIOD)`、`CUR_ROUTE_ID/INTERNAL_SR/TEST_RESULT/UPD_USER` |
| 18 | `GET_TEST_PROGRAM` | 0xAEC40 | stdcall | 3 | 取测试程序：`SELECT MODEL_PROGRAM FROM apitech.sim547 WHERE MODEL_NO=:MODEL_NO AND STATION=:STATION` |

---

## 6. 参数类型：能确定的与不能确定的

**能确定（有反汇编证据）**

| 结论 | 证据 |
|------|------|
| 字符串参数是 ANSI `char*`（PAnsiChar），不是 Unicode | 参数被送进 `call 00403D50`（Delphi `@PCharToStr`：先扫描 `#0` 求长度再建 AnsiString）、`00402C10`（拷成 ShortString）等 ANSI 例程 |
| 返回值在 `EAX`，为 32 位整数 | 所有导出结尾 `mov eax, ebx` / `mov eax,[ebp-4]` 后 `ret imm16` |
| `stdcall`（被调用方清栈） | 结尾 `ret 0Ch / 10h / 14h …`；除 `ACBEL_DATABASE_OPEN/CLOSE`、`GET_ALL_INFO` 外全部如此 |
| 两个 0 参数函数 | 结尾是 `mov eax,ebx; pop edi; pop esi; pop ebx; mov esp,ebp; pop ebp; ret`，且函数体内不存在任何 `[ebp+8]` 以上的参数读取 |

**不能从二进制确定（必须靠文档或实测）**

- 每个参数的**确切含义与顺序**（下表给出的是强候选，不是定论）。
- 哪些参数是**输出缓冲区**、缓冲区多大、是 `PAnsiChar` 还是 Delphi `AnsiString`。
- 返回码语义（0 是否代表成功）、失败时是否需要调用方处理弹窗。

**参数名候选（来自函数内嵌 SQL 的绑定变量名，可信度较高但顺序为推断）**

| 函数 | 候选参数名（括号内为按 arg1..argN 的推断顺序） |
|------|------|
| `ACBEL_SMT_LAB_CHK` | (错误信息, XMO, XNUM, XEMP, XSTA, XSEC) |
| `ACBEL_SMT_LAB_INS` | (错误信息, XMO, XBAR_A, XBAR_NEW, XLINE, XEMP, XSTA, XSEC) |
| `ACBEL_SMT_LAB_LK` | (错误信息, A_SN, XBAR_B, XSTA, XSEC) |
| `ACBEL_SYS_DATA` | (错误信息, MO_NO, XSTATION, QUL_data, QUL_status, Qcrt_user, QEQUID_NO) |
| `FUN_VALUE` | (?, ?, ?, QUL_data1, QUL_data2, QUL_status, Qcrt_user, QEQUID_NO) |
| `ACBEL_MAPPING_STATION` | (?, MO_NO, ?, LINE_NAME, STATION, INTERNAL_SR, PERIOD…) |
| `ACBEL_AOI_UP` | (…, MO_NO/CUR_ROUTE_ID, PERIOD/STATION, LINE_NAME/TEST_RESULT, INTERNAL_SR/UPD_USER, …, XTIME) |
| `GREATR_BAR_CODE` | (?, MO_NO, ?, PART_NO/SCAN_SEQ, BARCODE1…6) |
| `CHKUCL_LCL` / `CHKML_NO` | (MO_NO) / (XTOOL_NO) |
| `GET_TEST_PROGRAM` | (MODEL_NO, STATION, MODEL_PROGRAM) |

---

## 7. C# 用法

完整声明见同目录 `AcbelChromaTest.Interop.cs`。要点：

```csharp
[DllImport("Acbel_Chroma_Test.dll",
           CallingConvention = CallingConvention.StdCall,   // 关键：stdcall
           CharSet = CharSet.Ansi,                          // 关键：ANSI
           ExactSpelling = true)]
public static extern int CHKUCL_LCL(string moNo);
```

```csharp
// 典型调用顺序（示例，务必在测试站上先验证）
int rc = AcbelChromaTest.ACBEL_DATABASE_OPEN();
try
{
    if (rc != 0) { /* 打开 MES 连接失败 */ }

    rc = AcbelChromaTest.ACBEL_SET_BASIC_INFO(station, empNo, lineName, moNo);
    rc = AcbelChromaTest.CHKBAR_CODE(barcode, IntPtr.Zero, IntPtr.Zero);
    rc = AcbelChromaTest.ACBEL_INSERT_DATA(...);
}
finally
{
    AcbelChromaTest.ACBEL_DATABASE_CLOSE();
}
```

调用约定写错的后果是**栈被破坏**（表现为随机的 `AccessViolation` / 数据错乱），所以：
`stdcall` 一定要显式声明（`Winapi` 在 32 位下等价于 StdCall，但显式更清楚）。

**没有把握的参数一律先用 `IntPtr`**，用 `Marshal.StringToHGlobalAnsi` 传字符串，确认后再改成 `string`：

```csharp
IntPtr p = Marshal.StringToHGlobalAnsi("AC12345678");
try { AcbelChromaTest.ACBEL_SMT_LAB_CHK(p, ...); }
finally { Marshal.FreeHGlobal(p); }
```

---

## 8. 运行环境依赖与风险（重要）

这个 DLL 是 **Chroma PowerPro 站点机上的 MES 对接库**，不是通用组件：

1. **文件/目录依赖**（写在代码里的绝对路径）：
   `C:\Program Files\Chroma\PowerPro\Chroma.ini`、`…\Status.ini`、`D:\CHROMA\FAC_CODE.txt`、
   `D:\Chroma\TP Info.txt`、`D:\TEST_DATA1\`、`D:\AMZ_LOG\DB_LOG.txt`、`D:\FUN_TEST\`、`C:\ew518fe\WIN518F.INI`。
2. **数据库**：连接串形如 `…USER NAME=APITECH PASSWORD=PAPIWTECHD…`，且客户端是**运行时动态加载**的
   （静态导入表里没有 OCI/ODBC）。运行环境需具备对应的数据库客户端与网络可达的 MES。
3. **安全**：二进制中明文含有数据库账号口令；不要把它带出受控环境。
4. **UI 风险**：内含 VCL 窗体并导入 `MessageBoxA` → 失败时可能**弹窗阻塞**，在自动化流程里要放在独立进程并设超时。
5. **业务风险**：这些函数会**写 MES 生产数据**（`SIM53/SIM117A/SIM556…`）。验证阶段请只用只读路径
   （如 `GET_TEST_PROGRAM`、`CHKUCL_LCL`），不要在生产库上盲调 `ACBEL_INSERT_DATA`、`*_MAPPING`、`CHANGE_BARCODE_DC`。
6. **`ACBEL_DATABASE_CLOSE` 与 `OPEN` 是同一实现**：如果业务流程依赖“关闭连接”，需自行确认（或在桥接进程里用退出进程来保证释放）。

---

## 9. 如何自行复现/继续确认签名

```powershell
$dumpbin = 'D:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231\bin\Hostx64\x86\dumpbin.exe'
& $dumpbin /exports  .\Reference\Acbel_Chroma_Test.dll
& $dumpbin /imports  .\Reference\Acbel_Chroma_Test.dll
& $dumpbin /headers  .\Reference\Acbel_Chroma_Test.dll
& $dumpbin /disasm   .\Reference\Acbel_Chroma_Test.dll > .\disasm.txt   # 线性反汇编在数据区会漂移
```

`.analysis\` 下的脚本（均为一次性分析工具）：

| 脚本 | 作用 |
|------|------|
| `api_final.py` / `profile.py` | 逐函数反汇编（先用 NOP 填充实现对齐），给出 `ret imm16` 参数、`[ebp+X]` 参数槽、字符串引用 |
| `helpers.py` | 识别 Delphi 运行库辅助例程（如 `00403D50 = @PCharToStr`），据此判断参数是字符串 |
| `retval.py` | 查看每个函数结尾的返回值设置方式 |
| `bindmap.py` | 把 SQL 绑定变量名映射到参数槽 |
| `refprobe\`、`bindprobe\` | 实测 `<Reference>` 与 `<None>` 的差异、以及 x86 .NET 进程加载 23 个导出的验证程序 |

**仍然推荐**：向厂商索取 `Acbel_Chroma_Test` 的接口文档或 `.h/.pas`，本文件第 5、6 节的推断即可被替换为确定结论。

---

## 10. 厂商文档口径与返回值修正（2026-10-13 追加）

用户提供了 `鐳雕DLL說明.docx`（内部标题「镭雕软件与MES系统通讯规范」），逐段文本已提取到 [镭雕DLL說明-文本.md](镭雕DLL說明-文本.md)。

**修正第 5 节的结论**：返回值不是 `int`，而是 **ANSI 字符串指针（PChar）**。文档明确写 `Function ACBEL_DATABASE_OPEN: PCHAR; stdcall;`、`CHANGE_BARCODE_DC(...): PCHAR; stdcall;`；反汇编中返回前是 `mov eax, ebx`，ebx 指向只读常量串。

| 结果形态 | 含义 | 实例 |
|---|---|---|
| `"database"` | `ACBEL_DATABASE_OPEN` 成功 | 返回的就是 BDE 别名/库名 |
| `"1"` | 一般成功 | `ACBEL_SET_BASIC_INFO` / `CHKBAR_CODE` / `ACBEL_INSERT_DATA` / `ACBEL_SYS_DATA` |
| 新条码字符串 | `CHANGE_BARCODE_DC` 成功 | 直接返回给镭雕机的新条码 |
| `"0}XXXX-…"` | 业务卡控失败（XXXX 为异常码） | `0}B006-線別錯誤The line name is incorrect`、`0}C029-請及時測SAMPLE…`、`0}站別不能為空` |
| `"2}調用函數…"` | 内部异常，文档要求**重新调用一次** | `2}調用函數CHKBAR_CODE錯誤 請重新調用此函數` |

`Reference\AcbelChromaTest.Interop.cs` 已同步修正：23 个声明改为返回 `IntPtr`，并新增 `ToText()` / `IsOk()` / `IsRetryNeeded()` / `IsBusinessFail()` 辅助方法（已用 `csc /platform:x86` 编译通过；文件保持 UTF-8 BOM）。

文档只覆盖镭雕流程用到的 **6 个**函数（`ACBEL_DATABASE_OPEN`、`ACBEL_SET_BASIC_INFO`、`CHKBAR_CODE`、`CHANGE_BARCODE_DC`、`ACBEL_INSERT_DATA`、`ACBEL_SYS_DATA`），其余 17 个导出仍按第 5、6 节的推断使用。文档另确认：`CHANGE_BARCODE_DC` 会把卡控过程写入 `D:\LOG.txt`。

## 11. 两版 DLL 差异与选型（2026-10-13 追加）

对比对象：本仓库旧版 `Reference\Acbel_Chroma_Test.dll`（828416B，sha256 `7EDBF66…`）与用户新提供的新版（832000B，sha256 `736A2932…`）。

| 维度 | 结论 |
|---|---|
| 位数/格式 | 完全相同：PE32 / x86 / ImageBase 0x400000 / Delphi 编译（仅入口点不同） |
| **导出接口** | **完全兼容**：同样 23 个导出、同名、同 ordinal、同调用约定与参数个数（实测 SET_BASIC_INFO 4、CHKBAR_CODE 3、CHANGE_BARCODE_DC 4、INSERT_DATA 3、SYS_DATA 7、DATABASE_OPEN 0 等两版一致） |
| 代码体积 | 新版 CODE 节 +2524 字节；`SET_BASIC_INFO` 之后的函数 RVA 全部后移；`DATABASE_OPEN/CLOSE` RVA 未变但函数体 775→824 字节 |
| 数据库语句 | 归一化后 SQL 214 条 → 224 条（新增 36、改写/删除 26） |
| 新增 | `laser_first`（镭雕首件）检查、Amazon 专用账号（`USER NAME=AMZUSR` / `PASSWORD=AMZ12345`）、`amz_smt_ls` 表、新异常码（0}C0371 / 0}C049 / 0}C025-NDF / 0}C025-RC / 0}D038 / 0}D039）、超音波熔接/铆接规格项（AMPLITUDE / DESCENT_SPEED / STRESS_TRI / COOLDOWN_TIME / ENERGY1 / PRE_TRIGGER / ULTRASONIC_MODE）、SIM524 映射链查询、`PRIOR_DATE` |
| 改写 | `laser_first` 时间比较从「只比 HH24MISS + 1 小时」改为「`UPD_DATE‖UPD_TIME` + 3 小时」（修掉跨零点判断错误）；SIM524 链路去掉 `barcode_new not like '9%'`；多条 SIM03 `CONNECT BY` 路由查询与 SIM24 判断条件改写 |
| 删除（旧版独有） | `SIM093` 上线检查、`SIM125` 人员栏位检查、`SIM551` LIKE 查询、`sim25` 若干写入/查询，以及 `insert SIM53 first` / `ready start check sim25` /「Testing failed before and not yet transfer to repair center…」等提示串 |

**选型建议：用新版**——接口零成本兼容（导出/序号/参数/调用约定不变），且带上了镭雕首件的时间比较修复与 Amazon / 超音波相关的新卡控。唯一要留意的是新版删改了一批旧 SQL：若现场流程依赖 `SIM093` 上线检查或 `SIM125` 人员检查，请先与厂商确认是「移到别处」还是「确实取消」。

复现脚本：`.analysis\compare\` 下的 `read_docx.py`、`dll_diff.py`、`deep_diff.py`、`rettype.py` 及输出 `diff_out.txt`、`deep_out.txt`。
