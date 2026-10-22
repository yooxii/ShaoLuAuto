using System.Windows;
using System.Windows.Input;

namespace ShaoLu.Views
{
    /// <summary>
    /// 扫码对话框：运行时弹出，扫码枪输入或手工输入条码，回车/确定返回。
    /// </summary>
    public partial class WindowScanCode : Window
    {
        /// <summary>扫码得到的条码（取消时为 null）</summary>
        public string ScannedCode { get; private set; }

        public WindowScanCode()
        {
            InitializeComponent();
            Loaded += (_, __) =>
            {
                CodeBox.Focus();
                Keyboard.Focus(CodeBox);
            };
        }

        private void CodeBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ScannedCode = (CodeBox.Text ?? string.Empty).Trim();
                DialogResult = true;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ScannedCode = (CodeBox.Text ?? string.Empty).Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            ScannedCode = null;
            DialogResult = false;
        }

        /// <summary>在 UI 线程弹出扫码对话框并等待结果；取消返回 null。</summary>
        public static string ShowAndWait(Window owner = null)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                return dispatcher.Invoke(() => ShowAndWait(owner));

            var window = new WindowScanCode();
            if (owner != null) window.Owner = owner;
            else if (Application.Current?.MainWindow != null) window.Owner = Application.Current.MainWindow;

            return window.ShowDialog() == true ? window.ScannedCode : null;
        }
    }
}
