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

本仓库采用 NLog 作为统一的应用程序日志框架，并通过独立的 ExecutionLogService + SQLite（FreeSql）实现业务执行历史的结构化持久化。两者职责分离：NLog 负责运行期诊断与异常追踪，ExecutionLogService 负责记录自动化步骤的执行结果供用户查询。

**使用的框架与工具**
- NLog：通过 NLog.config 配置文件驱动，启用异步写入、按日期分文件、同时输出到文件和控制台。
- FreeSql + SQLite：用于持久化 StepExecutionLog 实体，数据库文件位于 %APPDATA%\AutoShaoLu\execution_log.db。

**核心文件与位置**
- ShaoLu/NLog.config：NLog 全局配置，定义 File 和 Console 两个目标，规则为 minlevel="Debug" 同时写入两个目标。
- ShaoLu/App.xaml.cs：应用启动/退出生命周期中集中使用 NLog.Logger 记录关键流程（启动、初始化、异常、退出），并订阅 DispatcherUnhandledException、AppDomain.UnhandledException、TaskScheduler.UnobservedTaskException 三个全局异常钩子。
- ShaoLu/Services/ExecutionLogService.cs：封装 FreeSql 单例，提供 Log、Query、QueryCount、GetDistinctFileNames、CleanupOldLogs 等静态方法，所有内部异常均回退到 NLog.Error 记录。
- ShaoLu/Models/StepExecutionLog.cs：FreeSql 实体映射，字段包含 StepUid、StepFileName、StepName、InputText、OCRText、ExecutedAt。

**架构与约定**
1. 日志级别策略：NLog 规则统一从 Debug 开始记录；应用级关键事件使用 Info，异常使用 Error/Fatal，未捕获异常在 App.xaml.cs 中分别以 Error 或 Fatal 记录。
2. 异步写入：NLog targets 声明 async="true"，避免阻塞 UI 线程。
3. 结构化字段：业务执行日志通过 StepExecutionLog 实体以关系型方式存储，支持关键字搜索、文件名/UID/时间范围筛选、分页与排序。
4. 日志清理：Startup 时调用 ExecutionLogService.CleanupOldLogs(Settings.App.LogRetentionDays)，保留天数由设置控制，≤0 则跳过清理。
5. 错误降级：ExecutionLogService 的写库操作被 try/catch 包裹，失败时仅记录 NLog.Error，不中断主流程。

**约束与规范**
- 所有类通过 NLog.LogManager.GetCurrentClassLogger() 获取 logger 实例，未在代码中发现自定义 Logger 工厂或 DI 注入。
- 日志布局固定为 ${longdate} | ${uppercase:${level}} | ${logger} | ${message} ${exception:format=Message}，便于统一解析。
- 执行日志数据库路径硬编码为 %APPDATA%\AutoShaoLu\execution_log.db，未提供可配置项。
- NLog 配置 throwExceptions="false"，运行时异常不会向上抛出。