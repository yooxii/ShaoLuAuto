---
kind: build_system
name: MSBuild + NuGet 构建与发布体系
category: build_system
scope:
    - '**'
source_files:
    - ShaoLu.slnx
    - ShaoLu/ShaoLu.csproj
    - NUnitTest/NUnitTest.csproj
---

该仓库采用传统的 .NET Framework 4.8 WPF 桌面应用构建方案，以 MSBuild 为核心构建引擎，通过 .csproj 文件管理项目依赖、编译选项与发布配置，配合 Visual Studio Solution Extensions（.slnx）统一编排多项目解决方案。

**构建系统与工具链**
- 构建引擎：MSBuild（ToolsVersion 15.0），目标框架为 .NET Framework 4.8
- 包管理：NuGet PackageReference 方式管理依赖，包括 OpenCvSharp4、TesseractOCR、NLog、EPPlus、CommunityToolkit.Mvvm 等
- 解决方案编排：ShaoLu.slnx 定义 Any CPU 与 x64 两种平台配置，强制 ShaoLu 主项目使用 x64 平台
- 测试框架：NUnit 4.6.1 + NUnit3TestAdapter 6.2.0，测试项目通过 ProjectReference 引用主项目

**关键构建配置**
- 输出类型：WinExe（WPF 应用程序），启动对象为 ShaoLu.App
- 语言版本：preview（启用 C# 预览特性）
- 确定性构建：Deterministic=true，确保可重复构建
- 运行时标识：win-x64，仅支持 Windows x64 平台
- 平台目标：所有配置均固定为 x64，不生成 AnyCPU 混合模式二进制

**发布与部署策略**
- 发布目标：UNC 路径 \\bnt56\品保部\ORT實驗資料\21. ORT Programs\6. AutoShaoLu\Publish\
- 安装方式：ClickOnce 部署，支持后台更新（UpdateMode=Background，UpdateInterval=7天）
- 程序集签名：使用 ShaoLu_TemporaryKey.pfx 证书生成清单（GenerateManifests=true），但未启用清单签名（SignManifests=false）
- 引导程序：包含 .NET Framework 4.8 和 .NET Framework 3.5 SP1 作为可选引导包
- 资源复制：tessdata 下的 OCR 训练数据、NLog.config、图标等资源通过 CopyToOutputDirectory=PreserveNewest 复制到输出目录

**项目结构与依赖关系**
- ShaoLu.csproj：主应用程序项目，包含 WPF UI、MVVM 视图模型、服务层、工具类等全部业务逻辑
- NUnitTest.csproj：单元测试项目，引用主项目并覆盖自动化步骤、条件评估器、JSON 序列化等核心功能
- Reference/WPFDevelopers.dll：本地引用的第三方 WPF 控件库
- 外部依赖通过 NuGet 包管理器自动下载，无需手动维护 bin 目录

**构建约束与约定**
- 所有配置文件（App.config、NLog.config）通过 None 项包含并复制到输出目录
- 资源文件（.resx）通过 PublicResXFileCodeGenerator 自动生成 Designer.cs 代码文件
- XAML 文件通过 MSBuild:Compile 生成器在构建时编译为 BAML
- 调试配置使用 full 调试符号，Release 配置使用 pdbonly 优化体积
- 警告级别设置为 4，错误报告策略为 prompt