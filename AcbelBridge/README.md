# AcbelBridge —— x86 桥接进程

`Reference\Acbel_Chroma_Test.dll` 是 **32 位原生（Delphi）DLL**，64 位的 ShaoLu 主程序无法在进程内加载它
（`LoadLibrary` 会返回 `ERROR_BAD_EXE_FORMAT 193`）。本工程是一个 **x86 控制台程序**，独占加载该 DLL，
通过 stdin/stdout 行协议对外提供调用，主程序用 `ShaoLu\Services\AcbelBridgeClient.cs` 与它通信。

```
ShaoLu.exe (x64)
   └─ AcbelBridgeClient ── stdin/stdout 行协议 ──> AcbelBridge.exe (x86)
                                                       └─ Acbel_Chroma_Test.dll (32 位原生)
                                                            └─ BDE(idapi32) → SQLORA8/OCI → Oracle
```

## 当前暴露的接口

| 命令 | 对应导出函数 | 说明 |
|---|---|---|
| `open` | `ACBEL_DATABASE_OPEN()` | **验证与 MES 数据库是否连通**。成功返回库名（通常 `"database"`）；失败返回 `"0}數據庫無法連接"` / `"連接DB Connection ERROR"` |
| `set_basic_info` | `ACBEL_SET_BASIC_INFO(db, 工单号, 站别+工号, 线别)` | 写入基础资料（工单 `SIM40` / 站别 `SIM02` / 员工受训 `SIM501` / 线别 `SIM11` 校验）。成功 `"1"`，失败 `"0}B001-…"~"0}B006-…"`，`"2}…"` 需重调一次 |
| `chkbar_code` | `CHKBAR_CODE(站别+工号, 条码)` | 扫码后的条码检查。成功 `"1"`，失败 `"0}C0xx-…"` |
| `insert` | `ACBEL_INSERT_DATA(db, 上传资料, 镭雕程式文件名)` | ★**会写入 MES 生产库**。第 1 个参数（db）由桥接进程用上一次成功 `open` 的返回值自动填入 |
| `validate` | ——（不调用 DLL） | 只按 27 段字段规范校验「上传资料」，离线可用，见 [ACBEL_INSERT_DATA-字段与校验.md](../Reference/ACBEL_INSERT_DATA-字段与校验.md) |

另有两条控制命令：`ping`（自检，**不调用任何 DLL 函数**）、`quit`。

> 「上传Acbel」步骤的执行顺序：`open` → `set_basic_info` → `insert`。
> `ACBEL_INSERT_DATA` 依赖 `ACBEL_SET_BASIC_INFO` 先写入基础资料，缺了会被业务卡控拒收；
> 因此基础信息写入失败时该步骤直接判失败，不会继续写库。
> 取值口径：**工单号 = MO_NO（工令）**、站别+工号 = `STATION` + `UPD_USER` 直接拼接、线别 = `LINE_NAME`。

## 运行环境（DLL 硬编码的 D 盘路径）

DLL 内部写死了几个绝对路径，桥接进程**启动时自动补齐**（只补缺失项，已存在的一律不动）：

| 路径 | 缺失时的实际表现 | 处理 |
|---|---|---|
| `D:\CHROMA\FAC_CODE.txt` | `open` 内部读取异常被吞掉，返回 `"0}數據庫無法連接"`（看着像连不上库，其实是缺文件） | 自动创建，内容为厂区代码（默认 `H`，`--fac-code` 可改） |
| `D:\AMZ_LOG\` | `open` 一开头写 `D:\AMZ_LOG\DB_LOG.txt` 就抛 Delphi 异常（`0xEEDFADE`）冲出 DLL | 自动创建目录 |
| `D:\TEST_DATA1\` | `insert` 写 `D:\TEST_DATA1\LOG1.txt` 时抛异常 | 自动创建目录 |

`FAC_CODE.txt` 的**第一行第一个字符**决定 BDE 别名：`H`→`BSFC`、`P`→`BSFC2`、`E`→`ESFC`、`M`→`MMRP2`，其它字符→`AMDB`。
文件必须写成**无 BOM、无换行**的纯文本（桥接就是这么写的），否则首字符不是 `H`，会退到 `AMDB`。
厂区不同就要改这个文件——`H` 只对应 `BSFC`。

准备失败（例如没有 D 盘、没权限）时，`open`/`insert` 会在调用 DLL 之前直接返回 `FAIL`，并在说明里点出是哪个路径没准备好。

## 协议

UTF-8 文本，一行一请求、一行一响应；**所有参数一律 Base64(UTF-8)**，避免逗号/制表符/中文带来的转义问题。

```
请求:  <命令>[\t<base64 arg1>[\t<base64 arg2>]]
响应:  OK\t<base64 负载>        负载是文本（ping 为多行 key=value）
       FAIL\t<base64 说明>
```

`ping` 的负载示例：

```
bitness=x86
dll=D:\...\AcbelBridge\bin\Debug\Acbel_Chroma_Test.dll
dllExists=true
callTimeoutSeconds=60
validateInsertData=true
allowExtraFields=false
prepareEnvironment=true
env.amzLog=true
env.testData1=true
env.facCodeFile=true
env.facCode=H
insertFieldCount=27
insertRequiredFields=PASS/FAIL/INTERNAL_SR/MO_NO/STATION/LINE_NAME/START_TIME/END_TIME/TEST_DATE/UPD_USER
loadLibrary=ok
export.ACBEL_DATABASE_OPEN=true
export.ACBEL_INSERT_DATA=true
```

## 命令行

```
AcbelBridge.exe [--console] [--allow-write] [--dll <Acbel_Chroma_Test.dll 路径>] [--call-timeout <秒>]
                [--no-validate] [--allow-extra-fields]
                [--no-prepare-env] [--fac-code <厂区代码>] [--log|--no-log] [--version]
```

默认从 exe 同级目录加载 `Acbel_Chroma_Test.dll`（工程已配置为随生成复制），调用超时默认 60 秒。
`--no-prepare-env` 关闭运行环境自动补齐（站点机上路径已由 Chroma 装好时可用），
`--fac-code` 指定写入 `FAC_CODE.txt` 的厂区代码（默认 `H`），只在文件缺失时才写入。
`--console` 进入**控制台命令集**（见下一节），`--allow-write` 允许控制台执行写库命令。

## 控制台命令集（`--console`）

手工控制 DLL 全部 **23 个导出**，参数直接写文本（不用 Base64）：

```powershell
.\AcbelBridge.exe --console
```

```
AcbelBridge 控制台 — Acbel_Chroma_Test.dll（23 个导出）
DLL：D:\...\AcbelBridge\bin\Debug\Acbel_Chroma_Test.dll
允许写 MES：否（写入类命令需 --allow-write）
输入 help 查看命令，help <命令> 看用法，quit 退出。

AcbelBridge> help ACBEL_SET_BASIC_INFO
命令：ACBEL_SET_BASIC_INFO
说明：写入基础资料（工单/站别工号/线别），ACBEL_INSERT_DATA 之前必须先调它
用法：ACBEL_SET_BASIC_INFO db "工单号(工令)" "站别+工号" 线别
参数：
  1) db
  2) 工单号(工令)
  3) 站别+工号
  4) 线别
返回：成功 1；失败 0}B001~B006（B006=線別錯誤）；2} 需重调一次
示例：set_basic_info @db HE47H1G001H18163 16521807605 H18

AcbelBridge> open
返回：database
判定：成功
已记住库名 database，后续参数可用 @db
```

约定：

- 命令名 = 导出名（大小写不敏感），另提供 `open` / `close` / `insert` / `basic` / `chkbar` /
  `chkucl` / `chkml` / `get_program` / `sys_data` / `smt_lab_chk` … 等简写；
- 含空格的参数用双引号包起来，空参数写 `""` 或 `-`；
- `db` 参数写 `@db` 会自动用上一次 `open` 成功的库名（也可 `db <库名>` 手动设置）；
- **写入类命令**（`insert`、`sys_data`、`change_barcode_dc`、`aoi_insert`、`amz_mapping`、
  `upload_data`、`ict_insert`、`fun_value`、`smt_lab_ins`、`smt_lab_lk`、`aoi_up`、
  `greatr_bar_code`、`mapping_station`）默认拒绝执行，启动时加 `--allow-write` 才放行；
- 输出参数（`错误信息(out)`、`MODEL_PROGRAM(out)` 等）调用后会打印“写回的参数”，
  便于看清 DLL 往缓冲区里写了什么；缓冲区 8 KB，超长会被截断；
- 每个参数的缓冲区都是可写的 8 KB ANSI 缓冲区，所以厂商文档里那些“输出缓冲区”也能直接用；
- `ping` 只做自检（不调任何函数），`env` 看运行环境，`db` 看/改当前库名。

> 行协议（主程序用）不受影响：`--console` 是另一条入口；协议模式也新增了 `help` 命令，
> 返回同样的命令清单。

## 安全设计

1. **写库闸门**：`insert` 必须先有一次成功的 `open`；否则桥接**在调用 DLL 之前**就返回
   `FAIL 尚未成功执行 open…`，不会写任何数据。
2. **上传资料校验（第 2 道闸门）**：`insert` 的第 2 个参数会按 27 段字段规范校验
   （共享源码 `Shared\AcbelInsertDataSpec.cs`，与主程序同一份规则）；不通过同样拒绝且不写库。
   `--no-validate` 关闭校验，`--allow-extra-fields` 允许字段数多于 27 段。
3. **调用超时**：每次 DLL 调用都在独立线程上执行并等待 `--call-timeout`；超时返回 `FAIL TIMEOUT…`，
   桥接进程不会永久挂死（数据库不可达、或 DLL 弹出模态对话框时尤其重要）。客户端在超时后会直接结束该进程。
4. **stdout 只走协议**，诊断信息走 stderr，避免污染协议流。
5. 返回值是 ANSI 字符串（PChar）：成功 `"database"`/`"1"`，业务失败 `"0}XXXX-…"`，内部异常 `"2}…"`（文档要求重试一次）。

> 两个闸门的顺序：先查 `open`（写库安全），再查字段规范（数据正确）。

## 日志

每条请求与响应都会写进 exe 同级的 `AcbelBridge.log`（Debug 默认开、Release 默认关）。
日志里的参数与返回值**已经解码成文本**（不再是 Base64），换行写成 `\n`，一条请求占一行，例如：

```
2026-10-30 15:21:14.892 → validate	PASS,,,O4769455,HE47H1G001H18163,165,H18, …
2026-10-30 15:21:14.911 ← OK	校验通过：字段数 27 段齐全
```

## 在站点机上验证连通性

```powershell
# 1) 自检（不碰数据库）：必须是 x86、loadLibrary=ok、两个导出 true
'ping
quit' | & .\AcbelBridge\bin\Debug\AcbelBridge.exe

# 2) 校验上传资料（离线可跑，不碰数据库）
$payload = 'PASS,,,O4769455,HE47H1G001H18163,165,H18,140735,140846,20210520,21807605,0.1,0.2,0.3,0.4,FSC007A047H1GB2116000136,TOOL,TOOL1,TOOL2,TOOL3,TOOL4,TOOL5,TOOL6,0.5,GMAU0001,CCD01,D205'
$b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload))
"validate`t$b64`nquit`n" | & .\AcbelBridge\bin\Debug\AcbelBridge.exe

# 3) 连库验证：成功输出 "database"，失败输出 "0}數據庫無法連接" 之类
'open
quit' | & .\AcbelBridge\bin\Debug\AcbelBridge.exe
```

自动化测试（`NUnitTest\AcbelBridgeTests.cs`）：

```powershell
# 只跑桥接相关测试；连库那条是 [Explicit]，需显式选择才会执行
powershell -NoProfile -File tools/test.ps1 -Filter "FullyQualifiedName~AcbelBridgeTests"
```

- `Ping_桥接进程为x86且两个导出可解析` —— 离线可跑
- `InsertData_未成功OPEN前被拒绝且不写库` —— 离线可跑，验证写库闸门
- `DatabaseOpen_返回字符串且能判定连通性` —— **[Explicit]**，会真正连库，只在站点机上手动跑

## 待办

- `tools\pack-release.ps1` 目前只把 `ShaoLu.csproj` 的产物打进发布包，**还没有包含 AcbelBridge.exe**；
  正式发布前需要把桥接输出（`AcbelBridge.exe` + `Acbel_Chroma_Test.dll`）一并放进包内同级目录，
  否则运行时会报「找不到 AcbelBridge.exe」。
- 客户端定位桥接程序的顺序：显式路径 → 环境变量 `ACBEL_BRIDGE_EXE` → 程序目录 → 开发期向上查找
  `AcbelBridge\bin\{Debug,Release}\`。发布包建议直接把 `AcbelBridge.exe` 放在 ShaoLu.exe 同级。
