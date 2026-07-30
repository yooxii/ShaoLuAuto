---
kind: logging_system
name: NLog 日志系统与执行日志持久化
category: logging_system
scope:
    - '**'
source_files:
    - ShaoLu/NLog.config
    - ShaoLu/App.xaml.cs
    - ShaoLu/Services/ExecutionLogService.cs
    - ShaoLu/Models/StepExecutionLog.cs
---

## 1. 使用的系统/框架
- **应用级日志**：使用 **NLog** 作为结构化日志框架，通过 `NLog.config` 配置文件管理输出目标与规则。
- **业务执行日志**：使用 **FreeSql + SQLite** 持久化自动化步骤的执行历史，存储在 `%APPDATA%\AutoShaoLu\execution_log.db`。

## 2. 核心文件与位置
- `ShaoLu/NLog.config` — NLog 配置，定义文件与控制台两个异步输出目标，统一按日期命名日志文件。
- `ShaoLu/App.xaml.cs` — 应用启动/退出生命周期中记录关键流程日志（Info/Fatal）。
- `ShaoLu/Services/ExecutionLogService.cs` — 执行日志服务，封装 FreeSql 的增删查改、分页查询、清理策略。
- `ShaoLu/Models/StepExecutionLog.cs` — 执行日志实体模型，包含 StepUid、StepFileName、StepName、InputText、OCRText、ExecutedAt 等字段。
- 其他服务类（`ConditionEvaluator.cs`、`LanguageService.cs`、`OCRService.cs`）均通过 `NLog.LogManager.GetCurrentClassLogger()` 获取 logger 实例。

## 3. 架构与约定
- **日志级别**：常规流程使用 `Info`，异常捕获使用 `Error`，启动失败使用 `Fatal`；所有日志最低级别为 `Debug`。
- **输出格式**：`${longdate} | ${uppercase:${level}} | ${logger} | ${message} ${exception:format=Message}`，同时写入 `logs/${shortdate}.log` 与控制台。
- **异步写入**：NLog targets 启用 `async="true"`，避免阻塞 UI 线程。
- **自动重载**：`autoReload="true"`，修改配置无需重启。
- **执行日志持久化**：通过 FreeSql 的 `Lazy<IFreeSql>` 单例初始化数据库，支持关键字搜索、时间范围筛选、排序与分页。
- **日志清理策略**：根据用户配置的 `LogRetentionDays` 定期清理过期执行日志，防止数据库膨胀。

## 4. 开发者应遵循的规则
- 在需要记录日志的服务类中，使用 `private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();` 获取 logger 实例。
- 正常流程使用 `Info`，可恢复异常使用 `Error`，不可恢复错误使用 `Fatal`，并附带异常对象以便 NLog 输出堆栈。
- 不要直接调用 `Console.WriteLine` 或 `MessageBox.Show` 做调试输出，统一走 NLog。
- 业务执行结果（如 OCR 识别、步骤执行）应通过 `ExecutionLogService.Log(...)` 持久化到 SQLite，便于后续审计与回溯。
- 新增日志目标或修改输出格式时，仅修改 `NLog.config`，无需改动代码。