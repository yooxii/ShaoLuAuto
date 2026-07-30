---
kind: frontend_style
name: WPF 前端样式体系：WPFDevelopers 主题 + 自定义资源字典
category: frontend_style
scope:
    - '**'
source_files:
    - ShaoLu/App.xaml
    - ShaoLu/MainWindow.xaml
    - ShaoLu/Themes/Generic.xaml
    - ShaoLu/Themes/Styles.xaml
    - ShaoLu/Themes/Icons.xaml
    - ShaoLu/Templates/SettingsTemplates.xaml
    - ShaoLu/Converters/BoolToInverseBoolConverter.cs
    - ShaoLu/Controls/MultiCropImage.cs
---

## 系统概述

ShaoLu 自动化烧录客户端采用 WPF 框架构建，前端样式体系基于 **WPFDevelopers** 第三方控件库与项目内自定义 ResourceDictionary 相结合的方式实现。应用通过 App.xaml 中的 ResourceDictionary.MergedDictionaries 集中管理主题资源，支持 Light/Dark 自动切换。

## 核心架构与文件组织

### 主题入口与全局资源
- `App.xaml`：应用级资源字典入口，合并 WPFDevelopers 的 Theme.xaml 和 Resources，启用自动主题跟随系统
- `MainWindow.xaml`：主窗口使用 lex:Loc 多语言绑定，体现 MVVM + 本地化模式

### 自定义样式层（Themes/）
- `Generic.xaml`：自定义控件 MultiCropImage 的 ControlTemplate 定义，包含裁剪框、旋转手柄等交互元素样式
- `Styles.xaml`：图像编辑工具栏专用样式，定义 IconButton、IconCloseButton、ArrowIconButton、IconCheckBox、IconRadioButton 等组件模板，统一 30x30 尺寸、#1887bf 主色调、悬停高亮效果
- `Icons.xaml`：集中管理所有 SVG PathGeometry 图标资源，命名规范为 `ImageTool.Icon.{功能}`，涵盖裁剪、缩放、旋转、绘制、颜色选择等完整工具集

### 模板与视图分离
- `Templates/` 目录存放 SettingsTemplates.xaml、StepDetailTemplates.xaml、StepSummaryTemplates.xaml，按功能域分离 DataTemplate 定义
- `Views/` 目录按 Window 和 UserControl 分类，每个界面独立 xaml.cs 代码后置
- `Converters/` 目录集中处理数据绑定转换，如 BoolToVisibilityConverter、ConditionModeToVisibilityConverter 等

## 设计约定与规范

### 样式命名约定
- 工具栏按钮样式统一前缀 `ImageTool.`，便于在图像编辑模块内复用
- 图标资源使用 PathGeometry 而非图片文件，保持矢量缩放质量
- 颜色常量集中在 Generic.xaml 中定义，如 PrimaryNormalSolidColorBrush = #3498DB

### 交互状态处理
- 所有按钮样式均定义 IsMouseOver、IsPressed、IsEnabled 三种状态的视觉反馈
- 使用 Storyboard 定义淡入动画（ImageTool.FadeIn），时长 0.3s
- DropShadowEffect 用于箭头按钮的悬浮阴影效果

### 多语言与国际化
- 通过 WPFLocalizeExtension (lex:) 实现 UI 文本本地化
- 资源文件按语言分置：Strings.resx（默认）、Strings.zh-CN.resx、Strings.en-US.resx、Strings.zh-TW.resx
- 所有用户可见文本必须通过 {lex:Loc Key} 绑定，禁止硬编码字符串

## 依赖与扩展点

### 第三方库集成
- WPFDevelopers：提供现代化控件库和主题系统，支持 Radius 圆角、Color 主题色自定义
- CommunityToolkit.Mvvm：MVVM 基础框架，提供 RelayCommand、BindableBase 等
- NLog：日志记录框架，配置文件 NLog.config

### 样式扩展建议
- 新增控件样式应遵循现有命名空间约定，放在 Themes/Styles.xaml
- 图标资源统一添加到 Icons.xaml，保持 ImageTool.Icon.* 命名格式
- 复杂控件的 ControlTemplate 应放在 Generic.xaml，并遵循 PART_* 命名规范
- 颜色值应提取为静态资源，避免魔法数字散落各处