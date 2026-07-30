using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace ShaoLu.Views
{
    /// <summary>
    /// OCR 区域选择窗口
    /// 调用方通过 ShowAndSelect() 静态方法使用
    /// </summary>
    public partial class WindowEditOCR : Window
    {
        private Point _startPoint;
        private bool _isDragging;
        private bool _isReady; // 窗口完全就绪后才允许 Deactivated 关闭
        private double _screenOffsetX;
        private double _screenOffsetY;

        public Rect SelectedRegion { get; private set; } = Rect.Empty;

        private WindowEditOCR(BitmapSource screenshot, Rect workArea)
        {
            InitializeComponent();

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = workArea.X;
            Top = workArea.Y;
            Width = workArea.Width;
            Height = workArea.Height;

            _screenOffsetX = workArea.X;
            _screenOffsetY = workArea.Y;

            ScreenshotImage.Source = screenshot;

            ContentRendered += (_, _) =>
            {
                // 强制获取前台和焦点
                ForceForeground();
                DrawCanvas.Focus();
                _isReady = true;
            };
        }

        /// <summary>
        /// 静态入口：截屏并打开选择窗口
        /// </summary>
        public static Rect ShowAndSelect()
        {
            var mainWindow = Application.Current?.MainWindow;
            var mainVis = mainWindow?.Visibility ?? Visibility.Visible;
            if (mainWindow != null)
                mainWindow.Visibility = Visibility.Hidden;

            try
            {
                // 等系统重绘
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

                var workArea = SystemParameters.WorkArea;

                // 获取 DPI 缩放因子，将逻辑像素转换为物理像素用于截图
                double dpiX = 1.0, dpiY = 1.0;
                if (mainWindow != null)
                {
                    var source = PresentationSource.FromVisual(mainWindow);
                    if (source?.CompositionTarget != null)
                    {
                        dpiX = source.CompositionTarget.TransformToDevice.M11;
                        dpiY = source.CompositionTarget.TransformToDevice.M22;
                    }
                }

                // 截图使用物理像素
                int w = (int)(workArea.Width * dpiX);
                int h = (int)(workArea.Height * dpiY);
                int sx = (int)(workArea.X * dpiX);
                int sy = (int)(workArea.Y * dpiY);

                BitmapSource screenshot;
                using (var bmp = new Bitmap(w, h))
                {
                    using (var g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(sx, sy, 0, 0, new System.Drawing.Size(w, h));
                    using var ms = new MemoryStream();
                    bmp.Save(ms, ImageFormat.Png);
                    ms.Position = 0;
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = ms;
                    img.EndInit();
                    img.Freeze();
                    screenshot = img;
                }

                var window = new WindowEditOCR(screenshot, workArea);
                bool? result = window.ShowDialog();

                if (result == true && !window.SelectedRegion.IsEmpty)
                {
                    var r = window.SelectedRegion;
                    // 返回逻辑像素坐标（OCRService.RecognizeRegion 内部会乘以 DPI 缩放）
                    return new Rect(r.X + workArea.X, r.Y + workArea.Y, r.Width, r.Height);
                }
                return Rect.Empty;
            }
            finally
            {
                if (mainWindow != null)
                    mainWindow.Visibility = mainVis;
            }
        }

        /// <summary>
        /// 使用 Win32 API 强制窗口到前台
        /// </summary>
        private void ForceForeground()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // 模拟 Alt 键按下以绕过 Windows 前台锁定
            keybd_event(0x12, 0, 0, 0); // VK_MENU (Alt) down
            SetForegroundWindow(hwnd);
            keybd_event(0x12, 0, 2, 0); // VK_MENU up
            Activate();
            Focus();
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        #region 关闭逻辑（统一处理）

        private void DoClose(bool confirm)
        {
            if (_isDragging)
            {
                _isDragging = false;
                DrawCanvas.ReleaseMouseCapture();
            }

            if (confirm && !SelectedRegion.IsEmpty && SelectedRegion.Width > 5 && SelectedRegion.Height > 5)
            {
                DialogResult = true;
            }
            else if (!confirm)
            {
                DialogResult = false;
            }
            // 如果 confirm 但没有有效区域，不关闭（提示用户）
        }

        private void HandleEscape()
        {
            if (_isDragging)
            {
                _isDragging = false;
                DrawCanvas.ReleaseMouseCapture();
                HideSelection();
            }
            else
            {
                DialogResult = false;
            }
        }

        private void HandleEnter()
        {
            if (_isDragging)
            {
                _isDragging = false;
                DrawCanvas.ReleaseMouseCapture();
            }
            if (!SelectedRegion.IsEmpty && SelectedRegion.Width > 5 && SelectedRegion.Height > 5)
            {
                DialogResult = true;
            }
        }

        private void HandleRightClick()
        {
            if (_isDragging)
            {
                _isDragging = false;
                DrawCanvas.ReleaseMouseCapture();
                HideSelection();
            }
            else
            {
                DialogResult = false;
            }
        }

        #endregion

        #region 鼠标交互

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                DrawCanvas.ReleaseMouseCapture();
            }

            _startPoint = e.GetPosition(DrawCanvas);
            _isDragging = true;

            OCRRect.Visibility = Visibility.Visible;
            SizeTip.Visibility = Visibility.Visible;
            MaskTop.Visibility = Visibility.Visible;
            MaskBottom.Visibility = Visibility.Visible;
            MaskLeft.Visibility = Visibility.Visible;
            MaskRight.Visibility = Visibility.Visible;

            Canvas.SetLeft(OCRRect, _startPoint.X);
            Canvas.SetTop(OCRRect, _startPoint.Y);
            OCRRect.Width = 0;
            OCRRect.Height = 0;

            DrawCanvas.CaptureMouse();
            // 捕获鼠标后仍然保持键盘焦点
            DrawCanvas.Focus();
            e.Handled = true;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var cur = e.GetPosition(DrawCanvas);

            double x = Math.Min(_startPoint.X, cur.X);
            double y = Math.Min(_startPoint.Y, cur.Y);
            double w = Math.Abs(cur.X - _startPoint.X);
            double h = Math.Abs(cur.Y - _startPoint.Y);

            Canvas.SetLeft(OCRRect, x);
            Canvas.SetTop(OCRRect, y);
            OCRRect.Width = w;
            OCRRect.Height = h;

            UpdateMask(x, y, w, h);

            SizeTipText.Text = $"{(int)w} x {(int)h}";
            Canvas.SetLeft(SizeTip, x);
            Canvas.SetTop(SizeTip, y - 25);
            HintText.Text = $"X:{x + _screenOffsetX:F0}, Y:{y + _screenOffsetY:F0}, W:{w:F0}, H:{h:F0}  |  Enter=确认  Esc/右键=取消";
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            DrawCanvas.ReleaseMouseCapture();

            var end = e.GetPosition(DrawCanvas);
            double x = Math.Min(_startPoint.X, end.X);
            double y = Math.Min(_startPoint.Y, end.Y);
            double w = Math.Abs(end.X - _startPoint.X);
            double h = Math.Abs(end.Y - _startPoint.Y);

            if (w > 5 && h > 5)
            {
                SelectedRegion = new Rect(x, y, w, h);
                HintText.Text = $"已选择 W:{w:F0} x H:{h:F0}  |  Enter=确认  Esc/右键=取消  再次拖拽=重选";
            }
            else
            {
                HideSelection();
            }

            // 释放鼠标捕获后恢复键盘焦点
            DrawCanvas.Focus();
            e.Handled = true;
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            HandleRightClick();
            e.Handled = true;
        }

        private void UpdateMask(double x, double y, double w, double h)
        {
            double cw = DrawCanvas.ActualWidth;
            double ch = DrawCanvas.ActualHeight;

            Canvas.SetLeft(MaskTop, 0); Canvas.SetTop(MaskTop, 0);
            MaskTop.Width = cw; MaskTop.Height = Math.Max(0, y);

            Canvas.SetLeft(MaskBottom, 0); Canvas.SetTop(MaskBottom, y + h);
            MaskBottom.Width = cw; MaskBottom.Height = Math.Max(0, ch - y - h);

            Canvas.SetLeft(MaskLeft, 0); Canvas.SetTop(MaskLeft, y);
            MaskLeft.Width = Math.Max(0, x); MaskLeft.Height = h;

            Canvas.SetLeft(MaskRight, x + w); Canvas.SetTop(MaskRight, y);
            MaskRight.Width = Math.Max(0, cw - x - w); MaskRight.Height = h;
        }

        private void HideSelection()
        {
            OCRRect.Visibility = Visibility.Collapsed;
            SizeTip.Visibility = Visibility.Collapsed;
            MaskTop.Visibility = Visibility.Collapsed;
            MaskBottom.Visibility = Visibility.Collapsed;
            MaskLeft.Visibility = Visibility.Collapsed;
            MaskRight.Visibility = Visibility.Collapsed;
            SelectedRegion = Rect.Empty;
            HintText.Text = (string)FindResource("OCR_EditRegionHint") ?? "拖拽框选区域，Enter确认，Esc取消";
        }

        #endregion

        #region 键盘事件（Canvas 级别 + Window Preview 级别双重保障）

        private void Canvas_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { HandleEscape(); e.Handled = true; }
            else if (e.Key == Key.Enter) { HandleEnter(); e.Handled = true; }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { HandleEscape(); e.Handled = true; }
            else if (e.Key == Key.Enter) { HandleEnter(); e.Handled = true; }
        }

        private void Window_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            HandleRightClick();
            e.Handled = true;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            HandleEnter();
        }

        private void BtnEsc_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // 窗口未就绪时忽略（ShowDialog 初始化期间会触发 Deactivated）
            if (!_isReady) return;
            // 窗口失去焦点时自动关闭（防止窗口"消失"后关不掉）
            if (!_isDragging)
            {
                DialogResult = false;
                Close();
            }
        }

        #endregion
    }
}
