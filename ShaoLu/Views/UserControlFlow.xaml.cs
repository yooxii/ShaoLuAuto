using Nodify;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace ShaoLu.Views
{
    /// <summary>
    /// 步骤的流程图视图（Nodify 版）：节点可拖动摆放，顺序 / 真 / 假 / 引用画成连线，
    /// 从出口拖到目标步骤即可改跳转。数据仍是 StepsViewModel.AutomationStepBases，与表格视图共用一份。
    /// </summary>
    public partial class UserControlFlow : UserControl
    {
        private readonly StepsViewModel _vm = SingletonLocator.Steps;

        private static readonly Brush TrueBrush = Freeze(0x2F, 0xA3, 0x6B);
        private static readonly Brush FalseBrush = Freeze(0xDC, 0x5B, 0x4C);
        private static readonly Brush SeqBrush = Freeze(0x8A, 0x96, 0xA3);
        private static readonly Brush RefBrush = Freeze(0x7B, 0x5C, 0xD6);
        private static readonly Brush OtherBrush = Freeze(0x7D, 0x8A, 0x99);

        private static Brush Freeze(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public ObservableCollection<FlowNode> Nodes { get; } = new ObservableCollection<FlowNode>();
        public ObservableCollection<FlowEdge> Connections { get; } = new ObservableCollection<FlowEdge>();
        public ObservableCollection<FlowNode> Selection { get; } = new ObservableCollection<FlowNode>();
        public FlowPending Pending { get; } = new FlowPending();

        private bool _loadedOnce;
        private bool _syncingSelection;
        private readonly List<AutomationStepBase> _hookedSteps = new List<AutomationStepBase>();
        private readonly List<FlowNode> _hookedNodes = new List<FlowNode>();
        private readonly List<FlowConnector> _hookedConnectors = new List<FlowConnector>();

        /// <summary>节点卡片本身的尺寸（不含左右各 14 的出口 / 入口列）</summary>
        private const double NodeWidth = 220d;
        private const double NodeHeight = 96d;

        /// <summary>卡片左右两侧各一排连接口所占的宽度</summary>
        private const double NodeConnectorWidth = 14d;

        /// <summary>连线绕开节点时留出的安全距离</summary>
        private const double RouteMargin = FlowRouteGrid.DefaultMargin;

        /// <summary>出口 / 入口先水平挣脱节点的那一小段</summary>
        private const double RouteStub = 18d;

        /// <summary>节点或连线太多时退回简单折线，避免拖拽卡顿</summary>
        private const int RouteNodeLimit = 120;
        private const int RouteEdgeLimit = 200;

        private readonly DispatcherTimer _routeTimer;

        public UserControlFlow()
        {
            InitializeComponent();
            DataContext = this;

            Pending.Completed = OnPendingCompleted;
            Selection.CollectionChanged += Selection_CollectionChanged;
            _vm.PropertyChanged += Vm_PropertyChanged;

            InputBindings.Add(new KeyBinding { Key = Key.Delete, Command = _vm.DelStepCommand });
            Focusable = true;

            _routeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(140) };
            _routeTimer.Tick += (s, e) => { _routeTimer.Stop(); RouteConnections(); };
        }

        #region 生命周期

        /// <summary>切到流程图视图时刷新节点、连线和版面</summary>
        public void RefreshView()
        {
            EnsureLoaded();
            RebuildGraph();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyAutoLayoutForUnplacedNodes();
                UpdateEmptyHint();
                UpdateDim();
                SyncSelectionFromViewModel();
                RouteConnections();
                ScheduleRoute();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>保存工程前调用：把节点位置落进版面表</summary>
        public void CommitLayoutToStore()
        {
            foreach (var node in Nodes)
                FlowLayoutStore.Set(node.Step.Uid, node.Location);
        }

        private void EnsureLoaded()
        {
            if (_loadedOnce) return;
            _loadedOnce = true;

            if (_vm.AutomationStepBases != null)
                _vm.AutomationStepBases.CollectionChanged += Steps_CollectionChanged;
        }

        private void Steps_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildGraph();
            Dispatcher.BeginInvoke(new Action(ApplyAutoLayoutForUnplacedNodes),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Vm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StepsViewModel.SelectedStep))
                SyncSelectionFromViewModel();
        }

        #endregion

        #region 图构建

        private void RebuildGraph()
        {
            UnhookSteps();

            var steps = _vm.AutomationStepBases ?? new ObservableCollection<AutomationStepBase>();
            var previous = Nodes.ToDictionary(n => n.Step.Uid);

            _syncingSelection = true;
            try
            {
                Nodes.Clear();
                foreach (var step in steps)
                {
                    if (step == null) continue;
                    var node = new FlowNode(step);
                    Nodes.Add(node);
                    step.PropertyChanged += Step_PropertyChanged;
                    node.PropertyChanged += Node_PropertyChanged;
                    foreach (var connector in node.AllConnectors)
                    {
                        connector.PropertyChanged += Connector_PropertyChanged;
                        _hookedConnectors.Add(connector);
                    }
                    _hookedSteps.Add(step);
                    _hookedNodes.Add(node);
                }
            }
            finally
            {
                _syncingSelection = false;
            }

            RebuildConnections();
        }

        private void UnhookSteps()
        {
            foreach (var step in _hookedSteps)
                step.PropertyChanged -= Step_PropertyChanged;
            _hookedSteps.Clear();

            foreach (var node in _hookedNodes)
                node.PropertyChanged -= Node_PropertyChanged;
            _hookedNodes.Clear();

            foreach (var connector in _hookedConnectors)
                connector.PropertyChanged -= Connector_PropertyChanged;
            _hookedConnectors.Clear();
        }

        private void Node_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FlowNode.Location))
                ScheduleRoute();
        }

        private void Connector_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FlowConnector.Anchor))
                ScheduleRoute();
        }

        private void Step_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AutomationStepBase.TrueGotoUid):
                case nameof(AutomationStepBase.FalseGotoUid):
                case nameof(AutomationStepBase.Conditions):
                case nameof(AutomationStepBase.ConditionMode):
                    RebuildConnections();
                    break;
                case nameof(AutomationStepBase.IsError):
                case nameof(AutomationStepBase.ErrorMessage):
                case nameof(AutomationStepBase.IsNeed):
                    if (sender is AutomationStepBase step) Nodes.FirstOrDefault(n => n.Step == step)?.Refresh();
                    break;
            }
        }

        private void RebuildConnections()
        {
            var byUid = Nodes.ToDictionary(n => n.Step.Uid);
            var edges = new List<FlowEdge>();

            foreach (var connector in Nodes.SelectMany(n => n.AllConnectors))
                connector.IsConnected = false;

            var steps = _vm.AutomationStepBases ?? new ObservableCollection<AutomationStepBase>();
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step == null || !byUid.TryGetValue(step.Uid, out var node)) continue;

                // 顺序连线：落到下一步
                if (i + 1 < steps.Count && byUid.TryGetValue(steps[i + 1].Uid, out var next))
                    edges.Add(new FlowEdge(node.Seq, next.In, SeqBrush, 1.4, _vm));

                if (step.TrueGotoUid is Guid trueUid && blUid(trueUid, step.Uid) && byUid.TryGetValue(trueUid, out var trueNode))
                    edges.Add(new FlowEdge(node.True, trueNode.In, TrueBrush, 1.8, _vm));

                if (step.FalseGotoUid is Guid falseUid && blUid(falseUid, step.Uid) && byUid.TryGetValue(falseUid, out var falseNode))
                    edges.Add(new FlowEdge(node.False, falseNode.In, FalseBrush, 1.8, _vm));

                // 引用连线：条件里引用了别的步骤
                if (step.Conditions != null && step.ConditionMode == ShaoLu.Models.ConditionMode.Custom)
                {
                    foreach (var condition in step.Conditions)
                    {
                        if (condition == null || condition.Source != ShaoLu.Models.ConditionSource.Step) continue;
                        if (!condition.StepUid.HasValue || condition.StepUid.Value == Guid.Empty) continue;
                        if (!byUid.TryGetValue(condition.StepUid.Value, out var refNode)) continue;
                        if (refNode == node) continue;
                        edges.Add(new FlowEdge(node.Ref, refNode.In, RefBrush, 1.5, _vm, dashed: true));
                    }
                }
            }

            Connections.Clear();
            foreach (var edge in edges) Connections.Add(edge);
            UpdateDim();
            RouteConnections();

            static bool blUid(Guid target, Guid self) => target != Guid.Empty && target != self;
        }

        private void UpdateEmptyHint()
        {
            if (EmptyHint != null)
                EmptyHint.Visibility = Nodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region 连线创建

        private void OnPendingCompleted(object target)
        {
            var source = Pending.Source as FlowConnector;
            if (source == null) return;

            var targetNode = target as FlowNode;
            if (targetNode == null && target is FlowConnector connector) targetNode = connector.Node;
            if (targetNode == null) return;
            if (ReferenceEquals(targetNode, source.Node)) return;

            if (!SingletonLocator.Main.IsLoggedIn)
                return;

            var step = source.Node.Step;
            switch (source.Kind)
            {
                case FlowConnectorKind.True:
                    step.TrueGotoUid = targetNode.Step.Uid;
                    break;
                case FlowConnectorKind.False:
                    step.FalseGotoUid = targetNode.Step.Uid;
                    break;
                default:
                    return;
            }

            RebuildConnections();
        }

        #endregion

        #region 走线

        /// <summary>稍微延迟再重算走线：拖动节点时不必每帧都跑一遍 A*</summary>
        private void ScheduleRoute()
        {
            if (_routeTimer == null) return;
            _routeTimer.Stop();
            _routeTimer.Start();
        }

        /// <summary>按当前节点位置重算所有连线的折线点</summary>
        private void RouteConnections()
        {
            var edges = Connections.ToList();
            if (edges.Count == 0) return;

            var nodes = Nodes.ToList();
            if (nodes.Count > RouteNodeLimit || edges.Count > RouteEdgeLimit)
            {
                // 节点太多时省掉绕障，只保留简单折线，保证拖拽流畅
                foreach (var edge in edges)
                    edge.Points = ToCollection(SimplePolyline(edge));
                return;
            }

            var obstacles = nodes.Select(NodeBounds).ToList();
            var jobs = new List<(FlowEdge Edge, Point Source, Point StubStart, Point StubEnd, Point Target)>();
            foreach (var edge in edges)
            {
                var source = edge.Source.Anchor;
                var target = edge.Target.Anchor;
                if (!IsUsable(source) || !IsUsable(target)) continue;
                jobs.Add((edge, source, new Point(source.X + RouteStub, source.Y), new Point(target.X - RouteStub, target.Y), target));
            }

            if (jobs.Count == 0) return;

            var terminals = jobs.SelectMany(j => new[]
            {
                j.StubStart,
                j.StubEnd,
                new Point(j.Source.X + RouteStub * 2, j.Source.Y),
                new Point(j.Target.X - RouteStub * 2, j.Target.Y),
            }).ToList();
            var grid = FlowRouteGrid.Build(obstacles, RouteMargin, terminals);

            foreach (var job in jobs)
            {
                var points = new List<Point> { job.Source };
                var routed = grid.Solve(job.StubStart, job.StubEnd);
                if (routed != null)
                {
                    points.AddRange(routed);
                }
                else
                {
                    // 起点 / 终点离节点太近时，退一步用更长的水平接头再试一次
                    var wideStart = new Point(job.Source.X + RouteStub * 2, job.Source.Y);
                    var wideEnd = new Point(job.Target.X - RouteStub * 2, job.Target.Y);
                    var wide = grid.Solve(wideStart, wideEnd);
                    points.Add(job.StubStart);
                    if (wide != null)
                    {
                        points.AddRange(wide);
                    }
                    else
                    {
                        points.Add(new Point(job.StubStart.X, job.Target.Y));
                        points.Add(job.StubEnd);
                    }
                }

                if (points[points.Count - 1] != job.Target) points.Add(job.Target);
                job.Edge.Points = ToCollection(points);
            }

            static bool IsUsable(Point point)
                => !double.IsNaN(point.X) && !double.IsNaN(point.Y) && Math.Abs(point.X) + Math.Abs(point.Y) > 0.5d;
        }

        /// <summary>节点太多或走线失败时的简单折线</summary>
        private List<Point> SimplePolyline(FlowEdge edge)
        {
            var source = edge.Source.Anchor;
            var target = edge.Target.Anchor;
            return new List<Point>
            {
                source,
                new Point(source.X + RouteStub, source.Y),
                new Point(source.X + RouteStub, target.Y),
                new Point(target.X - RouteStub, target.Y),
                target,
            };
        }

        private static PointCollection ToCollection(List<Point> points)
        {
            var collection = new PointCollection(points);
            collection.Freeze();
            return collection;
        }

        private static Rect NodeBounds(FlowNode node)
            => new Rect(node.Location.X + NodeConnectorWidth, node.Location.Y, NodeWidth, NodeHeight);

        #endregion

        #region 选中 / 焦点预览

        private void Selection_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_syncingSelection) return;

            _syncingSelection = true;
            try
            {
                _vm.SelectedStep = Selection.FirstOrDefault()?.Step;
            }
            finally
            {
                _syncingSelection = false;
            }

            UpdateDim();
        }

        private void SyncSelectionFromViewModel()
        {
            if (_syncingSelection) return;

            _syncingSelection = true;
            try
            {
                var step = _vm.SelectedStep;
                var node = step == null ? null : Nodes.FirstOrDefault(n => n.Step == step);
                Selection.Clear();
                if (node != null) Selection.Add(node);
            }
            finally
            {
                _syncingSelection = false;
            }

            UpdateDim();
        }

        private bool FocusMode => FocusModeToggle != null && FocusModeToggle.IsChecked == true;

        private void FocusMode_Click(object sender, RoutedEventArgs e) => UpdateDim();

        private void UpdateDim()
        {
            var selected = new HashSet<Guid>(Selection.Select(n => n.Step.Uid));
            bool focus = FocusMode && selected.Count > 0;

            foreach (var edge in Connections)
            {
                edge.IsDim = focus
                    && !selected.Contains(edge.Source.Node.Step.Uid)
                    && !selected.Contains(edge.Target.Node.Step.Uid);
            }
        }

        #endregion

        #region 版面 / 缩放

        private void ApplyAutoLayoutForUnplacedNodes()
        {
            if (Nodes.Count == 0) return;
            if (Editor == null) return;

            double viewWidth = Editor.ActualWidth > 40 ? Editor.ActualWidth : 900;
            int columns = Math.Max(1, (int)((viewWidth - 80) / 300));
            double x0 = 60, y0 = 36, cellW = 300, cellH = 140;

            bool any = false;
            for (int i = 0; i < Nodes.Count; i++)
            {
                var node = Nodes[i];
                if (FlowLayoutStore.TryGet(node.Step.Uid, out _)) continue;

                int row = i / columns;
                int col = i % columns;
                if (row % 2 == 1) col = Math.Max(0, columns - 1 - col);
                node.Location = new Point(x0 + col * cellW, y0 + row * cellH);
                any = true;
            }

            if (any)
            {
                UpdateEditorExtent();
                RouteConnections();
            }
        }

        private void UpdateEditorExtent()
        {
            // Nodify 自动计算范围；这里只保证为空时提示正确
            UpdateEmptyHint();
        }

        private void AutoLayout_Click(object sender, RoutedEventArgs e)
        {
            double viewWidth = Editor != null && Editor.ActualWidth > 40 ? Editor.ActualWidth : 900;
            int columns = Math.Max(1, (int)((viewWidth - 80) / 300));
            const double x0 = 60, y0 = 36, cellW = 300, cellH = 140;

            for (int i = 0; i < Nodes.Count; i++)
            {
                int row = i / columns;
                int col = i % columns;
                if (row % 2 == 1) col = Math.Max(0, columns - 1 - col);
                Nodes[i].Location = new Point(x0 + col * cellW, y0 + row * cellH);
            }

            FitToWindow();
        }

        private void FitToWindow_Click(object sender, RoutedEventArgs e) => FitToWindow();

        private void FitToWindow()
        {
            if (Nodes.Count == 0 || Editor == null) return;

            double left = Nodes.Min(n => n.Location.X);
            double top = Nodes.Min(n => n.Location.Y);
            double right = Nodes.Max(n => n.Location.X + 300);
            double bottom = Nodes.Max(n => n.Location.Y + 96);

            double vw = Editor.ActualWidth - 40;
            double vh = Editor.ActualHeight - 40;
            if (vw <= 0 || vh <= 0) return;

            double zoom = Math.Min(vw / Math.Max(1, right - left), vh / Math.Max(1, bottom - top));
            Editor.ViewportZoom = Math.Max(0.2, Math.Min(2.0, zoom));
            Editor.ViewportLocation = new Point(left - 20, top - 20);
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (Editor != null) Editor.ViewportZoom = Math.Min(2.0, Editor.ViewportZoom * 1.15);
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (Editor != null) Editor.ViewportZoom = Math.Max(0.2, Editor.ViewportZoom / 1.15);
        }

        #endregion

        #region 节点交互

        private void LogBadge_Click(object sender, MouseButtonEventArgs e)
        {
            if (!SingletonLocator.Main.IsLoggedIn) return;
            if (sender is FrameworkElement element && element.DataContext is FlowNode node)
            {
                node.Step.EnableLog = !node.Step.EnableLog;
                e.Handled = true;
            }
        }

        #endregion

        #region 撤销 / 恢复路由（交给步骤列表的撤销栈）

        public bool CanUndoFromRouter() => _vm.UndoCommand != null && _vm.UndoCommand.CanExecute(null);
        public bool CanRedoFromRouter() => _vm.RedoCommand != null && _vm.RedoCommand.CanExecute(null);
        public void UndoFromRouter() => _vm.UndoCommand?.Execute(null);
        public void RedoFromRouter() => _vm.RedoCommand?.Execute(null);

        #endregion

        #region 窗口内全屏

        private bool _isFullscreen;

        /// <summary>全屏状态变化：宿主（UserControlSteps）据此让出编辑区、把详情面板改成悬浮</summary>
        public event EventHandler FullscreenChanged;

        public bool IsFullscreen => _isFullscreen;

        public void SetFullscreen(bool fullscreen)
        {
            if (FullscreenToggle == null) return;
            if (FullscreenToggle.IsChecked != fullscreen)
                FullscreenToggle.IsChecked = fullscreen;
            ApplyFullscreenState(fullscreen);
        }

        public void ExitFullscreen() => SetFullscreen(false);

        private void FullscreenToggle_Click(object sender, RoutedEventArgs e)
            => ApplyFullscreenState(FullscreenToggle.IsChecked == true);

        private void ApplyFullscreenState(bool fullscreen)
        {
            if (_isFullscreen == fullscreen) return;
            _isFullscreen = fullscreen;

            UpdateFullscreenToolTip();
            FullscreenChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateFullscreenToolTip()
        {
            bool fullscreen = _isFullscreen;
            FullscreenToggle.ToolTip = LanguageService.GetLocalizedString(
                fullscreen ? "FlowChart_ExitFullscreen_ToolTip" : "FlowChart_Fullscreen_ToolTip",
                fullscreen ? "退出全屏" : "全屏");
        }

        #endregion
    }

    #region 视图模型

    public enum FlowConnectorKind
    {
        In,
        True,
        False,
        Ref,
        Seq,
    }

    /// <summary>连线端点（出口 / 入口）</summary>
    public sealed class FlowConnector : INotifyPropertyChanged
    {
        private Point _anchor;
        private bool _isConnected;

        public FlowConnector(FlowNode node, FlowConnectorKind kind)
        {
            Node = node;
            Kind = kind;
        }

        public FlowNode Node { get; }
        public FlowConnectorKind Kind { get; }

        public Point Anchor
        {
            get => _anchor;
            set { if (_anchor != value) { _anchor = value; Raise(nameof(Anchor)); } }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set { if (_isConnected != value) { _isConnected = value; Raise(nameof(IsConnected)); } }
        }

        public string Text => Kind switch
        {
            FlowConnectorKind.True => "T",
            FlowConnectorKind.False => "F",
            FlowConnectorKind.Ref => "R",
            FlowConnectorKind.Seq => "S",
            _ => "I",
        };

        public Brush Brush => Kind switch
        {
            FlowConnectorKind.True => FreezePublic(0x2F, 0xA3, 0x6B),
            FlowConnectorKind.False => FreezePublic(0xDC, 0x5B, 0x4C),
            FlowConnectorKind.Ref => FreezePublic(0x7B, 0x5C, 0xD6),
            FlowConnectorKind.Seq => FreezePublic(0x8A, 0x96, 0xA3),
            _ => FreezePublic(0x7D, 0x8A, 0x99),
        };

        public string Tip => Kind switch
        {
            FlowConnectorKind.True => LanguageService.GetLocalizedString("FlowChart_TruePort_ToolTip"),
            FlowConnectorKind.False => LanguageService.GetLocalizedString("FlowChart_FalsePort_ToolTip"),
            FlowConnectorKind.Ref => LanguageService.GetLocalizedString("FlowReference_Port_ToolTip"),
            _ => string.Empty,
        };

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static Brush FreezePublic(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>节点：一个步骤卡片，带入口与四个出口</summary>
    public sealed class FlowNode : INotifyPropertyChanged
    {
        private Point _location;

        public FlowNode(AutomationStepBase step)
        {
            Step = step;
            In = new FlowConnector(this, FlowConnectorKind.In);
            Seq = new FlowConnector(this, FlowConnectorKind.Seq);
            True = new FlowConnector(this, FlowConnectorKind.True);
            False = new FlowConnector(this, FlowConnectorKind.False);
            Ref = new FlowConnector(this, FlowConnectorKind.Ref);

            Inputs.Add(In);
            Inputs.Add(Ref);
            Outputs.Add(Seq);
            Outputs.Add(True);
            Outputs.Add(False);

            if (FlowLayoutStore.TryGet(step.Uid, out var stored)) _location = stored;
        }

        public AutomationStepBase Step { get; }
        public ObservableCollection<FlowConnector> Inputs { get; } = new ObservableCollection<FlowConnector>();
        public ObservableCollection<FlowConnector> Outputs { get; } = new ObservableCollection<FlowConnector>();

        public FlowConnector In { get; }
        public FlowConnector Seq { get; }
        public FlowConnector True { get; }
        public FlowConnector False { get; }
        public FlowConnector Ref { get; }

        public IEnumerable<FlowConnector> AllConnectors => new[] { In, Seq, True, False, Ref };

        /// <summary>节点位置（拖动时写回版面表）</summary>
        public Point Location
        {
            get => _location;
            set
            {
                if (_location == value) return;
                _location = value;
                FlowLayoutStore.Set(Step.Uid, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
            }
        }

        public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Step)));

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>一条连线：出口 → 目标节点入口</summary>
    public sealed class FlowEdge : INotifyPropertyChanged
    {
        private bool _isDim;
        private PointCollection _points;

        public FlowEdge(FlowConnector source, FlowConnector target, Brush brush, double thickness, StepsViewModel vm, bool dashed = false)
        {
            Source = source;
            Target = target;
            Brush = brush;
            Thickness = thickness;
            Dashed = dashed;
            Source.IsConnected = true;
            Target.IsConnected = true;
        }

        public FlowConnector Source { get; }
        public FlowConnector Target { get; }
        public Brush Brush { get; }
        public double Thickness { get; }
        public bool Dashed { get; }

        private static readonly DoubleCollection DashStroke = CreateDashStroke();

        private static DoubleCollection CreateDashStroke()
        {
            var dash = new DoubleCollection { 4d, 3d };
            dash.Freeze();
            return dash;
        }

        /// <summary>参考线用虚线，其余连线不设虚线</summary>
        public DoubleCollection DashArray => Dashed ? DashStroke : null;

        /// <summary>绕开节点后的折线点（由视图算好后填进来）</summary>
        public PointCollection Points
        {
            get => _points;
            set
            {
                _points = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Points)));
            }
        }

        public bool IsDim
        {
            get => _isDim;
            set { if (_isDim != value) { _isDim = value; Raise(nameof(Opacity)); } }
        }

        public double Opacity => IsDim ? 0.18 : 1.0;

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>拖线创建：记录起点，落在节点 / 端子上完成</summary>
    public sealed class FlowPending
    {
        public ICommand StartCommand { get; }
        public ICommand CompletedCommand { get; }

        public FlowPending()
        {
            StartCommand = new SimpleCommand<object>(OnStarted);
            CompletedCommand = new SimpleCommand<object>(OnCompleted);
        }

        public object Source { get; private set; }
        public Action<object> Completed { get; set; }

        private void OnStarted(object parameter) => Source = parameter;

        private void OnCompleted(object parameter)
        {
            Completed?.Invoke(parameter);
            Source = null;
        }
    }

    /// <summary>Nodify 命令里用的小包装</summary>
    public sealed class SimpleCommand<T> : ICommand
    {
        private readonly Action<T> _action;

        public SimpleCommand(Action<T> action) => _action = action;

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => _action(parameter is T value ? value : default);
    }

    #endregion
}
