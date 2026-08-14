using ShaoLu.Models;
using ShaoLu.Viewmodels;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ShaoLu.Views
{
    /// <summary>
    /// 点击点拖拽路径编辑窗口：预览原图与点击点（只读），
    /// 在某个点击点上按住左键拖向下一个点击点生成移动轨迹；中途松手则从当前位置直接连线到下一个点。
    /// 轨迹保存为相对裁剪区左上角的偏移，运行时随匹配区域换算屏幕坐标。
    /// </summary>
    public partial class WindowDragPathClick : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private const double CircleRadius = 6;
        private const double CircleRadiusActive = 11;

        private readonly MouseActionItem _item;
        private readonly List<Models.Point> _clickPoints;
        private readonly Rect _croppedRect;
        private readonly List<Ellipse> _circles = new();
        private readonly List<System.Windows.Point> _tempPoints = new();

        private double _scaleX = 1;
        private double _scaleY = 1;
        private int _dragSegment = -1;
        private int _hoverIndex = -1;
        private Polyline _liveLine;

        private double _radius = 20;
        /// <summary>激活半径（显示像素）：鼠标进入该范围内点击点放大表示可以连线</summary>
        public double Radius { get => _radius; set { _radius = value; OnPropertyChanged(); } }

        public WindowDragPathClick(ImageSource imgSrc, List<ClickThumb> thumbs, Rect croppedRect, MouseActionItem item)
        {
            InitializeComponent();
            DataContext = this;
            _item = item;
            _croppedRect = croppedRect;
            _clickPoints = thumbs?.Select(t => t.ClickPoint).ToList() ?? new List<Models.Point>();
            SourceImage.Source = imgSrc;
            Loaded += (s, e) =>
            {
                OverlayCanvas.Width = SourceImage.ActualWidth;
                OverlayCanvas.Height = SourceImage.ActualHeight;
                if (imgSrc is BitmapSource bmp && SourceImage.ActualWidth > 0 && SourceImage.ActualHeight > 0)
                {
                    _scaleX = bmp.PixelWidth / SourceImage.ActualWidth;
                    _scaleY = bmp.PixelHeight / SourceImage.ActualHeight;
                }
                RedrawAll();
            };
        }

        #region 交互

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(OverlayCanvas);
            int idx = FindClickablePoint(pos);
            if (idx < 0) return;

            // 开始记录第 idx 段（点击点 idx → idx+1）
            _dragSegment = idx;
            _tempPoints.Clear();
            _tempPoints.Add(pos);
            OverlayCanvas.CaptureMouse();
            UpdateLiveLine(pos);
            e.Handled = true;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(OverlayCanvas);

            if (_dragSegment >= 0)
            {
                var last = _tempPoints[_tempPoints.Count - 1];
                if ((last - pos).Length >= 2)
                {
                    _tempPoints.Add(pos);
                    UpdateLiveLine(pos);
                }
                return;
            }

            UpdateHover(FindClickablePoint(pos));
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragSegment < 0) return;

            OverlayCanvas.ReleaseMouseCapture();
            SaveTrajectory(_dragSegment);
            _dragSegment = -1;
            _tempPoints.Clear();
            RedrawAll();
            e.Handled = true;
        }

        private void Canvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_dragSegment < 0)
                UpdateHover(-1);
        }

        private void ClearTrajectories_Click(object sender, RoutedEventArgs e)
        {
            _item.ClickDragTrajectories.Clear();
            RedrawAll();
        }

        #endregion

        #region 私有方法

        /// <summary>查找激活半径内可发起连线的最近点击点（最后一个点没有下一点，不可连线）</summary>
        private int FindClickablePoint(System.Windows.Point pos)
        {
            int best = -1;
            double bestDist = double.MaxValue;
            for (int i = 0; i < _clickPoints.Count - 1; i++)
            {
                var disp = ToDisplay(_clickPoints[i]);
                double dist = (disp - pos).Length;
                if (dist <= Radius && dist < bestDist)
                {
                    best = i;
                    bestDist = dist;
                }
            }
            return best;
        }

        /// <summary>进入/离开激活范围时放大/还原点击点</summary>
        private void UpdateHover(int idx)
        {
            if (idx == _hoverIndex) return;
            if (_hoverIndex >= 0 && _hoverIndex < _circles.Count)
                SetCircleSize(_circles[_hoverIndex], CircleRadius);
            _hoverIndex = idx;
            if (idx >= 0 && idx < _circles.Count)
                SetCircleSize(_circles[idx], CircleRadiusActive);
        }

        /// <summary>保存当前段的手绘轨迹（显示坐标 → 图像像素 → 相对裁剪区左上角偏移）</summary>
        private void SaveTrajectory(int segment)
        {
            var stored = new List<Models.Point>();
            foreach (var p in _tempPoints)
            {
                int imgX = (int)(p.X * _scaleX);
                int imgY = (int)(p.Y * _scaleY);
                stored.Add(new Models.Point((int)(imgX - _croppedRect.X), (int)(imgY - _croppedRect.Y)));
            }

            while (_item.ClickDragTrajectories.Count <= segment)
                _item.ClickDragTrajectories.Add(new List<Models.Point>());
            _item.ClickDragTrajectories[segment] = stored;
        }

        private void UpdateLiveLine(System.Windows.Point current)
        {
            if (_liveLine == null)
            {
                _liveLine = new Polyline
                {
                    Stroke = Brushes.LimeGreen,
                    StrokeThickness = 2,
                    IsHitTestVisible = false,
                };
                OverlayCanvas.Children.Add(_liveLine);
            }
            _liveLine.Points.Clear();
            _liveLine.Points.Add(ToDisplay(_clickPoints[_dragSegment]));
            foreach (var p in _tempPoints)
                _liveLine.Points.Add(p);
            _liveLine.Points.Add(current);
        }

        /// <summary>重绘已保存的轨迹与点击点</summary>
        private void RedrawAll()
        {
            OverlayCanvas.Children.Clear();
            _circles.Clear();
            _liveLine = null;
            _hoverIndex = -1;

            // 已保存轨迹：点击点 i → 轨迹 → 点击点 i+1
            var trajs = _item.ClickDragTrajectories;
            for (int i = 0; i < _clickPoints.Count - 1; i++)
            {
                if (trajs == null || i >= trajs.Count || trajs[i].Count == 0) continue;
                var line = new Polyline
                {
                    Stroke = Brushes.DodgerBlue,
                    StrokeThickness = 2,
                    IsHitTestVisible = false,
                };
                line.Points.Add(ToDisplay(_clickPoints[i]));
                foreach (var t in trajs[i])
                    line.Points.Add(ToDisplay(FromCropOffset(t)));
                line.Points.Add(ToDisplay(_clickPoints[i + 1]));
                OverlayCanvas.Children.Add(line);
            }

            // 点击点（只读，不可拖动改动）
            for (int i = 0; i < _clickPoints.Count; i++)
            {
                var disp = ToDisplay(_clickPoints[i]);
                var circle = new Ellipse
                {
                    Fill = new SolidColorBrush(Color.FromArgb(120, 255, 60, 60)),
                    Stroke = Brushes.Red,
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false,
                };
                SetCircleSize(circle, CircleRadius);
                Canvas.SetLeft(circle, disp.X - CircleRadius);
                Canvas.SetTop(circle, disp.Y - CircleRadius);
                OverlayCanvas.Children.Add(circle);
                _circles.Add(circle);
            }
        }

        private static void SetCircleSize(Ellipse circle, double radius)
        {
            double cx = Canvas.GetLeft(circle) + circle.Width / 2;
            double cy = Canvas.GetTop(circle) + circle.Height / 2;
            circle.Width = radius * 2;
            circle.Height = radius * 2;
            Canvas.SetLeft(circle, cx - radius);
            Canvas.SetTop(circle, cy - radius);
        }

        /// <summary>图像像素坐标 → 显示坐标</summary>
        private System.Windows.Point ToDisplay(Models.Point p)
        {
            return new System.Windows.Point(p.X / _scaleX, p.Y / _scaleY);
        }

        /// <summary>相对裁剪区偏移 → 图像像素坐标</summary>
        private Models.Point FromCropOffset(Models.Point t)
        {
            return new Models.Point((int)(t.X + _croppedRect.X), (int)(t.Y + _croppedRect.Y));
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
