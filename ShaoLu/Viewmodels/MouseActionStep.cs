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

        /// <summary>位置模式</summary>
        public PositionMode PositionMode { get => _positionMode; set => SetProperty(ref _positionMode, value); }

        /// <summary>目标 X 坐标（逻辑像素，Absolute 模式）</summary>
        public double TargetX { get => _targetX; set => SetProperty(ref _targetX, value); }

        /// <summary>目标 Y 坐标（逻辑像素，Absolute 模式）</summary>
        public double TargetY { get => _targetY; set => SetProperty(ref _targetY, value); }

        /// <summary>鼠标动作序列</summary>
        public ObservableCollection<MouseActionItem> MouseActions { get => _mouseActions; set => SetProperty(ref _mouseActions, value); }

        /// <summary>所有位置模式（供 ComboBox 绑定）</summary>
        [JsonIgnore]
        public List<PositionMode> PositionModes { get; } = new() { PositionMode.Absolute, PositionMode.CurrentMouse };

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

                // 移动到目标位置
                if (PositionMode == PositionMode.Absolute)
                {
                    int physX = (int)(TargetX * dpiX);
                    int physY = (int)(TargetY * dpiY);
                    Autogui.MoveMouseTo(physX, physY);
                }
                // CurrentMouse 模式：不移动，直接在当前位置执行

                // 执行动作序列（绝对位置拖拽路径：逻辑像素转物理像素）
                Autogui.ExecuteMouseActions(
                    new List<MouseActionItem>(MouseActions),
                    act =>
                    {
                        var path = new List<System.Drawing.Point>();
                        foreach (var p in act.DragPath)
                            path.Add(new System.Drawing.Point((int)(p.X * dpiX), (int)(p.Y * dpiY)));
                        return path;
                    });
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
                TargetX = point.Value.X;
                TargetY = point.Value.Y;
            }
        }

        private void EditDragPath(object parameter)
        {
            // 鼠标操作步骤的拖拽：全屏按住左键绘制移动轨迹
            if (parameter is MouseActionItem item)
            {
                var existing = item.DragPath.Select(p => new System.Windows.Point(p.X, p.Y)).ToList();
                var trajectory = Views.WindowDragPathAbsolute.ShowAndCapture(existing.Count > 0 ? existing : null);
                if (trajectory == null) return; // 取消不改动

                item.DragPath.Clear();
                foreach (var p in trajectory)
                    item.DragPath.Add(new Models.Point((int)p.X, (int)p.Y));
            }
        }

        #endregion
    }
}
