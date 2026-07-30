using System;
using System.Windows;
using System.Windows.Threading;

namespace ShaoLu.Views
{
    /// <summary>
    /// 屏幕区域标示覆盖窗口，用于可视化 OCR 区域
    /// </summary>
    public partial class WindowRegionOverlay : Window
    {
        private readonly DispatcherTimer _closeTimer;

        public WindowRegionOverlay(Rect logicalRegion, double durationSeconds = 2.0)
        {
            InitializeComponent();

            // 将逻辑像素坐标转换为屏幕位置
            var source = PresentationSource.FromVisual(Application.Current?.MainWindow);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

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
        public static void ShowRegion(Rect logicalRegion, double durationSeconds = 2.0)
        {
            if (logicalRegion.IsEmpty || logicalRegion.Width <= 0 || logicalRegion.Height <= 0)
                return;

            try
            {
                var overlay = new WindowRegionOverlay(logicalRegion, durationSeconds);
                overlay.Show();
            }
            catch
            {
                // 忽略显示错误（如非 UI 线程调用）
            }
        }
    }
}
