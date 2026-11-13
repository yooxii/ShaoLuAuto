using ShaoLu.Viewmodels;
using System.Windows;
using System.Windows.Controls;

namespace ShaoLu.Views
{
    /// <summary>
    /// 日志窗口：两个页签——「执行日志」（数据库里的步骤执行记录）与「程序日志」（NLog 文本日志）。
    /// 两者都以纯文本方式展示，一行一条记录，可阅读、选中、复制。
    /// </summary>
    public partial class WindowExecutionLog : Window
    {
        private readonly ExecutionLogViewModel _viewModel;

        /// <summary>程序日志页签的 ViewModel（页签切到前台时才读文件、开自动刷新）</summary>
        public ProgramLogViewModel ProgramLog { get; }

        public WindowExecutionLog()
        {
            InitializeComponent();
            _viewModel = new ExecutionLogViewModel();
            ProgramLog = new ProgramLogViewModel();
            DataContext = _viewModel;

            Closed += (_, __) =>
            {
                _viewModel.StopWatching();
                ProgramLog.Deactivate();
            };
        }

        private void LogTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 页签内的下拉框也会冒泡 SelectionChanged，只处理页签本身的选择变化
            if (!ReferenceEquals(e.Source, LogTabs)) return;

            if (LogTabs.SelectedIndex == 1)
                ProgramLog.Activate();
            else
                ProgramLog.Deactivate();
        }

        /// <summary>复制执行日志当前页全文</summary>
        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            CopyText(LogTextBox?.Text);
        }

        /// <summary>复制程序日志当前筛选结果全文</summary>
        private void CopyProgramLog_Click(object sender, RoutedEventArgs e)
        {
            CopyText(ProgramLogTextBox?.Text);
        }

        private static void CopyText(string text)
        {
            try
            {
                if (!string.IsNullOrEmpty(text))
                    Clipboard.SetText(text);
            }
            catch
            {
                // 剪贴板被占用等异常不影响查看
            }
        }
    }
}
