# ACBEL_INSERT_DATA 上传资料字段规范与校验

`ACBEL_INSERT_DATA(db, 上传资料, 镭雕程式文件名)` 的第 2 个参数是一个**定长、逗号分隔**的字符串。
本文的字段清单来自用户提供的截图（27 段，其中 **9 段必填**），空格字段也必须保留逗号占位。

实现：`Shared\AcbelInsertDataSpec.cs`（**同一份源码同时编进 x64 主程序与 x86 桥接进程**，
`AcbelInsertDataSpec` / `AcbelInsertDataValidator` / `AcbelInsertDataBuilder`，命名空间 `AcbelMes`）。

## 1. 字段清单

| 段 | 字段名 | 中文 | 必填 | 类型 | 校验规则 |
|---:|---|---|:--:|---|---|
| 1 | `PASS/FAIL` | 測試結果 | ✔ | 枚举 | 只能是 `PASS` / `FAIL`（忽略大小写） |
| 2 | `FAIL REASON` | 失敗原因 | | 文本 | FAIL 时为空→提示；PASS 时有值→提示 |
| 3 | `FAIL_VALUE` | 失敗值 | | 文本 | 同上 |
| 4 | `INTERNAL_SR` | 單體條碼 | ✔ | 文本 | 非空 |
| 5 | `MO_NO` | 工令 | ✔ | 文本 | 非空 |
| 6 | `STATION` | 站別 | ✔ | 文本 | 非空 |
| 7 | `LINE_NAME` | 線別 | ✔ | 文本 | 非空 |
| 8 | `START_TIME` | 測試開始時間 | ✔ | 时间 | `HHMMSS` 或 `HH:MM:SS`，时/分/秒需在范围内 |
| 9 | `END_TIME` | 測試結束時間 | ✔ | 时间 | 同上；早于开始时间→提示 |
| 10 | `TEST_DATE` | 測試日期 | ✔ | 日期 | `YYYYMMDD`，且必须是合法日期 |
| 11 | `UPD_USER` | 測試員工號 | ✔ | 文本 | 非空 |
| 12 | `TEST_DATA1` | 深度 | | 数字 | 填了就要能解析为十进制数 |
| 13 | `TEST_DATA2` | 能量 | | 数字 | 同上 |
| 14 | `TEST_DATA3` | 超聲時間 | | 数字 | 同上 |
| 15 | `TEST_DATA4` | 壓力 | | 数字 | 同上 |
| 16 | `OUT_BARCODE` | 外控條碼 | | 文本 | — |
| 17 | `TOOL_NO` | 治具1 | | 文本 | — |
| 18 | `TOOL_NO1` | 治具2 | | 文本 | — |
| 19 | `TOOL_NO2` | 治具3 | | 文本 | — |
| 20 | `TOOL_NO3` | 治具4 | | 文本 | — |
| 21 | `TOOL_NO4` | 治具5 | | 文本 | — |
| 22 | `TOOL_NO5` | 治具6 | | 文本 | — |
| 23 | `TOOL_NO6` | 治具7 | | 文本 | — |
| 24 | `test_data5` | 結合時間 | | 数字 | 同上 |
| 25 | `Laser SN` | 實際鐳雕條碼 | | 文本 | — |
| 26 | `Error Code` | CCD 不良代碼 | | 文本 | FAIL 时为空→提示 |
| 27 | `設備編號` | 設備編號 | | 文本 | — |

**必填 9 段**：1、4、5、6、7、8、9、10、11。其余为「視需求」，可以为空但**逗号必须保留**。

## 2. 校验分级

| 级别 | 触发条件 | 处理 |
|---|---|---|
| 错误（阻断） | 字段数 ≠ 27；必填为空；`PASS/FAIL` 非法；时间/日期/数字格式非法 | **拒绝调用 DLL**（主程序在本地就拦下，桥接进程再拦一次），不会写库 |
| 提示（不阻断） | 字段前后有空白（已自动 trim）；FAIL 缺 FAIL REASON / FAIL_VALUE / Error Code；PASS 却填了失败信息；结束时间早于开始时间 | 照常调用，但把提示写进结果/日志 |

## 3. 与厂商说明书样例的差异（重要）

`Reference\镭雕DLL說明-文本.md` 里的样例是 **28 段**，比本规范多一个 `TOOL7`：

```
PASS,,,O4769455,HE47H1G001H18163,165,H18,140735,140846,20210520,21807605,0.1,0.2,0.3,0.4,
FSC007A047H1GB2116000136,TOOL,TOOL1,TOOL2,TOOL3,TOOL4,TOOL5,TOOL6,TOOL7,0.5,GMAU0001,D205,LD005001
```

1~23 段与本规范一致，第 24 段起样例多了 `TOOL7`，导致后面整体错位。**以截图 27 段为准**；
若现场实际按 28 段传，可用放宽开关跳过段数检查（见下），但错位风险自负。

## 4. 用法

```csharp
using AcbelMes;

// 用构造器按字段名赋值，避免手工拼错顺序
string payload = new AcbelInsertDataBuilder()
    .SetResult(pass: true)                       // 第 1 段 PASS/FAIL（FAIL 时可带 reason/value/errorCode）
    .Set("INTERNAL_SR", "O4769455")
    .Set("MO_NO",       "HE47H1G001H18163")
    .Set("STATION",     "165")
    .Set("LINE_NAME",   "H18")
    .Set("START_TIME",  "140735")
    .Set("END_TIME",    "140846")
    .Set("TEST_DATE",   "20210520")
    .Set("UPD_USER",    "21807605")
    .Set("TEST_DATA1",  "0.1").Set("TEST_DATA2", "0.2")
    .Set("TEST_DATA3",  "0.3").Set("TEST_DATA4", "0.4")
    .Set("OUT_BARCODE","FSC007A047H1GB2116000136")
    .Set("TOOL_NO", "TOOL").Set("TOOL_NO1", "TOOL1") /* … TOOL_NO6 … */
    .Set("test_data5",  "0.5")
    .Set("Laser SN",    "GMAU0001")
    .Set("Error Code",  "CCD01")
    .Set("設備編號",     "D205")
    .Build();

// 1) 只校验（离线、不连库、不调用 DLL）
AcbelPayloadValidationResult v = AcbelInsertDataValidator.Validate(payload);
if (!v.IsValid) { /* v.Errors / v.Warnings / v.ToErrorText() */ }

// 2) 通过客户端提交：默认会先本地校验，不通过就直接返回、不发桥接调用
using (var acbel = new AcbelBridgeClient())
{
    AcbelCallResult r = acbel.InsertData(payload, "LASER_TEST");   // 需先 OpenDatabase() 成功
    if (!r.IsOk) { /* r.ValidationErrors / r.Error / r.Value */ }
}
```

放宽段数检查（允许 27 段以后还有字段）：`Validate(payload, allowExtraFields: true)`、
`InsertData(payload, program, allowExtraFields: true)`，或桥接启动参数 `--allow-extra-fields`；
桥接侧还可用 `--no-validate` 完全关闭校验（不建议）。

## 5. 在站点机上直接校验（不连数据库）

```powershell
$payload = 'PASS,,,O4769455,HE47H1G001H18163,165,H18,140735,140846,20210520,21807605,0.1,0.2,0.3,0.4,FSC007A047H1GB2116000136,TOOL,TOOL1,TOOL2,TOOL3,TOOL4,TOOL5,TOOL6,0.5,GMAU0001,CCD01,D205'
$b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload))
"validate`t$b64`nquit`n" | & .\AcbelBridge\bin\Debug\AcbelBridge.exe
```

返回 `OK` + 说明，或 `FAIL` + 逐条错误（段号、字段名、原因）。
