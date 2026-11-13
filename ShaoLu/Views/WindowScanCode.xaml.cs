using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ShaoLu.Views
{
    /// <summary>
    /// 扫码对话框：运行时弹出，扫码枪输入或手工输入条码。
    /// 关闭方式：回车 / 制表 / 点「确定」；扫码枪成串输入结束停顿片刻也会自动确认，
    /// 条码里夹带的 \r / \n / \t 结束符会被去掉（相当于回车）。
    /// 弹出时主动把窗口提到前台并让输入框拿到键盘焦点（自动化运行时主窗口常常不在前台，
    /// Windows 会阻止后台程序抢焦点，所以这里先临时置顶一次再取消，并显式调用 SetForegroundWindow）。
    /// </summary>
    public partial class WindowScanCode : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>扫码枪输入结束后静默多久自动确认（毫秒）。</summary>
        private const int ScanIdleConfirmMs = 350;

        /// <summary>扫码枪常见的结束符：回车、换行、制表符。</summary>
        private static readonly char[] ScannerTerminators = { '\r', '\n', '\t' };

        /// <summary>按键节奏计时（.NET Framework 4.8 没有 Environment.TickCount64，用 Stopwatch）。</summary>
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        private readonly DispatcherTimer _idleTimer;
        private readonly Utils.ScanBurstTracker _burst = new Utils.ScanBurstTracker();
        private bool _pasteSeen;
        private bool _confirming;

        /// <summary>扫码得到的条码（取消时为 null）</summary>
        public string ScannedCode { get; private set; }

        public WindowScanCode()
        {
            InitializeComponent();

            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ScanIdleConfirmMs) };
            _idleTimer.Tick += (_, __) => OnIdleElapsed();

            // 节奏判定走 PreviewTextInput：粘贴不会触发它，天然排除「整段粘贴」被误判成扫码
            CodeBox.PreviewTextInput += CodeBox_PreviewTextInput;
            CodeBox.PreviewKeyDown += CodeBox_PreviewKeyDown;
            CodeBox.TextChanged += CodeBox_TextChanged;
            DataObject.AddPastingHandler(CodeBox, CodeBox_OnPaste);
            Closed += (_, __) => _idleTimer.Stop();

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

        #region 扫码枪识别与自动确认

        /// <summary>字符输入：按相邻间隔判断这一串是不是扫码枪打进来的。</summary>
        private void CodeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            string text = e.Text ?? string.Empty;
            if (text.Length == 0) return;

            // 条码里夹带的结束符（CR / LF / Tab）：不写进输入框，直接当成「扫完了」
            if (text.IndexOfAny(ScannerTerminators) >= 0)
            {
                e.Handled = true;
                Confirm();
                return;
            }

            _pasteSeen = false;
            _burst.OnChar(Clock.ElapsedMilliseconds, text.Length);

            // 输入还在继续就重置静默计时；停下来 ScanIdleConfirmMs 后才判断要不要自动确认
            _idleTimer.Stop();
            _idleTimer.Start();
        }

        /// <summary>静默计时到点：字符够多、节奏够快（扫码枪），就自动确认关闭。</summary>
        private void OnIdleElapsed()
        {
            _idleTimer.Stop();
            if (_pasteSeen) return;
            if (_burst.LooksLikeScanner())
                Confirm();
        }

        private void CodeBox_OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            // 粘贴不是扫码枪输入：清掉节奏状态，交给回车 / 确定
            _pasteSeen = true;
            _burst.Reset();
            _idleTimer.Stop();
        }

        /// <summary>制表符也当结束符（部分扫码枪把后缀配成 Tab）。</summary>
        private void CodeBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab)
            {
                e.Handled = true;
                Confirm();
            }
        }

        /// <summary>输入框里出现 CR / LF / Tab（例如整段粘贴带进来的）时，去掉结束符并确认。</summary>
        private void CodeBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string raw = CodeBox.Text ?? string.Empty;
            int cut = raw.IndexOfAny(ScannerTerminators);
            if (cut < 0) return;

            // 结束符之前的才是条码；剩下的内容写回输入框，避免把 \r\n 存进条码
            string code = raw.Substring(0, cut);
            CodeBox.Text = code;
            Confirm(code);
        }

        #endregion

        private void CodeBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Confirm();
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Confirm();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            ScannedCode = null;
            DialogResult = false;
        }

        /// <summary>取值并关闭窗口（结束符、回车、点「确定」、自动确认都走这里）。</summary>
        private void Confirm(string code = null)
        {
            if (_confirming) return;
            _confirming = true;
            _idleTimer.Stop();
            _burst.Reset();

            ScannedCode = (code ?? CodeBox.Text ?? string.Empty).Trim();
            try
            {
                DialogResult = true;
            }
            catch
            {
                // 非模态方式打开时没有 DialogResult，直接关闭
                Close();
            }
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
