---
kind: configuration_system
name: 配置系统 — JSON 设置文件与 NLog 日志配置
category: configuration_system
scope:
    - '**'
source_files:
    - ShaoLu/Models/Settings.cs
    - ShaoLu/Services/SettingsService.cs
    - ShaoLu/Utils/SingletonLocator.cs
    - ShaoLu/Services/IConfigurationService.cs
    - ShaoLu/NLog.config
    - ShaoLu/App.config
---

## 1. 使用的系统与框架
- **应用设置**：使用 C# `System.Text.Json` 将强类型模型序列化为 `settings.json` 文件，位于应用程序目录（`AppDomain.CurrentDomain.BaseDirectory/settings.json`）。
- **用户数据**：通过 FreeSql + SQLite 持久化到 `%APPDATA%\AutoShaoLu\users.db`。
- **日志配置**：NLog 通过 `NLog.config` 管理，支持自动重载、异步写入、按日期分文件输出。
- **.NET Framework 运行时版本**：由 `App.config` 指定为 .NET Framework 4.8。

## 2. 核心文件与包
- `ShaoLu/Models/Settings.cs` — 定义 `AppSettings`、`AppSettingsModel`、`StepSettingsModel`、`UserSettingsModel`、`HotKeySetting`、`OverlaySetting` 等强类型配置模型。
- `ShaoLu/Services/SettingsService.cs` — 提供 `LoadAsync()` / `SaveAsync()` 静态方法，读写 `settings.json`。
- `ShaoLu/Utils/SingletonLocator.cs` — 在应用启动时同步加载 `AppSettings` 并暴露为 `SingletonLocator.Settings`。
- `ShaoLu/Services/IConfigurationService.cs` — 定义了 `GetSettingAsync<T>` / `SaveSettingAsync<T>` / `RemoveSettingAsync` 接口（目前未见实现类）。
- `ShaoLu/NLog.config` — NLog 配置文件，定义文件与控制台目标及 Debug 级别规则。
- `ShaoLu/App.config` — 声明支持的 .NET Framework 运行时版本。
- `ShaoLu/Properties/Settings.settings` — 空的 Visual Studio 用户设置文件（未使用）。

## 3. 架构与设计约定
- **分层清晰**：配置模型集中在 `Models/Settings.cs`，持久化逻辑集中在 `Services/SettingsService.cs`，通过 `SingletonLocator` 以单例形式全局访问。
- **强类型优先**：所有设置项都有对应的 C# 属性与默认值，避免字符串键带来的拼写错误。
- **异步 I/O**：`SettingsService` 的 Load/Save 均为 `async Task`，但 `SingletonLocator` 中通过 `.GetAwaiter().GetResult()` 同步阻塞加载，存在潜在死锁风险。
- **默认值内聚**：每个配置类在属性声明处赋予合理默认值（如窗口尺寸、热键、阈值等），保证首次运行无需外部配置即可工作。
- **覆盖层可视化配置**：OCR、图像匹配、点击位置均提供独立的 `OverlaySetting` 配置（颜色 + 显示时长），便于调试。
- **日志与配置分离**：NLog 独立于业务配置，通过 XML 配置，支持运行时自动重载。

## 4. 约定与约束
- **配置文件位置固定**：`settings.json` 始终位于应用程序根目录（`BaseDirectory`），不支持环境变量或用户目录覆盖。
- **JSON 格式规范**：使用 `JsonSerializerOptions { WriteIndented = true }` 生成可读性良好的格式化 JSON。
- **空文件处理**：当 `settings.json` 不存在时，`LoadAsync()` 返回全新实例（含所有默认值），不会抛出异常。
- **用户数据存储路径**：SQLite 数据库存储在 `%APPDATA%\AutoShaoLu\users.db`，首次使用时自动创建目录。
- **密码安全**：使用 PBKDF2-SHA256（10000 次迭代）+ 随机盐进行密码哈希，比较时使用恒定时间算法防止时序攻击。
- **NLog 规则**：所有 logger 的最低级别为 Debug，同时输出到文件和控制台，文件名按日期命名（`${shortdate}.log`）。
- **IConfigurationService 未实现**：接口已定义但未找到实现类，当前仅 `SettingsService` 被实际使用。
- **Settings.settings 为空**：Visual Studio 用户设置文件未添加任何属性，未被代码引用。

## 5. 已知问题与改进点
- `SingletonLocator.Settings` 使用同步阻塞方式加载异步配置，可能引发 UI 线程死锁。
- `IConfigurationService` 接口闲置，建议统一抽象或移除。
- 缺少配置验证机制，无法在保存前校验配置值的合法性。
- 没有配置迁移或版本管理机制，未来模型变更可能导致兼容性问题。