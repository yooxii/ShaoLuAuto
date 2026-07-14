using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using ShaoLu.Services;
using ShaoLu.Viewmodels;
using ShaoLu.Viewmodels.AutomationStep;
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
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                Logger.Info("Program start...");
                base.OnStartup(e);

                Logger.Info("Init LanguageService...");
                // 1. 初始化语言
                Services.LanguageService.Initialize();
                Logger.Info("Init LanguageService finished.");


                Logger.Info("Init OtherServices...");
                // 2. 配置依赖注入容器 (一次性注册所有服务)
                var services = new ServiceCollection();

                // 注册 ViewModel 单例
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<StepsViewModel>();
                services.AddSingleton<FileServices>();

                // 如果有其他服务 (如 IUserService)，也在这里注册
                // services.AddSingleton<IUserService, UserService>();

                // 3. 构建并配置 Ioc 容器
                Ioc.Default.ConfigureServices(services.BuildServiceProvider());
                Logger.Info("Ioc container configured.");
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex, "Program start error.");
                throw;
            }
        }
        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info("Program exit...");
            Logger.Info("Dispose ImageRecognitionBase...");
            var fileServices = Ioc.Default.GetService<FileServices>();
            var stepsViewModel = Ioc.Default.GetService<StepsViewModel>();
            foreach(var step in stepsViewModel.AutomationStepBases)
            {
                if (step is ImageRecognitionBase imageRecognitionStep)
                {
                    imageRecognitionStep.Dispose();
                }
            }
            fileServices.CommitPendingDeletions();
            Logger.Info("Finished.");

            base.OnExit(e);
        }
    }
}
