# Acbel_Chroma_Test.dll 数据库交互提取 与 自研可行性评估

> 数据部分由 `.analysis\db\` 下的脚本从 DLL 二进制机械提取；结论部分由静态分析 + 本机环境核查得出。
> 提取口径：字符串表 + 反汇编引用定位，只保证「这个字符串被这个函数引用」，不保证执行顺序与业务语义。

## 0. 结论速览

| 问题 | 结论 |
|---|---|
| 能不能把「数据库交互」单独提取出来？ | **能，已经提取完毕**：226 条内嵌 SQL、71 个表/视图、4 个存储过程、3 个序列、8 个 PL/SQL 函数调用，全部列在下面的数据部分。 |
| 能不能直接在项目里实现（不再依赖这个 32 位 DLL）？ | **能，但要分级**：只读 / 存储过程型 / 单表写入类（约 12 个函数）自研成本可控；`CHKBAR_CODE`、`ACBEL_INSERT_DATA`、`ACBEL_AMZ_MAPPING`、`GREATR_BAR_CODE`、`CHANGE_BARCODE_DC` 这 5 个是「业务规则引擎」，不要手写。 |
| 最大的意外收获 | **大量业务规则其实在 Oracle 服务端**（`APITECH.GET_MO_MODEL_NO`、`GET_ROUTE_ID`、`GET_MO_YN`、`ACBEL_SMT_LAB_*` 等）。自研只要调用同一批 SQL / 存储过程，就自动继承这些规则。 |
| 最大的阻塞项 | 连接信息与账号：DLL 走的是 **BDE 别名 → Oracle**，真实服务器名在站点机的 BDE 注册表里，本机没有（本机不是站点机）。 |

## 1. 这个 DLL 的数据库交互机制（不是直连 Oracle）

```
Acbel_Chroma_Test.dll (Delphi/VCL, 32 位)
   └─ 运行时 LoadLibrary("idapi32.DLL")            ← Borland Database Engine (BDE)
        └─ BDE 别名 BSFC / BSFC2 / ESFC / MMRP2     ← 由 D:\CHROMA\FAC_CODE.txt 决定用哪个
             └─ DRIVERS\ORACLE: DLL32=SQLORA8.DLL, VENDOR INIT=OCI.DLL, NET PROTOCOL=TNS
                  └─ Oracle 客户端 C:\oracle\ora81（8.1.7）
                       └─ tnsnames.ora 里的服务名（NT_AOA01/02、AIX_HISMRP2、BTPLS*、NT_TZMRP2 …）
```

证据：

- 静态导入表里**没有任何数据库客户端**（只有 kernel32/user32/advapi32/gdi32/ole32/oleaut32/comctl32）→ 客户端是运行时加载的；
- 字符串含 `idapi32.DLL`、`Software\Borland\Database Engine`、`\DATABASES\%s\DB OPEN`、`\DRIVERS\%s\DB OPEN`、`AliasName`、`TDatabase`/`TQuery`/`TStoredProc`/`TSession`、`ftOraBlob`/`ftOraClob`；
- 本机注册表 `HKLM\SOFTWARE\WOW6432Node\Borland\Database Engine\Settings\DRIVERS\ORACLE` 存在，其 `INIT` 值为 `DLL32=SQLORA8.DLL, VENDOR INIT=OCI.DLL, TYPE=SERVER, VERSION=4.0`，`DB OPEN` 为 `NET PROTOCOL=TNS`；
- `ACBEL_DATABASE_OPEN`/`CLOSE`（**同一段代码**，775 字节，0 参数）函数体按顺序引用：`In the DB` → `D:\CHROMA\FAC_CODE.txt` → `BSFC`/`BSFC2`/`ESFC`/`MMRP2` → `Free` → `database` → `USER NAME=…`/`PASSWORD=…`（内嵌共享账号，本文档不重复其值）→ `database`。

> 含义：自研时要**自己解决连接信息**。站点机上可以从 `HKLM\SOFTWARE\WOW6432Node\Borland\Database Engine\Settings\DATABASES\<别名>\DB OPEN` 或 `tnsnames.ora` 取到真实服务名。

## 2. 提取到的数据库资产

| 类别 | 数量 | 备注 |
|---|---:|---|
| 内嵌 SQL 语句 / 片段 | **226 条** | 分布在 16 个函数里；`CHKBAR_CODE` 83 条、`ACBEL_AMZ_MAPPING` 43 条、`ACBEL_INSERT_DATA` 31 条 |
| 表 / 视图 | **71 个** | 核心：`SIM53`(53 次)、`SIM03`(21)、`SIM40`(18)、`SIM524`(14)、`SIM11`(7)、`SIM43`(7)、`TBMAS`、`TBMAS_FIXTURE`、`SIM117`/`SIM117A`、`SIM115_1/2`、`SIM547`、`SIM556`、`IVM81`、`HMM01_v1` |
| 存储过程 | 4 | `ACBEL_SMT_LAB_CHECK`、`ACBEL_SMT_LAB_INSERT`、`ACBEL_SMT_LAB_LINK`、`SIM537_P01`（`XERROR_MSG` 为 OUT 参数） |
| 序列 | 3 | `MID_COMPOPAL`、`SQ_BARCODE`、`MID_FUN_V` |
| PL/SQL 函数 | 8 | `APITECH.GET_MO_MODEL_NO`(28×)、`APITECH.GET_ROUTE_ID`(20×)、`GET_MO_YN`(6×)、`GET_PART_NO`、`GET_PARENT_MO`、`GET_SIM92`、`APITECH.GET_MODEL_NO`、`APITECH.GET_SR_ROUTE` |
| 其它 DB 侧对象 | 若干 | `BARCODE_M` / `BARCODE_N` / `BARCODE_RETRY` / `BARCODE_RETRY_NEW`（条码生成的 DB 端函数）、`AUTO_EQUIP_INT_T`、`SIM629`、`SIM639`、`SIM638`、`SIM01`、`SIM21`、`SIM22`、`SIM25`、`SIM132_PCB_BARCODE`、`SIM70_IE_PHASE`、`LS_AMZ_NEWPART` |

## 3. 自研分级（按代码体积 + SQL 条数 + 是否写库）

### 一级：可以直接在项目里实现（只读，或只是转发存储过程）

| 函数 | 代码 | SQL | 写 | 说明 |
|---|---:|---:|:--:|---|
| `CHKUCL_LCL` | 890B | 2 | — | 查 `SIM115_2`→`SIM115_1` 的规格上下限（DEPTH/ENERGY/SOUNDWAVE_TIME/PRESSURE/RIVETING 的 UCL/LCL） |
| `CHECK_PART_NO` | 347B | 1 | — | 依 `MO_NO` 查 `SIM40`/`SIM43` 料号 |
| `CHKML_NO` | 286B | 存储过程 | — | 调用 `SIM537_P01(XTOOL_NO)`，回读 `XERROR_MSG` |
| `GET_TEST_PROGRAM` | 1191B | 5 | — | 查 `SIM547` 取 `MODEL_PROGRAM`（含几条写死料号白名单的分支） |
| `ACBEL_SMT_LAB_CHK` / `_INS` / `_LK` | 471/783/561B | 存储过程 | 服务端 | 逻辑最薄：拼参数 → 调 `ACBEL_SMT_LAB_*` → 回读 `XERROR_MSG`；写库动作全在存储过程里 |

### 二级：中等成本（查询 + 单表写入，规则可读）

| 函数 | 代码 | SQL | 写 | 说明 |
|---|---:|---:|:--:|---|
| `ACBEL_SET_BASIC_INFO` | 4130B | 4 | — | 工单/站别/员工受训/线别四项校验，只读 |
| `ACBEL_SYS_DATA` | 1005B | 2 | `TBMAS` | 取 `MID_COMPOPAL.NEXTVAL` → INSERT |
| `FUN_VALUE` | 1102B | 2 | `TBMAS_FIXTURE` | 取 `MID_FUN_V.NEXTVAL` → INSERT |
| `ACBEL_UPLOAD_DATA` | 662B | 1 | `AUTO_EQUIP_INT_T` | 单表写入 |
| `ICT_INSERT_DATA` | 636B | 1 | `SIM629` | 单表写入 |
| `AOI_INSERT_DATA` | 2394B | 6 | `SIM11/SIM17/SIM22` | 班别/时段换算 + 计数 + 写入 |
| `ACBEL_AOI_UP` | 2305B | 3 | `SIM53` | 班别/时段 + INSERT SIM53 |
| `ACBEL_MAPPING_STATION` | 3789B | 8 | `SIM524` 等 | 站别映射；有显式 `COMMIT`，事务需自管 |

### 三级：不建议手写（规则引擎，且直接写生产 MES）

| 函数 | 代码 | SQL | 涉及表 | 说明 |
|---|---:|---:|---:|---|
| `CHKBAR_CODE` | 32830B | 83 | 46 | 条码总检查：路由 `CONNECT BY`、前站 PASS、QC、时距、样品/托盘、Amazon 特判… |
| `ACBEL_INSERT_DATA` | 28878B | 31 | 19 | 主力入库：SIM53 主记录 + 一堆维修/重工/材料表 |
| `ACBEL_AMZ_MAPPING` | 17514B | 43 | 18 | Amazon 映射：多级父子板、`MAPPING_CNT`、`BARCODE_CTRL` |
| `GREATR_BAR_CODE` | 8136B | 10 | 7 | 条码流水号生成算法（`SN_1_RANGE_SEQ`/`SN_OTH_RANGE_SEQ`/`0123456789ABC…`），`SIM117A` 增改 |
| `CHANGE_BARCODE_DC` | 7329B | 24 | 10 | DC 换码：调用 DB 端 `BARCODE_N` 等函数重新生成条码 |

## 4. 在项目里实现的技术前提

1. **连接信息**（必须先从站点机取到）：
   - BDE 注册表 `HKLM\SOFTWARE\WOW6432Node\Borland\Database Engine\Settings\DATABASES\<BSFC|BSFC2|ESFC|MMRP2>\DB OPEN` → 真实服务器名/TNS 服务名；
   - 或 `tnsnames.ora` 里的服务名 + 端口；厂区别名由 `D:\CHROMA\FAC_CODE.txt` 选择。
2. **账号**：DLL 内嵌的是共享账号，自研建议向 IT 申请**只读账号**（写库流程另行评估授权）。
3. **驱动选型（.NET Framework 4.8 + x64）**：
   - 首选 `Oracle.ManagedDataAccess`（纯托管，不需要装 Oracle 客户端）——**前提是 MES 服务端版本够新**；
   - ⚠ 本机装的是 **Oracle 8.1.7** 客户端，SQL 也全是老语法（`DECODE`/`ROWNUM`/`CONNECT BY`），**必须先确认服务端版本**：若是 8i/9i，托管驱动可能连不上，备选是匹配位数的 OCI/ODBC（本机 `C:\oracle\ora81\ODBC` 是 32 位老驱动，x64 主程序用不了）；
   - 若走 FreeSql，可加 `FreeSql.Provider.Oracle`，与现有 `FreeSql.Provider.Sqlite` 技术栈保持一致（它底层仍是 Oracle 托管驱动，同样受版本限制）。
4. **事务**：DLL 里有显式 `COMMIT`（`ACBEL_MAPPING_STATION` 可见），自研实现必须自己定义提交/回滚边界。
5. **验证手段**：先用 x86 桥接进程保留原 DLL，做 **A/B 对拍**（同一输入下比较 C# 实现与 DLL 的结果），确认一致后再切换。

## 5. 推荐落地路径

1. **第一步（只读切片）**：只实现 `GET_TEST_PROGRAM` + `CHKUCL_LCL` + `CHECK_PART_NO`（外加 `ACBEL_SET_BASIC_INFO` 如需基础资料校验），在 x64 主程序里直连，彻底摆脱位数问题；
2. **第二步**：加 `ACBEL_SMT_LAB_*` 三个存储过程转发（成本最低、且写库逻辑在服务端）；
3. **第三步**：单表写入类（`ACBEL_SYS_DATA`/`FUN_VALUE`/`ICT_INSERT_DATA`/`ACBEL_UPLOAD_DATA`），先在测试库验证；
4. **第四步**：`SIM53` 主记录写入（`ACBEL_INSERT_DATA`/`ACBEL_AOI_UP`）——风险最高，建议保留 DLL（x86 桥接）做主路径；
5. **不建议**：`CHKBAR_CODE`、`ACBEL_AMZ_MAPPING`、`GREATR_BAR_CODE`、`CHANGE_BARCODE_DC` 手工重写。

## 6. 风险清单

- 这些 SQL 会**写生产 MES**（`SIM53`、`SIM117A`、`SIM556`、`TBMAS`…），自研实现的分支差异可能静默污染生产数据 → 必须有 A/B 对拍与测试库。
- 部分规则依赖**运行环境**（`Chroma.ini`、`FAC_CODE.txt`、`TP Info.txt`、`D:\TEST_DATA1\` 等日志目录），自研时要么保留要么显式去掉（去掉可能改变判定结果）。
- 没有厂商文档：本清单是**静态提取**，参数类型/顺序、失败分支语义仍需实测确认。
- 服务端 PL/SQL 函数/存储过程的所有权不属于本项目，若 MES 侧改动，两边都会变。

---

## 附：自动提取的数据部分

> 本节由 `.analysis/db/` 下的脚本从 DLL 二进制中机械提取（字符串 + 反汇编引用定位），
> 只保证“这个字符串被这个函数引用”，不保证业务语义与执行顺序。

### A. 函数 × 数据库访问一览

| 函数 | 序数 | 代码字节 | SQL 条数 | 读 | 写 | 涉及表/视图 |
|---|---:|---:|---:|:--:|:--:|---|
| `CHKBAR_CODE` | 20 | 32830 | 83 | ✔ |  | `APITECH.SIM01`, `APITECH.SIM02`, `APITECH.SIM03`, `APITECH.SIM160`, `APITECH.SIM24`, `APITECH.SIM40`, `APITECH.SIM53`, `APITECH.SIM543`, `APITECH.SIM547`, `APITECH.SIM551`, `APITECH.SIM556`, `APITECH.SIM561`, `DUAL`, `FAM20` … |
| `ACBEL_AMZ_MAPPING` | 8 | 17514 | 43 | ✔ | ✔ | `APITECH.SIM03`, `APITECH.SIM11`, `APITECH.SIM24`, `APITECH.SIM25`, `APITECH.SIM524`, `APITECH.SIM53`, `DUAL`, `HMM01_V1`, `SIM01`, `SIM03`, `SIM11`, `SIM132_PCB_BARCODE`, `SIM24`, `SIM40` … |
| `ACBEL_INSERT_DATA` | 12 | 28878 | 31 | ✔ | ✔ | `APITECH.SIM11`, `APITECH.SIM24`, `APITECH.SIM40`, `APITECH.SIM524`, `APITECH.SIM53`, `APITECH.SIM551`, `APITECH.SIM556`, `APITECH.SIM638`, `DUAL`, `SIM01`, `SIM02`, `SIM11`, `SIM24`, `SIM25` … |
| `CHANGE_BARCODE_DC` | 10 | 7329 | 24 | ✔ | ✔ | `BARCODE_N`, `DUAL`, `LS_AMZ_NEWPART`, `SIM03`, `SIM117`, `SIM40`, `SIM53`, `SIM638`, `SIM70_IE_PHASE`, `SIM94` |
| `GREATR_BAR_CODE` | 17 | 8136 | 10 | ✔ | ✔ | `APITECH.SIM639`, `DUAL`, `SIM117`, `SIM117A`, `SIM40`, `SIM43`, `SIM524` |
| `ACBEL_MAPPING_STATION` | 16 | 3789 | 8 | ✔ | ✔ | `APITECH.SIM11`, `APITECH.SIM524`, `DUAL`, `SIM02`, `SIM11`, `SIM53` |
| `AOI_INSERT_DATA` | 9 | 2394 | 6 | ✔ | ✔ | `APITECH.SIM17`, `APITECH.SIM22`, `DUAL`, `SIM11`, `SIM21`, `SIM22` |
| `GET_TEST_PROGRAM` | 18 | 1191 | 5 | ✔ |  | `APITECH.SIM02`, `APITECH.SIM40`, `APITECH.SIM53`, `APITECH.SIM547`, `DUAL`, `SIM53` |
| `ACBEL_SET_BASIC_INFO` | 21 | 4130 | 4 | ✔ |  | `APITECH.SIM40`, `SIM02`, `SIM11`, `SIM501` |
| `ACBEL_AOI_UP` | 1 | 2305 | 3 | ✔ | ✔ | `APITECH.SIM17`, `SIM11`, `SIM53` |
| `CHKUCL_LCL` | 14 | 890 | 2 | ✔ |  | `SIM115_1`, `SIM115_2` |
| `ACBEL_SYS_DATA` | 11 | 1005 | 2 | ✔ | ✔ | `DUAL`, `TBMAS` |
| `FUN_VALUE` | 5 | 1102 | 2 | ✔ | ✔ | `DUAL`, `TBMAS_FIXTURE` |
| `CHECK_PART_NO` | 15 | 347 | 1 | ✔ |  | `SIM40`, `SIM43` |
| `ACBEL_UPLOAD_DATA` | 7 | 662 | 1 |  | ✔ | `AUTO_EQUIP_INT_T` |
| `ICT_INSERT_DATA` | 6 | 636 | 1 |  | ✔ | `SIM629` |
| `ACBEL_DATABASE_CLOSE` | 23 | 775 | 0 |  |  |  |
| `ACBEL_DATABASE_OPEN` | 22 | 775 | 0 |  |  |  |
| `GET_ALL_INFO` | 19 | 122 | 0 |  |  |  |
| `CHKML_NO` | 13 | 286 | 0 |  |  |  |
| `ACBEL_SMT_LAB_CHK` | 4 | 471 | 0 |  |  |  |
| `ACBEL_SMT_LAB_INS` | 3 | 783 | 0 |  |  |  |
| `ACBEL_SMT_LAB_LK` | 2 | 561 | 0 |  |  |  |

### B. 各函数引用的 SQL（按代码中出现顺序）


#### `ACBEL_DATABASE_CLOSE`（序数 23，RVA 0x7A9A0，代码 775 字节）

非 SQL 依赖：`D:\CHROMA\FAC_CODE.txt`


#### `ACBEL_DATABASE_OPEN`（序数 22，RVA 0x7A9A0，代码 775 字节）

非 SQL 依赖：`D:\CHROMA\FAC_CODE.txt`


#### `ACBEL_SET_BASIC_INFO`（序数 21，RVA 0x7AE6C，代码 4130 字节）

非 SQL 依赖：`C:\Program Files\Chroma\PowerPro\Chroma.ini`

<details><summary>+0xC55（53 字符，绑定变量 `:MO_NO`）</summary>

```sql
SELECT MO_NO FROM APITECH.SIM40   WHERE MO_NO= :MO_NO
```
</details>

<details><summary>+0xD68（49 字符，绑定变量 `:STATION`）</summary>

```sql
SELECT STATION FROM SIM02  WHERE STATION=:STATION
```
</details>

<details><summary>+0xE35（74 字符，绑定变量 `:EMP_NO`, `:STATION`）</summary>

```sql
SELECT A.* FROM SIM501 A  WHERE A.EMP_NO =:EMP_NO   AND A.STATION=:STATION
```
</details>

<details><summary>+0xF1C（83 字符，绑定变量 `:LINE_NAME`）</summary>

```sql
SELECT LINE_NAME FROM SIM11  WHERE LINE_NAME = :LINE_NAME  And NVL(STOP_YN,'N')='N'
```
</details>


#### `CHKBAR_CODE`（序数 20，RVA 0x7C744，代码 32830 字节）

非 SQL 依赖：`D:\Chroma\TP Info.txt`；`C:\Program Files\Chroma\PowerPro\Status.ini`；`C:\ew518fe\WIN518F.INI`

<details><summary>+0x1B8（105 字符，绑定变量 `:MI`, `:SS`）</summary>

```sql
SELECT TO_CHAR(SYSDATE,'HH24:MI:SS')TTIME,ROUND(TO_DATE(TO_CHAR(SYSDATE,'YYYYMMDD'),'YYYYMMDD')-TO_DATE('
```
</details>

<details><summary>+0xF47（172 字符，绑定变量 `:XBAR`）</summary>

```sql
SELECT A.DURATION_MIN,get_mo_model_no(B.MO_NO) PART_NO FROM SIM03 A,SIM53 B WHERE  A.ROUTE_ID=B.CUR_ROUTE_ID AND B.INTERNAL_SR=:XBAR AND B.STATION='030' AND A.STATION='174'
```
</details>

<details><summary>+0x103B（346 字符，绑定变量 `:MI`, `:SS`, `:XEQ`, `:XPART`）</summary>

```sql
SELECT    (SYSDATE - TO_DATE(upd_date||upd_time, 'YYYY-MM-DD HH24:MI:SS')) * 24 * 60 * 60 AS seconds_difference  FROM   (SELECT * FROM sim53  where prod_date=to_char(sysdate,'YYYYMMDD') and equip_no=:XEQ                         and station='174' and get_mo_model_no(mo_no)=:XPART                  order by upd_date||upd_time desc ) where rownum=1
```
</details>

<details><summary>+0x1177（131 字符，绑定变量 `:XBAR`）</summary>

```sql
SELECT * FROM SIM125_QC WHERE INTERNAL_SR=:XBAR AND TIMEOUT_YN='Y' AND QC_EMP_NO IS  NULL AND QC_DATE IS  NULL AND QC_TIME IS  NULL
```
</details>

<details><summary>+0x1267（315 字符，绑定变量 `:XBAR`, `:XEQUIP`, `:XSTA`）</summary>

```sql
SELECT LS_EQUIP_NO FROM SIM093 WHERE LS_DATE=TO_CHAR(SYSDATE,'YYYYMMDD') AND LS_EQUIP_NO=:XEQUIP AND LS_STATION=:XSTA  AND LS_PART_NO=(SELECT GET_MO_MODEL_NO(MO_NO) AS MODEL_NO      FROM ( SELECT MO_NO   FROM SIM53 WHERE INTERNAL_sR = :XBAR   AND STATION = '030' ORDER BY upd_date || upd_time DESC)WHERE ROWNUM = 1)
```
</details>

<details><summary>+0x138D（228 字符，绑定变量 `:XEQUIP`, `:mi`, `:ss`）</summary>

```sql
select EQUIP_NO from laser_first where upd_date=TO_CHAR(SYSDATE,'YYYYMMDD') AND EQUIP_NO=:XEQUIP  and TO_CHAR(SYSDATE,'HH24MISS')>=to_char(TO_DATE(UPD_TIME,'hh24:mi:ss')+1/24,'hh24miss')  AND ((COMFRIM='N') OR (COMFRIM IS NULL))
```
</details>

<details><summary>+0x146F（226 字符，绑定变量 `:XEQUIP`, `:mi`, `:ss`）</summary>

```sql
select EQUIP_NO from USW_first where upd_date=TO_CHAR(SYSDATE,'YYYYMMDD') AND EQUIP_NO=:XEQUIP  and TO_CHAR(SYSDATE,'HH24MISS')>=to_char(TO_DATE(UPD_TIME,'hh24:mi:ss')+1/24,'hh24miss')  AND ((COMFRIM='N')  OR (STATUS IS NULL))
```
</details>

<details><summary>+0x1551（348 字符，绑定变量 `:XBAR`）</summary>

```sql
SELECT * FROM SIM53 A      LEFT JOIN SIM14 B ON A.ERROR_CODE=B.REASON_CODE       LEFT JOIN IVM81 C ON GET_MO_MODEL_NO(A.MO_NO)=C.PART_NO   WHERE A.INTERNAL_SR=:XBAR                 AND A.TEST_RESULT='FAIL' AND C.END_CUST_SHNM IN ('AMZ','AMAZON')  AND B.AOI_RC_MARK ='Y' AND A.INTERNAL_SR NOT IN (SELECT BARCODE FROM SIM556 WHERE MODEL_NO=C.PART_NO)
```
</details>

<details><summary>+0x1605（77 字符，绑定变量 `:XBAR`）</summary>

```sql
SELECT * FROM SIM203 WHERE bar_code=:XBAR AND (QC_EMP IS NULL OR QC_YN ='NG')
```
</details>

<details><summary>+0x16EA（49 字符，绑定变量 `:STATION`）</summary>

```sql
SELECT STATION FROM SIM02  WHERE STATION=:STATION
```
</details>

<details><summary>+0x17C9（206 字符，绑定变量 `:xbar`）</summary>

```sql
SELECT STATION,ROUTE_ID FROM SIM03 WHERE ROUTE_ID IN (SELECT  DISTINCT CUR_ROUTE_ID FROM SIM53 WHERE INTERNAL_sR =:xbar AND station='030'  AND INTERNAL_sR NOT IN (SELECT BARCODE FROM SIM556)) AND MOI_YN='Y'
```
</details>

<details><summary>+0x186D（223 字符，绑定变量 `:XROUTE`, `:XSTA`, `:xbar`）</summary>

```sql
SELECT COUNT(*) AS CNT FROM SIM224 WHERE MO_NO=(SELECT GET_MO_YN('',STATION,:xbar) FROM SIM03 where ROUTE_ID=:XROUTE and next_station=:XSTA AND ROWNUM=1)  AND (MOI_YN<>'Y' OR EMP_YN<>'Y' OR MOI_YN IS NULL OR EMP_YN IS NULL)
```
</details>

<details><summary>+0x19C0（355 字符，绑定变量 `:xbar`）</summary>

```sql
select error_code from sim071 where error_code = (  SELECT error_code FROM (SELECT * FROM sim53 where internal_Sr=:xbar AND  station='171'  and get_part_no(mo_no) in (select part_no from ivm81 where END_CUST_SHNM in('AMAZON','AMZ') and part_no not like 'ADH042-%' )  and TEST_RESULT='FAIL' order by upd_date||upd_time desc)where  rownum=1) and stop_yn='N'
```
</details>

<details><summary>+0x1A6D（71 字符，绑定变量 `:xbar`）</summary>

```sql
select internal_Sr from sim53 where internal_Sr=:xbar and station='040'
```
</details>

<details><summary>+0x1B38（191 字符，绑定变量 `:xbar`）</summary>

```sql
SELECT * FROM sim521 where pcb_sn in (Select BARCODE_NEW From SIM524 where barcode_new not like '9%' START WITH BARCODE_NEW = :xbar  connect by prior BARCODE_OLD = BARCODE_NEW )AND status='U'
```
</details>

<details><summary>+0x1BF9（61 字符，绑定变量 `:BARCODE`, `:station`）</summary>

```sql
select GET_MO_YN('TEST',:station,:BARCODE) AS MO_NO FROM DUAL
```
</details>

<details><summary>+0x1D10（481 字符，绑定变量 `:INTERNAL_SR`, `:STATION`）</summary>

```sql
SELECT DECODE((Select PARENT_MO_YN From APITECH.SIM02  Where STATION=:STATION),'N',MO_NO,PARENT_MO) MO_NO From APITECH.SIM40  WHERE  :INTERNAL_SR BETWEEN BAR_CODE_ST AND BAR_CODE_END  AND  BAR_CODE_ST LIKE 'AC%'   AND  BAR_CODE_END LIKE 'AC%'  and PART_NO IN ('RLOD-5SG06-711L-1','RLOD-5SG05-710L-1','CLOD-4SG02-711-1',  'RLOD-4SG02-711L-1','RLOD-4SG02-715L-1','RLOD-4SG02-716L-1','RLOD-SG7003-710G-1', 'RSG9006-710G-2','RLOD-SG7008-710G-1','RLOD-5SG06-711G-1','RLOD-4SG06-711L-1')
```
</details>

<details><summary>+0x1DDE（323 字符，绑定变量 `:MO_NO`）</summary>

```sql
SELECT MODEL_NO FROM (  SELECT APITECH.GET_MO_MODEL_NO(:MO_NO) AS MODEL_NO FROM DUAL)   WHERE MODEL_NO IN('API5SG06-711L','API5SG06-710L','API5SG05-710L','API4SG02-711L','API4SG02-713L', 'API4SG02-715L','API4SG02-711','API4SG02-710','SG7003-710L','SG8005-710L',  'SG9006-710G','SG7008-710G','API5SG06-711G','API4SG06-711L')
```
</details>

<details><summary>+0x208A（46 字符）</summary>

```sql
select to_char(sysdate,'hh24') xtime FROM DUAL
```
</details>

<details><summary>+0x2132（106 字符，绑定变量 `:EQUIP_NO`）</summary>

```sql
SELECT * FROM SIM230  WHERE prod_date = TO_CHAR(SYSDATE, 'yyyymmdd') AND EQUIP_NO=:EQUIP_NO AND INTERVAL='
```
</details>

<details><summary>+0x224A（150 字符，绑定变量 `:XBAR`）</summary>

```sql
SELECT * FROM ivm81 where SUBSTR(PART_NO,1,6) IN('PCL012','PCM010','FSL031') AND PART_NO=(SELECT PART_NO FROM sim94 WHERE BAR_CODE=:XBAR AND ROWNUM=1)
```
</details>

<details><summary>+0x2308（61 字符，绑定变量 `:XBAR`）</summary>

```sql
SELECT * FROM SIM53 WHERE INTERNAL_SR=:XBAR AND STATION='183'
```
</details>

<details><summary>+0x23ED（190 字符，绑定变量 `:XBAR`）</summary>

```sql
SELECT PART_NO FROM sim537 WHERE  PART_NO LIKE (SELECT '%'||get_sim92(PALLET_NO)||'%' PART_NO FROM sim98 WHERE (BAR_CODE=:XBAR OR  BAR_CODE2=:XBAR) AND ROWNUM=1) AND substr(TOOL_NO,1,2)='QC'
```
</details>

<details><summary>+0x24A1（100 字符，绑定变量 `:xpart`, `:xsta`）</summary>

```sql
SELECT * FROM sim646 WHERE UPD_DATE=TO_CHAR(SYSDATE,'YYYYMMDD') AND PART_NO=:xpart and station=:xsta
```
</details>

<details><summary>+0x25F5（60 字符，绑定变量 `:MO_NO`）</summary>

```sql
SELECT APITECH.GET_MO_MODEL_NO(:MO_NO) AS MODEL_NO FROM DUAL
```
</details>

<details><summary>+0x2714（356 字符，绑定变量 `:INTERNAL_SR`）</summary>

```sql
SELECT COUNT(*) XCNT FROM SIM43                  WHERE SON_PART IN (                 SELECT PART_NO FROM SIM40         WHERE MO_NO = (                   select mo_no from(                 SELECT MO_NO FROM SIM53            WHERE INTERNAL_SR=:INTERNAL_SR     AND STATION='030' order by upd_date||upd_time desc) where rownum=1))             AND PCB LIKE 'D%'
```
</details>

<details><summary>+0x27DE（75 字符，绑定变量 `:BAR_CODE`）</summary>

```sql
SELECT SAMPLE_YN  FROM APITECH.SIM551 WHERE BAR_CODE=:BAR_CODE AND ROWNUM=1
```
</details>

<details><summary>+0x28B4（62 字符，绑定变量 `:internal_sr`）</summary>

```sql
select STATUS from apitech.sim556  where barcode =:internal_sr
```
</details>

<details><summary>+0x29BC（170 字符，绑定变量 `:BARCODE`）</summary>

```sql
SELECT MODEL_NO FROM APITECH.SIM556 WHERE BARCODE =:BARCODE  and model_no=(select distinct get_mo_model_no(mo_no) from sim53 where internal_Sr=:BARCODE and station='030')
```
</details>

<details><summary>+0x2AFD（498 字符，绑定变量 `:INTERNAL_SR`）</summary>

```sql
SELECT END_CUST_SHNM      FROM IVM81   WHERE END_CUST_SHNM IN ('AMAZON', 'AMZ')       and part_no IN          (SELECT GET_MO_MODEL_NO(MO_NO)           FROM SIM53                      WHERE INTERNAL_SR = :INTERNAL_SR             AND STATION = '030'               AND CUR_ROUTE_ID IN (SELECT MAX(CUR_ROUTE_ID) CUR_ROUTE_ID                                  FROM SIM53                                           WHERE INTERNAL_SR = :INTERNAL_SR                                      AND STATION = '030'))
```
</details>

<details><summary>+0x2BF5（96 字符，绑定变量 `:INTERNAL_SR`）</summary>

```sql
SELECT PALLET_NO FROM SIM98 WHERE (BAR_CODE=:INTERNAL_SR OR BAR_CODE2=:INTERNAL_SR) AND ROWNUM=1
```
</details>

<details><summary>+0x2C9C（209 字符，绑定变量 `:STATION`, `:XPALLET`）</summary>

```sql
SELECT MODEL_PROGRAM FROM (select MODEL_PROGRAM from apitech.sim547 where MODEL_NO=(SELECT PART_NO FROM SIM92 WHERE PALLET_NO=:XPALLET)   AND STATION=:STATION   order by upd_Date||upd_time desc) where rownum=1
```
</details>

<details><summary>+0x2DBC（164 字符，绑定变量 `:MODEL_NO`, `:STATION`）</summary>

```sql
SELECT MODEL_PROGRAM FROM (select MODEL_PROGRAM from apitech.sim547 where MODEL_NO=:MODEL_NO AND STATION=:STATION   order by upd_Date||upd_time desc) where rownum=1
```
</details>

<details><summary>+0x2EAC（566 字符，绑定变量 `:INTERNAL_sR`, `:PART_NO`）</summary>

```sql
SELECT A.PART_NO, B.SON_PART, B.PART_NO             FROM (SELECT *                                          FROM SIM40                                     WHERE MO_NO =                                          (select * from (SELECT MO_NO                                       FROM SIM53                                        WHERE INTERNAL_SR = :INTERNAL_sR                            AND STATION = '030' order by upd_date||upd_time desc)where rownum=1)) A,                      (SELECT * FROM SIM43 WHERE PART_NO = :PART_NO) B         WHERE A.PART_NO = B.SON_PART
```
</details>

<details><summary>+0x3053（121 字符，绑定变量 `:MODEL_NO`, `:MODEL_PROGRAM`, `:STATION`）</summary>

```sql
select MODEL_PROGRAM from apitech.sim547 where MODEL_NO=:MODEL_NO AND STATION=:STATION   AND MODEL_PROGRAM=:MODEL_PROGRAM
```
</details>

<details><summary>+0x3580（110 字符，绑定变量 `:MODEL_NO`, `:STATION`, `:XMODEL_PROGRAM`）</summary>

```sql
select MODEL_NO from sim547  where MODEL_NO=:MODEL_NO AND STATION=:STATION   AND MODEL_PROGRAM=:XMODEL_PROGRAM
```
</details>

<details><summary>+0x385F（565 字符，绑定变量 `:INTERNAL_sR`, `:PART_NO`）</summary>

```sql
SELECT A.PART_NO, B.SON_PART, B.PART_NO             FROM (SELECT *                                          FROM SIM40                                     WHERE MO_NO =                                          (select * from(SELECT MO_NO                                       FROM SIM53                                        WHERE INTERNAL_SR = :INTERNAL_sR                            AND STATION = '030' order by upd_date||upd_time desc)where rownum=1)) A,                      (SELECT * FROM SIM43 WHERE PART_NO = :PART_NO) B         WHERE A.PART_NO = B.SON_PART
```
</details>

<details><summary>+0x3CB1（108 字符，绑定变量 `:MO_NO`, `:STATION`）</summary>

```sql
SELECT SAMPLE_YN FROM SIM03 WHERE ROUTE_ID=SUBSTR(APITECH.get_route_id(:MO_NO ),1,20) AND STATION = :STATION
```
</details>

<details><summary>+0x3D91（222 字符，绑定变量 `:INTERNAL_SR`）</summary>

```sql
SELECT END_CUST_SHNM   FROM IVM81   WHERE END_CUST_SHNM IN ('AMAZON', 'AMZ')  and part_no IN          (SELECT GET_MO_MODEL_NO(MO_NO)  FROM SIM53                     WHERE INTERNAL_SR = :INTERNAL_SR    AND STATION = '030' )
```
</details>

<details><summary>+0x3EDC（276 字符，绑定变量 `:EQUIP_NO`, `:MODEL_NO`, `:STATION`, `:XDATE`）</summary>

```sql
SELECT COUNT(*) XCNT FROM SIM53                                                 WHERE PROD_DATE =:XDATE               AND APITECH.get_mo_model_no(MO_NO) =:MODEL_NO                       AND STATION=:STATION                                                AND EQUIP_NO=:EQUIP_NO
```
</details>

<details><summary>+0x3FDF（351 字符，绑定变量 `:EQUIP_NO`, `:MO_NO`, `:STATION`, `:XDATE`）</summary>

```sql
select count(*)XCNT from sim53 where PROD_DATE =:XDATE               AND APITECH.get_mo_model_no(MO_NO) =GET_MO_MODEL_NO(:MO_NO)  AND EQUIP_NO=:EQUIP_NO                      AND STATION=:STATION  AND TEST_RESULT='PASS'                             AND INTERNAL_sR IN (select barcode from  SIM556 where MODEL_NO=GET_MO_MODEL_NO(:MO_NO) AND STATUS LIKE '
```
</details>

<details><summary>+0x4116（236 字符，绑定变量 `:EQUIP_NO`, `:STATION`, `:XDATE`）</summary>

```sql
SELECT TEST_RESULT,INTERNAL_SR,UPD_DATE,UPD_TIME  FROM    (SELECT * FROM SIM53 a  WHERE   PROD_DATE=:XDATE         AND STATION=:STATION   AND EQUIP_NO=:EQUIP_NO         and  INTERNAL_SR IN (SELECT BARCODE FROM SIM556 WHERE STATUS like '
```
</details>

<details><summary>+0x425E（25 字符）</summary>

```sql
select (sysdate-to_date('
```
</details>

<details><summary>+0x4536（258 字符，绑定变量 `:LINE_NAME`）</summary>

```sql
SELECT * FROM SIM17                         WHERE SHIFT_CLASS IN (                      SELECT SHIFT_CLASS FROM SIM11               WHERE LINE_NAME=:LINE_NAME )                AND TO_CHAR(SYSDATE,'HH24MI')>=START_TIME  AND TO_CHAR(SYSDATE,'HH24MI')<=END_TIME
```
</details>

<details><summary>+0x4717（521 字符，绑定变量 `:EQUIP_NO`, `:MODEL_NO`, `:PERIOD1`, `:PERIOD2`, `:STATION`, `:XDATE`）</summary>

```sql
SELECT COUNT(*) XCNT FROM SIM53                                                 WHERE PROD_DATE =:XDATE               AND APITECH.get_mo_model_no(MO_NO) =:MODEL_NO                       AND STATION=:STATION                                                AND ((PERIOD=:PERIOD1 ) OR (PERIOD=:PERIOD2 ))                     AND TEST_RESULT='PASS'                                            AND EQUIP_NO=:EQUIP_NO                                              AND INTERNAL_SR IN (SELECT BARCODE FROM SIM556 WHERE STATUS LIKE '
```
</details>

<details><summary>+0x4861（360 字符，绑定变量 `:EQUIP_NO`, `:MODEL_NO`, `:PERIOD1`, `:PERIOD2`, `:STATION`, `:XDATE`）</summary>

```sql
SELECT COUNT(*) XCNT FROM SIM53                                     WHERE ((PERIOD=:PERIOD1 ) OR (PERIOD=:PERIOD2 ))                    AND APITECH.get_mo_model_no(MO_NO) =:MODEL_NO                       AND STATION=:STATION                                                AND EQUIP_NO= :EQUIP_NO                                            AND PROD_DATE =:XDATE
```
</details>

<details><summary>+0x4A16（149 字符，绑定变量 `:line_name`, `:mo_no`）</summary>

```sql
select LINE_NAME from apitech.sim561 where line_name=:line_name and PART_NO in ( select part_no from apitech.sim40 where mo_no=:mo_no )and status='N'
```
</details>

<details><summary>+0x4B05（244 字符，绑定变量 `:EQUIP_NO`, `:MO_NO`, `:STATION`）</summary>

```sql
Select b.stop_value,c.yield_rate From sim40 a,sim531 b,sim532 c Where  a.mo_no= :MO_NO  And b.station= :STATION  And c.equip_no= :EQUIP_NO  And APITECH.get_model_no(a.part_no)=b.model_no  And b.model_no=c.model_no  And b.stop_value>c.yield_rate
```
</details>

<details><summary>+0x4C50（157 字符，绑定变量 `:MO_NO`）</summary>

```sql
SELECT     COVER_INTERVAL    FROM SIM01                               WHERE PART_NO=(Select PART_NO  From  SIM40                         Where MO_NO=:MO_NO )
```
</details>

<details><summary>+0x4D6C（576 字符，绑定变量 `:INTERNAL_SR`, `:STATION`）</summary>

```sql
SELECT  TEST_RESULT,RC_NO,INTERNAL_SR   FROM SIM53                                          WHERE INTERNAL_SR=:INTERNAL_SR                                  AND STATION=:STATION                                            AND (STATION<>'180' AND STATION<>'181')                     AND CUR_ROUTE_ID=                                               (SELECT NVL(MAX(NVL(CUR_ROUTE_ID,'XX')),'00')  FROM   SIM53   WHERE INTERNAL_SR=:INTERNAL_SR    )                              AND SEQ_NO=(SELECT MAX(SEQ_NO) FROM APITECH.SIM53 WHERE INTERNAL_SR  =:INTERNAL_SR AND STATION=:STATION )
```
</details>

<details><summary>+0x4E59（583 字符，绑定变量 `:INTERNAL_SR`, `:MO_NO`, `:STATION`）</summary>

```sql
SELECT  TEST_RESULT,RC_NO,INTERNAL_SR   FROM SIM53                                          WHERE INTERNAL_SR=:INTERNAL_SR                                  AND STATION=:STATION                                            AND (STATION<>'180' AND STATION<>'181')                                           AND CUR_ROUTE_ID=                                               (SELECT NVL(MAX(NVL(CUR_ROUTE_ID,'XX')),'00')  FROM   SIM53   WHERE INTERNAL_SR=:INTERNAL_SR AND MO_NO=:MO_NO   ) AND SEQ_NO=(SELECT MAX(SEQ_NO) FROM APITECH.SIM53 WHERE INTERNAL_SR =:INTERNAL_SR AND STATION=:STATION)
```
</details>

<details><summary>+0x4F3F（28 字符）</summary>

```sql
Select  nvl(ROUND((TO_DATE('
```
</details>

<details><summary>+0x4F70（57 字符）</summary>

```sql
(Select MAX(END_DATE||END_TIME) as FINAL_TIME from  SIM53
```
</details>

<details><summary>+0x508D（68 字符，绑定变量 `:STATION`）</summary>

```sql
SELECT * FROM APITECH.SIM02 WHERE STATION=:STATION AND RETEST_YN='Y'
```
</details>

<details><summary>+0x519D（452 字符，绑定变量 `:INTERNAL_SR`, `:STATION`）</summary>

```sql
SELECT  count(*) XCNT   FROM SIM53_log                                          WHERE INTERNAL_SR=:INTERNAL_SR                                  AND STATION=:STATION                                            AND (STATION<>'180' AND STATION<>'181')                                           AND CUR_ROUTE_ID=                                               (SELECT NVL(MAX(NVL(CUR_ROUTE_ID,'XX')),'00')  FROM   SIM53_log   WHERE INTERNAL_SR=:INTERNAL_SR )
```
</details>

<details><summary>+0x52C6（130 字符，绑定变量 `:INTERNAL_SR`, `:RC_NO`）</summary>

```sql
Select * From  SIM24                  WHERE RC_NO=:RC_NO AND INTERNAL_SR=:INTERNAL_SR                  AND   trim(RC_DATE) IS NULL
```
</details>

<details><summary>+0x5469（162 字符，绑定变量 `:STATION`, `:internal_sr`, `:mo_no`）</summary>

```sql
SELECT * FROM apitech.SIM24   where trim(rc_date) is null   and internal_sr =:internal_sr  and station <>:STATION  and GET_PARENT_MO(mo_no) =GET_PARENT_MO(:mo_no)
```
</details>

<details><summary>+0x55DD（204 字符，绑定变量 `:INTERNAL_SR`）</summary>

```sql
Select in_sr,instr(in_sr,'_') As TT From                   ( SELECT NVL(MAX(NVL(CUR_ROUTE_ID,'XX')),'00')         As in_sr  FROM   apitech.SIM53                           WHERE INTERNAL_SR=:INTERNAL_SR  )
```
</details>

<details><summary>+0x56DA（231 字符，绑定变量 `:INTERNAL_SR`, `:MO_NO`）</summary>

```sql
Select in_sr,instr(in_sr,'_') As TT From                   ( SELECT NVL(MAX(NVL(CUR_ROUTE_ID,'XX')),'00')         As in_sr  FROM   apitech.SIM53                           WHERE INTERNAL_SR=:INTERNAL_SR AND MO_NO=:MO_NO            )
```
</details>

<details><summary>+0x57C0（255 字符，绑定变量 `:MO_NO`, `:STATION`）</summary>

```sql
Select Level KK,A.STATION From     (Select * From  APITECH.SIM03    Where ROUTE_ID=SUBSTR(APITECH.GET_ROUTE_ID( :MO_NO ),1,18) ) A   Where BARCODE_CTRL='Y' And Level>1  Start With STATION= :STATION    Connect By Prior STATION=NEXT_STATION   Order By Level
```
</details>

<details><summary>+0x58A9（379 字符，绑定变量 `:INTERNAL_SR`, `:MO_NO`, `:STATION`）</summary>

```sql
SELECT * FROM (SELECT TEST_RESULT FROM SIM53                                    WHERE GET_ROUTE_ID(MO_NO)=SUBSTR(APITECH.GET_ROUTE_ID(:MO_NO ),1,18)  AND STATION = :STATION                                                 AND INTERNAL_SR = :INTERNAL_SR                                         ORDER BY UPD_DATE DESC,UPD_TIME DESC) A                                WHERE ROWNUM = 1
```
</details>

<details><summary>+0x5A6A（387 字符，绑定变量 `:MO_NO`, `:STATION`）</summary>

```sql
Select Level KK,A.* From                                             (Select * From SIM03                                              Where ROUTE_ID=SUBSTR(APITECH.GET_ROUTE_ID(:MO_NO),1,18) ) A   Where Level>1 And Rownum=1                 Start With STATION=:STATION                                       Connect By Prior STATION=NEXT_STATION                             Order By Level
```
</details>

<details><summary>+0x5B16（184 字符，绑定变量 `:INTERNAL_SR`, `:STATION`, `:cur_route_id`）</summary>

```sql
Select * From APITECH.SIM53  Where INTERNAL_SR=:INTERNAL_SR    And STATION=:STATION    AND STATION<>'071'        AND   cur_route_id=:cur_route_id       Order By UPD_DATE||UPD_TIME Desc
```
</details>

<details><summary>+0x5C68（230 字符，绑定变量 `:XCODE`, `:XMO`）</summary>

```sql
SELECT SUM(X) X1 FROM           (SELECT COUNT(*) X FROM ivm81 where end_cust_shnm in ('AMZ','AMAZON') AND PART_NO=get_mo_model_no(:XMO)   UNION  all       SELECT COUNT(*) X FROM sim14 where aoi_rc_mark ='Y' AND REASON_CODE=:XCODE)
```
</details>

<details><summary>+0x5DE3（408 字符，绑定变量 `:MO_NO`, `:STATION`）</summary>

```sql
Select Level KK,A.* From                                             (Select * From SIM03                                              Where ROUTE_ID=SUBSTR(APITECH.GET_ROUTE_ID(:MO_NO),1,18) ) A   Where BARCODE_CTRL='Y' And Level>1 And Rownum=1                 Start With STATION=:STATION                                       Connect By Prior STATION=NEXT_STATION                             Order By Level
```
</details>

<details><summary>+0x5F36（196 字符，绑定变量 `:cur_route_id`, `:internal_sr`, `:station`）</summary>

```sql
Select * From apitech.sim53            Where  cur_route_id=:cur_route_id      And    station=:station                And    internal_sr=:internal_sr        Order By UPD_DATE  Desc , upd_time  Desc
```
</details>

<details><summary>+0x630B（611 字符，绑定变量 `:MO_NO`, `:STATION`）</summary>

```sql
Select Level KK,A.* From   (Select    ROUTE_ID ,SEQ_NO,STATION,NEXT_STATION,BARCODE_CTRL,RC_ROUTE_ID,  PHANTOM_STATION,     PASS_INPUT_STATION,FAIL_INPUT_STATION, RC_REFLOW_STATION,SECTION_CODE,MO_STATION,MAPPING_CNT,      NVL(REFLOW_YN,'Y')   As  REFLOW    From  SIM03                            Where ROUTE_ID=SUBSTR(APITECH.GET_ROUTE_ID(:MO_NO),1,18) ) A                Where BARCODE_CTRL='Y'   And  REFLOW='Y'  And Level>1 And Rownum=1      Start With STATION=:STATION                                                 Connect By Prior STATION=NEXT_STATION                                       Order By Level
```
</details>

<details><summary>+0x63B7（115 字符，绑定变量 `:CUR_ROUTE_ID`, `:INTERNAL_SR`）</summary>

```sql
SELECT * FROM APITECH.SIM53 WHERE INTERNAL_SR=:INTERNAL_SR AND   CUR_ROUTE_ID=:CUR_ROUTE_ID  AND TEST_RESULT='FAIL'
```
</details>

<details><summary>+0x6473（76 字符，绑定变量 `:CUR_ROUTE_ID`）</summary>

```sql
SELECT SUBSTR(:CUR_ROUTE_ID,1,INSTR(:CUR_ROUTE_ID,'_')-1) AS ROUTE FROM DUAL
```
</details>

<details><summary>+0x650F（108 字符，绑定变量 `:ROUTE_ID`, `:STATION`）</summary>

```sql
SELECT RC_REFLOW_STATION,NDF_REFLOW_STATION FROM APITECH.SIM03 WHERE ROUTE_ID=:ROUTE_ID AND STATION=:STATION
```
</details>

<details><summary>+0x65F3（140 字符，绑定变量 `:INTERNAL_SR`, `:STATION`）</summary>

```sql
SELECT * FROM SIM24 WHERE RC_NO IN (SELECT RC_NO FROM SIM25 WHERE INTERNAL_SR=:INTERNAL_SR AND REASON_CODE LIKE '%024') AND STATION=:STATION
```
</details>

<details><summary>+0x6715（1012 字符，绑定变量 `:MO_NO`, `:RC_STATION`, `:STATION`）</summary>

```sql
Select Level KK,A.* From   (Select    ROUTE_ID ,SEQ_NO,STATION,NEXT_STATION,BARCODE_CTRL,RC_ROUTE_ID,  PHANTOM_STATION,  PASS_INPUT_STATION,FAIL_INPUT_STATION, RC_REFLOW_STATION,SECTION_CODE,MO_STATION,MAPPING_CNT,   NVL(REFLOW_YN,'Y')   As  REFLOW    From  APITECH.SIM03   Where ROUTE_ID=SUBSTR(APITECH.GET_ROUTE_ID(:MO_NO),1,18) ) A   Where BARCODE_CTRL='Y'   And  REFLOW='Y'  And Level>1 And Rownum=1   And STATION Not IN( Select STATION From (Select Level KK,A.* From   (Select    ROUTE_ID ,SEQ_NO,STATION,NEXT_STATION,BARCODE_CTRL,RC_ROUTE_ID,  PHANTOM_STATION,   PASS_INPUT_STATION,FAIL_INPUT_STATION, RC_REFLOW_STATION,SECTION_CODE,MO_STATION,MAPPING_CNT,  NVL(REFLOW_YN,'Y')   As  REFLOW    From  APITECH.SIM03   Where ROUTE_ID=SUBSTR(APITECH.GET_ROUTE_ID(:MO_NO),1,18) ) A  Where BARCODE_CTRL='Y'  And  REFLOW='Y'  And Level>1   Start With STATION=:RC_STATION  Connect By Prior STATION=NEXT_STATION   Order By Level ))   Start With STATION=:STATION  Connect By Prior STATION=NEXT_STATION  Order By Level
```
</details>

<details><summary>+0x681A（192 字符，绑定变量 `:cur_route_id`, `:internal_sr`, `:station`）</summary>

```sql
Select * From apitech.sim53           Where  cur_route_id=:cur_route_id     And    station=:station               And    internal_sr=:internal_sr       Order By UPD_DATE  Desc , upd_time  Desc
```
</details>

<details><summary>+0x6EBE（225 字符，绑定变量 `:INTERNAL_SR`）</summary>

```sql
SELECT BI_REFLOW FROM APITECH.SIM01              WHERE PART_NO =(                                  SELECT distinct GET_MO_MODEL_NO(MO_NO) FROM APITECH.SIM53 WHERE INTERNAL_SR=:INTERNAL_SR                    AND STATION='030')
```
</details>

<details><summary>+0x6FAA（281 字符，绑定变量 `:INTERNAL_SR`, `:ROUTE_ID`）</summary>

```sql
SELECT A.STATION FAIL_STATION FROM SIM53 A           WHERE A.CUR_ROUTE_ID = :ROUTE_ID                   AND A.TEST_RESULT='FAIL'                         AND A.STATION<>'171'                             AND A.STATION<>'071'                             AND A.INTERNAL_SR=:INTERNAL_SR
```
</details>

<details><summary>+0x72BC（99 字符，绑定变量 `:MO_NO`, `:STATION`）</summary>

```sql
SELECT SEQ_NO FROM APITECH.SIM03  WHERE STATION=:STATION  AND ROUTE_ID=APITECH.GET_ROUTE_ID(:MO_NO)
```
</details>

<details><summary>+0x74DA（278 字符，绑定变量 `:INTERNAL_SR`, `:MO_NO`）</summary>

```sql
SELECT * FROM APITECH.SIM53  WHERE STATION='091'  AND INTERNAL_SR= :INTERNAL_SR  AND TEST_RESULT='PASS'  And CUR_ROUTE_ID=(    Select Max(cur_route_id) From  APITECH.SIM53                                   Where INTERNAL_SR=:INTERNAL_SR                       AND MO_NO= :MO_NO )
```
</details>

<details><summary>+0x761F（109 字符，绑定变量 `:LINE_NAME`, `:MO_NO`）</summary>

```sql
SELECT * FROM APITECH.SIM160  WHERE STATUS='X' AND MODEL_NO=GET_MO_MODEL_NO(:MO_NO)  AND LINE_NAME=:LINE_NAME
```
</details>

<details><summary>+0x7722（74 字符，绑定变量 `:EMP_NO`, `:STATION`）</summary>

```sql
SELECT A.* FROM SIM501 A  WHERE A.EMP_NO =:EMP_NO   AND A.STATION=:STATION
```
</details>

<details><summary>+0x7811（168 字符，绑定变量 `:INTERNAL_SR`）</summary>

```sql
SELECT * FROM SIM125 WHERE INTERNAL_SR= :INTERNAL_SR AND ((LINE_OUTPUT_EMP IS NULL) OR (RC_INPUT_EMP IS NULL )  OR (RC_OUTPUT_EMP IS NULL ) OR (LINE_INPUT_EMP IS NULL))
```
</details>

<details><summary>+0x790B（981 字符，绑定变量 `:DIVISION_CODE`, `:EQUIP_NO`）</summary>

```sql
Select   asset_no   From fam20 a                     Where asset_stu='A' And TO_CHAR(Sysdate+5,'YYYYMMDD')>=NEXT_ARRIVE_DATE       And  Exists                                                                     (                                                                                Select * From (                                                                  Select   PARENT_EQUIP_NO As   EQUIP_NO From APITECH.SIM543                      Where  EQUIP_NO=:EQUIP_NO                                                    Union                                                                           Select    B.SON_EQUIP_NO  As   EQUIP_NO From APITECH.SIM543  A  ,APITECH.SIM544 B    Where A.EQUIP_NO=:EQUIP_NO                                                         And   A.PARENT_EQUIP_NO=B.PARENT_EQUIP_NO ) b                                        Where a.asset_no=b.equip_no      )                                                  AND DIVISION_CODE = :DIVISION_CODE
```
</details>

<details><summary>+0x7ABE（980 字符，绑定变量 `:DIVISION_CODE`, `:EQUIP_NO`）</summary>

```sql
Select   asset_no   From fam20 a                     Where asset_stu='A' And TO_CHAR(Sysdate+1,'YYYYMMDD')>=NEXT_ARRIVE_DATE      And  Exists                                                                    (                                                                               Select * From (                                                                  Select   PARENT_EQUIP_NO As   EQUIP_NO From APITECH.SIM543                      Where  EQUIP_NO=:EQUIP_NO                                                       Union                                                                           Select    B.SON_EQUIP_NO  As   EQUIP_NO From APITECH.SIM543  A  ,APITECH.SIM544 B    Where A.EQUIP_NO=:EQUIP_NO                                                         And   A.PARENT_EQUIP_NO=B.PARENT_EQUIP_NO ) b                                        Where a.asset_no=b.equip_no       )                                                AND DIVISION_CODE = :DIVISION_CODE
```
</details>

<details><summary>+0x7D50（93 字符，绑定变量 `:MO_NO`, `:STATION`）</summary>

```sql
SELECT PROBE_YN,PCB_QTY FROM SIM03 WHERE ROUTE_ID = GET_ROUTE_ID(:MO_NO) AND STATION=:STATION
```
</details>


#### `GREATR_BAR_CODE`（序数 17，RVA 0x8DB84，代码 8136 字节）

非 SQL 依赖：`D:\TEST_DATA1\`；`.txt`

<details><summary>+0x65（110 字符，绑定变量 `:MI`, `:SS`）</summary>

```sql
select to_char(TO_DATE(TO_CHAR(SYSDATE,'YYYYMMDDHH24:MI:SS'),'YYYYMMDDHH24:MI:SS'),'YYIWMM') AS YYIW from DUAL
```
</details>

<details><summary>+0x1FF（285 字符，绑定变量 `:MO_NO`）</summary>

```sql
SELECT PART_NO AS MODEL_NO       FROM (SELECT PART_NO               FROM SIM43                WHERE SON_PART IN                 (SELECT PART_NO                      FROM SIM40                        WHERE MO_NO = :MO_NO)          ORDER BY LENGTH(PART_NO) , part_no)    WHERE ROWNUM = 1
```
</details>

<details><summary>+0x2D4（407 字符，绑定变量 `:PART_NO`, `:SCAN_SEQ`）</summary>

```sql
SELECT MODEL_NO,PRE_FIXED_CD,   PRE_SEMIFIXED_LGTH,       (SELECT TO_CHAR(SYSDATE, 'YYIWMM') FROM DUAL) YYWW,      SN_LGTH,                   SN_1_RANGE_SEQ,            SN_OTH_RANGE_SEQ,          LAST_SEMIFIXED_LGTH,       LAST_FIXED_CD,             B.*                      FROM SIM117 A, SIM117A B      WHERE A.MODEL_NO =:PART_NO    AND A.SCAN_SEQ = :SCAN_SEQ    AND A.MODEL_NO=B.PART_NO(+)   AND ROWNUM=1
```
</details>

<details><summary>+0x4ED（174 字符，绑定变量 `:BARCODE1`, `:BARCODE2`, `:BARCODE3`, `:BARCODE4`, `:BARCODE5`, `:BARCODE6`, `:PART_NO`）</summary>

```sql
INSERT INTO SIM117A   (PART_NO,BARCODE1,BARCODE2,BARCODE3,BARCODE4,BARCODE5,BARCODE6)     VALUES        (:PART_NO,:BARCODE1,:BARCODE2,:BARCODE3,:BARCODE4,:BARCODE5,:BARCODE6)
```
</details>

<details><summary>+0xD20（122 字符，绑定变量 `:BARCODE1`, `:BARCODE2`, `:BARCODE3`, `:BARCODE4`, `:PART_NO`）</summary>

```sql
UPDATE SIM117A    SET BARCODE1=:BARCODE1,BARCODE2=:BARCODE2,BARCODE3=:BARCODE3,BARCODE4=:BARCODE4   WHERE PART_NO=:PART_NO
```
</details>

<details><summary>+0x1059（141 字符，绑定变量 `:BARCODE1`, `:BARCODE2`, `:BARCODE3`, `:BARCODE4`, `:BARCODE5`, `:PART_NO`）</summary>

```sql
UPDATE SIM117A    SET BARCODE1=:BARCODE1,BARCODE2=:BARCODE2,BARCODE3=:BARCODE3,BARCODE4=:BARCODE4,BARCODE5=:BARCODE5   WHERE PART_NO=:PART_NO
```
</details>

<details><summary>+0x140F（160 字符，绑定变量 `:BARCODE1`, `:BARCODE2`, `:BARCODE3`, `:BARCODE4`, `:BARCODE5`, `:BARCODE6`, `:PART_NO`）</summary>

```sql
UPDATE SIM117A    SET BARCODE1=:BARCODE1,BARCODE2=:BARCODE2,BARCODE3=:BARCODE3,BARCODE4=:BARCODE4,BARCODE5=:BARCODE5,BARCODE6=:BARCODE6   WHERE PART_NO=:PART_NO
```
</details>

<details><summary>+0x1B65（59 字符，绑定变量 `:BAR_CODE`）</summary>

```sql
select * from apitech.SIM639       WHERE BAR_CODE=:BAR_CODE
```
</details>

<details><summary>+0x1C11（249 字符，绑定变量 `:MI`, `:SS`, `:bar_code`, `:line_name`, `:mo_no`, `:station`, `:upd_user`）</summary>

```sql
INSERT INTO APITECH.SIM639(prod_date,line_name,bar_code,      station,mo_no,upd_user,upd_date,upd_time)  values(to_char(sysdate,'YYYYMMDD'),:line_name,:bar_code,    :station,:mo_no,:upd_user,to_char(sysdate,'YYYYMMDD'),to_char(sysdate,'HH24:MI:SS'))
```
</details>

<details><summary>+0x1D69（104 字符，绑定变量 `:barcode_OLD`, `:barcode_new`）</summary>

```sql
SELECT count(*) as xcount FROM sim524  where barcode_new =:barcode_new     or  barcode_OLD =:barcode_OLD
```
</details>


#### `ACBEL_MAPPING_STATION`（序数 16，RVA 0x907AC，代码 3789 字节）

<details><summary>+0x212（60 字符，绑定变量 `:MO_NO`）</summary>

```sql
SELECT APITECH.GET_MO_MODEL_NO(:MO_NO) AS MODEL_NO FROM DUAL
```
</details>

<details><summary>+0x308（1128 字符，绑定变量 `:LINE_NAME`）</summary>

```sql
Select * From (                                                                  SELECT   B.PERIOD,START_TIME,END_TIME,                                              Case  When  TO_NUMBER(SUBSTR(TO_CHAR(Sysdate,'HH24MISS'),1,2))<12  Then           (Case When START_TIME>END_TIME  Then  '0000'   Else START_TIME  End)               Else  START_TIME End    As  XSTART_TIME,                                           Case  When  TO_NUMBER(SUBSTR(TO_CHAR(Sysdate,'HH24MISS'),1,2)) >12  Then          (Case When START_TIME>END_TIME  Then  TO_CHAR(TO_NUMBER(( SUBSTR(END_TIME,1,2)+24)))||SUBSTR(END_TIME,3,2)   Else END_TIME  End)     Else  END_TIME End    As  XEND_TIME                                                FROM  APITECH.SIM11 A,APITECH.SIM17 B                                               WHERE A.SHIFT_CLASS=B.SHIFT_CLASS                                                   AND  A.LINE_NAME=:LINE_NAME                                                         )                                                                                   Where   SUBSTR(TO_CHAR(Sysdate,'HH24MISS'),1,4) between Xstart_time and Xend_time
```
</details>

<details><summary>+0x3E3（91 字符，绑定变量 `:LINE_NAME`）</summary>

```sql
SELECT LINE_NAME,LINE_NO FROM SIM11  WHERE LINE_NAME = :LINE_NAME  And NVL(STOP_YN,'N')='N'
```
</details>

<details><summary>+0x4B5（49 字符，绑定变量 `:STATION`）</summary>

```sql
SELECT STATION FROM SIM02  WHERE STATION=:STATION
```
</details>

<details><summary>+0x5EF（96 字符，绑定变量 `:barcode_new`, `:barcode_old`）</summary>

```sql
select * from apitech.sim524      where barcode_old=:barcode_old    and barcode_new=:barcode_new
```
</details>

<details><summary>+0x6F1（290 字符，绑定变量 `:MI`, `:SS`, `:barcode_new`, `:barcode_old`, `:line_name`, `:mo_no`, `:station`, `:upd_user`）</summary>

```sql
INSERT INTO APITECH.SIM524(prod_date,line_name,barcode_old,      barcode_new,upd_user,upd_date,upd_time,station,mo_no)            values(to_char(sysdate,'YYYYMMDD'),:line_name,:barcode_old,    :barcode_new,:upd_user,to_char(sysdate,'YYYYMMDD'),to_char(sysdate,'HH24:MI:SS'),:station,:mo_no)
```
</details>

<details><summary>+0x8AC（65 字符，绑定变量 `:XBARCODE`）</summary>

```sql
SELECT * FROM SIM53 WHERE INTERNAL_SR=:XBARCODE AND STATION='032'
```
</details>

<details><summary>+0x998（546 字符，绑定变量 `:CUR_ROUTE_ID`, `:EQUIP_NO`, `:INTERNAL_SR`, `:LINE_NAME`, `:LINE_NO`, `:MI`, `:MO_NO`, `:PERIOD`, `:SEQ_NO`, `:SS`, `:STATION`, `:TEST_RESULT`, `:UPD_USER`）</summary>

```sql
INSERT INTO SIM53     (MO_NO,PROD_DATE,LINE_NO,PERIOD,SEQ_NO,STATION,INTERNAL_SR,UPD_USER,UPD_DATE,UPD_TIME,TEST_RESULT,LINE_NAME,CUR_ROUTE_ID,   STATUS,START_TIME,END_TIME,START_DATE,END_DATE,EQUIP_NO)     VALUES    (:MO_NO,TO_CHAR(SYSDATE,'YYYYMMDD'),:LINE_NO,:PERIOD,:SEQ_NO,:STATION,:INTERNAL_SR,:UPD_USER,to_char(sysdate,'YYYYMMDD'),to_char(sysdate,'HH24:MI:SS'),   :TEST_RESULT,:LINE_NAME,:CUR_ROUTE_ID,'SIM132',to_char(sysdate,'HH24:MI:SS'),to_char(sysdate,'HH24:MI:SS'),  to_char(sysdate,'YYYYMMDD'),to_char(sysdate,'YYYYMMDD'),:EQUIP_NO)
```
</details>


#### `CHECK_PART_NO`（序数 15，RVA 0x92504，代码 347 字节）

<details><summary>+0x5D（285 字符，绑定变量 `:MO_NO`）</summary>

```sql
SELECT PART_NO AS MODEL_NO       FROM (SELECT PART_NO               FROM SIM43                WHERE SON_PART IN                 (SELECT PART_NO                      FROM SIM40                        WHERE MO_NO = :MO_NO)          ORDER BY LENGTH(PART_NO) , part_no)    WHERE ROWNUM = 1
```
</details>


#### `CHKUCL_LCL`（序数 14，RVA 0x927DC，代码 890 字节）

<details><summary>+0x75（55 字符，绑定变量 `:EQUIP_NO`）</summary>

```sql
SELECT USW_SPECI FROM SIM115_2 WHERE EQUIP_NO=:EQUIP_NO
```
</details>

<details><summary>+0x10B（87 字符，绑定变量 `:MO_NO`, `:USW_SPECI`）</summary>

```sql
SELECT * FROM SIM115_1 WHERE PART_NO=get_mo_model_no(:MO_NO) AND USW_SPECI = :USW_SPECI
```
</details>


#### `ACBEL_SYS_DATA`（序数 11，RVA 0x93038，代码 1005 字节）

<details><summary>+0x15E（43 字符）</summary>

```sql
SELECT MID_COMPOPAL.NEXTVAL mcode FROM DUAL
```
</details>

<details><summary>+0x1D8（264 字符，绑定变量 `:LINE_NO`, `:MI`, `:MID`, `:MO_NO`, `:QEQUID_NO`, `:QUL_data`, `:QUL_status`, `:Qcrt_user`, `:Qremark`, `:SS`, `:XSTATION`, `:XX`）</summary>

```sql
INSERT INTO tbmas (MID,LINE_NO,MO_NO,STATION,UL_data,UL_status,remark,crt_user,EQUID_NO,X,UPD_DATE,UPD_TIME )VALUES (:MID,:LINE_NO,:MO_NO,:XSTATION,:QUL_data,:QUL_status,:Qremark,:Qcrt_user ,:QEQUID_NO,:XX,TO_CHAR(SYSDATE,'YYYYMMDD'),TO_CHAR(SYSDATE,'HH24:MI:SS'))
```
</details>


#### `ACBEL_INSERT_DATA`（序数 12，RVA 0x937CC，代码 28878 字节）

<details><summary>+0x3BA（1131 字符，绑定变量 `:LINE_NAME`）</summary>

```sql
Select period From (                                                                SELECT   B.PERIOD,START_TIME,END_TIME,                                              Case  When  TO_NUMBER(SUBSTR(TO_CHAR(Sysdate,'HH24MISS'),1,2))<12  Then           (Case When START_TIME>END_TIME  Then  '0000'   Else START_TIME  End)               Else  START_TIME End    As  XSTART_TIME,                                           Case  When  TO_NUMBER(SUBSTR(TO_CHAR(Sysdate,'HH24MISS'),1,2)) >12  Then          (Case When START_TIME>END_TIME  Then  TO_CHAR(TO_NUMBER(( SUBSTR(END_TIME,1,2)+24)))||SUBSTR(END_TIME,3,2)   Else END_TIME  End)     Else  END_TIME End    As  XEND_TIME                                                FROM  APITECH.SIM11 A,APITECH.SIM17 B                                               WHERE A.SHIFT_CLASS=B.SHIFT_CLASS                                                   AND  A.LINE_NAME=:LINE_NAME                                                         )                                                                                   Where   SUBSTR(TO_CHAR(Sysdate,'HH24MISS'),1,4) between Xstart_time and Xend_time
```
</details>

<details><summary>+0x489（43 字符）</summary>

```sql
SELECT LINE_NO FROM SIM11 WHERE LINE_NAME='
```
</details>

<details><summary>+0x6EB（86 字符，绑定变量 `:BAR_CODE`）</summary>

```sql
SELECT COUNT(*) AS CNT  FROM APITECH.SIM551 WHERE BAR_CODE=:BAR_CODE AND SAMPLE_YN='Y'
```
</details>

<details><summary>+0x7C0（438 字符，绑定变量 `:BARCODE`）</summary>

```sql
SELECT MO_NO FROM (select  A.MO_NO from (select mo_no,prod_date,upd_date,upd_time,line_name    from sim42  where prod_date = to_char(sysdate, 'yyyymmdd') AND MO_NO  NOT LIKE 'R%') a,      (select mo_no,part_no,substr(mo_no,-6,3) as xl from APITECH.SIM40 where part_no in (select model_no from APITECH.SIM556 WHERE BARCODE =:BARCODE) )b   where a.mo_no = b.mo_no  and b.xl=a.line_name order by a.upd_date || a.upd_time desc) WHERE ROWNUM=1
```
</details>

<details><summary>+0x887（65 字符，绑定变量 `:internal_sr`, `:mo_no`, `:station`）</summary>

```sql
select get_mo_yn(:mo_no,:station,:internal_sr) AS MO_NO from dual
```
</details>

<details><summary>+0x9C2（123 字符，绑定变量 `:INTERNAL_SR`）</summary>

```sql
SELECT INTERNAL_SR FROM SIM53        WHERE INTERNAL_SR = :INTERNAL_SR  AND STATION='087' ORDER BY UPD_DATE ASC,UPD_TIME ASC
```
</details>

<details><summary>+0xA81（804 字符，绑定变量 `:CUR_ROUTE_ID`, `:END_DATE`, `:END_TIME`, `:EQUIP_NO`, `:ERROR_CODE`, `:INTERNAL_SR`, `:LINE_NAME`, `:LINE_NAME1`, `:LINE_NO`, `:MO_NO`, `:OUT_BARCODE`, `:PERIOD`, `:PROD_DATE`, `:RC_EMP_NO`, `:RC_NO`, `:RESULT`, `:SEQ_NO`, `:START_DATE`, `:START_TIME`, `:STATION`, `:TEST_DATA1`, `:TEST_DATA2`, `:TEST_DATA3`, `:TEST_DATA4`, `:TEST_DATE5`, `:TEST_PROGRAM`, `:TEST_RESULT`, `:TOOL_NO`, `:TOOL_NO1`, `:TOOL_NO2`, `:TOOL_NO3`, `:TOOL_NO4`, `:TOOL_NO5`, `:TOOL_NO6`, `:UPD_DATE`, `:UPD_TIME`, `:UPD_USER`）</summary>

```sql
INSERT INTO SIM53  (MO_NO,PROD_DATE,LINE_NO,PERIOD,SEQ_NO,STATION,INTERNAL_SR,TEST_RESULT,UPD_USER,UPD_DATE,UPD_TIME,RESULT,RC_NO,LINE_NAME,RC_EMP_NO,  EQUIP_NO,STATUS,CUR_ROUTE_ID,START_TIME,END_TIME,ERROR_CODE,START_DATE,END_DATE,TEST_PROGRAM,TEST_DATA1,TEST_DATA2,TEST_DATA3,TEST_DATA4,  LINE_NAME1,OUT_BARCODE,TOOL_NO1,TOOL_NO,TOOL_NO2,TOOL_NO3,TOOL_NO4,TOOL_NO5,TOOL_NO6,TEST_DATE5)  VALUES  (:MO_NO,:PROD_DATE,:LINE_NO,:PERIOD,:SEQ_NO,:STATION,:INTERNAL_SR,:TEST_RESULT,:UPD_USER,:UPD_DATE,:UPD_TIME,:RESULT,:RC_NO,:LINE_NAME,:RC_EMP_NO,  :EQUIP_NO,'LS_DLL',:CUR_ROUTE_ID,:START_TIME,:END_TIME,:ERROR_CODE,:START_DATE,:END_DATE,:TEST_PROGRAM,:TEST_DATA1,:TEST_DATA2,:TEST_DATA3,:TEST_DATA4,  :LINE_NAME1,:OUT_BARCODE,:TOOL_NO1,:TOOL_NO,:TOOL_NO2,:TOOL_NO3,:TOOL_NO4,:TOOL_NO5,:TOOL_NO6,:TEST_DATE5)
```
</details>

<details><summary>+0x1083（94 字符，绑定变量 `:INTERNAL_SR`, `:MO_NO`, `:STATION`, `:TEST_RESULT`）</summary>

```sql
SELECT APITECH.GET_SR_ROUTE(:MO_NO,:INTERNAL_SR,:TEST_RESULT,:STATION)  AS SR_ROUTE  FROM DUAL
```
</details>

<details><summary>+0x12FD（106 字符，绑定变量 `:BARCODE_NEW`, `:BARCODE_OLD`）</summary>

```sql
select BARCODE_OLD from apitech.SIM638      WHERE (BARCODE_OLD=:BARCODE_OLD  and BARCODE_NEW=:BARCODE_NEW)
```
</details>

<details><summary>+0x1405（280 字符，绑定变量 `:MI`, `:SS`, `:barcode_new`, `:barcode_old`, `:line_name`, `:mo_no`, `:station`, `:upd_user`）</summary>

```sql
INSERT INTO APITECH.SIM638(prod_date,line_name,barcode_old,      barcode_new,station,mo_no,upd_user,upd_date,upd_time)  values(to_char(sysdate,'YYYYMMDD'),:line_name,:barcode_old,    :barcode_new,:station,:mo_no,:upd_user,to_char(sysdate,'YYYYMMDD'),to_char(sysdate,'HH24:MI:SS'))
```
</details>

<details><summary>+0x15BE（106 字符，绑定变量 `:barcode_new`, `:barcode_old`）</summary>

```sql
select barcode_old from apitech.sim524      where barcode_old=:barcode_old    and barcode_new=:barcode_new
```
</details>

<details><summary>+0x16C6（290 字符，绑定变量 `:MI`, `:SS`, `:barcode_new`, `:barcode_old`, `:line_name`, `:mo_no`, `:station`, `:upd_user`）</summary>

```sql
INSERT INTO APITECH.SIM524(prod_date,line_name,barcode_old,      barcode_new,upd_user,upd_date,upd_time,station,mo_no)            values(to_char(sysdate,'YYYYMMDD'),:line_name,:barcode_old,    :barcode_new,:upd_user,to_char(sysdate,'YYYYMMDD'),to_char(sysdate,'HH24:MI:SS'),:station,:mo_no)
```
</details>

<details><summary>+0x18B4（613 字符，绑定变量 `:INTERNAL_SR`）</summary>

```sql
SELECT MO_NO,PROD_DATE,LINE_NO,LINE_NAME,PERIOD,STATION,SEQ_NO,ERROR_CODE,INTERNAL_SR,TEST_RESULT,TEMPERATURE,VOLTAGE,MOVE_TO,REMARK,      RESULT,EQUIP_NO,SUB_STATION,RC_NO,LINE_NAME,UPD_USER,UPD_DATE,UPD_TIME,RC_EMP_NO,LOCATION,CUR_ROUTE_ID,CART_NO,BI_ROOM_NO,LAYER,POSITION,       TEST_START_TIME,TEST_FAIL_TIME,FAIL_HRS,TOOL_NO,START_TIME,END_TIME, START_DATE,FAIL_DATE,TEST_HRS,END_DATE,TEST_PROGRAM, TEST_DATA1,TEST_DATA2,TEST_DATA3,TEST_DATA4,LINE_NAME1,STATUS,TOOL_NO1,TOOL_NO2,TOOL_NO3,TOOL_NO4,TOOL_NO5,TOOL_NO6,TEST_DATE5 FROM SIM53  WHERE INTERNAL_SR = :INTERNAL_SR   ORDER BY UPD_DATE ASC,UPD_TIME ASC
```
</details>

<details><summary>+0x1969（559 字符）</summary>

```sql
INSERT INTO APITECH.SIM53         (MO_NO,PROD_DATE,LINE_NO,LINE_NAME,PERIOD,STATION,SEQ_NO,ERROR_CODE,INTERNAL_SR,TEST_RESULT,TEMPERATURE,VOLTAGE,MOVE_TO,REMARK,      RESULT,EQUIP_NO,SUB_STATION,RC_NO,UPD_USER,UPD_DATE,UPD_TIME,RC_EMP_NO,LOCATION,CUR_ROUTE_ID,CART_NO,BI_ROOM_NO,LAYER,POSITION,       TEST_START_TIME,TEST_FAIL_TIME,FAIL_HRS,TOOL_NO,START_TIME,END_TIME, START_DATE,FAIL_DATE,TEST_HRS,END_DATE,TEST_PROGRAM,TEST_DATA1,TEST_DATA2,TEST_DATA3,TEST_DATA4,LINE_NAME1,STATUS,TOOL_NO1,TOOL_NO2,TOOL_NO3,TOOL_NO4,TOOL_NO5,TOOL_NO6,TEST_DATE5)    VALUES
```
</details>

<details><summary>+0x23BC（300 字符，绑定变量 `:INTERNAL_sR`）</summary>

```sql
SELECT RC_NO,MO_NO,LINE_NO,STATION,ERROR_CODE,TEST_COND,FAILURE,FAIL_DESC,CATEGORY,REMARK,UPD_DATE,UPD_USER,UPD_TIME,SCRAP,EMP_NO,RC_DATE , PROD_DATE,INTERNAL_SR,RC,PE_YN,LINE_NAME,RC_EMP_NO,MODEL_NO,PE_MARK FROM sim24 where internal_Sr=(SELECT barcode_old FROM sim524 where barcode_new=:INTERNAL_sR)
```
</details>

<details><summary>+0x2482（527 字符，绑定变量 `:CATEGORY`, `:EMP_NO`, `:ERROR_CODE`, `:FAILURE`, `:FAIL_DESC`, `:INTERNAL_SR`, `:LINE_NAME`, `:LINE_NO`, `:MODEL_NO`, `:MO_NO`, `:PE_MARK`, `:PE_YN`, `:PROD_DATE`, `:RC`, `:RC_DATE`, `:RC_EMP_NO`, `:RC_NO`, `:REMARK`, `:SCRAP`, `:STATION`, `:TEST_COND`, `:UPD_DATE`, `:UPD_TIME`, `:UPD_USER`）</summary>

```sql
update apitech.SIM24 set          MO_NO = :MO_NO, LINE_NO = :LINE_NO,STATION = :STATION, ERROR_CODE = :ERROR_CODE, TEST_COND = :TEST_COND, FAILURE = :FAILURE, FAIL_DESC = :FAIL_DESC, CATEGORY =:CATEGORY, REMARK = :REMARK, UPD_DATE = :UPD_DATE,UPD_USER=:UPD_USER,UPD_TIME=:UPD_TIME,SCRAP=:SCRAP,EMP_NO =:EMP_NO, RC_DATE = :RC_DATE, PROD_DATE = :PROD_DATE, RC = :RC, PE_YN = :PE_YN, LINE_NAME = :LINE_NAME, RC_EMP_NO = :RC_EMP_NO, MODEL_NO = :MODEL_NO, PE_MARK = :PE_MARK      where  RC_NO = :RC_NO and INTERNAL_SR = :INTERNAL_SR
```
</details>

<details><summary>+0x2A21（101 字符，绑定变量 `:INTERNAL_sR`）</summary>

```sql
SELECT * FROM sim25 where internal_Sr=(SELECT barcode_old FROM sim524 where barcode_new=:INTERNAL_sR)
```
</details>

<details><summary>+0x2AE1（360 字符，绑定变量 `:DATE_CODE`, `:DEAL_METHOD`, `:INTERNAL_SR`, `:LOCATION`, `:LOCATION_FOR`, `:LOT_NO`, `:MANUFACTOR`, `:MTL_PKG_ID`, `:NDF_CHECK_ITEM`, `:NEW_DATE_CODE`, `:NEW_LOT_NO`, `:NEW_MANUFACTOR`, `:NEW_PART_NO`, `:NEW_SPEC`, `:PART_NO`, `:PE_CATEGORY`, `:RC_LOT_LIST`, `:RC_NO`, `:REASON_CODE`, `:REASON_CODE_OLD`, `:REMARK`, `:SECTION_NAME`, `:SEQ_NO`, `:SPEC`, `:UPD_DATE`, `:UPD_TIME`, `:UPD_USER`, `:VENDOR_SHNM`）</summary>

```sql
Insert into sim25 values (:RC_NO,:SEQ_NO,:LOCATION,:SPEC,:MANUFACTOR,:DATE_CODE,:UPD_DATE,  :UPD_USER,:UPD_TIME,  :REASON_CODE,:REMARK,:LOCATION_FOR,:PART_NO,:NEW_PART_NO,:NEW_SPEC,:NEW_MANUFACTOR, :NEW_DATE_CODE,:DEAL_METHOD,  :PE_CATEGORY,:SECTION_NAME,:REASON_CODE_OLD,:NEW_LOT_NO,:INTERNAL_SR,:LOT_NO,:NDF_CHECK_ITEM,:MTL_PKG_ID, :VENDOR_SHNM,:RC_LOT_LIST)
```
</details>

<details><summary>+0x3712（65 字符，绑定变量 `:XTA`）</summary>

```sql
SELECT COUNT(*) AS CNT FROM SIM02 WHERE STATION=:XTA AND TYPE='1'
```
</details>

<details><summary>+0x37E8（89 字符，绑定变量 `:MO_NO`, `:barcode`）</summary>

```sql
select barcode from  SIM556 where barcode = :barcode AND MODEL_NO=GET_MO_MODEL_NO(:MO_NO)
```
</details>

<details><summary>+0x3AAF（114 字符）</summary>

```sql
SELECT rowidtochar(ROWID) AS ID,A.RESULT,A.TEST_RESULT,A.RC_NO,START_DATE||START_TIME FINAL_TIME, ROUND((TO_DATE('
```
</details>

<details><summary>+0x3AD0（60 字符）</summary>

```sql
SELECT MAX(START_DATE||START_TIME) as FINAL_TIME from  SIM53
```
</details>

<details><summary>+0x3D4C（157 字符，绑定变量 `:MO_NO`）</summary>

```sql
SELECT     COVER_INTERVAL    FROM SIM01                               WHERE PART_NO=(Select PART_NO  From  SIM40                         Where MO_NO=:MO_NO )
```
</details>

<details><summary>+0x3E5D（66 字符，绑定变量 `:STATION`）</summary>

```sql
SELECT STATION FROM SIM02 WHERE STATION=:STATION AND RETEST_YN='N'
```
</details>

<details><summary>+0x3F32（97 字符，绑定变量 `:XBAR`, `:XRC`）</summary>

```sql
select RC_NO  from  sim24  where  RC_NO= :XRC And trim(rc_date) is not null AND INTERNAL_SR=:XBAR
```
</details>

<details><summary>+0x4BE8（605 字符，绑定变量 `:INTERNAL_SR`）</summary>

```sql
SELECT MO_NO,PROD_DATE,LINE_NO,LINE_NAME,PERIOD,STATION,SEQ_NO,ERROR_CODE,INTERNAL_SR,TEST_RESULT,TEMPERATURE,VOLTAGE,MOVE_TO,REMARK,      RESULT,EQUIP_NO,SUB_STATION,RC_NO,UPD_USER,UPD_DATE,UPD_TIME,RC_EMP_NO,LOCATION,CUR_ROUTE_ID,CART_NO,BI_ROOM_NO,LAYER,POSITION,       TEST_START_TIME,TEST_FAIL_TIME,FAIL_HRS,TOOL_NO,START_TIME,END_TIME, START_DATE,FAIL_DATE,TEST_HRS,END_DATE,TEST_PROGRAM, TEST_DATA1,TEST_DATA2,TEST_DATA3,TEST_DATA4,LINE_NAME1,STATUS,TOOL_NO1,TOOL_NO2,TOOL_NO3,TOOL_NO4,TOOL_NO5,TOOL_NO6,TEST_DATE5 FROM SIM53    WHERE INTERNAL_SR = :INTERNAL_SR   ORDER BY UPD_DATE ASC,UPD_TIME ASC
```
</details>

<details><summary>+0x56E6（101 字符，绑定变量 `:INTERNAL_sR`）</summary>

```sql
SELECT * FROM sim24 where internal_Sr=(SELECT barcode_old FROM sim524 where barcode_new=:INTERNAL_sR)
```
</details>

<details><summary>+0x5D4B（427 字符，绑定变量 `:INTERNAL_sR`）</summary>

```sql
SELECT  RC_NO, SEQ_NO, LOCATION, SPEC, MANUFACTOR, DATE_CODE, UPD_DATE, UPD_USER, UPD_TIME, REASON_CODE, REMARK, LOCATION_FOR, PART_NO, NEW_PART_NO,  NEW_SPEC, NEW_MANUFACTOR, NEW_DATE_CODE, DEAL_METHOD, PE_CATEGORY, SECTION_NAME, REASON_CODE_OLD, NEW_LOT_NO, INTERNAL_SR, LOT_NO, NDF_CHECK_ITEM,  MTL_PKG_ID, VENDOR_SHNM, RC_LOT_LIST FROM sim25 where internal_Sr=(SELECT barcode_old FROM sim524 where barcode_new=:INTERNAL_sR)
```
</details>

<details><summary>+0x5E0B（689 字符，绑定变量 `:DATE_CODE`, `:DEAL_METHOD`, `:INTERNAL_SR`, `:LOCATION`, `:LOCATION_FOR`, `:LOT_NO`, `:MANUFACTOR`, `:MTL_PKG_ID`, `:NDF_CHECK_ITEM`, `:NEW_DATE_CODE`, `:NEW_LOT_NO`, `:NEW_MANUFACTOR`, `:NEW_PART_NO`, `:NEW_SPEC`, `:PART_NO`, `:PE_CATEGORY`, `:RC_LOT_LIST`, `:RC_NO`, `:REASON_CODE`, `:REASON_CODE_OLD`, `:REMARK`, `:SECTION_NAME`, `:SEQ_NO`, `:SPEC`, `:UPD_DATE`, `:UPD_TIME`, `:UPD_USER`, `:VENDOR_SHNM`）</summary>

```sql
Insert into sim25   ( RC_NO, SEQ_NO, LOCATION, SPEC, MANUFACTOR, DATE_CODE, UPD_DATE, UPD_USER, UPD_TIME, REASON_CODE, REMARK, LOCATION_FOR, PART_NO, NEW_PART_NO, NEW_SPEC, NEW_MANUFACTOR, NEW_DATE_CODE, DEAL_METHOD, PE_CATEGORY, SECTION_NAME, REASON_CODE_OLD, NEW_LOT_NO, INTERNAL_SR, LOT_NO, NDF_CHECK_ITEM, MTL_PKG_ID, VENDOR_SHNM, RC_LOT_LIST)  values (:RC_NO,:SEQ_NO,:LOCATION,:SPEC,:MANUFACTOR,:DATE_CODE,:UPD_DATE, :UPD_USER,:UPD_TIME,:REASON_CODE,:REMARK,:LOCATION_FOR,:PART_NO,:NEW_PART_NO,  :NEW_SPEC,:NEW_MANUFACTOR,:NEW_DATE_CODE,:DEAL_METHOD,  :PE_CATEGORY,:SECTION_NAME,:REASON_CODE_OLD,:NEW_LOT_NO,:INTERNAL_SR, :LOT_NO,:NDF_CHECK_ITEM,:MTL_PKG_ID,:VENDOR_SHNM,:RC_LOT_LIST)
```
</details>

<details><summary>+0x6527（655 字符，绑定变量 `:INTERNAL_SR`）</summary>

```sql
SELECT MO_NO,PROD_DATE,LINE_NO,LINE_NAME,PERIOD,STATION,SEQ_NO,ERROR_CODE,INTERNAL_SR,TEST_RESULT,TEMPERATURE,VOLTAGE,MOVE_TO,REMARK,      RESULT,EQUIP_NO,SUB_STATION,RC_NO,UPD_USER,UPD_DATE,UPD_TIME,RC_EMP_NO,LOCATION,CUR_ROUTE_ID,CART_NO,BI_ROOM_NO,LAYER,POSITION,       TEST_START_TIME,TEST_FAIL_TIME,FAIL_HRS,TOOL_NO,START_TIME,END_TIME, START_DATE,FAIL_DATE,TEST_HRS,END_DATE,TEST_PROGRAM,TEST_DATA1,TEST_DATA2,TEST_DATA3,   TEST_DATA4,LINE_NAME1,STATUS,TOOL_NO1,TOOL_NO2,TOOL_NO3,TOOL_NO4,TOOL_NO5,TOOL_NO6,TEST_DATE5 from (select *  FROM SIM53   WHERE INTERNAL_SR = :INTERNAL_SR  AND STATION='097'  order by upd_date||upd_time desc ) where rownum=1
```
</details>

<details><summary>+0x65FD（559 字符）</summary>

```sql
INSERT INTO APITECH.SIM53         (MO_NO,PROD_DATE,LINE_NO,LINE_NAME,PERIOD,STATION,SEQ_NO,ERROR_CODE,INTERNAL_SR,TEST_RESULT,TEMPERATURE,VOLTAGE,MOVE_TO,REMARK,      RESULT,EQUIP_NO,SUB_STATION,RC_NO,TEST_PROGRAM,UPD_USER,UPD_DATE,UPD_TIME,RC_EMP_NO,LOCATION,CUR_ROUTE_ID,CART_NO,BI_ROOM_NO,LAYER,POSITION,       TEST_START_TIME,TEST_FAIL_TIME,FAIL_HRS,TOOL_NO,START_TIME,END_TIME, START_DATE,FAIL_DATE,TEST_HRS,END_DATE,TEST_DATA1,TEST_DATA2,TEST_DATA3,TEST_DATA4,LINE_NAME1,STATUS,TOOL_NO1,TOOL_NO2,TOOL_NO3,TOOL_NO4,TOOL_NO5,TOOL_NO6,TEST_DATE5)    VALUES
```
</details>


#### `CHANGE_BARCODE_DC`（序数 10，RVA 0x9E474，代码 7329 字节）

<details><summary>+0xC6（127 字符，绑定变量 `:MO_NO`）</summary>

```sql
SELECT ACBEL_MODEL AS  MODEL_NO FROM  ls_amz_newpart  WHERE ACBEL_MODEL= (SELECT GET_MO_MODEL_NO(:MO_NO) AS MODEL_NO FROM DUAL)
```
</details>

<details><summary>+0x189（48 字符）</summary>

```sql
select to_char(sysdate,'hh24MI') xtime FROM DUAL
```
</details>

<details><summary>+0x225（88 字符）</summary>

```sql
select to_char(TO_DATE(TO_CHAR(SYSDATE,'YYYYMMDD'),'YYYYMMDD'),'YIWD') AS YIWD from DUAL
```
</details>

<details><summary>+0x2F4（90 字符）</summary>

```sql
select to_char(TO_DATE(TO_CHAR(SYSDATE-1,'YYYYMMDD'),'YYYYMMDD'),'YIWD') AS YIWD from DUAL
```
</details>

<details><summary>+0x3AD（24 字符）</summary>

```sql
SELECT A.PRE_FIXED_CD||'
```
</details>

<details><summary>+0x3DD（108 字符）</summary>

```sql
AND SCAN_SEQ = '1'    AND ROWNUM = 1 AND VERSION=(select MAX(VERSION) from SIM117 WHERE MODEL_NO=A.MODEL_NO)
```
</details>

<details><summary>+0x4D3（54 字符）</summary>

```sql
SELECT PHASE_CODE FROM SIM70_IE_PHASE WHERE PART_NO= '
```
</details>

<details><summary>+0x60F（180 字符，绑定变量 `:DDMO_NO`, `:NEW_BE`）</summary>

```sql
select COUNT(*) AS CNT from sim53 where internal_Sr=:NEW_BE and station=(select STATION from sim03  where route_id=GET_ROUTE_ID(:DDMO_NO) AND REPLACE_CODE IS NOT NULL AND ROWNUM=1)
```
</details>

<details><summary>+0x6BF（57 字符，绑定变量 `:NEW_BE`）</summary>

```sql
SELECT COUNT(*) AS CNT1 FROM SIM94 WHERE BAR_CODE=:NEW_BE
```
</details>

<details><summary>+0x79B（70 字符，绑定变量 `:MODEL_NO`, `:NEW_BAR_CODE`）</summary>

```sql
SELECT BARCODE_RETRY_NEW(:NEW_BAR_CODE,:MODEL_NO) AS NEW_BAR FROM DUAL
```
</details>

<details><summary>+0x8CD（95 字符，绑定变量 `:BARCODE_NEW`, `:BARCODE_OLD`）</summary>

```sql
select BARCODE_OLD from SIM638   WHERE (BARCODE_OLD=:BARCODE_OLD   OR BARCODE_NEW=:BARCODE_NEW)
```
</details>

<details><summary>+0x99B（62 字符，绑定变量 `:BARCODE_NEW`）</summary>

```sql
select INTERNAL_SR from SIM53   WHERE INTERNAL_SR=:BARCODE_NEW
```
</details>

<details><summary>+0xA78（90 字符）</summary>

```sql
SELECT PRE_FIXED_CD,PRE_SEMIFIXED_LGTH,SN_LGTH,LAST_FIXED_CD from SIM117 WHERE MODEL_NO= '
```
</details>

<details><summary>+0xC20（69 字符，绑定变量 `:DEDSTATION`, `:OLD_BAR_CODE`）</summary>

```sql
select get_mo_yn('',:DEDSTATION,:OLD_BAR_CODE) AS PARENT_MO from dual
```
</details>

<details><summary>+0xCE6（60 字符，绑定变量 `:MO_NO`）</summary>

```sql
SELECT APITECH.GET_MO_MODEL_NO(:MO_NO) AS MODEL_NO FROM DUAL
```
</details>

<details><summary>+0xDDA（110 字符，绑定变量 `:MI`, `:SS`）</summary>

```sql
select to_char(TO_DATE(TO_CHAR(SYSDATE,'YYYYMMDDHH24:MI:SS'),'YYYYMMDDHH24:MI:SS'),'YYIWMM') AS YYIW from DUAL
```
</details>

<details><summary>+0xF8F（647 字符，绑定变量 `:NEWDC`, `:OLD_BAR_CODE`, `:XMODEL_NO`）</summary>

```sql
SELECT REPLACE(SUBSTR(:OLD_BAR_CODE, 1, LENG), DC, :NEWDC) ||             SUBSTR(:OLD_BAR_CODE, LENG+1, 10) NEW_BARCODE                        FROM (SELECT SUBSTR(:OLD_BAR_CODE, BAR_CODE1 + 1, BAR_CODE2) DC,                       BAR_CODE1 + BAR_CODE2 LENG                                           FROM (SELECT LENGTH(PRE_FIXED_CD) BAR_CODE1,                                           PRE_SEMIFIXED_LGTH BAR_CODE2                                      FROM SIM117                                                               WHERE MODEL_NO = :XMODEL_NO                                                AND SCAN_SEQ = '1'            AND ROWNUM = 1))
```
</details>

<details><summary>+0x10FC（54 字符）</summary>

```sql
SELECT PRE_SEMIFIXED_LGTH from SIM117 WHERE MODEL_NO='
```
</details>

<details><summary>+0x1107（63 字符）</summary>

```sql
' AND VERSION=(select MAX(VERSION) from SIM117 WHERE MODEL_NO='
```
</details>

<details><summary>+0x11DB（267 字符，绑定变量 `:barcode_old`）</summary>

```sql
SELECT PHASE_CODE FROM SIM70_IE_PHASE WHERE PART_NO=(select PART_NO from SIM40 WHERE MO_NO=  (SELECT MO_NO FROM (select MO_NO from SIM53 WHERE INTERNAL_SR=:barcode_old AND STATION='086' AND TEST_RESULT='PASS' ORDER BY UPD_DATE||UPD_TIME) WHERE ROWNUM=1)) AND ROWNUM=1
```
</details>

<details><summary>+0x1319（269 字符，绑定变量 `:ACT`, `:MODEL_NO`, `:NEW_BAR_CODE`）</summary>

```sql
select SUBSTR(:NEW_BAR_CODE,1,11)||REPLACE(SUBSTR(REPLACE(:NEW_BAR_CODE ,substr(:NEW_BAR_CODE ,0,2),:ACT),12,5),SUBSTR(REPLACE(:NEW_BAR_CODE ,substr(:NEW_BAR_CODE,0,2),:ACT),-6,5),BARCODE_M(SQ_BARCODE.NEXTVAL,:MODEL_NO))||SUBSTR(:NEW_BAR_CODE,-1,1) AS NEW_BAR from dual
```
</details>

<details><summary>+0x15C2（66 字符，绑定变量 `:MODEL_NO`, `:NEW_BAR_CODE`）</summary>

```sql
SELECT BARCODE_RETRY(:NEW_BAR_CODE,:MODEL_NO) AS NEW_BAR FROM DUAL
```
</details>

<details><summary>+0x1A71（65 字符，绑定变量 `:BARCODE_NEW`）</summary>

```sql
SELECT internal_sr FROM  BARCODE_N WHERE INTERNAL_SR=:BARCODE_NEW
```
</details>

<details><summary>+0x1B45（84 字符）</summary>

```sql
INSERT INTO BARCODE_N(internal_sr,EQUIP_NO,Upd_Date,Upd_Time,BARCODE_OLD)  VALUES ('
```
</details>


#### `AOI_INSERT_DATA`（序数 9，RVA 0xA1630，代码 2394 字节）

<details><summary>+0x19A（60 字符，绑定变量 `:XBAR_CODE`, `:XSTATION`）</summary>

```sql
SELECT GET_MO_YN('',:XSTATION,:XBAR_CODE) AS MO_NO FROM DUAL
```
</details>

<details><summary>+0x2AB（49 字符，绑定变量 `:XLINE_NAME`）</summary>

```sql
SELECT *   FROM SIM11 WHERE LINE_NAME=:XLINE_NAME
```
</details>

<details><summary>+0x388（117 字符，绑定变量 `:SHIFT_CLASS`, `:TEST_TIME`）</summary>

```sql
SELECT PERIOD FROM APITECH.SIM17 WHERE SHIFT_CLASS=:SHIFT_CLASS AND :TEST_TIME > START_TIME AND :TEST_TIME < END_TIME
```
</details>

<details><summary>+0x49C（170 字符，绑定变量 `:LINE_NAME`, `:MO_NO`, `:PERIOD`, `:START_DATE`, `:STATION`）</summary>

```sql
SELECT COUNT(DISTINCT BAR_CODE) AS XQTY FROM APITECH.SIM22 WHERE MO_NO=:MO_NO AND LINE_NAME=:LINE_NAME  AND STATION=:STATION AND PERIOD=:PERIOD AND START_DATE=:START_DATE
```
</details>

<details><summary>+0x5C4（200 字符，绑定变量 `:LINE_NAME`, `:MO_NO`, `:PERIOD`, `:START_DATE`, `:STATION`, `:XQTY`）</summary>

```sql
UPDATE SIM21 SET SAMPLING_QTY=:XQTY,TEST_QTY=:XQTY  WHERE MO_NO=:MO_NO            AND LINE_NAME=:LINE_NAME      AND STATION=:STATION          AND PERIOD=:PERIOD            AND START_DATE = :START_DATE
```
</details>

<details><summary>+0x735（511 字符）</summary>

```sql
INSERT INTO SIM22     (MO_NO,LINE_NO,EMP_NO,STATION,PERIOD,SEQ_NO,LOCATION,REASON_CODE,QTY,START_DATE,INSPECTOR,LINE_NAME,BAR_CODE,COMPUTER_NAME,    PE_CATEGORY,MACHINE_REASONCODE,JUDGE_RESULT,PCB,PROGRAM_NAME,RC_NO,                                                    SOFTWARE_NAME,EQUIP_NAME,TEST_DATE,TEST_TIME,UPD_USER,UPD_DATE,UPD_TIME)                                               VALUES                                                                                                                    ('
```
</details>


#### `ACBEL_AMZ_MAPPING`（序数 8，RVA 0xA29A4，代码 17514 字节）

<details><summary>+0x1EE（69 字符，绑定变量 `:XEMP`）</summary>

```sql
SELECT EMP_NO FROM HMM01_v1 WHERE EMP_NO=:XEMP and leave_date is null
```
</details>

<details><summary>+0x2C8（245 字符，绑定变量 `:internal_sr`）</summary>

```sql
SELECT  MO_NO,PART_NO,APITECH.GET_ROUTE_ID(MO_NO) AS  ROUTE_ID FROM (      select  mo_no,substr(get_part_no(mo_no),1,18) as  part_no  from sim53 a where internal_sr=:internal_sr    ORDER   BY  START_DATE  DESC ,START_TIME  DESC ) WHERE  ROWNUM=1
```
</details>

<details><summary>+0x3E9（245 字符，绑定变量 `:MO_NO`, `:STATION`）</summary>

```sql
Select Level KK,A.* From (Select * From SIM03   Where ROUTE_ID=SUBSTR(APITECH.GET_ROUTE_ID(:MO_NO),1,10) ) A   Where BARCODE_CTRL='Y' And Level>1 And Rownum=1       Start With STATION=:STATION Connect By Prior STATION=NEXT_STATION Order By Level
```
</details>

<details><summary>+0x505（97 字符，绑定变量 `:internal_sr`, `:station`）</summary>

```sql
select * from sim53 where  internal_sr=:internal_sr and  station=:station AND  TEST_RESULT='PASS'
```
</details>

<details><summary>+0x656（67 字符，绑定变量 `:ROUTE_ID`, `:STATION`）</summary>

```sql
SELECT * FROM SIM03 WHERE  ROUTE_ID=:ROUTE_ID AND  STATION=:STATION
```
</details>

<details><summary>+0x7A4（73 字符，绑定变量 `:internal_sr`, `:station`）</summary>

```sql
select * from sim53 where  internal_sr=:internal_sr and  station=:station
```
</details>

<details><summary>+0x8F3（65 字符，绑定变量 `:BARCODE_OLD`）</summary>

```sql
SELECT COUNT(*) XCNT FROM SIM524 WHERE BARCODE_OLD = :BARCODE_OLD
```
</details>

<details><summary>+0xBA7（80 字符，绑定变量 `:BARCODE_NEW`, `:BARCODE_OLD`）</summary>

```sql
SELECT * FROM SIM524 WHERE BARCODE_OLD=:BARCODE_OLD AND BARCODE_NEW=:BARCODE_NEW
```
</details>

<details><summary>+0xCB0（38 字符，绑定变量 `:mo_no`）</summary>

```sql
select * from sim40 where mo_no=:mo_no
```
</details>

<details><summary>+0xD60（86 字符，绑定变量 `:SN`, `:xstation`）</summary>

```sql
SELECT * FROM SIM524 WHERE  (BARCODE_NEW=:SN OR BARCODE_OLD=:SN) and station=:xstation
```
</details>

<details><summary>+0xE66（142 字符，绑定变量 `:XINTERNAL_SR`, `:xstation`）</summary>

```sql
SELECT MAPPING_FQ FROM SIM03 WHERE ROUTE_ID IN (SELECT DISTINCT CUR_ROUTE_ID FROM SIM53 WHERE INTERNAL_SR=:XINTERNAL_SR) AND STATION=:xstation
```
</details>

<details><summary>+0xF63（177 字符，绑定变量 `:xinternal_sr`）</summary>

```sql
SELECT A.XA + B.XB AS XC FROM (SELECT COUNT(*) AS XA FROM SIM524   WHERE BARCODE_OLD = :xinternal_sr) A, (SELECT COUNT(*) AS XB FROM SIM524  WHERE BARCODE_NEW = :xinternal_sr) B
```
</details>

<details><summary>+0x1075（221 字符，绑定变量 `:XSTATION`, `:xinternal_sr`）</summary>

```sql
SELECT A.XA + B.XB AS XC  FROM (SELECT COUNT(*) AS XA FROM SIM524 WHERE BARCODE_OLD = :xinternal_sr AND STATION=:XSTATION) A, (SELECT COUNT(*) AS XB FROM SIM524   WHERE BARCODE_NEW = :xinternal_sr AND STATION=:XSTATION) B
```
</details>

<details><summary>+0x11C7（51 字符，绑定变量 `:INTERNAL_SR`）</summary>

```sql
SELECT * FROM SIM53 WHERE INTERNAL_SR =:INTERNAL_SR
```
</details>

<details><summary>+0x12A3（77 字符，绑定变量 `:MO_NO`）</summary>

```sql
SELECT * FROM SIM01 WHERE PART_NO=get_mo_model_no(:MO_NO) AND CHK_BARCODE='Y'
```
</details>

<details><summary>+0x133C（187 字符，绑定变量 `:XBARCODE`）</summary>

```sql
select *  from SIM03 WHERE ROUTE_ID = (select * from (select CUR_ROUTE_ID from SIM53 WHERE INTERNAL_SR = :XBARCODE    ORDER BY UPD_DATE || UPD_TIME ASC)  WHERE ROWNUM = 1)  AND MO_YN ='Y'
```
</details>

<details><summary>+0x13FF（72 字符，绑定变量 `:BAR_CODE`, `:MO_NO`）</summary>

```sql
SELECT * FROM SIM551 WHERE MO_NO LIKE :MO_NO AND BAR_CODE LIKE :BAR_CODE
```
</details>

<details><summary>+0x1517（66 字符，绑定变量 `:MO_NO`）</summary>

```sql
SELECT LENGTH(BAR_CODE_ST)LENGTHH  FROM SIM40 WHERE MO_NO = :MO_NO
```
</details>

<details><summary>+0x1620（350 字符，绑定变量 `:bar_code`, `:son_part`）</summary>

```sql
select * from sim43 where son_part=:son_part and part_no in(     SELECT   b.part_no  FROM SIM40 a ,(select distinct mo_no,part_no from (Select  MO_NO,part_no From  sim40 Where   length(:bar_code) =length(bar_code_st) AND 1=1 UNION ALL  SELECT MO_NO,' part_no FROM SIM551 WHERE BAR_CODE = :bar_code))b where a.mo_no=b.mo_no) And   upd_date >'20100501'
```
</details>

<details><summary>+0x16D1（262 字符，绑定变量 `:INTERNAL_sR`, `:PART_NO`）</summary>

```sql
SELECT A.PART_NO, B.SON_PART, B.PART_NO PART_NO2 FROM (SELECT * FROM SIM40 WHERE MO_NO =   (SELECT MO_NO FROM SIM53 WHERE INTERNAL_SR = :INTERNAL_sR            AND STATION = '030')) A,(SELECT * FROM SIM43 WHERE PART_NO = :PART_NO) B  WHERE A.PART_NO = B.SON_PART
```
</details>

<details><summary>+0x17DC（599 字符，绑定变量 `:XMO_NO`, `:xstation`）</summary>

```sql
SELECT ROUTE_ID,STATION,SEQ_NO FROM (Select Level KK, A.*        From (Select ROUTE_ID, SEQ_NO,STATION, NEXT_STATION,BARCODE_CTRL,RC_ROUTE_ID,PHANTOM_STATION,PASS_INPUT_STATION,FAIL_INPUT_STATION,          RC_REFLOW_STATION,SECTION_CODE,MO_STATION,MAPPING_CNT,NVL(REFLOW_YN, 'Y') As REFLOW From APITECH.SIM03                       Where ROUTE_ID = (SELECT ROUTE_ID FROM SIM01 WHERE PART_NO = get_mo_model_no(:XMO_NO) AND ROWNUM = 1)) A                         Where BARCODE_CTRL = 'Y' Start WITH STATION = :xstation Connect By PRIOR STATION = NEXT_STATION Order By Level )where KK<>1 ORDER BY SEQ_NO
```
</details>

<details><summary>+0x1892（92 字符，绑定变量 `:INTERNAL_SR`, `:STATION`）</summary>

```sql
SELECT * FROM SIM53 WHERE INTERNAL_SR=:INTERNAL_SR AND STATION=:STATION ORDER BY TEST_RESULT
```
</details>

<details><summary>+0x19BE（208 字符，绑定变量 `:XMO_NO`, `:XSTATION`）</summary>

```sql
SELECT COUNT(*) AS CNT FROM SIM03 WHERE (REFLOW_YN='Y' OR REFLOW_YN IS NULL)          AND STATION=:XSTATION AND ROUTE_ID IN (SELECT ROUTE_ID FROM SIM01 WHERE PART_NO = get_mo_model_no(:XMO_NO) AND ROWNUM = 1)
```
</details>

<details><summary>+0x1A96（194 字符，绑定变量 `:INTERNAL_SR`, `:STATION`）</summary>

```sql
SELECT * FROM SIM24 WHERE RC_NO IN (SELECT MAX(RC_NO) FROM SIM53 WHERE      INTERNAL_SR=:INTERNAL_SR AND STATION=:STATION AND TEST_RESULT='FAIL')AND STATION=:STATION AND INTERNAL_SR=:INTERNAL_SR
```
</details>

<details><summary>+0x1C0E（87 字符，绑定变量 `:INTERNAL_SR`）</summary>

```sql
SELECT * FROM SIM53 WHERE INTERNAL_SR = :INTERNAL_SR ORDER BY UPD_DATE ASC,UPD_TIME ASC
```
</details>

<details><summary>+0x1CB3（460 字符，绑定变量 `:XBARCODE_NEW`, `:XBARCODE_OLD`）</summary>

```sql
SELECT MO_NO FROM SIM40 WHERE MO_NO IN (                                                                                        SELECT PARENT_MO FROM sim40 where mo_no=(SELECT DISTINCT mo_no FROM sim53 where internal_sr=:XBARCODE_OLD and station='030' ))AND PART_NO IN (  SELECT PART_NO FROM SIM40 WHERE MO_NO IN ((SELECT PARENT_MO FROM sim40 where :XBARCODE_NEW between bar_code_st and bar_code_end and substr(bar_code_st, 1, 1) <> '0'  AND MO_NO=PARENT_MO)))
```
</details>

<details><summary>+0x1D89（314 字符，绑定变量 `:XMO_NO`）</summary>

```sql
select STATION from SIM03 WHERE ROUTE_ID=GET_ROUTE_ID(:XMO_NO) AND BARCODE_CTRL='Y'  AND SEQ_NO<(select SEQ_NO from SIM03 WHERE ROUTE_ID=GET_ROUTE_ID(:XMO_NO) AND BARCODE_CTRL='Y' AND STATION='067')  AND SEQ_NO>=(select SEQ_NO from SIM03 WHERE ROUTE_ID=GET_ROUTE_ID(:XMO_NO) AND BARCODE_CTRL='Y' AND STATION='070')
```
</details>

<details><summary>+0x1E6C（72 字符，绑定变量 `:BARCODE`, `:MO_NO`, `:STATION`）</summary>

```sql
SELECT GET_MO_YN(GET_ROUTE_ID(:MO_NO),:STATION,:BARCODE) MO_NO FROM DUAL
```
</details>

<details><summary>+0x1FB6（231 字符，绑定变量 `:XM_NO`, `:XSTATION`）</summary>

```sql
select PCB_CNT,PCB_ATB,MAPPING_CNT,MAPPING_CNT/(LENGTHB(TRANSLATE(PCB_ATB,'_'||PCB_ATB,'_'))+1) MAPPING_QTY,   LENGTHB(TRANSLATE(PCB_ATB,'_'||PCB_ATB,'_'))+1  PXS from SIM03 WHERE ROUTE_ID=get_route_id(:XM_NO) AND STATION=:XSTATION
```
</details>

<details><summary>+0x20BE（61 字符，绑定变量 `:NEW_BAR`）</summary>

```sql
SELECT * FROM SIM132_PCB_BARCODE WHERE PCB_BARCODE_S=:NEW_BAR
```
</details>

<details><summary>+0x2185（49 字符）</summary>

```sql
select * from SIM03 WHERE ROUTE_ID=get_route_id('
```
</details>

<details><summary>+0x22BA（108 字符，绑定变量 `:NEW_BAR`, `:NMO_NO`, `:OLD_BAR`）</summary>

```sql
select COUNT(*) P_CNT from SIM524 WHERE BARCODE_OLD=:OLD_BAR AND BARCODE_NEW LIKE :NEW_BAR AND MO_NO=:NMO_NO
```
</details>

<details><summary>+0x2715（871 字符，绑定变量 `:BI_ROOM_NO`, `:CART_NO`, `:CUR_ROUTE_ID`, `:END_DATE`, `:END_TIME`, `:EQUIP_NO`, `:ERROR_CODE`, `:FAIL_DATE`, `:FAIL_HRS`, `:INTERNAL_SR`, `:LAYER`, `:LINE_NAME`, `:LINE_NO`, `:LOCATION`, `:MOVE_TO`, `:MO_NO`, `:PERIOD`, `:POSITION`, `:PROD_DATE`, `:RC_EMP_NO`, `:RC_NO`, `:REMARK`, `:RESULT`, `:SEQ_NO`, `:START_DATE`, `:START_TIME`, `:STATION`, `:SUB_STATION`, `:TEMPERATURE`, `:TEST_FAIL_TIME`, `:TEST_RESULT`, `:TEST_START_TIME`, `:TOOL_NO`, `:UPD_DATE`, `:UPD_TIME`, `:UPD_USER`, `:VOLTAGE`）</summary>

```sql
insert into APITECH.SIM53 ( MO_NO , PROD_DATE , LINE_NO , LINE_NAME, PERIOD , STATION , SEQ_NO, ERROR_CODE,INTERNAL_SR, TEST_RESULT, TEMPERATURE, VOLTAGE, MOVE_TO, REMARK, RESULT, EQUIP_NO, SUB_STATION, RC_NO, UPD_USER, UPD_DATE, UPD_TIME, RC_EMP_NO,LOCATION,CUR_ROUTE_ID, CART_NO, BI_ROOM_NO, LAYER, POSITION, TEST_START_TIME, TEST_FAIL_TIME, FAIL_HRS, TOOL_NO, START_TIME, END_TIME, START_DATE, FAIL_DATE, END_DATE, STATUS)  values (  :MO_NO ,  :PROD_DATE ,  :LINE_NO ,  :LINE_NAME,:PERIOD, :STATION, :SEQ_NO, :ERROR_CODE, :INTERNAL_SR, :TEST_RESULT, :TEMPERATURE, :VOLTAGE, :MOVE_TO, :REMARK, :RESULT, :EQUIP_NO, :SUB_STATION, :RC_NO, :UPD_USER, :UPD_DATE, :UPD_TIME, :RC_EMP_NO,:LOCATION,:CUR_ROUTE_ID,    :CART_NO,:BI_ROOM_NO,:LAYER,:POSITION,:TEST_START_TIME,:TEST_FAIL_TIME,:FAIL_HRS,:TOOL_NO, :START_TIME, :END_TIME, :START_DATE, :FAIL_DATE, :END_DATE, 'SIM132' )
```
</details>

<details><summary>+0x2E31（1128 字符，绑定变量 `:LINE_NAME`）</summary>

```sql
Select * From (                                                                  SELECT   B.PERIOD,START_TIME,END_TIME,                                              Case  When  TO_NUMBER(SUBSTR(TO_CHAR(Sysdate,'HH24MISS'),1,2))<12  Then           (Case When START_TIME>END_TIME  Then  '0000'   Else START_TIME  End)               Else  START_TIME End    As  XSTART_TIME,                                           Case  When  TO_NUMBER(SUBSTR(TO_CHAR(Sysdate,'HH24MISS'),1,2)) >12  Then          (Case When START_TIME>END_TIME  Then  TO_CHAR(TO_NUMBER(( SUBSTR(END_TIME,1,2)+24)))||SUBSTR(END_TIME,3,2)   Else END_TIME  End)     Else  END_TIME End    As  XEND_TIME                                                FROM  APITECH.SIM11 A,APITECH.SIM17 B                                               WHERE A.SHIFT_CLASS=B.SHIFT_CLASS                                                   AND  A.LINE_NAME=:LINE_NAME                                                         )                                                                                   Where   SUBSTR(TO_CHAR(Sysdate,'HH24MISS'),1,4) between Xstart_time and Xend_time
```
</details>

<details><summary>+0x2F08（194 字符，绑定变量 `:XBAR_OLD`, `:XSTATION`）</summary>

```sql
SELECT * FROM SIM03 WHERE ROUTE_ID=(SELECT * FROM (select distinct max(cur_route_id)  from SIM53 WHERE INTERNAL_sR=:XBAR_OLD AND STATION='030')where rownum=1) and station=:XSTATION and mo_yn='Y'
```
</details>

<details><summary>+0x3114（196 字符，绑定变量 `:XBARCODE`）</summary>

```sql
select * from SIM03 WHERE ROUTE_ID = (select *          from (select CUR_ROUTE_ID from SIM53 WHERE INTERNAL_SR = :XBARCODE      ORDER BY UPD_DATE || UPD_TIME ASC)  WHERE ROWNUM = 1) AND MO_YN ='Y'
```
</details>

<details><summary>+0x3218（70 字符）</summary>

```sql
SELECT LINE_NO FROM SIM11 WHERE  NVL(STOP_YN,'N') ='N' AND LINE_NAME='
```
</details>

<details><summary>+0x32EF（573 字符，绑定变量 `:CUR_ROUTE_ID`, `:EQUIP_NO`, `:INTERNAL_SR`, `:LINE_NAME`, `:LINE_NO`, `:MI`, `:MO_NO`, `:PERIOD`, `:REMARK`, `:SEQ_NO`, `:SS`, `:STATION`, `:TEST_RESULT`, `:UPD_USER`）</summary>

```sql
INSERT INTO SIM53        (MO_NO,PROD_DATE,LINE_NO,PERIOD,SEQ_NO,STATION,INTERNAL_SR,REMARK,UPD_USER,UPD_DATE,UPD_TIME,TEST_RESULT,LINE_NAME,CUR_ROUTE_ID,     STATUS,START_TIME,END_TIME,START_DATE,END_DATE,EQUIP_NO)      VALUES        (:MO_NO,to_char(sysdate,'YYYYMMDD'),:LINE_NO,:PERIOD,:SEQ_NO,:STATION,:INTERNAL_SR,:REMARK,:UPD_USER,to_char(sysdate,'YYYYMMDD'),to_char(sysdate,'HH24:MI:SS'),  :TEST_RESULT,:LINE_NAME,:CUR_ROUTE_ID,     'SIM132',to_char(sysdate,'HH24:MI:SS'),to_char(sysdate,'HH24:MI:SS'),to_char(sysdate,'YYYYMMDD'),to_char(sysdate,'YYYYMMDD'),:EQUIP_NO)
```
</details>

<details><summary>+0x357A（290 字符，绑定变量 `:MI`, `:SS`, `:barcode_new`, `:barcode_old`, `:line_name`, `:mo_no`, `:station`, `:upd_user`）</summary>

```sql
INSERT INTO APITECH.SIM524(prod_date,line_name,barcode_old,      barcode_new,upd_user,upd_date,upd_time,station,mo_no)            values(to_char(sysdate,'YYYYMMDD'),:line_name,:barcode_old,    :barcode_new,:upd_user,to_char(sysdate,'YYYYMMDD'),to_char(sysdate,'HH24:MI:SS'),:station,:mo_no)
```
</details>

<details><summary>+0x37AC（60 字符，绑定变量 `:internal_sr`）</summary>

```sql
select * from  apitech.SIM24  where internal_sr=:internal_sr
```
</details>

<details><summary>+0x3855（527 字符，绑定变量 `:CATEGORY`, `:EMP_NO`, `:ERROR_CODE`, `:FAILURE`, `:FAIL_DESC`, `:INTERNAL_SR`, `:LINE_NAME`, `:LINE_NO`, `:MODEL_NO`, `:MO_NO`, `:PE_MARK`, `:PE_YN`, `:PROD_DATE`, `:RC`, `:RC_DATE`, `:RC_EMP_NO`, `:RC_NO`, `:REMARK`, `:SCRAP`, `:STATION`, `:TEST_COND`, `:UPD_DATE`, `:UPD_TIME`, `:UPD_USER`）</summary>

```sql
update apitech.SIM24 set          MO_NO = :MO_NO, LINE_NO = :LINE_NO,STATION = :STATION, ERROR_CODE = :ERROR_CODE, TEST_COND = :TEST_COND, FAILURE = :FAILURE, FAIL_DESC = :FAIL_DESC, CATEGORY =:CATEGORY, REMARK = :REMARK, UPD_DATE = :UPD_DATE,UPD_USER=:UPD_USER,UPD_TIME=:UPD_TIME,SCRAP=:SCRAP,EMP_NO =:EMP_NO, RC_DATE = :RC_DATE, PROD_DATE = :PROD_DATE, RC = :RC, PE_YN = :PE_YN, LINE_NAME = :LINE_NAME, RC_EMP_NO = :RC_EMP_NO, MODEL_NO = :MODEL_NO, PE_MARK = :PE_MARK      where  RC_NO = :RC_NO and INTERNAL_SR = :INTERNAL_SR
```
</details>

<details><summary>+0x3DFB（60 字符，绑定变量 `:internal_sr`）</summary>

```sql
select * from  apitech.SIM25  where internal_sr=:internal_sr
```
</details>

<details><summary>+0x3EA4（596 字符，绑定变量 `:DATE_CODE`, `:DEAL_METHOD`, `:INTERNAL_SR`, `:LOCATION`, `:LOCATION_FOR`, `:MANUFACTOR`, `:NEW_DATE_CODE`, `:NEW_LOT_NO`, `:NEW_MANUFACTOR`, `:NEW_PART_NO`, `:NEW_SPEC`, `:PART_NO`, `:PE_CATEGORY`, `:RC_NO`, `:REASON_CODE`, `:REASON_CODE_OLD`, `:REMARK`, `:SECTION_NAME`, `:SEQ_NO`, `:SPEC`, `:UPD_DATE`, `:UPD_TIME`, `:UPD_USER`）</summary>

```sql
insert into apitech.SIM25         (RC_NO, SEQ_NO, LOCATION, SPEC, MANUFACTOR, DATE_CODE, UPD_DATE, UPD_USER,UPD_TIME, REASON_CODE, REMARK, LOCATION_FOR, PART_NO, NEW_PART_NO, NEW_SPEC, NEW_MANUFACTOR, NEW_DATE_CODE, DEAL_METHOD, PE_CATEGORY, SECTION_NAME,REASON_CODE_OLD, NEW_LOT_NO, INTERNAL_SR)   values (:RC_NO, :SEQ_NO, :LOCATION, :SPEC, :MANUFACTOR, :DATE_CODE, :UPD_DATE,:UPD_USER, :UPD_TIME, :REASON_CODE, :REMARK, :LOCATION_FOR, :PART_NO,      :NEW_PART_NO, :NEW_SPEC, :NEW_MANUFACTOR, :NEW_DATE_CODE, :DEAL_METHOD,:PE_CATEGORY, :SECTION_NAME, :REASON_CODE_OLD, :NEW_LOT_NO, :INTERNAL_SR)
```
</details>


#### `ACBEL_UPLOAD_DATA`（序数 7，RVA 0xAAD70，代码 662 字节）

<details><summary>+0xD1（236 字符，绑定变量 `:CRT_USER`, `:EQUID_NO`, `:EQUIP_NAME`, `:ID`, `:MI`, `:NUM`, `:SS`, `:UL_DATA`, `:UL_STATUS`）</summary>

```sql
INSERT INTO AUTO_EQUIP_INT_T    (ID,NUM,UL_DATA,UL_STATUS,EQUIP_NAME,CRT_USER,EQUID_NO,UPD_DATE,UPD_TIME)   VALUES  (:ID,:NUM,:UL_DATA,:UL_STATUS,:EQUIP_NAME,:CRT_USER,:EQUID_NO,to_char(sysdate,'YYYYMMDD'),to_char(sysdate,'HH24:MI:SS'))
```
</details>


#### `ICT_INSERT_DATA`（序数 6，RVA 0xAB448，代码 636 字节）

<details><summary>+0x14A（271 字符）</summary>

```sql
INSERT INTO SIM629     (MODEL_NO,MO_nO,LINE_NO,MACHINE_NAME,TYPE,MESSAGE,UPD_USER,UPD_DATE,UPD_TIME)                                               VALUES                                                                                                                    ('
```
</details>


#### `FUN_VALUE`（序数 5，RVA 0xABD40，代码 1102 字节）

<details><summary>+0x10F（40 字符）</summary>

```sql
SELECT MID_FUN_V.NEXTVAL mcode FROM DUAL
```
</details>

<details><summary>+0x195（326 字符，绑定变量 `:LINE_NO`, `:MI`, `:MID`, `:QEQUID_NO`, `:QUL_data1`, `:QUL_data2`, `:QUL_status`, `:Qcrt_user`, `:Qremark`, `:SS`, `:XSTATION`, `:XX`）</summary>

```sql
INSERT INTO tbmas_Fixture  (MID,LINE_NO,STATION,UL_data,UL_status,remark,crt_user,EQUID_NO,XCNT,UPD_DATE,UPD_TIME )VALUES (:MID,:LINE_NO,:XSTATION,to_cLOB(substr(:QUL_data1,1,4000))||to_clob(substr(:QUL_data2,1,4000)),  :QUL_status,:Qremark,:Qcrt_user ,:QEQUID_NO,:XX,TO_CHAR(SYSDATE,'YYYYMMDD'),TO_CHAR(SYSDATE,'HH24:MI:SS'))
```
</details>


#### `ACBEL_AOI_UP`（序数 1，RVA 0xADDD4，代码 2305 字节）

<details><summary>+0x39E（67 字符，绑定变量 `:XLINE_NAME`）</summary>

```sql
SELECT SHIFT_CLASS,LINE_NO   FROM SIM11 WHERE LINE_NAME=:XLINE_NAME
```
</details>

<details><summary>+0x46C（117 字符，绑定变量 `:SHIFT_CLASS`, `:TEST_TIME`）</summary>

```sql
SELECT PERIOD FROM APITECH.SIM17 WHERE SHIFT_CLASS=:SHIFT_CLASS AND :TEST_TIME > START_TIME AND :TEST_TIME < END_TIME
```
</details>

<details><summary>+0x5CC（486 字符，绑定变量 `:CUR_ROUTE_ID`, `:EQUIP_NO`, `:INTERNAL_SR`, `:LINE_NAME`, `:LINE_NO`, `:MI`, `:MO_NO`, `:PERIOD`, `:SEQ_NO`, `:SS`, `:STATION`, `:TEST_PROGRAM`, `:TEST_RESULT`, `:UPD_USER`, `:XDATE`, `:XTIME`）</summary>

```sql
INSERT INTO SIM53     (MO_NO,PROD_DATE,LINE_NO,PERIOD,SEQ_NO,STATION,INTERNAL_SR,UPD_USER,UPD_DATE,UPD_TIME,TEST_RESULT,LINE_NAME,CUR_ROUTE_ID,   STATUS,START_TIME,END_TIME,START_DATE,END_DATE,EQUIP_NO,TEST_PROGRAM)     VALUES    (:MO_NO,TO_CHAR(SYSDATE,'YYYYMMDD'),:LINE_NO,:PERIOD,:SEQ_NO,:STATION,:INTERNAL_SR,:UPD_USER,to_char(sysdate,'YYYYMMDD'),to_char(sysdate,'HH24:MI:SS'),   :TEST_RESULT,:LINE_NAME,:CUR_ROUTE_ID,'AOI_DLL',:XTIME,:XTIME,  :XDATE,:XDATE,:EQUIP_NO,:TEST_PROGRAM)
```
</details>


#### `GET_TEST_PROGRAM`（序数 18，RVA 0xAEC40，代码 1191 字节）

<details><summary>+0x75（368 字符，绑定变量 `:INTERNAL_SR`, `:STATION`）</summary>

```sql
Select DECODE((Select PARENT_MO_YN From APITECH.SIM02  Where STATION=:STATION),'N',A.MO_NO,PARENT_MO) MO_NO From APITECH.SIM40 A, (SELECT MO_NO FROM SIM53          WHERE INTERNAL_SR like :INTERNAL_SR  AND STATION='030'           AND SEQ_NO=(SELECT MAX(SEQ_NO) FROM APITECH.SIM53 WHERE INTERNAL_SR LIKE :INTERNAL_SR  AND STATION='030' )) B         Where A.MO_NO=B.MO_NO
```
</details>

<details><summary>+0x128（481 字符，绑定变量 `:INTERNAL_SR`, `:STATION`）</summary>

```sql
SELECT DECODE((Select PARENT_MO_YN From APITECH.SIM02  Where STATION=:STATION),'N',MO_NO,PARENT_MO) MO_NO From APITECH.SIM40  WHERE  :INTERNAL_SR BETWEEN BAR_CODE_ST AND BAR_CODE_END  AND  BAR_CODE_ST LIKE 'AC%'   AND  BAR_CODE_END LIKE 'AC%'  and PART_NO IN ('RLOD-5SG06-711L-1','RLOD-5SG05-710L-1','CLOD-4SG02-711-1',  'RLOD-4SG02-711L-1','RLOD-4SG02-715L-1','RLOD-4SG02-716L-1','RLOD-SG7003-710G-1', 'RSG9006-710G-2','RLOD-SG7008-710G-1','RLOD-5SG06-711G-1','RLOD-4SG06-711L-1')
```
</details>

<details><summary>+0x1F0（323 字符，绑定变量 `:MO_NO`）</summary>

```sql
SELECT MODEL_NO FROM (  SELECT APITECH.GET_MO_MODEL_NO(:MO_NO) AS MODEL_NO FROM DUAL)   WHERE MODEL_NO IN('API5SG06-711L','API5SG06-710L','API5SG05-710L','API4SG02-711L','API4SG02-713L', 'API4SG02-715L','API4SG02-711','API4SG02-710','SG7003-710L','SG8005-710L',  'SG9006-710G','SG7008-710G','API5SG06-711G','API4SG06-711L')
```
</details>

<details><summary>+0x341（60 字符，绑定变量 `:MO_NO`）</summary>

```sql
SELECT APITECH.GET_MO_MODEL_NO(:MO_NO) AS MODEL_NO FROM DUAL
```
</details>

<details><summary>+0x3DA（168 字符，绑定变量 `:MODEL_NO`, `:STATION`）</summary>

```sql
SELECT MODEL_PROGRAM FROM  (select MODEL_PROGRAM from apitech.sim547 where MODEL_NO=:MODEL_NO AND STATION=:STATION order by upd_date DESC,upd_time DESC ) WHERE ROWNUM=1
```
</details>
