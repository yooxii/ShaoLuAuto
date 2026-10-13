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

## 当前暴露的接口（按需求，只有两个）

| 命令 | 对应导出函数 | 说明 |
|---|---|---|
| `open` | `ACBEL_DATABASE_OPEN()` | **验证与 MES 数据库是否连通**。成功返回库名（通常 `"database"`）；失败返回 `"0}數據庫無法連接"` / `"連接DB Connection ERROR"` |
| `insert` | `ACBEL_INSERT_DATA(db, 上传资料, 镭雕程式文件名)` | ★**会写入 MES 生产库**。第 1 个参数（db）由桥接进程用上一次成功 `open` 的返回值自动填入 |
| `validate` | ——（不调用 DLL） | 只按 27 段字段规范校验「上传资料」，离线可用，见 [ACBEL_INSERT_DATA-字段与校验.md](../Reference/ACBEL_INSERT_DATA-字段与校验.md) |

另有两条控制命令：`ping`（自检，**不调用任何 DLL 函数**）、`quit`。

> 其余 21 个导出（`CHKBAR_CODE`、`ACBEL_SET_BASIC_INFO`、`ACBEL_SYS_DATA`…）暂未开放。

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
insertFieldCount=27
insertRequiredFields=PASS/FAIL/INTERNAL_SR/MO_NO/STATION/LINE_NAME/START_TIME/END_TIME/TEST_DATE/UPD_USER
loadLibrary=ok
export.ACBEL_DATABASE_OPEN=true
export.ACBEL_INSERT_DATA=true
```

## 命令行

```
AcbelBridge.exe [--dll <Acbel_Chroma_Test.dll 路径>] [--call-timeout <秒>]
                [--no-validate] [--allow-extra-fields] [--version]
```

默认从 exe 同级目录加载 `Acbel_Chroma_Test.dll`（工程已配置为随生成复制），调用超时默认 60 秒。

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
