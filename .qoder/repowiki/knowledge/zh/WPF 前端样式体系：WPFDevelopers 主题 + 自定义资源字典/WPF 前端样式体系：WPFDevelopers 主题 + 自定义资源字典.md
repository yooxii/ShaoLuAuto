---
kind: frontend_style
name: WPF 前端样式体系：WPFDevelopers 主题 + 自定义资源字典
category: frontend_style
scope:
    - '**'
source_files:
    - ShaoLu/App.xaml
    - ShaoLu/Themes/Generic.xaml
    - ShaoLu/Themes/Styles.xaml
    - ShaoLu/Themes/Icons.xaml
    - ShaoLu/MainWindow.xaml
    - ShaoLu/Templates/SettingsTemplates.xaml
    - ShaoLu/Templates/StepDetailTemplates.xaml
    - ShaoLu/Templates/StepSummaryTemplates.xaml
---

本项目的 UI 样式基于 WPF 框架，采用 **WPFDevelopers** 第三方控件库作为主题与组件基础，并通过本地 ResourceDictionary 进行扩展与定制。整体风格遵循 MVVM 模式，XAML 视图与 C# 代码分离，样式集中在 Themes 目录的 ResourceDictionary 中统一管理。

### 1. 使用的系统与工具
- **WPFDevelopers**：通过 `xmlns:wd="https://github.com/WPFDevelopersOrg/WPFDevelopers"` 引入，在 App.xaml 中合并其 Theme.xaml 与 Resources，启用 Light/Dark 自动主题切换能力。
- **WPF Localize Extension (lex)**：通过 `xmlns:lex="http://wpflocalizeextension.codeplex.com"` 实现多语言资源绑定（Strings.resx / Strings.zh-CN.resx / Strings.en-US.resx）。
- **原生 WPF ResourceDictionary**：用于定义全局样式、控件模板、动画 Storyboard、图标 PathGeometry 等设计资产。

### 2. 核心样式文件与位置
- `ShaoLu/App.xaml`：应用级资源入口，合并 WPFDevelopers 主题与本地资源。
- `ShaoLu/Themes/Generic.xaml`：自定义控件 `MultiCropImage` 的 ControlTemplate 与全局色值（如 `PrimaryNormalSolidColorBrush`）。
- `ShaoLu/Themes/Styles.xaml`：图像编辑工具栏按钮、CheckBox、RadioButton 的专用样式（`ImageTool.*` 命名空间前缀），包含悬停、按下、禁用状态触发器。
- `ShaoLu/Themes/Icons.xaml`：集中存放所有 SVG 风格的 PathGeometry 图标（关闭、设置、裁剪、缩放、旋转、画笔、颜色等），供 `ImageTool.Icon.*` 键引用。
- `ShaoLu/Templates/*.xaml`：步骤模板（SettingsTemplates、StepDetailTemplates、StepSummaryTemplates）使用 wd 控件库渲染界面。
- `ShaoLu/Views/*.xaml`：各 Window/UserControl 视图，统一通过 `wd:` 前缀调用 WPFDevelopers 控件。

### 3. 架构与约定
- **主题分层**：App.xaml 加载 WPFDevelopers 主题 → 再加载本地 ResourceDictionary，确保本地样式可覆盖默认主题。
- **控件模板化**：自定义控件（如 `MultiCropImage`）在 Generic.xaml 中提供完整 ControlTemplate，使用 `PART_*` 命名约定暴露可视树节点。
- **图标与样式命名空间隔离**：图像编辑相关样式统一以 `ImageTool.` 为 Key 前缀，避免与其他模块冲突。
- **MVVM 绑定**：XAML 中通过 `d:DataContext` 与设计时 ViewModel 关联，运行时由 ViewModels 层驱动 UI。
- **多语言**：所有用户可见文本通过 `{lex:Loc ...}` 标记绑定到 resx 资源文件，支持 en-US、zh-CN、zh-TW 三种语言。

### 4. 约束与规范
- 所有 XAML 视图必须声明 `xmlns:wd="https://github.com/WPFDevelopersOrg/WPFDevelopers"` 以使用标准控件。
- 自定义控件模板必须遵循 WPF 的 `PART_*` 命名约定，以便代码后台通过 `GetTemplateChild` 访问。
- 图标必须以 `PathGeometry` 形式定义在 Icons.xaml 中，并通过 `Key="ImageTool.Icon.<Name>"` 引用，禁止在 XAML 中内联路径数据。
- 主题色通过静态 SolidColorBrush（如 `PrimaryNormalSolidColorBrush`）集中管理，避免硬编码颜色值。
- 动画统一使用 Storyboard 定义在 Styles.xaml 中，通过 `StaticResource` 引用，禁止在 XAML 中直接编写 `<EventTrigger>` 动画。