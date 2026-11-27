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
        private const double RouteStub = 26d;

        /// <summary>节点或连线太多时退回简单折线，避免拖拽卡顿</summary>
        private const int RouteNodeLimit = 120;
        private const int RouteEdgeLimit = 200;

        /// <summary>同向并线时最多错开几条车道，每条错开一点，避免两根线重叠</summary>
        private const int RouteLaneCount = 3;
        private const double RouteLaneGap = 6d;

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
                foreach (var edge in edges)
                {
                    var card = CardBounds(edge.Target.Node);
                    var port = LeftPort(card, edge.Source.Anchor);
                    edge.TargetPoint = port;
                    edge.TargetOrientation = Orientation.Horizontal;
                    edge.ArrowDirection = Nodify.ConnectionDirection.Forward;
                    edge.Points = ToCollection(SimplePolyline(edge, port));
                }
                return;
            }

            var obstacles = nodes.Select(NodeBounds).ToList();
            var jobs = new List<RouteJob>();
            foreach (var edge in edges)
            {
                var source = edge.Source.Anchor;
                if (!IsUsable(source)) continue;

                var card = CardBounds(edge.Target.Node);
                if (card.Width < 1 || card.Height < 1) continue;

                var job = new RouteJob
                {
                    Edge = edge,
                    Source = source,
                    StubStart = new Point(source.X + RouteStub, source.Y),
                };

                // 入口候选：卡片左/上/下边框各取几个位置，谁的路更短就用谁，避免竖直折返
                foreach (var y in PortSpots(card.Top, card.Bottom))
                    job.Candidates.Add(new RouteCandidate(new Point(card.Left, y), new Point(card.Left - RouteStub, y), Orientation.Horizontal, Nodify.ConnectionDirection.Forward, 0d));
                foreach (var x in PortSpots(card.Left, card.Right))
                    job.Candidates.Add(new RouteCandidate(new Point(x, card.Top), new Point(x, card.Top - RouteStub), Orientation.Vertical, Nodify.ConnectionDirection.Forward, 12d));
                foreach (var x in PortSpots(card.Left, card.Right))
                    job.Candidates.Add(new RouteCandidate(new Point(x, card.Bottom), new Point(x, card.Bottom + RouteStub), Orientation.Vertical, Nodify.ConnectionDirection.Backward, 12d));

                jobs.Add(job);
            }

            if (jobs.Count == 0) return;

            var terminals = new List<Point>();
            foreach (var job in jobs)
            {
                terminals.Add(job.StubStart);
                foreach (var candidate in job.Candidates) terminals.Add(candidate.Stub);
            }

            // 每条线从第 0 道开始试；若和已排好的线挤在同一条走廊上，就换下一道重新绕一次
            var grids = new FlowRouteGrid[RouteLaneCount];
            var accepted = new List<List<Point>>();

            foreach (var job in jobs)
            {
                RouteCandidate chosen = default(RouteCandidate);
                List<Point> chosenPoints = null;

                for (int lane = 0; lane < RouteLaneCount; lane++)
                {
                    if (grids[lane] == null)
                        grids[lane] = FlowRouteGrid.Build(obstacles, RouteMargin + lane * RouteLaneGap, BuildTerminals(jobs, lane));

                    var best = SolveJob(grids[lane], job, lane, out var bestPoints);
                    if (bestPoints == null) continue;

                    if (chosenPoints == null)
                    {
                        chosen = best;
                        chosenPoints = bestPoints;
                    }

                    if (!OverlapsAny(bestPoints, accepted))
                    {
                        chosen = best;
                        chosenPoints = bestPoints;
                        break;
                    }
                }

                if (chosenPoints == null)
                {
                    var card = CardBounds(job.Edge.Target.Node);
                    var port = LeftPort(card, job.Source);
                    chosen = new RouteCandidate(port, new Point(port.X - RouteStub, port.Y), Orientation.Horizontal, Nodify.ConnectionDirection.Forward, 0d);
                    chosenPoints = SimplePolyline(job.Edge, port);
                }

                accepted.Add(chosenPoints);
                job.Edge.TargetPoint = chosen.Port;
                job.Edge.TargetOrientation = chosen.Orientation;
                job.Edge.ArrowDirection = chosen.Direction;
                job.Edge.Points = ToCollection(chosenPoints);
            }

            static RouteCandidate SolveJob(FlowRouteGrid grid, RouteJob job, int lane, out List<Point> points)
            {
                RouteCandidate best = default(RouteCandidate);
                points = null;
                double bestCost = double.MaxValue;

                foreach (var candidate in job.Candidates)
                {
                    var routed = grid.Solve(StubStartFor(job, lane), candidate.Stub);
                    if (routed == null) continue;

                    var path = new List<Point> { job.Source };
                    path.AddRange(routed);
                    if (path[path.Count - 1] != candidate.Port) path.Add(candidate.Port);

                    double cost = PathLength(path) + candidate.Penalty;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = candidate;
                        points = path;
                    }
                }

                return best;
            }

            static bool OverlapsAny(List<Point> path, List<List<Point>> others)
            {
                foreach (var other in others)
                {
                    if (SharesCorridor(path, other)) return true;
                }

                return false;
            }

            static bool IsUsable(Point point)
                => !double.IsNaN(point.X) && !double.IsNaN(point.Y) && Math.Abs(point.X) + Math.Abs(point.Y) > 0.5d;
        }

        /// <summary>一条线在目标节点边框上的入口候选</summary>
        private struct RouteCandidate
        {
            public RouteCandidate(Point port, Point stub, Orientation orientation, Nodify.ConnectionDirection direction, double penalty)
            {
                Port = port;
                Stub = stub;
                Orientation = orientation;
                Direction = direction;
                Penalty = penalty;
            }

            public Point Port { get; }
            public Point Stub { get; }
            public Orientation Orientation { get; }
            public Nodify.ConnectionDirection Direction { get; }
            public double Penalty { get; }
        }

        private sealed class RouteJob
        {
            public FlowEdge Edge { get; set; }
            public Point Source { get; set; }
            public Point StubStart { get; set; }
            public List<RouteCandidate> Candidates { get; } = new List<RouteCandidate>();
        }

        /// <summary>边框上的几个候选位置：两端各留一点余量，再加中点</summary>
        private static IEnumerable<double> PortSpots(double start, double end)
        {
            double span = end - start;
            double inset = Math.Min(18d, span / 3d);
            yield return start + inset;
            yield return start + span / 2d;
            yield return end - inset;
        }

        /// <summary>左边框上离给定高度最近的落点</summary>
        private static Point LeftPort(Rect card, Point near)
        {
            double y = Math.Max(card.Top + 14d, Math.Min(card.Bottom - 14d, near.Y));
            return new Point(card.Left, y);
        }

        private static Rect CardBounds(FlowNode node)
            => new Rect(node.Location.X + NodeConnectorWidth, node.Location.Y, NodeWidth - NodeConnectorWidth * 2, NodeHeight);

        /// <summary>出口先水平挣脱节点的那一段：每条线按自己的车道错开，避免同一起点出去的线挤在一条竖线上</summary>
        private static Point StubStartFor(RouteJob job, int lane)
            => new Point(job.Source.X + RouteStub + lane * RouteLaneGap, job.Source.Y);

        /// <summary>每一条车道用到的所有端点，供栅格建线</summary>
        private static List<Point> BuildTerminals(List<RouteJob> jobs, int lane)
        {
            var terminals = new List<Point>();
            foreach (var job in jobs)
            {
                terminals.Add(StubStartFor(job, lane));
                foreach (var candidate in job.Candidates) terminals.Add(candidate.Stub);
            }

            return terminals;
        }
        /// <summary>两条折线是否有贴在一起走的同向线段</summary>
        private static bool SharesCorridor(IReadOnlyList<Point> first, IReadOnlyList<Point> second)
        {
            for (int i = 1; i < first.Count; i++)
            {
                var a1 = first[i - 1];
                var a2 = first[i];
                bool aHorizontal = Math.Abs(a1.Y - a2.Y) < 0.5d;
                bool aVertical = Math.Abs(a1.X - a2.X) < 0.5d;
                if (!aHorizontal && !aVertical) continue;

                for (int j = 1; j < second.Count; j++)
                {
                    var b1 = second[j - 1];
                    var b2 = second[j];
                    bool bHorizontal = Math.Abs(b1.Y - b2.Y) < 0.5d;
                    bool bVertical = Math.Abs(b1.X - b2.X) < 0.5d;

                    if (aHorizontal && bHorizontal)
                    {
                        if (Math.Abs(a1.Y - b1.Y) > 2d) continue;
                        double overlap = Math.Min(Math.Max(a1.X, a2.X), Math.Max(b1.X, b2.X))
                                         - Math.Max(Math.Min(a1.X, a2.X), Math.Min(b1.X, b2.X));
                        if (overlap > 3d) return true;
                    }
                    else if (aVertical && bVertical)
                    {
                        if (Math.Abs(a1.X - b1.X) > 2d) continue;
                        double overlap = Math.Min(Math.Max(a1.Y, a2.Y), Math.Max(b1.Y, b2.Y))
                                         - Math.Max(Math.Min(a1.Y, a2.Y), Math.Min(b1.Y, b2.Y));
                        if (overlap > 3d) return true;
                    }
                }
            }

            return false;
        }
        private static double PathLength(IReadOnlyList<Point> points)
        {
            double length = 0d;
            for (int i = 1; i < points.Count; i++)
                length += Math.Abs(points[i].X - points[i - 1].X) + Math.Abs(points[i].Y - points[i - 1].Y);
            return length;
        }
        /// <summary>节点太多或走线失败时的简单折线</summary>
        private List<Point> SimplePolyline(FlowEdge edge, Point port)
        {
            var source = edge.Source.Anchor;
            return new List<Point>
            {
                source,
                new Point(source.X + RouteStub, source.Y),
                new Point(source.X + RouteStub, port.Y),
                new Point(port.X - RouteStub, port.Y),
                port,
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
                // 焦点预览只看「从选中节点走出去的线」：别的节点连进来的线一样变淡
                edge.IsDim = focus && !selected.Contains(edge.Source.Node.Step.Uid);
            }
        }

        #endregion

        #region 版面 / 缩放

        private void ApplyAutoLayoutForUnplacedNodes()
        {
            if (Nodes.Count == 0) return;
            if (Editor == null) return;

            if (ApplyAutoLayout(FlowArrangeKind.Perimeter, onlyUnplaced: true))
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

        /// <summary>自动整理：点按钮弹出二级菜单，选「默认」（口字形）或「垂直流」</summary>
        private void AutoLayout_Click(object sender, RoutedEventArgs e)
        {
            var menu = AutoLayoutButton == null ? null : AutoLayoutButton.ContextMenu;
            if (menu == null) return;

            menu.PlacementTarget = AutoLayoutButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void AutoLayoutDefault_Click(object sender, RoutedEventArgs e) => ArrangeAll(FlowArrangeKind.Perimeter);

        private void AutoLayoutVertical_Click(object sender, RoutedEventArgs e) => ArrangeAll(FlowArrangeKind.Vertical);

        private void ArrangeAll(FlowArrangeKind kind)
        {
            ApplyAutoLayout(kind, onlyUnplaced: false);
            FitToWindow();
        }

        /// <summary>自动排列的起始边距</summary>
        private const double LayoutOriginX = 60d;
        private const double LayoutOriginY = 36d;

        /// <summary>自动排列的排法：「默认」口字形 / 「垂直流」一列向下</summary>
        private enum FlowArrangeKind
        {
            Perimeter,
            Vertical,
        }

        /// <summary>
        /// 按指定排法摆放节点，锚定（节点右上角「锚」）的节点原地不动，新位置也不会压到它们：
        /// - 默认（口字形）：节点只占矩形四条边，左上 → 右上 → 右下 → 左下；节点多时把矩形撑大；
        /// - 垂直流：顶部居中一路向下排成一列，连线自然走两边。
        /// </summary>
        /// <param name="onlyUnplaced">true = 只摆还没有位置的节点（打开工程 / 新增步骤时用）</param>
        /// <returns>是否真的动过节点</returns>
        private bool ApplyAutoLayout(FlowArrangeKind kind, bool onlyUnplaced)
        {
            var nodes = Nodes.ToList();
            if (nodes.Count == 0) return false;

            var cells = kind == FlowArrangeKind.Vertical
                ? BuildVerticalCells(nodes.Count)
                : BuildPerimeterCells(nodes.Count);

            // 不动的节点（锚定的，或已经摆好且只补空缺时）先占住格子，新位置要避开它们；
            // 会移动的节点不能算占用，否则它自己的旧位置会把自己的新位置挡掉
            var occupied = new List<Rect>();
            foreach (var node in nodes)
            {
                if (!node.IsPinned && !(onlyUnplaced && FlowLayoutStore.TryGet(node.Step.Uid, out _))) continue;
                occupied.Add(CellBounds(node.Location));
            }

            int cursor = 0;
            bool moved = false;
            foreach (var node in nodes)
            {
                if (node.IsPinned) continue;
                if (onlyUnplaced && FlowLayoutStore.TryGet(node.Step.Uid, out _)) continue;

                Point target = NextFreeCell(cells, ref cursor, occupied, kind);
                occupied.Add(CellBounds(target));
                if (node.Location == target) continue;

                node.Location = target;
                moved = true;
            }

            return moved;
        }

        private static List<Point> BuildPerimeterCells(int count)
        {
            var cells = new List<Point>(count);
            foreach (var cell in FlowAutoLayout.BuildPerimeter(count))
                cells.Add(new Point(cell.X + LayoutOriginX, cell.Y + LayoutOriginY));
            return cells;
        }

        private static List<Point> BuildVerticalCells(int count)
        {
            var cells = new List<Point>(count);
            foreach (var cell in FlowAutoLayout.BuildVertical(count))
                cells.Add(new Point(cell.X, cell.Y + LayoutOriginY));
            return cells;
        }

        /// <summary>取下一个不和已占格子重叠的位置</summary>
        private static Point NextFreeCell(List<Point> cells, ref int cursor, IReadOnlyList<Rect> occupied, FlowArrangeKind kind)
        {
            while (true)
            {
                // 格子被锚定节点全占住的极端情况下继续往下补格子（正常排法用不到）
                if (cursor >= cells.Count) AppendExtraCells(cells, kind);

                var point = cells[cursor++];
                if (!OverlapsAny(CellBounds(point), occupied)) return point;
            }
        }

        private static void AppendExtraCells(List<Point> cells, FlowArrangeKind kind)
        {
            if (cells.Count == 0) return;

            if (kind == FlowArrangeKind.Vertical)
            {
                var last = cells[cells.Count - 1];
                cells.Add(new Point(last.X, last.Y + FlowAutoLayout.VerticalGap));
                return;
            }

            double minX = cells.Min(p => p.X);
            double maxX = cells.Max(p => p.X);
            double maxY = cells.Max(p => p.Y);
            for (double x = minX; x <= maxX + 0.5; x += FlowAutoLayout.CellWidth)
                cells.Add(new Point(x, maxY + FlowAutoLayout.CellHeight));
        }

        private static bool OverlapsAny(Rect rect, IReadOnlyList<Rect> others)
        {
            for (int i = 0; i < others.Count; i++)
            {
                // 只认真正压在一起的：紧挨着（共边）算不冲突，否则相邻格会被误判成占用
                var other = others[i];
                if (rect.Left < other.Right - 0.5 && rect.Right > other.Left + 0.5
                    && rect.Top < other.Bottom - 0.5 && rect.Bottom > other.Top + 0.5)
                    return true;
            }

            return false;
        }

        private static Rect CellBounds(Point topLeft)
            => new Rect(topLeft, new Size(FlowAutoLayout.CellWidth, FlowAutoLayout.CellHeight));

        private void FitToWindow_Click(object sender, RoutedEventArgs e) => FitToWindow();

        private void FitToWindow()
        {
            if (Nodes.Count == 0 || Editor == null) return;

            double left = Nodes.Min(n => n.Location.X);
            double top = Nodes.Min(n => n.Location.Y);
            double right = Nodes.Max(n => n.Location.X + NodeConnectorWidth * 2 + NodeWidth);
            double bottom = Nodes.Max(n => n.Location.Y + NodeHeight);

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

        /// <summary>节点右上角「锚」：钉住 / 取消锚定（自动整理时钉住的节点不动）</summary>
        private void PinBadge_Click(object sender, MouseButtonEventArgs e)
        {
            if (!SingletonLocator.Main.IsLoggedIn) return;
            if (sender is FrameworkElement element && element.DataContext is FlowNode node)
            {
                node.IsPinned = !node.IsPinned;
                e.Handled = true;
            }
        }

        /// <summary>双击节点标题就地改名称（回车确认、Esc 取消、点别处也算确认）</summary>
        private void NodeTitle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if (!SingletonLocator.Main.IsLoggedIn) return;
            if (!(sender is FrameworkElement element) || !(element.DataContext is FlowNode node)) return;

            e.Handled = true;
            node.IsTitleEditing = true;
        }

        private void NodeTitleBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(sender is TextBox box) || !box.IsVisible) return;

            // 输入框刚显示出来时先拿焦点并全选，直接输入就能替换旧名字
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    box.Focus();
                    Keyboard.Focus(box);
                    box.SelectAll();
                }
                catch { }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void NodeTitleBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is TextBox box)) return;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitNodeTitle(box, apply: true);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CommitNodeTitle(box, apply: false);
            }
        }

        private void NodeTitleBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box) CommitNodeTitle(box, apply: true);
        }

        /// <summary>结束编辑：apply=true 时把输入框里的文字写回步骤名称（空名称按原名处理）</summary>
        private void CommitNodeTitle(TextBox box, bool apply)
        {
            if (!(box.DataContext is FlowNode node)) return;

            string original = node.Step.Name ?? string.Empty;
            if (apply)
            {
                string name = (box.Text ?? string.Empty).Trim();
                if (name.Length > 0 && !string.Equals(original, name, StringComparison.Ordinal))
                {
                    _vm.PushUndoForFlowEdit();
                    node.Step.Name = name;
                }
            }

            box.Text = node.Step.Name ?? string.Empty;
            node.IsTitleEditing = false;
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
        private bool _isTitleEditing;

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

        /// <summary>
        /// 锚定：钉住（点节点右上角「锚」）后自动整理不会移动它，手动拖动照常。
        /// 状态存在 FlowLayoutStore 里，随工程写进 flow.json。
        /// </summary>
        public bool IsPinned
        {
            get => FlowLayoutStore.IsPinned(Step.Uid);
            set
            {
                if (IsPinned == value) return;
                FlowLayoutStore.SetPinned(Step.Uid, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));
            }
        }

        public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Step)));

        /// <summary>节点标题是否正在就地编辑（双击标题进入，回车 / 点别处结束）</summary>
        public bool IsTitleEditing
        {
            get => _isTitleEditing;
            set
            {
                if (_isTitleEditing == value) return;
                _isTitleEditing = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTitleEditing)));
            }
        }

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

        private Point _targetPoint;
        private Orientation _targetOrientation = Orientation.Horizontal;
        private Nodify.ConnectionDirection _arrowDirection = Nodify.ConnectionDirection.Forward;

        /// <summary>入口在卡片边框上的落点（每条线各自选一个，不再共用固定圆点）</summary>
        public Point TargetPoint
        {
            get => _targetPoint;
            set { if (_targetPoint != value) { _targetPoint = value; Raise(nameof(TargetPoint)); } }
        }

        /// <summary>进入方向：水平（左/右边框）或竖直（上/下边框）</summary>
        public Orientation TargetOrientation
        {
            get => _targetOrientation;
            set { if (_targetOrientation != value) { _targetOrientation = value; Raise(nameof(TargetOrientation)); } }
        }

        /// <summary>箭头朝向：下边框进入时箭头朝上</summary>
        public Nodify.ConnectionDirection ArrowDirection
        {
            get => _arrowDirection;
            set { if (_arrowDirection != value) { _arrowDirection = value; Raise(nameof(ArrowDirection)); } }
        }

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
