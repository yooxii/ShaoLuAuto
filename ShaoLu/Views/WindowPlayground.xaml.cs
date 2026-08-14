using ShaoLu.Utils;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ShaoLu.Views
{
    /// <summary>
    /// 控件试验场：左侧为各类靶子控件（供自动化测试/教学），右侧显示屏幕信息与点击记录
    /// </summary>
    public partial class WindowPlayground : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly DispatcherTimer _timer;

        #region 信息栏属性

        private string _screenNameText = string.Empty;
        private string _refreshRateText = string.Empty;
        private string _resolutionText = string.Empty;
        private string _mousePosText = string.Empty;
        private readonly StringBuilder _clickLogBuilder = new();
        private string _clickLog = string.Empty;

        public string ScreenNameText { get => _screenNameText; private set => SetProperty(ref _screenNameText, value); }
        public string RefreshRateText { get => _refreshRateText; private set => SetProperty(ref _refreshRateText, value); }
        public string ResolutionText { get => _resolutionText; private set => SetProperty(ref _resolutionText, value); }
        public string MousePosText { get => _mousePosText; private set => SetProperty(ref _mousePosText, value); }
        public string ClickLog { get => _clickLog; private set => SetProperty(ref _clickLog, value); }

        #endregion

        public WindowPlayground()
        {
            InitializeComponent();
            DataContext = this;

            UpdateScreenInfo();

            // 定时刷新鼠标位置
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += (s, e) => UpdateMousePosition();
            _timer.Start();

            Closed += (s, e) => _timer.Stop();
        }

        /// <summary>读取屏幕名称、刷新率、分辨率（启动时获取一次）</summary>
        private void UpdateScreenInfo()
        {
            try
            {
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                ScreenNameText = screen?.DeviceName ?? "-";
                ResolutionText = screen == null ? "-" : $"{screen.Bounds.Width} x {screen.Bounds.Height}";

                var dm = new NativeMethods.DEVMODE();
                dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.DEVMODE));
                if (NativeMethods.EnumDisplaySettings(null, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm))
                    RefreshRateText = $"{dm.dmDisplayFrequency} Hz";
                else
                    RefreshRateText = "-";
            }
            catch
            {
                ScreenNameText = "-";
                RefreshRateText = "-";
                ResolutionText = "-";
            }
        }

        private void UpdateMousePosition()
        {
            var pos = System.Windows.Forms.Cursor.Position;
            MousePosText = $"({pos.X}, {pos.Y})";
        }

        /// <summary>左侧靶子区任意位置按下左键时，记录被点击控件的信息</summary>
        private void TargetPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var element = FindNamedControl(e.OriginalSource as DependencyObject);
            if (element == null) return;

            string typeName = element.GetType().Name;
            string name = (element as FrameworkElement)?.Name ?? "-";
            string content = GetContentText(element);

            _clickLogBuilder.AppendLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] {typeName} | Name: {name} | Content: {content}");
            ClickLog = _clickLogBuilder.ToString();
        }

        /// <summary>从命中元素向上查找第一个带名称的控件</summary>
        private static DependencyObject FindNamedControl(DependencyObject source)
        {
            var current = source;
            while (current != null)
            {
                if (current is FrameworkElement fe
                    && !string.IsNullOrEmpty(fe.Name)
                    && (current is Control || current is TextBlock))
                {
                    return current;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static string GetContentText(DependencyObject element)
        {
            switch (element)
            {
                case TextBox tb:
                    return tb.Text;
                case HeaderedContentControl hcc:
                    return hcc.Header?.ToString() ?? hcc.Content?.ToString() ?? "-";
                case ContentControl cc:
                    return cc.Content?.ToString() ?? "-";
                case TextBlock tblk:
                    return tblk.Text;
                case Slider slider:
                    return slider.Value.ToString("F0");
                case ProgressBar pb:
                    return pb.Value.ToString("F0");
                default:
                    return "-";
            }
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
