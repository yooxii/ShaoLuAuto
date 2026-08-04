---
kind: error_handling
name: 错误处理体系：NLog 日志 + 结构化结果模型 + 全局异常兜底
category: error_handling
scope:
    - '**'
source_files:
    - ShaoLu/NLog.config
    - ShaoLu/App.xaml.cs
    - ShaoLu/Models/AutomationStepModel.cs
    - ShaoLu/Viewmodels/AutomationStep.cs
    - ShaoLu/Models/StepExecutionResult.cs
    - ShaoLu/Models/StepExecutionLog.cs
    - ShaoLu/Services/ExecutionLogService.cs
---

## 1. 系统/方法概述
该仓库采用“结构化执行结果 + 统一日志记录 + 应用级未处理异常兜底”的组合策略，不依赖自定义异常类型或中间件，而是通过枚举、模型和 NLog 完成错误的定义、传播与呈现。

- 日志框架：NLog（配置文件 `ShaoLu/NLog.config`），按日期输出到文件与控制台，支持异步写入与异常消息格式化。
- 运行时错误传播：步骤执行返回 `Task<bool>`，并通过 `AutomationStepBase` 上的 `IsError`、`ErrorMessage`、`ErrorType` 等属性传递错误状态；同时维护 `LastResult`（`StepExecutionResult`）作为最近一次执行的结构化结果。
- 持久化错误历史：`ExecutionLogService` 使用 FreeSql + SQLite 将每次步骤执行的输入、OCR 结果、时间等写入 `step_execution_log` 表，失败时回退到 NLog 记录错误。
- 全局异常兜底：在 `App.OnStartup` 中注册 `DispatcherUnhandledException`、`AppDomain.CurrentDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException` 三个处理器，统一记录并避免进程直接崩溃。

## 2. 关键文件与包
- `ShaoLu/NLog.config`：NLog 配置，定义文件与控制台目标、规则及异常格式化。
- `ShaoLu/App.xaml.cs`：应用启动入口，集中注册 UI 未处理异常、AppDomain 未处理异常、未观察 Task 异常的兜底处理器，并初始化 DI、语言服务、字体、DPI 缓存等。
- `ShaoLu/Models/AutomationStepModel.cs`：定义 `StepType`、`StepErrorType`、`PopupCloseMode`、`PopupWindowStyle` 等核心枚举。
- `ShaoLu/Viewmodels/AutomationStep.cs`：`AutomationStepBase` 基类，包含 `IsError`、`ErrorMessage`、`ErrorType`、`LastResult`、`EnableLog` 等错误相关属性，以及各具体 Step 的 `RunAsync` 实现中对这些属性的设置。
- `ShaoLu/Models/StepExecutionResult.cs`：步骤执行结果模型，包含 `IsTrue`、`ExecutionTimeMs`、`Similarity`、`ClickPosition`、`OCRText`、`PopupResult`、`ErrorMessage`、`ExecutedAt`。
- `ShaoLu/Models/StepExecutionLog.cs`：FreeSql 实体，映射 `step_execution_log` 表，用于持久化执行历史。
- `ShaoLu/Services/ExecutionLogService.cs`：执行日志服务，提供 `Log`、`Query`、`QueryCount`、`GetDistinctFileNames`、`CleanupOldLogs` 等静态方法，内部对数据库操作进行 try/catch 并记录 NLog。
- 其他广泛使用 NLog 的文件：`MainWindow.xaml.cs`、`Services/ConditionEvaluator.cs`、`Services/LanguageService.cs`、`Services/OCRService.cs`、`Utils/Autogui.cs`、`Viewmodels/ExecutionLogViewModel.cs`、`Viewmodels/StepsViewModel.cs` 等。

## 3. 架构与约定
- 错误分类：通过 `StepErrorType` 枚举对错误进行分类（如 `ImageNotFound`、`FileNotFound`、`OCRError`、`SelfReferenceLimit`、`CancelledByUser`、`Unknown` 等），便于 UI 展示与统计。
- 执行结果模型：每个步骤在执行后设置 `IsTrue`、`IsError`、`ErrorMessage`、`ErrorType`，并将完整结果放入 `LastResult`（`StepExecutionResult`），供 UI 绑定与诊断。
- 可选日志开关：`EnableLog` 控制是否调用 `ExecutionLogService.Log` 持久化执行历史；日志写入失败不会中断主流程，仅记录 NLog。
- 全局异常三件套：
  - UI 线程：`DispatcherUnhandledException` 记录错误并弹出 MessageBox，标记 `Handled = true` 阻止崩溃。
  - 非 UI 线程：`AppDomain.CurrentDomain.UnhandledException` 以 Fatal 级别记录。
  - 未观察 Task 异常：`TaskScheduler.UnobservedTaskException` 记录后调用 `SetObserved()` 避免终结器抛出。
- 日志策略：所有服务层与工具类通过 `NLog.LogManager.GetCurrentClassLogger()` 获取 Logger，统一使用 `Info/Error/Fatal/Debug` 等级别；NLog 配置 `throwExceptions="false"`，确保日志子系统自身异常不影响业务。

## 4. 约定与约束
- 步骤执行必须更新 `IsError`、`ErrorMessage`、`ErrorType` 与 `LastResult`，以便上层统一判断与展示（由 `AutomationStepBase` 及其派生类的 `RunAsync` 实现保证）。
- 需要持久化的执行历史应通过 `ExecutionLogService.Log` 记录，且允许失败（内部 try/catch 记录 NLog），不得因日志失败影响主流程。
- 全局异常必须在 `App.OnStartup` 中注册三类处理器，以保证未捕获异常可被记录与提示。
- 单元测试中通过 `Assert.Throws<ArgumentNullException>`、`Assert.Throws<ArgumentException>`、`Assert.Throws<FileNotFoundException>` 等方式验证参数校验与文件操作的异常行为，体现“参数非法抛标准异常”的约定。
- 日志文件路径固定为 `${basedir}/logs/${shortdate}.log`，并按天轮转；执行日志数据库位于 `%APPDATA%/AutoShaoLu/execution_log.db`。

置信度: high