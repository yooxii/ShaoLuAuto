using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ShaoLu.Models
{
    /// <summary>
    /// 鼠标动作类型
    /// </summary>
    public enum MouseActionType
    {
        LeftClick,
        RightClick,
        DoubleClick,
        /// <summary>长按：按下左键保持“间隔时间”后释放</summary>
        LeftPress,
        ScrollUp,
        ScrollDown,
        /// <summary>沿路径拖拽（按下左键经过各路径点后释放）</summary>
        Drag,
    }

    /// <summary>
    /// 拖拽路径点：值为屏幕逻辑像素或屏幕相对坐标（百分比），取决于所属鼠标操作步骤的位置模式。
    /// 使用 double 而非整数像素，避免相对模式下百分比取整带来的位置误差。
    /// </summary>
    public class DragPathPoint
    {
        public double X { get; set; }
        public double Y { get; set; }

        public DragPathPoint()
        {
        }

        public DragPathPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        /// <summary>兼容以整数像素点存储的旧格式拖拽路径</summary>
        public static implicit operator DragPathPoint(Point p)
        {
            return new DragPathPoint(p.X, p.Y);
        }
    }

    /// <summary>
    /// 鼠标动作项（步骤中顺序执行的动作列表）
    /// Drag 动作按步骤类型使用两种互斥的路径数据：
    /// - 鼠标操作步骤：DragPath（屏幕逻辑像素；相对模式下为占屏幕宽/高的百分比）
    /// - 图像类步骤：ClickDragTrajectories（相邻点击点之间的手绘轨迹，相对裁剪区左上角偏移）
    /// </summary>
    public class MouseActionItem : ObservableObject
    {
        private MouseActionType _actionType = MouseActionType.LeftClick;
        private int _count = 1;
        private double _interval = 0.1;
        private ObservableCollection<DragPathPoint> _dragPath = new();
        private List<List<Point>> _clickDragTrajectories = new();

        /// <summary>动作类型</summary>
        public MouseActionType ActionType { get => _actionType; set => SetProperty(ref _actionType, value); }

        /// <summary>执行次数（点击次数/滚动格数/拖拽次数），最小为0</summary>
        public int Count { get => _count; set => SetProperty(ref _count, value < 0 ? 0 : value); }

        /// <summary>动作间隔(秒)；长按动作下为按住时长</summary>
        public double Interval { get => _interval; set => SetProperty(ref _interval, value); }

        /// <summary>拖拽路径（仅鼠标操作步骤使用）：绝对模式存屏幕逻辑像素，相对模式存占屏幕宽/高的百分比</summary>
        public ObservableCollection<DragPathPoint> DragPath { get => _dragPath; set => SetProperty(ref _dragPath, value); }

        /// <summary>
        /// 点击点拖拽轨迹：第 i 项为点击点 i 到点击点 i+1 之间的手绘轨迹点
        /// （不含两端点击点，相对裁剪区左上角的偏移；空列表表示直线连接；仅图像类步骤使用）
        /// </summary>
        public List<List<Point>> ClickDragTrajectories { get => _clickDragTrajectories; set => SetProperty(ref _clickDragTrajectories, value); }

        /// <summary>所有可用动作类型（供 ComboBox 绑定）</summary>
        public static List<MouseActionType> AllTypes { get; } = new()
        {
            MouseActionType.LeftClick,
            MouseActionType.RightClick,
            MouseActionType.DoubleClick,
            MouseActionType.LeftPress,
            MouseActionType.ScrollUp,
            MouseActionType.ScrollDown,
            MouseActionType.Drag,
        };

        public MouseActionItem Clone()
        {
            var res = new MouseActionItem
            {
                ActionType = ActionType,
                Count = Count,
                Interval = Interval,
            };
            foreach (var p in DragPath)
                res.DragPath.Add(new DragPathPoint(p.X, p.Y));
            foreach (var segment in ClickDragTrajectories)
            {
                var copy = new List<Point>(segment.Count);
                foreach (var p in segment)
                    copy.Add(new Point(p.X, p.Y));
                res.ClickDragTrajectories.Add(copy);
            }
            return res;
        }
    }
}
