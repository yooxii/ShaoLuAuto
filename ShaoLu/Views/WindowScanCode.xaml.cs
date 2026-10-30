using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ShaoLu.Views
{
    /// <summary>
    /// 扫码对话框：运行时弹出，扫码枪输入或手工输入条码，回车/确定返回。
    /// 弹出时主动把窗口提到前台并让输入框拿到键盘焦点（自动化运行时主窗口常常不在前台，
    /// Windows 会阻止后台程序抢焦点，所以这里先临时置顶一次再取消，并显式调用 SetForegroundWindow）。
    /// </summary>
    public partial class WindowScanCode : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>扫码得到的条码（取消时为 null）</summary>
        public string ScannedCode { get; private set; }

        public WindowScanCode()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Activated += (_, __) => FocusCodeBox();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ActivateWindow();
        }

        /// <summary>把窗口提到前台、输入框拿到键盘焦点。</summary>
        private void ActivateWindow()
        {
            try
            {
                // 临时置顶可绕开“后台程序不能抢前台”的限制，随后立刻取消，不改变正常层级
                Topmost = true;
                Topmost = false;
                Activate();

                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);

                FocusCodeBox();

                // 布局/激活完成后再抢一次，避免个别机器上第一次 Activate 被系统忽略
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        Activate();
                        FocusCodeBox();
                    }
                    catch { }
                }), DispatcherPriority.Input);
            }
            catch
            {
                // 抢焦点失败不影响输入框可用
            }
        }

        private void FocusCodeBox()
        {
            try
            {
                CodeBox.Focus();
                Keyboard.Focus(CodeBox);
            }
            catch { }
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

        /// <summary>主窗口最小化/还原时，扫码窗口跟着一起最小化/还原。</summary>
        private void Owner_StateChanged(object sender, System.EventArgs e)
        {
            try
            {
                if (Owner == null) return;
                WindowState = Owner.WindowState == WindowState.Minimized
                    ? WindowState.Minimized
                    : WindowState.Normal;
            }
            catch { }
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

            window.ShowActivated = true;
            // 跟随主窗口最小化/还原
            if (window.Owner != null)
            {
                window.Owner.StateChanged += window.Owner_StateChanged;
                window.Closed += (_, __) => window.Owner.StateChanged -= window.Owner_StateChanged;
            }
            return window.ShowDialog() == true ? window.ScannedCode : null;
        }
    }
}
