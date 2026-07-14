using CommunityToolkit.Mvvm.DependencyInjection;
using ShaoLu.Services;
using ShaoLu.Viewmodels;

namespace ShaoLu.Utils
{
    public class SingletonLocator
    {
        public static MainViewModel Main => Ioc.Default.GetRequiredService<MainViewModel>();
        public static StepsViewModel Steps => Ioc.Default.GetRequiredService<StepsViewModel>();
        public static FileServices FileServices => Ioc.Default.GetRequiredService<FileServices>();
        // 实际项目中通常从 DI 容器或单例获取
    }
}
