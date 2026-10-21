using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;

// 有关程序集的一般信息由以下
// 控制。更改这些特性值可修改
// 与程序集关联的信息。
[assembly: AssemblyTitle("ShaoLu")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("ShaoLu")]
[assembly: AssemblyCopyright("Copyright ©  2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// 允许单元测试程序集访问 internal 成员（如 StepsFile.WorkDirResolver，供回归测试替换工作目录）
[assembly: InternalsVisibleTo("NUnitTest")]

// 将 ComVisible 设置为 false 会使此程序集中的类型
//对 COM 组件不可见。如果需要从 COM 访问此程序集中的类型
//请将此类型的 ComVisible 特性设置为 true。
[assembly: ComVisible(false)]

//若要开始生成可本地化的应用程序，请设置
//.csproj 文件中的 <UICulture>CultureYouAreCodingWith</UICulture>
//在 <PropertyGroup> 中。例如，如果你使用的是美国英语。
//使用的是美国英语，请将 <UICulture> 设置为 en-US。  然后取消
//对以下 NeutralResourceLanguage 特性的注释。  更新
//以下行中的“en-US”以匹配项目文件中的 UICulture 设置。

//[assembly: NeutralResourcesLanguage("en-US", UltimateResourceFallbackLocation.Satellite)]


[assembly: ThemeInfo(
    ResourceDictionaryLocation.None, //主题特定资源词典所处位置
                                     //(未在页面中找到资源时使用，
                                     //或应用程序资源字典中找到时使用)
    ResourceDictionaryLocation.SourceAssembly //常规资源词典所处位置
                                              //(未在页面中找到资源时使用，
                                              //、应用程序或任何主题专用资源字典中找到时使用)
)]


// 程序集的版本信息由下列四个值组成: 
//
//      主版本
//      次版本
//      生成号
//      修订号
//
// 版本号三处必须一致（tools/build-app.ps1 每次构建都会校验，不一致直接报错）：
//      1. ShaoLu.csproj 的 <ApplicationVersion>  -> ClickOnce 清单里的 ShaoLu.exe 版本
//      2. 本文件的 AssemblyVersion              -> 程序集标识
//      3. 本文件的 AssemblyFileVersion          -> 资源管理器里的「文件版本/产品版本」
// 需要临时用别的号构建时：tools/build-app.ps1 -Version <号> / tools/pack-release.ps1 -Version <号>。
[assembly: AssemblyVersion("0.6.8.2611")]
[assembly: AssemblyFileVersion("0.6.8.2611")]
