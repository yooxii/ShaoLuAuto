using CommunityToolkit.Mvvm.DependencyInjection;
using ShaoLu.Viewmodels;

namespace ShaoLu.Utils
{
    public class ViewModelLocator
    {
        public static MainViewModel Main => Ioc.Default.GetRequiredService<MainViewModel>();
        // 实际项目中通常从 DI 容器或单例获取
    }
}
