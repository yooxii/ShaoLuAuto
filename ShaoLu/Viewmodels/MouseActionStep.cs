using CommunityToolkit.Mvvm.Input;
using ShaoLu.Models;
using ShaoLu.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ShaoLu.Viewmodels.AutomationStep
{
    /// <summary>
    /// 鼠标操作位置模式
    /// </summary>
    public enum PositionMode
    {
        /// <summary>屏幕绝对坐标</summary>
        Absolute,
        /// <summary>鼠标当前位置</summary>
        CurrentMouse,
        /// <summary>屏幕相对坐标：横/纵坐标占屏幕宽度/高度的百分比</summary>
        Relative,
    }

    /// <summary>
    /// 鼠标操作步骤：在指定位置执行鼠标动作（不依赖图像识别）
    /// </summary>
    public class MouseActionStep : AutomationStepBase
    {
        private PositionMode _positionMode = PositionMode.Absolute;
        private double _targetX;
        private double _targetY;
        private ObservableCollection<MouseActionItem> _mouseActions = new() { new MouseActionItem() };

        /// <summary>位置模式（在绝对坐标与屏幕相对坐标之间切换时自动换算坐标单位）</summary>
        public PositionMode PositionMode
        {
            get => _positionMode;
            set
            {
                var old = _positionMode;
                if (!SetProperty(ref _positionMode, value)) return;
                ConvertCoordinateUnit(old == PositionMode.Relative, value == PositionMode.Relative);
            }
        }

        /// <summary>按百分比/像素单位切换换算本步骤的位置与拖拽路径</summary>
        private void ConvertCoordinateUnit(bool fromRelative, bool toRelative)
        {
            if (fromRelative == toRelative) return;

            TargetX = ScreenCoordinates.ConvertX(TargetX, fromRelative, toRelative);
            TargetY = ScreenCoordinates.ConvertY(TargetY, fromRelative, toRelative);

            if (MouseActions == null) return;
            foreach (var action in MouseActions)
            {
                if (action?.DragPath == null) continue;
                foreach (var p in action.DragPath)
                {
                    p.X = ScreenCoordinates.ConvertX(p.X, fromRelative, toRelative);
                    p.Y = ScreenCoordinates.ConvertY(p.Y, fromRelative, toRelative);
                }
            }
        }

        /// <summary>目标 X 坐标（Absolute 模式为逻辑像素，Relative 模式为占屏幕宽度的百分比）</summary>
        public double TargetX { get => _targetX; set => SetProperty(ref _targetX, value); }

        /// <summary>目标 Y 坐标（Absolute 模式为逻辑像素，Relative 模式为占屏幕高度的百分比）</summary>
        public double TargetY { get => _targetY; set => SetProperty(ref _targetY, value); }

        /// <summary>鼠标动作序列</summary>
        public ObservableCollection<MouseActionItem> MouseActions { get => _mouseActions; set => SetProperty(ref _mouseActions, value); }

        /// <summary>所有位置模式（供 ComboBox 绑定）</summary>
        [JsonIgnore]
        public List<PositionMode> PositionModes { get; } = new() { PositionMode.Absolute, PositionMode.CurrentMouse, PositionMode.Relative };

        #region 命令

        [JsonIgnore]
        private ICommand addMouseActionCommand;
        [JsonIgnore]
        public ICommand AddMouseActionCommand => addMouseActionCommand ??= new RelayCommand(() => MouseActions.Add(new MouseActionItem()));

        [JsonIgnore]
        private ICommand removeMouseActionCommand;
        [JsonIgnore]
        public ICommand RemoveMouseActionCommand => removeMouseActionCommand ??= new RelayCommand(() => { if (MouseActions.Count > 1) MouseActions.RemoveAt(MouseActions.Count - 1); });

        [JsonIgnore]
        private ICommand selectPositionCommand;
        [JsonIgnore]
        public ICommand SelectPositionCommand => selectPositionCommand ??= new RelayCommand(SelectPosition);

        [JsonIgnore]
        private ICommand editDragPathCommand;
        /// <summary>编辑指定动作的拖拽路径（参数：MouseActionItem）</summary>
        [JsonIgnore]
        public ICommand EditDragPathCommand => editDragPathCommand ??= new RelayCommand<object>(EditDragPath, null);

        #endregion

        #region 构造

        public MouseActionStep() : base()
        {
            Type = StepType.MouseAction;
        }

        public MouseActionStep(string name) : base()
        {
            Type = StepType.MouseAction;
            Name = name;
        }

        public MouseActionStep(string name, string description) : base()
        {
            Type = StepType.MouseAction;
            Name = name;
            Description = description;
        }

        #endregion

        public override AutomationStepBase Clone()
        {
            return new MouseActionStep(Name, Description)
            {
                PositionMode = PositionMode,
                TargetX = TargetX,
                TargetY = TargetY,
                MouseActions = new(MouseActions != null ? new List<MouseActionItem>(MouseActions.Count) : new List<MouseActionItem>()),
                WaitTime = WaitTime,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
                ConditionMode = ConditionMode,
                Conditions = new(Conditions),
            };
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            await Task.Delay((int)(WaitTime * 1000), cancellationToken);

            await Task.Run(() =>
            {
                double dpiX = Services.OCRService.CachedDpiX;
                double dpiY = Services.OCRService.CachedDpiY;

                // 移动到目标位置（相对模式先按百分比换算为逻辑像素）
                if (PositionMode == PositionMode.Absolute || PositionMode == PositionMode.Relative)
                {
                    bool relative = PositionMode == PositionMode.Relative;
                    int physX = (int)(ScreenCoordinates.ToPixelX(TargetX, relative) * dpiX);
                    int physY = (int)(ScreenCoordinates.ToPixelY(TargetY, relative) * dpiY);
                    Autogui.MoveMouseTo(physX, physY);
                }
                // CurrentMouse 模式：不移动，直接在当前位置执行

                // 执行动作序列（拖拽路径：逻辑像素转物理像素）
                Autogui.ExecuteMouseActions(
                    new List<MouseActionItem>(MouseActions),
                    act => ResolveDragPathPixels(act, dpiX, dpiY));
            }, cancellationToken);

            IsTrue = true;
            IsError = false;
            ErrorType = StepErrorType.None;

            LastResult = new StepExecutionResult
            {
                IsTrue = true,
                ExecutedAt = DateTime.Now,
            };

            return IsTrue;
        }

        #region 私有方法

        private void SelectPosition()
        {
            // 全屏点选获取一个坐标点
            var point = Views.WindowSelectPoint.ShowAndSelect();
            if (point.HasValue)
            {
                // 相对模式下存百分比，绝对模式下存逻辑像素
                bool relative = PositionMode == PositionMode.Relative;
                TargetX = ScreenCoordinates.ConvertX(point.Value.X, false, relative);
                TargetY = ScreenCoordinates.ConvertY(point.Value.Y, false, relative);
            }
        }

        /// <summary>把拖拽路径换算为屏幕物理像素（相对模式下先按百分比换算为逻辑像素，再乘 DPI 缩放）</summary>
        public List<System.Drawing.Point> ResolveDragPathPixels(MouseActionItem action, double dpiX, double dpiY)
        {
            var path = new List<System.Drawing.Point>();
            if (action?.DragPath == null) return path;

            bool relative = PositionMode == PositionMode.Relative;
            foreach (var p in action.DragPath)
            {
                path.Add(new System.Drawing.Point(
                    (int)(ScreenCoordinates.ToPixelX(p.X, relative) * dpiX),
                    (int)(ScreenCoordinates.ToPixelY(p.Y, relative) * dpiY)));
            }
            return path;
        }

        private void EditDragPath(object parameter)
        {
            // 鼠标操作步骤的拖拽：全屏按住左键绘制移动轨迹
            if (parameter is MouseActionItem item)
            {
                // 采集窗口始终以屏幕逻辑像素工作；相对模式下在读取与写回两端换算
                bool relative = PositionMode == PositionMode.Relative;
                var existing = item.DragPath
                    .Select(p => new System.Windows.Point(
                        ScreenCoordinates.ToPixelX(p.X, relative),
                        ScreenCoordinates.ToPixelY(p.Y, relative)))
                    .ToList();
                var trajectory = Views.WindowDragPathAbsolute.ShowAndCapture(existing.Count > 0 ? existing : null);
                if (trajectory == null) return; // 取消不改动

                item.DragPath.Clear();
                foreach (var p in trajectory)
                    item.DragPath.Add(new DragPathPoint(
                        ScreenCoordinates.ConvertX(p.X, false, relative),
                        ScreenCoordinates.ConvertY(p.Y, false, relative)));
            }
        }

        #endregion
    }
}
