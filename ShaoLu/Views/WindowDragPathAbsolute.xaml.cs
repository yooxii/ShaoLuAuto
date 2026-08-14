using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ShaoLu.Views
{
    /// <summary>
    /// 绝对位置拖拽路径采集窗口（全屏覆盖）：
    /// 按住左键拖拽生成移动轨迹；松手后不退出，再次按住重新生成；点击"确认"保存退出，右键/ESC 取消
    /// </summary>
    public partial class WindowDragPathAbsolute : Window
    {
        /// <summary>记录的轨迹点（屏幕逻辑像素）</summary>
        private readonly List<System.Windows.Point> _points = new();
        private bool _drawing;

        /// <summary>确认后的轨迹点集合（屏幕逻辑像素）</summary>
        public List<System.Windows.Point> Trajectory { get; private set; }

        public WindowDragPathAbsolute(IEnumerable<System.Windows.Point> existing = null)
        {
            InitializeComponent();
            if (existing != null)
            {
                foreach (var p in existing)
                    _points.Add(p);
                // 窗口显示后才能做屏幕坐标换算，回显延迟到 Loaded
                Loaded += (s, e) => Redraw();
            }
        }

        /// <summary>
        /// 打开采集窗口；确认返回轨迹点集合（屏幕逻辑像素），取消返回 null
        /// </summary>
        public static List<System.Windows.Point> ShowAndCapture(IEnumerable<System.Windows.Point> existing = null)
        {
            var window = new WindowDragPathAbsolute(existing);
            bool? result = window.ShowDialog();
            return result == true ? window.Trajectory : null;
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 再次按住：重新生成轨迹
            _points.Clear();
            Redraw();
            _drawing = true;
            SelectionCanvas.CaptureMouse();
            AddPoint(e.GetPosition(SelectionCanvas));
            e.Handled = true;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_drawing)
                AddPoint(e.GetPosition(SelectionCanvas));
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_drawing) return;
            _drawing = false;
            SelectionCanvas.ReleaseMouseCapture();
            ConfirmButton.IsEnabled = _points.Count >= 2;
            e.Handled = true;
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (_points.Count < 2) return;
            Trajectory = new List<System.Windows.Point>(_points);
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
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

        /// <summary>画布坐标转屏幕逻辑坐标并追加轨迹点（过近的点忽略）</summary>
        private void AddPoint(System.Windows.Point canvasPos)
        {
            var screenPoint = PointToScreen(canvasPos);
            var dpi = VisualTreeHelper.GetDpi(this);
            var logical = new System.Windows.Point(screenPoint.X / dpi.DpiScaleX, screenPoint.Y / dpi.DpiScaleY);

            if (_points.Count > 0)
            {
                var last = _points[_points.Count - 1];
                if ((last - logical).Length < 2) return;
            }

            _points.Add(logical);
            TrajectoryLine.Points.Add(canvasPos);
        }

        /// <summary>按已有轨迹点重绘（用于打开时回显）</summary>
        private void Redraw()
        {
            TrajectoryLine.Points.Clear();
            var dpi = VisualTreeHelper.GetDpi(this);
            foreach (var p in _points)
            {
                // 屏幕逻辑坐标 → 本窗口画布坐标
                var canvasPos = PointFromScreen(new System.Windows.Point(p.X * dpi.DpiScaleX, p.Y * dpi.DpiScaleY));
                TrajectoryLine.Points.Add(canvasPos);
            }
            ConfirmButton.IsEnabled = _points.Count >= 2;
        }
    }
}
