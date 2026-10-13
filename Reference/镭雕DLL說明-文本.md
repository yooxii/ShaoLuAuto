# 鐳雕DLL說明（原文档文本提取）

> 来源：用户提供的 `鐳雕DLL說明.docx`（内部标题：**镭雕软件与MES系统通讯规范**，222571 字节，含 1 张流程图 image1.png）。
> 本文是**逐段文本提取**（图片未提取），保留原文用词（繁体/简体混排、错别字与「疑问」批注均按原文）。
> 提取脚本：`.analysis\compare\read_docx.py`；纯文本：`.analysis\compare\docx_text.txt`。

---

镭雕软件与MES系统通讯规范

## 一、基本流程

验证网络——》扫码——》验证条码——》镭雕——》上传资料 ——》CCD检测——》上传资料

## 二、验证网络

```
Function  ACBEL_DATABASE_OPEN: PCHAR; stdcall;
    还回结果：正确 database
              否则 错误。
```

## 三、扫码

扫码枪扫码在镭雕软件运行。得到条码

### 3.1

```
Function ACBEL_SET_BASIC_INFO
(DEDDATABASE,DEDMO_NO,DEDSTATION_EMP,DEDLINE_NAME :PChar)
A: DEDDATABASE:數據庫;   // Function ACBEL_DATABASE_OPEN 得出
B: DEDMO_NO:”; 工單號  // 用戶輸入
C: DEDSTATION_EMP: 站別/工號”;    // 用戶輸入
     例如站別為‘060’，工號為‘21104689’則DEDSTATION_EMP  為 06021104689
D: DEDLINE_NAME: 線別”;    // 用戶輸入
E: Result:  若ok则为：1;   //返回结果
  若異常则为：
0}B006-線別錯誤The line name is incorrect； fail信息 (主要用0}+異常代碼)
2}調用函數ACBEL_SET_BASIC_INFO錯誤 請重新調用此函數Unknown software exception. fail信息 獲取到2}時候需重新調用此函數
```

## 四、验证条码

```
Function CHKBAR_CODE
参数1:数据库。由ACBEL_DATABASE_OPEN得出。
2：站别+工号。用户输入
    例如站别为‘060’，工号为‘21104689’则为 06021104689
3:条码;  //扫码得到
    还回结果：正确 1
否则FAIL  0}C029-請及時測SAMPLE 否則不能測試Please test the SAMPLE otherwise cannot be tested； fail信息 (主要用0}+異常代碼)
2}調用函數CHKBAR_CODE錯誤 請重新調用此函數Unknown software exception. fail信息 獲取到2}時候需重新調用此函數
```

```
4.Function CHANGE_BARCODE_DC(DEDDATABASE,OLD_BAR_CODE,DDMO_NO,DEDSTATION :PChar): PCHAR; stdcall;負責轉換DC換新條碼給鐳雕機
參數為: DEDDATABASE ;   // Function ACBEL_DATABASE_OPEN 得出 字符串  DATABASE
             OLD_BAR_CODE;    傳入的舊條碼
             DDMO_NO;               傳入的工令
             DEDSTATION ;          傳入的站別
           1.FUNCTION 會返回新條碼給調用者
           2. 在 D:\LOG.txt會記錄FUNCTION經歷的卡控
           3.該FUNCTION會返回CHANGE_BARCODE_DC:=‘2}…'; 为 ERROR 不然则是返回的条码;
           4. 返回1則是成功
```

## 五、上传资料

```
Function  ACBEL_INSERT_DATA  stdcall;
参数1:数据库。由ACBEL_DATABASE_OPEN得出。
2：上传资料
PASS,,,O4769455,HE47H1G001H18163,165,H18,140735,140846,20210520,21807605,0.1,0.2,0.3,0.4,FSC007A047H1GB2116000136,TOOL,TOOL1,TOOL2,TOOL3,TOOL4,TOOL5,TOOL6,TOOL7,0.5,GMAU0001,D205,LD005001
详情见图1。
3: 镭雕的程式文件名. 用户输入
还回结果：正确 1
否则FAIL  0} S004-站別不能為空； fail信息 (主要用0}+異常代碼)
2}調用函數ACBEL_INSERT_DATA錯誤 請重新調用此函數Unknown software exception. fail信息 獲取到2}時候需重新調用此函數

疑问：这里得工令、站别、线别、员工号是否与下面的上传资料流程中一样。
```

```
function  ACBEL_SYS_DATA
参数1: 线别名称
2：当前工令
3:当前站别
4:数据
如(ls_log,序号，时间，条码，位置，状态(PASS /FAIL), FAIL代码，FAIL原因 )
5:备注栏位
6: 厂商代码(机器编码)
根据我们提供给厂商代码进行厂商区分上传
7: 此栏位用来定义数据中有多少条数据
还回结果：正确 1
               否则 错误。

疑问：这里得工令、站别、线别、员工号是否与下面的上传资料流程中一样。
```

## 六、镭雕

镭雕在镭雕软件运行。验证条码错误不镭雕。

## 七、CCD检测

CD软件在本地电脑运行，把检测结果发送个镭雕软件。

## 八、上传资料

```
Function  ACBEL_INSERT_DATA  stdcall;
参数1:数据库。由ACBEL_DATABASE_OPEN得出。
2：上传资料
PASS,,,O4769455,HE47H1G001H18163,165,H18,140735,140846,20210520,21807605,0.1,0.2,0.3,0.4,FSC007A047H1GB2116000136,TOOL,TOOL1,TOOL2,TOOL3,TOOL4,TOOL5,TOOL6,TOOL7,0.5,GMAU0001,D205,LD005001
详情见下图。
3: 镭雕的程式文件名. 用户输入
还回结果：正确 1
               否则 错误。

疑问：这里得工令、站别、线别、员工号是否与下面的上传资料流程中一样。
```

```
function  ACBEL_SYS_DATA
参数1: 线别名称
2：当前工令
3:当前站别
4:数据
如(ls_log,序号，时间，条码，位置，状态(PASS /FAIL), FAIL代码，FAIL原因 )
5:备注栏位
6: 厂商代码(机器编码)
根据我们提供给厂商代码进行厂商区分上传
7: 此栏位用来定义数据中有多少条数据
还回结果：正确 1
               否则 错误。

疑问：这里得工令、站别、线别、员工号是否与下面的上传资料流程中一样。
```

---

## 文档 ↔ 二进制对照（本仓库核对结果）

| 文档函数 | 文档签名 | 二进制实测（参数个数/约定） | 返回值实测 |
|---|---|---|---|
| `ACBEL_DATABASE_OPEN` | `: PCHAR; stdcall` | 0 参 / 无栈参数 | 成功 `"database"`；失败 `"0}數據庫無法連接"`（另有 `"連接DB Connection ERROR"`） |
| `ACBEL_SET_BASIC_INFO` | `(DEDDATABASE, DEDMO_NO, DEDSTATION_EMP, DEDLINE_NAME: PChar)` | 4 参 / `stdcall`（`ret 10h`） | 成功 `"1"`；失败 `"0}B001-…"`~`"0}B006-…"`（B006=線別錯誤）、`"2}調用函數…"` |
| `CHKBAR_CODE` | `(数据库, 站别+工号, 条码)` | 3 参 / `stdcall`（`ret 0Ch`） | 成功 `"1"`；失败 `"0}C0xx-…"`（如 `0}C038-MOI未掃掃完,不可以測試.`、`0}C029-請及時測SAMPLE…`） |
| `CHANGE_BARCODE_DC` | `(DEDDATABASE, OLD_BAR_CODE, DDMO_NO, DEDSTATION): PCHAR; stdcall` | 4 参 / `stdcall`（`ret 10h`） | 返回新条码；错误 `"2}調用函數CHANGE_BARCODE_DC異常請重新調用"`、`"週期碼錯誤,請檢查086站時間資料"`；日志 `D:\LOG.txt` |
| `ACBEL_INSERT_DATA` | `(数据库, 上传资料, 镭雕程式文件名) stdcall` | 3 参 / `stdcall`（`ret 0Ch`） | 成功 `"1"`；失败 `"0}站別不能為空"`/`"0}工令不能為空"`/`"0}條碼不能為空"`/`"0}測試結果不能為空"` |
| `ACBEL_SYS_DATA` | `(线别名称, 当前工令, 当前站别, 数据, 备注栏位, 厂商代码, 数据条数)` | 7 参 / `stdcall`（`ret 1Ch`） | 成功 `"1"`，失败 `"0"` |

要点：**返回值是 ANSI 字符串指针（PChar），不是整数** —— 文档明确写 `: PCHAR`，反汇编里返回前 `mov eax, ebx` 且 ebx 指向 `"1"` / `"0}B00x-…"` 这类常量串。调用方应 `Marshal.PtrToStringAnsi` 后判断首字符（`1`=成功，`0}`/`2}`=失败），不要按 int 处理。
