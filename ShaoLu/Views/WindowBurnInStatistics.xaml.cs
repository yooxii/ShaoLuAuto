using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Viewmodels;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ShaoLu.Views
{
    /// <summary>
    /// 烧录统计窗口（三页签：工令汇总 / 烧录明细 / 配置）
    /// </summary>
    public partial class WindowBurnInStatistics : Window
    {
        /// <summary>当前已打开的统计窗口（单例）</summary>
        private static WindowBurnInStatistics _instance;

        /// <summary>显示统计窗口（若已打开则激活）</summary>
        public static void ShowSingleton()
        {
            if (_instance != null && _instance.IsLoaded)
            {
                _instance.Activate();
                return;
            }
            _instance = new WindowBurnInStatistics();
            _instance.Show();
        }

        public WindowBurnInStatistics()
        {
            InitializeComponent();
            DataContext = new BurnInViewModel();
            // 初始切到明细页签，自动加载
            if (DataContext is BurnInViewModel vm)
            {
                vm.RefreshRecords();
            }
        }

        private void SummaryGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is BurnInService.OrderSummary summary
                && DataContext is BurnInViewModel vm)
            {
                vm.JumpToDetail(summary.OrderNo);
            }
        }

        private void MatchedText_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BurnInRecord record)
            {
                if (!string.IsNullOrEmpty(record.DisplayMatchedText))
                    WindowTextPreview.Show(LanguageService.GetLocalizedString("BurnIn_MatchedText"), record.DisplayMatchedText);
            }
        }

        private void Screenshot_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string path && !string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    // 使用系统默认程序打开截图文件
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch { /* 忽略 */ }
            }
        }
    }
}
