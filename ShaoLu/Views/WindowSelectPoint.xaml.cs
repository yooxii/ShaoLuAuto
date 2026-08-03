using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShaoLu.Views
{
    /// <summary>
    /// 全屏点选窗口：用户单击屏幕选取一个坐标点
    /// </summary>
    public partial class WindowSelectPoint : Window
    {
        /// <summary>
        /// 用户选择的屏幕坐标（逻辑像素）
        /// </summary>
        public Point SelectedPoint { get; private set; }

        public WindowSelectPoint()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 打开点选窗口，返回选取的点；取消返回 null
        /// </summary>
        public static Point? ShowAndSelect()
        {
            var window = new WindowSelectPoint();
            bool? result = window.ShowDialog();
            return result == true ? window.SelectedPoint : null;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(SelectionCanvas);

            // 十字准星
            CrossH.Visibility = Visibility.Visible;
            CrossV.Visibility = Visibility.Visible;
            CrossH.X1 = 0;
            CrossH.Y1 = pos.Y;
            CrossH.X2 = SelectionCanvas.ActualWidth;
            CrossH.Y2 = pos.Y;
            CrossV.X1 = pos.X;
            CrossV.Y1 = 0;
            CrossV.X2 = pos.X;
            CrossV.Y2 = SelectionCanvas.ActualHeight;

            // 坐标提示（转为屏幕逻辑坐标）
            var screenPoint = PointToScreen(pos);
            var dpi = VisualTreeHelper.GetDpi(this);
            double logicalX = screenPoint.X / dpi.DpiScaleX;
            double logicalY = screenPoint.Y / dpi.DpiScaleY;

            PosTip.Visibility = Visibility.Visible;
            PosTipText.Text = $"X:{logicalX:F0}, Y:{logicalY:F0}";
            Canvas.SetLeft(PosTip, pos.X + 15);
            Canvas.SetTop(PosTip, pos.Y + 15);
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(SelectionCanvas);
            var screenPoint = PointToScreen(pos);
            var dpi = VisualTreeHelper.GetDpi(this);
            // 存储为逻辑像素坐标
            SelectedPoint = new Point(screenPoint.X / dpi.DpiScaleX, screenPoint.Y / dpi.DpiScaleY);
            DialogResult = true;
            Close();
            e.Handled = true;
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
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
