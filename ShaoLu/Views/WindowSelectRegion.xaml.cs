using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShaoLu.Views
{
    /// <summary>
    /// WindowSelectRegion.xaml 的交互逻辑
    /// 全屏透明窗口，用户拖拽框选一个矩形区域
    /// </summary>
    public partial class WindowSelectRegion : Window
    {
        private Point _startPoint;
        private bool _isDragging = false;

        /// <summary>
        /// 用户选择的屏幕区域（绝对坐标）
        /// </summary>
        public Rect SelectedRegion { get; private set; } = Rect.Empty;

        public WindowSelectRegion()
        {
            InitializeComponent();
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(SelectionCanvas);
            _isDragging = true;
            SelectionRect.Visibility = Visibility.Visible;
            SizeTip.Visibility = Visibility.Visible;
            HintText.Visibility = Visibility.Collapsed;

            Canvas.SetLeft(SelectionRect, _startPoint.X);
            Canvas.SetTop(SelectionRect, _startPoint.Y);
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;

            SelectionCanvas.CaptureMouse();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            Point currentPoint = e.GetPosition(SelectionCanvas);

            double x = Math.Min(_startPoint.X, currentPoint.X);
            double y = Math.Min(_startPoint.Y, currentPoint.Y);
            double width = Math.Abs(currentPoint.X - _startPoint.X);
            double height = Math.Abs(currentPoint.Y - _startPoint.Y);

            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = width;
            SelectionRect.Height = height;

            // 更新尺寸提示
            SizeTipText.Text = $"{(int)width} x {(int)height}";
            Canvas.SetLeft(SizeTip, x);
            Canvas.SetTop(SizeTip, y - 25);
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            SelectionCanvas.ReleaseMouseCapture();

            Point endPoint = e.GetPosition(SelectionCanvas);

            double x = Math.Min(_startPoint.X, endPoint.X);
            double y = Math.Min(_startPoint.Y, endPoint.Y);
            double width = Math.Abs(endPoint.X - _startPoint.X);
            double height = Math.Abs(endPoint.Y - _startPoint.Y);

            if (width > 5 && height > 5)
            {
                // 将窗口坐标转换为屏幕绝对坐标（统一使用逻辑像素，DPI 缩放在 OCRService 中处理）
                Point screenPoint = PointToScreen(new Point(x, y));
                var dpi = VisualTreeHelper.GetDpi(this);
                // 存储为逻辑像素坐标（PointToScreen 返回物理像素，需除以 DPI 缩放）
                SelectedRegion = new Rect(screenPoint.X / dpi.DpiScaleX, screenPoint.Y / dpi.DpiScaleY, width, height);
                DialogResult = true;
            }
            else
            {
                // 选择区域太小，视为取消
                DialogResult = false;
            }
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }
    }
}
