using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using ShaoLu.Viewmodels;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using WPFLocalizeExtension.Engine;

namespace ShaoLu
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // 1. 初始化语言
            Services.LanguageService.Initialize();


            // 2. 配置依赖注入容器 (一次性注册所有服务)
            var services = new ServiceCollection();

            // 注册 ViewModel 单例
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<StepsViewModel>();

            // 如果有其他服务 (如 IUserService)，也在这里注册
            // services.AddSingleton<IUserService, UserService>();

            // 3. 构建并配置 Ioc 容器
            Ioc.Default.ConfigureServices(services.BuildServiceProvider());
        }
    }
}
