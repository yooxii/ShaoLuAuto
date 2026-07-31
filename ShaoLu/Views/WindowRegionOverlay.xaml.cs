using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ShaoLu.Views
{
    /// <summary>
    /// 屏幕区域标示覆盖窗口，用于可视化调试区域
    /// </summary>
    public partial class WindowRegionOverlay : Window
    {
        private readonly DispatcherTimer _closeTimer;

        public WindowRegionOverlay(Rect logicalRegion, string color = "#00CC00", double durationSeconds = 2.0)
        {
            InitializeComponent();

            // 应用自定义颜色
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(color);
                RegionBorder.BorderBrush = new SolidColorBrush(c);
                RegionBorder.Background = new SolidColorBrush(Color.FromArgb(0x22, c.R, c.G, c.B));
            }
            catch { /* 颜色解析失败时使用默认值 */ }

            // 窗口使用逻辑坐标定位（WPF 自动处理 DPI）
            Left = logicalRegion.X;
            Top = logicalRegion.Y;
            Width = logicalRegion.Width;
            Height = logicalRegion.Height;

            // 定时自动关闭
            _closeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(durationSeconds)
            };
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer.Stop();
                Close();
            };
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            _closeTimer.Start();
        }

        /// <summary>
        /// 在屏幕上显示区域标示（非阻塞）
        /// </summary>
        public static void ShowRegion(Rect logicalRegion, string color = "#00CC00", double durationSeconds = 2.0)
        {
            if (logicalRegion.IsEmpty || logicalRegion.Width <= 0 || logicalRegion.Height <= 0)
                return;

            try
            {
                var overlay = new WindowRegionOverlay(logicalRegion, color, durationSeconds);
                overlay.Show();
            }
            catch
            {
                // 忽略显示错误（如非 UI 线程调用）
            }
        }
    }
}
