---
kind: dependency_management
name: NuGet + MSBuild 包管理策略
category: dependency_management
scope:
    - '**'
source_files:
    - ShaoLu/ShaoLu.csproj
    - NUnitTest/NUnitTest.csproj
    - ShaoLu.slnx
---

本仓库采用 .NET Framework 4.8 的 NuGet 包管理机制，通过项目文件中的 `<PackageReference>` 元素声明依赖，由 MSBuild 在构建时自动还原。具体实践如下：

**包管理方式**
- 主项目 ShaoLu.csproj 使用 `<PackageReference>` 直接声明所有第三方库版本，包括 CommunityToolkit.Mvvm、EPPlus、FreeSql.Provider.Sqlite、InputSimulator、Microsoft.Extensions.Logging、NLog、OpenCvSharp4、TesseractOCR、System.Drawing.Common、System.Reactive、System.Text.Json、UTF.Unknown、WPFLocalizeExtension 等。
- 测试项目 NUnitTest.csproj 同样使用 `<PackageReference>` 声明 NUnit 4、NUnit3TestAdapter、FlaUI.UIA3 等测试依赖。
- 两个项目均启用 `<Deterministic>true</Deterministic>` 确保可重复构建。

**本地二进制引用**
- WPFDevelopers.dll 通过 `<HintPath>..\Reference\WPFDevelopers.dll</HintPath>` 以本地 DLL 引用方式引入，未走 NuGet 包管理。
- TesseractOCR 包被标记为 `ExcludeAssets="compile;runtime"`，实际 OCR 功能依赖项目内 tessdata 目录下的语言数据文件（chi_sim.traineddata、eng.traineddata 等）。

**解决方案编排**
- ShaoLu.slnx 作为解决方案入口，统一配置 Any CPU 和 x64 两种平台，并将 NUnitTest 与 ShaoLu 两个项目纳入同一解决方案。
- 测试项目通过 `<ProjectReference>` 直接引用主项目 ShaoLu.csproj，实现单元测试对业务代码的覆盖。

**发布与部署**
- 项目配置了基于 UNC 路径的 ClickOnce 发布目标（PublishUrl 指向网络共享），支持后台更新模式。
- 包含 BootstrapperPackage 用于安装 .NET Framework 4.8 运行时。
- 应用清单使用临时证书签名（ShaoLu_TemporaryKey.pfx），但 `SignManifests` 设置为 false。

**约束与约定**
- 所有 NuGet 包均在 csproj 中显式指定版本号，未使用浮动版本范围。
- 未检出 packages.lock.json 或 global.json 文件，版本锁定依赖 NuGet 缓存机制。
- 未检出 NuGet.config 或私有源配置，默认使用 nuget.org 公共源。
- .gitignore 中包含对 tools/packages.config 的注释规则，表明项目不使用传统的 packages.config 方式。