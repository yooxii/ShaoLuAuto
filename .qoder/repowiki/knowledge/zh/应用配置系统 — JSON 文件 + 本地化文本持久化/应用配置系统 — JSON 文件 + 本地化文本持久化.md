---
kind: configuration_system
name: 应用配置系统 — JSON 文件 + 本地化文本持久化
category: configuration_system
scope:
    - '**'
source_files:
    - ShaoLu/Models/Settings.cs
    - ShaoLu/Services/SettingsService.cs
    - ShaoLu/Services/IConfigurationService.cs
    - ShaoLu/Services/LanguageService.cs
    - ShaoLu/Utils/SingletonLocator.cs
    - ShaoLu/App.xaml.cs
    - ShaoLu/NLog.config
    - ShaoLu/App.config
---

## 1. 使用的系统与方案
- **运行时配置**：基于 `System.Text.Json` 的纯 JSON 文件持久化，无外部配置框架（如 Microsoft.Extensions.Configuration）。
- **本地化语言设置**：通过一个独立的 `current_language.txt` 文本文件保存当前 UI 语言代码，配合 `WPFLocalizeExtension` 的 `LocalizeDictionary` 生效。
- **日志配置**：使用 NLog 的 `NLog.config` 配置文件。
- **依赖注入**：通过 `CommunityToolkit.Mvvm.DependencyInjection` 的 `ServiceCollection` 在 `App.OnStartup` 中一次性注册服务，并暴露为静态 `SingletonLocator` 供全局访问。

## 2. 核心文件与包
- `ShaoLu/Models/Settings.cs` — 定义 `AppSettings`、`AppSettingsModel`、`StepSettingsModel`、`UserSettingsModel`、`HotKeySetting`、`FontModel` 等配置模型。
- `ShaoLu/Services/SettingsService.cs` — 负责 `settings.json` 的异步加载与保存，路径位于 `AppDomain.CurrentDomain.BaseDirectory/settings.json`。
- `ShaoLu/Services/IConfigurationService.cs` — 定义了键值形式的通用配置接口（Get/Set/Remove），但当前未被 SettingsService 实现。
- `ShaoLu/Services/LanguageService.cs` — 读取/写入 `current_language.txt`，初始化并切换 WPF 本地化文化。
- `ShaoLu/Utils/SingletonLocator.cs` — 启动时调用 `SettingsService.LoadAsync()` 将 `AppSettings` 作为静态单例暴露给全应用。
- `ShaoLu/App.xaml.cs` — 应用启动入口，先初始化语言，再构建 DI 容器，最后显示主窗口。
- `ShaoLu/NLog.config` — NLog 日志输出目标与规则。
- `ShaoLu/App.config` — 仅声明 .NET Framework 4.8 运行时版本，不包含应用配置项。
- `ShaoLu/Properties/Settings.settings` — VS 用户设置文件模板，当前为空，未实际使用。

## 3. 架构与约定
- **分层清晰**：配置模型集中在 `Models`，读写逻辑集中在 `Services`，全局访问通过 `Utils.SingletonLocator`。
- **JSON 序列化选项统一**：`SettingsService` 使用 `WriteIndented = true`；`StepsFile` 对步骤脚本使用更宽松的解析选项（允许注释、尾随逗号、大小写不敏感）。
- **默认值策略**：所有配置类字段都提供合理的默认值，当 `settings.json` 不存在或反序列化失败时返回空对象，保证应用可正常启动。
- **语言优先顺序**：启动时优先读取上次保存的语言，否则回退到系统 `CurrentUICulture`。
- **配置与业务解耦**：`IConfigurationService` 抽象了键值配置能力，但未与 `SettingsService` 打通，属于预留扩展点。

## 4. 开发者应遵循的规则
- **新增配置项**：在 `Models/Settings.cs` 中添加属性并赋予合理默认值，然后通过 `SingletonLocator.Settings` 读取，通过 `SettingsService.SaveAsync` 保存。
- **不要直接操作 settings.json**：必须通过 `SettingsService` 的 `LoadAsync` / `SaveAsync` 进行读写，确保线程安全与格式一致。
- **语言切换**：通过 `LanguageService.SetLanguage(cultureName)` 修改，会自动持久化到 `current_language.txt`。
- **避免硬编码路径**：如需自定义配置目录，应在 `SettingsService` 中集中修改，而非在各处拼接路径。
- **键值配置**：若需临时键值存储，应实现 `IConfigurationService` 并在 `App.OnStartup` 中注册，而不是散落在各处文件。
- **兼容旧格式**：步骤脚本仍保留 `LoadStepsFromJson` / `SaveStepsToJson` 方法（标记 `[Obsolete]`），新代码应使用 `.autostep` 压缩包格式。