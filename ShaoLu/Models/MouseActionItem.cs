using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

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
        ScrollUp,
        ScrollDown,
    }

    /// <summary>
    /// 鼠标动作项（点击图像步骤中顺序执行的动作列表）
    /// </summary>
    public class MouseActionItem : ObservableObject
    {
        private MouseActionType _actionType = MouseActionType.LeftClick;
        private int _count = 1;
        private double _interval = 0.1;

        /// <summary>动作类型</summary>
        public MouseActionType ActionType { get => _actionType; set => SetProperty(ref _actionType, value); }

        /// <summary>执行次数（点击次数/滚动格数），最小为0</summary>
        public int Count { get => _count; set => SetProperty(ref _count, value < 0 ? 0 : value); }

        /// <summary>动作间隔(秒)</summary>
        public double Interval { get => _interval; set => SetProperty(ref _interval, value); }

        /// <summary>所有可用动作类型（供 ComboBox 绑定）</summary>
        public static List<MouseActionType> AllTypes { get; } = new()
        {
            MouseActionType.LeftClick,
            MouseActionType.RightClick,
            MouseActionType.DoubleClick,
            MouseActionType.ScrollUp,
            MouseActionType.ScrollDown,
        };

        public MouseActionItem Clone()
        {
            return new MouseActionItem
            {
                ActionType = ActionType,
                Count = Count,
                Interval = Interval,
            };
        }
    }
}
