using ShaoLu.Viewmodels;
using System.Windows;

namespace ShaoLu.Views
{
    /// <summary>
    /// WindowExecutionLog.xaml 的交互逻辑。
    /// 窗口以文本方式直接展示原始执行日志（一行一条记录），可阅读、选中、复制。
    /// </summary>
    public partial class WindowExecutionLog : Window
    {
        private readonly ExecutionLogViewModel _viewModel;

        public WindowExecutionLog()
        {
            InitializeComponent();
            _viewModel = new ExecutionLogViewModel();
            DataContext = _viewModel;
            Closed += (_, __) => _viewModel.StopWatching();
        }

        /// <summary>复制当前显示的执行日志全文</summary>
        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(LogTextBox.Text))
                    Clipboard.SetText(LogTextBox.Text);
            }
            catch
            {
                // 剪贴板被占用等异常不影响查看
            }
        }
    }
}
