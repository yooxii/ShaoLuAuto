using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels;
using ShaoLu.Viewmodels.AutomationStep;
using Point = System.Windows.Point;
using Path = System.Windows.Shapes.Path;

namespace ShaoLu.Views
{
    /// <summary>
    /// 步骤的流程图视图：节点可拖动摆放，顺序与真/假跳转画成连线。
    /// 数据仍是 StepsViewModel.AutomationStepBases，与表格视图切换显示，不另存一份流程定义。
    /// </summary>
    public partial class UserControlFlow : UserControl
    {
        #region 布局常量

        private const double NodeWidth = 220;      // 节点卡片宽（不含右侧出口圆的溢出部分）
        private const double NodeHeight = 96;
        private const double NodeSlotWidth = 236;  // 含出口圆的占位宽
        private const double GridStep = 8;         // 拖动吸附步长
        private const double LaneStep = 13;        // 同一走线通道里多条连线之间的间距（重叠时错开得更明显）
        private const double SameRowTolerance = NodeHeight * 0.65;   // 判定「大致同一行」的容差
        private const double SameColumnTolerance = NodeWidth * 0.55; // 判定「大致同一列」的容差

        // S 型自动排布：从左往右排满一行后折到下一行（奇数行反向），
        // 连接断开的两步之间强制换行；CellW/CellH 同时是连线绕行用的网格间距。
        private double X0 = 40;                   // 左起位置（曲线模式下自动整理时留出更多边距）
        private double Y0 = 36;                   // 顶部位置（曲线模式下自动整理时留出更多边距）
        private double _gapX = 40;                 // 列间距（竖向走线通道），按设置可变
        private double _gapY = 46;                 // 行间距（横向走线通道），按设置可变
        private double CellW => NodeWidth + _gapX;
        private double CellH => NodeHeight + _gapY;
        private const int MinColumns = 1;
        // 竖直排列时节点之间的紧密间距
        private const double VerticalTightGap = 12;

        /// <summary>自动整理用竖直排列（居中一列往下排、不画顺序连线）</summary>
        private bool IsVerticalArrange
        {
            get
            {
                try { return SingletonLocator.Settings.Step.FlowArrangeMode == 1; }
                catch { return false; }
            }
        }

        private const double ZoomMin = 0.3;
        private const double ZoomMax = 2.0;

        #endregion

        #region 画笔

        private static readonly Brush EdgeNormalBrush = CreateFrozenBrush(0x9A, 0xA5, 0xB1);
        private static readonly Brush EdgeTrueBrush = CreateFrozenBrush(0x2F, 0xA3, 0x6B);
        private static readonly Brush EdgeFalseBrush = CreateFrozenBrush(0xDC, 0x5B, 0x4C);
        private static readonly Brush EdgeBothBrush = CreateFrozenBrush(0x7B, 0x5C, 0xD6);
        private static readonly Brush LinkBrush = CreateFrozenBrush(0x2B, 0x6F, 0xD6);
        private static readonly Brush AnchorHoverBrush = CreateFrozenBrush(0xD9, 0xF2, 0xE4);

        /// <summary>会改变右侧摘要的步骤属性</summary>
        private static readonly HashSet<string> SummaryProperties = new HashSet<string>
        {
            nameof(AutomationStepBase.Name),
            nameof(AutomationStepBase.Description),
            nameof(AutomationStepBase.EnableLog),
            "TextToType", "FilePath", "Title", "SelectedWindowTitle", "WindowTitleKeyword",
            "SignalName", "CustomName", "InputMode", "HasFields", "MouseActions", "ThreadCount",
            "CroppedImageName", "CroppedImg", "ImagePath", "SourceImageName",
        };

        #endregion

        #region 可添加的步骤类型

        private static readonly (string Tag, string ResourceKey)[] SimpleStepTypes =
        {
            ("ClickImage", "AddImageStep"),
            ("FindImage", "AddFindImageStep"),
            ("TypeText", "AddTextStep"),
            ("TypeTextMore", "AddTextMoreStep"),
            ("TypeTextFromFile", "AddTextFromFileStep"),
            ("MouseAction", "AddMouseActionStep"),
            ("GetInput", "AddGetInputStep"),
            ("Statistics", "AddStatisticsStep"),
            ("BurnInConfig", "AddBurnInConfigStep"),
            ("FocusWindow", "AddFocusWindowStep"),
            ("Empty", "AddEmptyStep"),
            ("End", "AddEndStep"),
            ("Popup", "AddPopupStep"),
            ("ScanCode", "AddScanCodeStep"),
            ("AcbelUpload", "AddAcbelUploadStep"),
            ("UserInput", "UserInput_StepName"),
        };

        private static readonly (string Tag, string ResourceKey)[] MultiThreadStepTypes =
        {
            ("MultiThread", "AddMultiThreadStep"),
            ("MultiThreadEnd", "AddMultiThreadEndStep"),
            ("SendSignal", "AddSendSignalStep"),
            ("ReceiveSignal", "AddReceiveSignalStep"),
            ("Interrupt", "AddInterruptStep"),
        };

        #endregion

        private readonly StepsViewModel _vm = SingletonLocator.Steps;
        private readonly Dictionary<Guid, ContentControl> _nodes = new Dictionary<Guid, ContentControl>();
        private readonly List<AutomationStepBase> _hookedSteps = new List<AutomationStepBase>();
        private ObservableCollection<AutomationStepBase> _hookedCollection;

        private ContextMenu _nodeMenu;
        private ContextMenu _canvasMenu;
        private ContextMenu _canvasEditMenu;

        // 节点拖动
        private ContentControl _dragNode;
        private Point _dragStartPoint;
        private Vector _dragOffset;
        private bool _dragging;

        // 从出口圆拖连线
        private AutomationStepBase _linkSource;
        private bool _linkIsTrue;
        private Path _linkPath;
        private Point _linkStartPoint;
        private bool _linkMoved;
        private AutomationStepBase _dropTarget;
        // 连线端点把手：记录连接线画完的末端位置，供拖动改接使用
        private Point? _drawingEdgeEnd;
        private Point? _drawingEdgeStart;
        private string _lastEdgeKey;
        // 正在把设置里的连线样式同步到工具栏下拉时不触发切换
        private bool _edgeStyleSyncing;
        // 正在把设置里的排列方式同步到工具栏下拉时不触发切换
        private bool _arrangeModeSyncing;
        // 正在拖的连线类型：true=引用步骤连线（拖已有引用线时带上原条件对象）
        private bool _linkIsReference;
        private Models.StepCondition _linkCondition;
        private ContentControl _dimmedNode;

        // 拖线时节点内部的自连锚点
        private Grid _selfAnchor;
        private Ellipse _selfAnchorEllipse;
        private Rect _selfAnchorRect = Rect.Empty;
        private AutomationStepBase _selfAnchorOwner;

        private bool _rebuilding;
        private bool _loadedOnce;
        private int _edgeLane;           // 同一批连线的走线偏移序号
        // 连线通道占位：同一条通道里已经走了几条，后面的往外错开，避免多条跳转线叠在一起看不清
        private readonly Dictionary<string, int> _bandLanes = new Dictionary<string, int>();

        // 多选 / 框选
        private readonly HashSet<Guid> _selectedUids = new HashSet<Guid>();
        private bool _syncingSelection;
        private bool _banding;
        private Point _bandStart;
        private Rectangle _bandRect;
        private bool _bandAdditive;

        // 流程图自己的剪贴板：保存的是拷贝时的原始步骤，粘贴时再克隆，才能保住块内跳转关系
        private readonly List<AutomationStepBase> _clipboard = new List<AutomationStepBase>();
        private bool _hasClipboard;

        // 拖动一个步骤悬停在另一个步骤上，满 0.3 秒后替换
        private DispatcherTimer _replaceTimer;
        private AutomationStepBase _replaceCandidate;
        private bool _replaceArmed;
        private ContentControl _replaceCandidateNode;

        // 流程图自己的细粒度撤销/恢复：连线、标题备注、日志开关、节点位置
        private sealed class FlowUndoEntry
        {
            public Action Undo;
            public Action Redo;
            /// <summary>入栈时步骤列表撤销栈的深度：列表栈更深说明之后又做过结构改动，要先撤销列表那边</summary>
            public int VmDepth;
        }

        private readonly List<FlowUndoEntry> _flowUndo = new List<FlowUndoEntry>();
        private readonly List<FlowUndoEntry> _flowRedo = new List<FlowUndoEntry>();

        private AutomationStepBase _dragUndoStep;
        // 多选拖动：每个被拖动节点的起始位置（Uid → 起点）
        private readonly Dictionary<Guid, Point> _multiDragOrigins = new Dictionary<Guid, Point>();
        private Point _dragUndoOldPos;
        private bool _dragUndoOldHadStore;

        private AutomationStepBase _editStep;
        private string _editProperty;
        private string _editOldText;

        public UserControlFlow()
        {
            InitializeComponent();
            DataContext = _vm;

            BuildMenus();

            _replaceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _replaceTimer.Tick += (_, __) => ArmDragReplace();

            // 右键按住 0.3 秒才弹出环状菜单
            _ringHoldTimer.Tick += RingHoldTimer_Tick;

            // 快捷键（需要控件拿到焦点；点画布/节点时会 Focus 自己）
            InputBindings.Add(new KeyBinding { Key = Key.A, Modifiers = ModifierKeys.Control, Command = new RelayCommand(SelectAllSteps) });
            InputBindings.Add(new KeyBinding { Key = Key.C, Modifiers = ModifierKeys.Control, Command = new RelayCommand(CopySelection) });
            InputBindings.Add(new KeyBinding { Key = Key.X, Modifiers = ModifierKeys.Control, Command = new RelayCommand(CutSelection) });
            InputBindings.Add(new KeyBinding { Key = Key.V, Modifiers = ModifierKeys.Control, Command = new RelayCommand(PasteSelection) });
            InputBindings.Add(new KeyBinding { Key = Key.Z, Modifiers = ModifierKeys.Control, Command = new RelayCommand(UndoFlow) });
            InputBindings.Add(new KeyBinding { Key = Key.Y, Modifiers = ModifierKeys.Control, Command = new RelayCommand(RedoFlow) });

            // 初始是折叠的（标准表格视图），真正切到流程图时再建节点，避免启动时白做一遍布局
            IsVisibleChanged += UserControlFlow_IsVisibleChanged;
        }

        #region 生命周期

        private void UserControlFlow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                RefreshView();
            }
            else
            {
                // 切回表格视图时把环状菜单收起来，避免它留在界面上
                CloseCanvasRing();
            }
        }

        private void EnsureLoaded()
        {
            if (_loadedOnce) return;
            _loadedOnce = true;

            HookSteps();
            _vm.PropertyChanged += Vm_PropertyChanged;
            _vm.SelectedSteps.CollectionChanged += SelectedSteps_CollectionChanged;
            RebuildAll();
        }

        /// <summary>
        /// 保存工程前调用：把当前每个节点的实际位置写进进程内的布局表，
        /// 这样文件里存下来的就是眼睛看到的版面（打开时原样恢复）。
        /// </summary>
        public void CommitLayoutToStore()
        {
            if (!_loadedOnce || _nodes.Count == 0) return;

            foreach (var pair in _nodes)
            {
                if (!TryGetNodeRect(pair.Value, out var rect)) continue;
                FlowLayoutStore.Set(pair.Key, new Point(Math.Max(0, rect.Left), Math.Max(0, rect.Top)));
            }
        }

        /// <summary>切到流程图视图时重新摆放 / 重画连线，并把缩放回到 100%（保证文字清晰）</summary>
        public void RefreshView()
        {
            EnsureLoaded();

            UpdateCanvasSize();
            RebuildEdges();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // 每次切过来都按当前可见宽度重排「没被手动摆过」的节点，保证 S 型排布贴着窗口边缘
                RelayoutAutoPositions();
                SetZoom(1.0);
                UpdateBackdrop();
                RefreshAllPreviews();
                SyncSelectionFromViewModel();
            }), DispatcherPriority.Background);
        }

        /// <summary>首次真正显示后，用当前可见宽度重排还没被手动摆过的节点</summary>
        private void RelayoutAutoPositions()
        {
            var steps = _vm.AutomationStepBases;
            if (steps == null || steps.Count == 0) return;

            UpdateLayoutMetrics();
            var points = BuildLayoutPoints(ComputeColumns());
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].FlowPinned) continue;
                if (FlowLayoutStore.TryGet(steps[i].Uid, out _)) continue;
                if (!_nodes.TryGetValue(steps[i].Uid, out var node)) continue;
                if (i >= points.Count) continue;

                Canvas.SetLeft(node, points[i].X);
                Canvas.SetTop(node, points[i].Y);
            }

            UpdateCanvasSize();
            CanvasHost.UpdateLayout();
            RebuildEdges();
        }

        /// <summary>节点刚建出来时模板还没应用，等一轮布局再填摘要/缩略图</summary>
        private void RefreshAllPreviews()
        {
            if (_vm.AutomationStepBases == null) return;
            foreach (var step in _vm.AutomationStepBases)
                UpdateNodePreview(step);
        }

        private void HookSteps()
        {
            var collection = _vm.AutomationStepBases;
            if (!ReferenceEquals(collection, _hookedCollection))
            {
                if (_hookedCollection != null)
                    _hookedCollection.CollectionChanged -= Steps_CollectionChanged;
                _hookedCollection = collection;
                if (_hookedCollection != null)
                    _hookedCollection.CollectionChanged += Steps_CollectionChanged;
            }
        }

        private void UnhookSteps()
        {
            foreach (var step in _hookedSteps)
                step.PropertyChanged -= Step_PropertyChanged;
            _hookedSteps.Clear();
            if (_hookedCollection != null)
            {
                _hookedCollection.CollectionChanged -= Steps_CollectionChanged;
                _hookedCollection = null;
            }
        }

        private void Steps_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_rebuilding) return;

            int insertedIndex = e.Action == NotifyCollectionChangedAction.Add ? e.NewStartingIndex : -1;
            int insertedCount = e.NewItems?.Count ?? 0;

            RebuildAll();

            // 新增步骤：把原本就在这个位置的节点往后挤一格，新步骤占进空出来的格子
            if (insertedIndex >= 0 && insertedCount > 0)
                ShiftAfterInsert(insertedIndex, insertedCount);
        }

        private void Vm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StepsViewModel.SelectedStep))
                HighlightSelection();
        }

        private void Step_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_rebuilding) return;

            // 只有跳转目标会影响连线；其余属性由节点模板的数据绑定自行刷新
            if (e.PropertyName == nameof(AutomationStepBase.TrueGotoUid)
                || e.PropertyName == nameof(AutomationStepBase.FalseGotoUid))
            {
                RebuildEdges();
            }

            if (sender is AutomationStepBase step && SummaryProperties.Contains(e.PropertyName))
                UpdateNodePreview(step);
        }

        #endregion

        #region 步骤布局（S 型 + 断连换行）

        /// <summary>按设置更新布局间距（自适应 / 自定义）</summary>
        private void UpdateLayoutMetrics()
        {
            var settings = SingletonLocator.Settings.Step;
            if (settings.FlowSpacingMode == 1)
            {
                double gap = Math.Min(200, Math.Max(20, settings.FlowCustomSpacing));
                _gapX = gap;
                _gapY = gap;
            }
            else
            {
                _gapX = 40;
                _gapY = 46;
            }

            // 曲线模式的弧线会甩到节点外侧：自动整理时整体往里挪一些，留出弧线的空间，免得被画布边缘裁掉
            if (settings.FlowEdgeStyle == 1)
            {
                X0 = 90;
                Y0 = 120;
            }
            else
            {
                X0 = 40;
                Y0 = 36;
            }
        }

        /// <summary>按当前可见宽度算一行放几个节点（还没布局出来时按 1000 估算）</summary>
        private int ComputeColumns()
        {
            double viewWidth = Scroll != null ? Scroll.ViewportWidth : 0;
            if (viewWidth <= 200)
                viewWidth = 1000;

            double usable = viewWidth - X0 - 24;
            int columns = (int)Math.Floor((usable + _gapX) / CellW);
            return Math.Max(MinColumns, columns);
        }

        /// <summary>
        /// 返回各步骤所在格子的坐标（按下标对应步骤）。
        /// 相邻两步之间没有顺序连线时（前一步的真、假都跳走了）另起一行。
        /// </summary>
        private List<Point> BuildLayoutPoints(int columns)
        {
            var steps = _vm.AutomationStepBases;

            // 竖直排列：所有节点居中排成一列、自上而下紧密排列
            if (IsVerticalArrange)
                return BuildVerticalLayoutPoints(steps);

            var points = new List<Point>();

            int row = 0;
            int posInRow = 0;
            bool forward = true;
            bool rowEmpty = true;

            Point CurrentCell() => new Point(
                X0 + (forward ? posInRow : Math.Max(0, columns - 1 - posInRow)) * CellW,
                Y0 + row * CellH);

            void Advance()
            {
                rowEmpty = false;
                posInRow++;
                if (posInRow >= columns)
                {
                    row++;
                    posInRow = 0;
                    forward = !forward;
                    rowEmpty = true;
                }
            }

            void ForceNewRow()
            {
                if (rowEmpty) return;
                row++;
                posInRow = 0;
                forward = !forward;
                rowEmpty = true;
            }

            if (steps != null)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    if (i > 0 && !IsStepsConnected(steps[i - 1], steps[i]))
                        ForceNewRow();

                    points.Add(CurrentCell());
                    Advance();
                }
            }

            return points;
        }

        /// <summary>
        /// 竖直排列：所有节点在同一列、水平居中，自上而下按紧密间距排列。
        /// 该排列不画顺序连线，真/假连线走右侧、引用连线走左侧。
        /// </summary>
        private List<Point> BuildVerticalLayoutPoints(IList<AutomationStepBase> steps)
        {
            var points = new List<Point>();

            // 居中的基准：优先按可见宽度算，还没量出宽度时按 1000 估算
            double viewWidth = Scroll != null && Scroll.ActualWidth > 0 ? Scroll.ActualWidth : 1000;
            double centerX = Math.Max(X0, (viewWidth - NodeWidth) / 2);
            double y = Y0;

            if (steps != null)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    points.Add(new Point(centerX, y));
                    y += NodeHeight + VerticalTightGap;
                }
            }

            return points;
        }

        /// <summary>前一步执行完是否可能顺序落到后一步（有任一分支没设跳转，或直接跳向它）</summary>
        private static bool IsStepsConnected(AutomationStepBase previous, AutomationStepBase current)
        {
            if (previous == null || current == null) return true;

            bool hasTrue = previous.TrueGotoUid.HasValue && previous.TrueGotoUid.Value != Guid.Empty;
            bool hasFalse = previous.FalseGotoUid.HasValue && previous.FalseGotoUid.Value != Guid.Empty;

            if (hasTrue && previous.TrueGotoUid.Value == current.Uid) return true;
            if (hasFalse && previous.FalseGotoUid.Value == current.Uid) return true;
            if (!hasTrue || !hasFalse) return true;
            return false;
        }

        /// <summary>新增步骤后，把原来就在这个位置的手动摆过的节点整体往后挪一格</summary>
        private void ShiftAfterInsert(int insertIndex, int count)
        {
            var steps = _vm.AutomationStepBases;
            if (steps == null || steps.Count == 0) return;

            UpdateLayoutMetrics();
            var points = BuildLayoutPoints(ComputeColumns());
            bool moved = false;

            for (int i = insertIndex + count; i < steps.Count; i++)
            {
                var step = steps[i];
                if (!FlowLayoutStore.TryGet(step.Uid, out var position)) continue;
                if (!_nodes.TryGetValue(step.Uid, out var node)) continue;

                int oldIndex = i - count;
                if (oldIndex >= points.Count || i >= points.Count) continue;

                var delta = new Vector(points[i].X - points[oldIndex].X, points[i].Y - points[oldIndex].Y);
                var shifted = new Point(Math.Max(0, position.X + delta.X), Math.Max(0, position.Y + delta.Y));
                FlowLayoutStore.Set(step.Uid, shifted);
                Canvas.SetLeft(node, shifted.X);
                Canvas.SetTop(node, shifted.Y);
                moved = true;
            }

            if (moved)
            {
                UpdateCanvasSize();
                CanvasHost.UpdateLayout();
                RebuildEdges();
            }
        }

        #endregion

        #region 节点 / 标志构建

        private void RebuildAll()
        {
            if (!_loadedOnce) return;

            _rebuilding = true;
            try
            {
                UnhookSteps();
                _dragNode = null;
                _dragging = false;
                ClearLinkVisual();

                NodeLayer.Children.Clear();
            UpdateLayoutMetrics();
                EdgeLayer.Children.Clear();
                HandleLayer.Children.Clear();
                OverlayLayer.Children.Clear();
                _nodes.Clear();
                _dropTarget = null;

                HookSteps();

                var steps = _vm.AutomationStepBases;
            UpdateLayoutMetrics();
                var points = BuildLayoutPoints(ComputeColumns());

                if (steps != null)
                {
                    for (int i = 0; i < steps.Count; i++)
                    {
                        var step = steps[i];
                        var node = BuildNode(step);
                        var fallback = i < points.Count ? points[i] : new Point(X0, Y0);
                        var position = FlowLayoutStore.TryGet(step.Uid, out var stored) ? stored : fallback;
                        Canvas.SetLeft(node, position.X);
                        Canvas.SetTop(node, position.Y);
                        NodeLayer.Children.Add(node);
                        _nodes[step.Uid] = node;

                        step.PropertyChanged += Step_PropertyChanged;
                        _hookedSteps.Add(step);
                    }
                }

            }
            finally
            {
                _rebuilding = false;
            }

            // 连线和选中框必须在 _rebuilding 复位之后再画，否则 RebuildEdges 会提前返回、连线被清空
            UpdateCanvasSize();
            RebuildEdges();
            UpdateEmptyHint();

            if (EdgeStyleBox != null)
            {
                _edgeStyleSyncing = true;
                try { EdgeStyleBox.SelectedIndex = SingletonLocator.Settings.Step.FlowEdgeStyle; } catch { }
                finally { _edgeStyleSyncing = false; }
            }

            if (ArrangeModeBox != null)
            {
                _arrangeModeSyncing = true;
                try { ArrangeModeBox.SelectedIndex = IsVerticalArrange ? 1 : 0; } catch { }
                finally { _arrangeModeSyncing = false; }
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var alive = new HashSet<Guid>(_nodes.Keys);
                _selectedUids.RemoveWhere(uid => !alive.Contains(uid));
                RefreshAllPreviews();
                HighlightSelection();
            }), DispatcherPriority.Loaded);
        }

        private ContentControl BuildNode(AutomationStepBase step)
        {
            var node = new ContentControl
            {
                Content = step,
                // DataContext 必须显式指定：内容模板拿得到步骤，但 ContentControl 自身默认继承的是界面层的
                // StepsViewModel，拖动/命中测试都靠它取回步骤
                DataContext = step,
                ContentTemplate = (DataTemplate)FindResource("FlowNodeTemplate"),
                Focusable = false,
                Cursor = Cursors.SizeAll,
            };
            node.MouseLeftButtonDown += Node_MouseLeftButtonDown;
            node.MouseMove += Node_MouseMove;
            node.MouseLeftButtonUp += Node_MouseLeftButtonUp;
            node.MouseRightButtonDown += Node_MouseRightButtonDown;
            node.MouseDoubleClick += Node_MouseDoubleClick;
            return node;
        }

        private void UpdateEmptyHint()
        {
            EmptyHint.Visibility = _nodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }


        #endregion

        #region 画布尺寸与缩放

        private void UpdateCanvasSize()
        {
            double maxX = 0;
            double maxY = 0;
            foreach (var pair in _nodes)
            {
                if (!TryGetNodeRect(pair.Value, out var rect)) continue;
                maxX = Math.Max(maxX, rect.Right);
                maxY = Math.Max(maxY, rect.Bottom);
            }

            // 用 Scroll 自身的 ActualWidth/Height 兜底（ViewportWidth 会随滚动条出现变化，容易来回跳）
            double minWidth = Scroll != null && Scroll.ActualWidth > 0 ? Scroll.ActualWidth : 900;
            double minHeight = Scroll != null && Scroll.ActualHeight > 0 ? Scroll.ActualHeight : 600;

            CanvasHost.Width = Math.Max(minWidth, maxX + 200);
            CanvasHost.Height = Math.Max(minHeight, maxY + 180);
        }

        private void SetZoom(double zoom)
        {
            zoom = Math.Min(ZoomMax, Math.Max(ZoomMin, zoom));
            ZoomTransform.ScaleX = zoom;
            ZoomTransform.ScaleY = zoom;
            ZoomLabel.Text = string.Format("{0:F0}%", zoom * 100);
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(ZoomTransform.ScaleX * 1.15);

        private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(ZoomTransform.ScaleX / 1.15);

        private void FitToWindow_Click(object sender, RoutedEventArgs e) => FitToWindow();

        /// <summary>把整幅流程缩放到可见区域里（文字会变模糊，平时保持 100%）</summary>
        private void FitToWindow()
        {
            if (CanvasHost.Width <= 0 || CanvasHost.Height <= 0) return;

            double viewWidth = Scroll.ViewportWidth - 16;
            double viewHeight = Scroll.ViewportHeight - 16;
            if (viewWidth <= 0 || viewHeight <= 0) return;

            SetZoom(Math.Min(viewWidth / CanvasHost.Width, viewHeight / CanvasHost.Height));
        }

        #region 窗口内全屏

        private bool _isFullscreen;

        /// <summary>全屏状态变化：宿主（UserControlSteps）据此让出编辑区、把详情面板改成悬浮</summary>
        public event EventHandler FullscreenChanged;

        /// <summary>当前是否处于窗口内全屏</summary>
        public bool IsFullscreen => _isFullscreen;

        /// <summary>设置全屏状态（只改界面与画布尺寸，不动步骤数据）</summary>
        public void SetFullscreen(bool fullscreen)
        {
            if (FullscreenToggle == null) return;
            if (FullscreenToggle.IsChecked != fullscreen)
                FullscreenToggle.IsChecked = fullscreen;
            ApplyFullscreenState(fullscreen);
        }

        /// <summary>退出全屏（外部切回表格视图时调用）</summary>
        public void ExitFullscreen() => SetFullscreen(false);

        // 点击时 IsChecked 已经翻过来了，这里只看新状态，不做「值相同就返回」的判断
        private void FullscreenToggle_Click(object sender, RoutedEventArgs e)
            => ApplyFullscreenState(FullscreenToggle.IsChecked == true);

        private void ApplyFullscreenState(bool fullscreen)
        {
            if (_isFullscreen == fullscreen) return;
            _isFullscreen = fullscreen;

            UpdateFullscreenToolTip();
            FullscreenChanged?.Invoke(this, EventArgs.Empty);

            // 可见区域变了：等布局跑完再重算画布下限并重画连线
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateCanvasSize();
                CanvasHost.UpdateLayout();
                RebuildEdges();
            }), DispatcherPriority.Background);
        }

        private void UpdateFullscreenToolTip()
        {
            bool fullscreen = _isFullscreen;
            FullscreenToggle.ToolTip = LanguageService.GetLocalizedString(
                fullscreen ? "FlowChart_ExitFullscreen_ToolTip" : "FlowChart_Fullscreen_ToolTip",
                fullscreen ? "退出全屏" : "全屏");
        }

        #endregion

        /// <summary>按设置重建画布背景：点 / 线 / 虚线，小格 / 大格</summary>
        private void UpdateBackdrop()
        {
            if (BackdropRect == null) return;

            var settings = SingletonLocator.Settings.Step;
            double cell = settings.FlowGridSize == 0 ? 20 : 40;

            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, cell, cell))));

            if (settings.FlowBackgroundStyle == 0)
            {
                group.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(Color.FromRgb(0xDC, 0xE3, 0xEA)), null,
                    new EllipseGeometry(new Point(1, 1), 1, 1)));
            }
            else
            {
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xE1, 0xE6, 0xEC)), 1);
                if (settings.FlowBackgroundStyle == 2)
                    pen.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);

                var lines = new GeometryGroup();
                lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(cell, 0)));
                lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, cell)));
                group.Children.Add(new GeometryDrawing(null, pen, lines));
            }

            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, cell, cell),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None,
            };
            brush.Freeze();

            BackdropRect.Fill = brush;
        }

        private void Scroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            SetZoom(ZoomTransform.ScaleX * (e.Delta > 0 ? 1.1 : 1 / 1.1));
            e.Handled = true;
        }

        private void AutoLayout_Click(object sender, RoutedEventArgs e) => ArrangeAll(pushUndo: true);

        /// <summary>工具栏切换排列方式：存进设置后按新模式重排一遍</summary>
        private void ArrangeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ArrangeModeBox == null || _arrangeModeSyncing || ArrangeModeBox.SelectedIndex < 0) return;
            try { SingletonLocator.Settings.Step.FlowArrangeMode = ArrangeModeBox.SelectedIndex; } catch { }
            if (!_loadedOnce) return;

            // 换排列方式也会改出口位置（引用出口左右挪），先重建节点再整理一次
            RebuildAll();
            ArrangeAll(pushUndo: true);
        }

        /// <summary>按当前排列方式重摆所有没被锚定的节点（横向 S 型 / 竖直居中一列）</summary>
        private void ArrangeAll(bool pushUndo)
        {
            var steps = _vm.AutomationStepBases;
            if (steps == null) return;

            UpdateLayoutMetrics();
            var points = BuildLayoutPoints(ComputeColumns());
            var before = pushUndo ? CapturePositions() : null;

            for (int i = 0; i < steps.Count; i++)
            {
                if (i >= points.Count) break;
                if (steps[i].FlowPinned) continue;   // 锚定的节点保持原位
                var position = points[i];
                FlowLayoutStore.Set(steps[i].Uid, position);
                if (_nodes.TryGetValue(steps[i].Uid, out var node))
                {
                    Canvas.SetLeft(node, position.X);
                    Canvas.SetTop(node, position.Y);
                }
            }

            if (pushUndo)
            {
                var after = CapturePositions();
                PushFlowUndo(() => RestorePositions(before), () => RestorePositions(after));
            }

            // 先让节点位置真正生效（走完一次布局），再按新位置重新走线
            UpdateCanvasSize();
            CanvasHost.UpdateLayout();
            RebuildEdges();
            HighlightSelection();
        }

        #endregion

        #region 节点拖动与选中

        private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ContentControl node || StepOf(node) is not AutomationStepBase step) return;
            if (_linkSource != null) return;

            Focus();   // 让快捷键（Ctrl+A/C/X/V、Del）落在本视图上

            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                ToggleSelection(step);
                e.Handled = true;
                return;   // Ctrl+点击只切换选中，不开始拖动
            }

            var selected = GetSelectedSteps();
            if (selected.Count <= 1 || !selected.Any(s => s.Uid == step.Uid))
                ApplySelection(new[] { step }, step);

            _dragNode = node;
            _dragStartPoint = e.GetPosition(CanvasHost);
            var left = Canvas.GetLeft(node);
            var top = Canvas.GetTop(node);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            // 记录拖动前的位置，用于「改变步骤位置」的撤销
            _dragUndoStep = step;
            _dragUndoOldHadStore = FlowLayoutStore.TryGet(step.Uid, out var storedPos);
            _dragUndoOldPos = _dragUndoOldHadStore ? storedPos : new Point(left, top);

            _dragOffset = new Vector(left - _dragStartPoint.X, top - _dragStartPoint.Y);
            _dragging = false;

            // 多选时整组一起移动：记下每个选中节点的起始位置
            _multiDragOrigins.Clear();
            foreach (var selectedStep in GetSelectedSteps())
            {
                if (!_nodes.TryGetValue(selectedStep.Uid, out var selectedNode)) continue;
                double left0 = Canvas.GetLeft(selectedNode);
                double top0 = Canvas.GetTop(selectedNode);
                if (double.IsNaN(left0)) left0 = 0;
                if (double.IsNaN(top0)) top0 = 0;
                _multiDragOrigins[selectedStep.Uid] = new Point(left0, top0);
            }
            ResetDragReplace();
            node.CaptureMouse();
            e.Handled = true;
        }

        private void Node_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragNode == null || e.LeftButton != MouseButtonState.Pressed) return;

            var point = e.GetPosition(CanvasHost);
            if (!_dragging)
            {
                if ((point - _dragStartPoint).Length < 3) return;
                _dragging = true;
            }

            double rawX = Math.Max(0, point.X + _dragOffset.X);
            double rawY = Math.Max(0, point.Y + _dragOffset.Y);
            bool snapToGrid = SingletonLocator.Settings.Step.FlowSnapToGrid;
            double x = snapToGrid ? Snap(rawX) : Math.Round(rawX);
            double y = snapToGrid ? Snap(rawY) : Math.Round(rawY);
            Canvas.SetLeft(_dragNode, x);
            Canvas.SetTop(_dragNode, y);

            var draggedStep = StepOf(_dragNode);
            if (draggedStep != null)
                FlowLayoutStore.Set(draggedStep.Uid, new Point(x, y));

            // 其余选中的节点跟着整体平移
            if (_multiDragOrigins.Count > 1 && draggedStep != null)
            {
                var anchorOrigin = _multiDragOrigins.TryGetValue(draggedStep.Uid, out var origin) ? origin : new Point(x, y);
                var delta = new Vector(x - anchorOrigin.X, y - anchorOrigin.Y);
                foreach (var pair in _multiDragOrigins)
                {
                    if (pair.Key == draggedStep.Uid) continue;
                    if (!_nodes.TryGetValue(pair.Key, out var otherNode)) continue;
                    var moved = new Point(Math.Max(0, pair.Value.X + delta.X), Math.Max(0, pair.Value.Y + delta.Y));
                    Canvas.SetLeft(otherNode, moved.X);
                    Canvas.SetTop(otherNode, moved.Y);
                    FlowLayoutStore.Set(pair.Key, moved);
                }
            }

            UpdateCanvasSize();
            RebuildEdges();
            UpdateDragReplaceCandidate(x, y);
        }

        private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragNode == null) return;

            var dragged = StepOf(_dragNode);
            var target = _replaceCandidate;
            bool armed = _replaceArmed;
            bool moved = _dragging;

            var undoStep = _dragUndoStep;
            var oldPos = _dragUndoOldPos;
            var oldHadStore = _dragUndoOldHadStore;
            _dragUndoStep = null;

            double newLeft = Canvas.GetLeft(_dragNode);
            double newTop = Canvas.GetTop(_dragNode);

            ResetDragReplace();
            _dragNode.ReleaseMouseCapture();
            _dragNode = null;
            _dragging = false;

            // 悬停满 0.3 秒只是「待替换」，松开左键才真正替换
            if (armed && dragged != null && target != null && !ReferenceEquals(dragged, target))
            {
                ExecuteDragReplace(dragged, target);
                return;
            }

            if (!moved || undoStep == null) return;

            var newPos = new Point(double.IsNaN(newLeft) ? 0 : newLeft, double.IsNaN(newTop) ? 0 : newTop);
            if (Math.Abs(newPos.X - oldPos.X) < 0.5 && Math.Abs(newPos.Y - oldPos.Y) < 0.5
                && _multiDragOrigins.Count <= 1)
                return;

            // 多选拖动：整组位置作为一条撤销记录一起恢复
            if (_multiDragOrigins.Count > 1)
            {
                var before = new List<(Guid uid, bool hadStore, Point pos)>();
                foreach (var pair in _multiDragOrigins)
                    before.Add((pair.Key, true, pair.Value));

                var after = new List<(Guid uid, bool hadStore, Point pos)>();
                foreach (var pair in _multiDragOrigins)
                {
                    if (_nodes.TryGetValue(pair.Key, out var node))
                    {
                        double left1 = Canvas.GetLeft(node);
                        double top1 = Canvas.GetTop(node);
                        after.Add((pair.Key, true, new Point(double.IsNaN(left1) ? 0 : left1, double.IsNaN(top1) ? 0 : top1)));
                    }
                    else
                    {
                        after.Add((pair.Key, true, pair.Value));
                    }
                }

                _multiDragOrigins.Clear();
                PushFlowUndo(() => RestorePositions(before), () => RestorePositions(after));
                return;
            }

            _multiDragOrigins.Clear();

            Guid undoUid = undoStep.Uid;
            PushFlowUndo(
                () => RestoreNodePosition(undoUid, oldPos, oldHadStore),
                () => RestoreNodePosition(undoUid, newPos, true));
        }

        private static double Snap(double value) => Math.Round(value / GridStep) * GridStep;

        /// <summary>锚点吸附：设置里关掉就按鼠标原位置放</summary>
        private static Point SnapAnchor(Point point)
        {
            bool snap = true;
            try { snap = SingletonLocator.Settings.Step.FlowSnapAnchorsToGrid; } catch { }
            return snap ? new Point(Snap(point.X), Snap(point.Y)) : point;
        }

        #region 拖动悬停替换（悬停 0.3 秒后松开左键，用拖动的步骤顶替悬停的步骤）

        private void UpdateDragReplaceCandidate(double x, double y)
        {
            if (_dragNode == null) return;

            var center = new Point(x + NodeWidth / 2, y + NodeHeight / 2);
            ContentControl bestNode = null;
            AutomationStepBase bestStep = null;
            double bestDistance = double.MaxValue;

            foreach (var pair in _nodes)
            {
                if (ReferenceEquals(pair.Value, _dragNode)) continue;
                if (!TryGetNodeRect(pair.Value, out var rect)) continue;

                var hitArea = new Rect(rect.Left - 8, rect.Top - 8, rect.Width + 16, rect.Height + 16);
                if (!hitArea.Contains(center)) continue;

                double dx = center.X - (rect.Left + rect.Width / 2);
                double dy = center.Y - (rect.Top + rect.Height / 2);
                double distance = dx * dx + dy * dy;
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                bestNode = pair.Value;
                bestStep = StepOf(pair.Value);
            }

            if (ReferenceEquals(bestStep, _replaceCandidate)) return;

            _replaceTimer.Stop();
            SetReplaceHint(_replaceCandidateNode, false);
            _replaceCandidate = bestStep;
            _replaceCandidateNode = bestNode;
            _replaceArmed = false;

            if (bestStep == null) return;

            SetReplaceHint(bestNode, true);
            _replaceTimer.Start();
        }

        /// <summary>悬停满 0.3 秒：标记为「松手即替换」，并把高亮加粗做提示</summary>
        private void ArmDragReplace()
        {
            _replaceTimer.Stop();
            if (_dragNode == null || _replaceCandidate == null) return;

            _replaceArmed = true;
            SetReplaceHint(_replaceCandidateNode, true);
        }

        private void SetReplaceHint(ContentControl node, bool on)
        {
            if (node == null) return;
            var ring = FindChild<Border>(node, "DropRing");
            if (ring == null) return;

            ring.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            ring.BorderThickness = new Thickness(on && _replaceArmed ? 3.5 : 2);
        }

        private void ResetDragReplace()
        {
            _replaceTimer?.Stop();
            _replaceArmed = false;
            SetReplaceHint(_replaceCandidateNode, false);
            _replaceCandidate = null;
            _replaceCandidateNode = null;
        }

        private void ExecuteDragReplace(AutomationStepBase dragged, AutomationStepBase target)
        {
            // 让被拖动的步骤落到目标格子里，而不是保留拖动时的自由位置
            FlowLayoutStore.Remove(dragged.Uid);
            FlowLayoutStore.Remove(target.Uid);

            if (!_vm.ReplaceStepAt(target, dragged)) return;

            SelectStep(dragged);   // 替换后 dragged 已接管 target 的 Uid
        }

        #endregion

        #region 多选 / 框选

        private void SelectStep(AutomationStepBase step)
        {
            if (step == null) return;
            ApplySelection(new[] { step }, step);
        }

        private List<AutomationStepBase> GetSelectedSteps()
        {
            var steps = _vm.SelectedSteps?.Where(s => s != null).ToList() ?? new List<AutomationStepBase>();
            if (steps.Count == 0 && _vm.SelectedStep != null)
                steps.Add(_vm.SelectedStep);
            return steps;
        }

        private void ApplySelection(IReadOnlyList<AutomationStepBase> steps, AutomationStepBase primary)
        {
            var list = steps?.Where(s => s != null).ToList() ?? new List<AutomationStepBase>();

            _syncingSelection = true;
            try
            {
                // 先设主选中：表格的 SelectedItem 绑定会触发它的 SelectionChanged，
                // 那边会用表格选中项回写 SelectedSteps，所以多选集合必须在它之后再写。
                _vm.SelectedStep = primary ?? list.FirstOrDefault();

                if (_vm.SelectedSteps != null)
                {
                    _vm.SelectedSteps.Clear();
                    foreach (var step in list)
                        _vm.SelectedSteps.Add(step);
                }
            }
            finally
            {
                _syncingSelection = false;
            }

            _selectedUids.Clear();
            foreach (var step in list)
                _selectedUids.Add(step.Uid);

            HighlightSelection();
        }

        private void ToggleSelection(AutomationStepBase step)
        {
            if (step == null) return;

            var list = GetSelectedSteps();
            if (list.Any(s => s.Uid == step.Uid))
                list.RemoveAll(s => s.Uid == step.Uid);
            else
                list.Add(step);

            ApplySelection(list, step);
        }

        private void SelectAllSteps()
        {
            var steps = _vm.AutomationStepBases;
            if (steps == null || steps.Count == 0) return;
            var all = steps.ToList();
            ApplySelection(all, _vm.SelectedStep != null && all.Contains(_vm.SelectedStep) ? _vm.SelectedStep : all[0]);
        }

        /// <summary>表格视图里的选中变化同步到流程图的选中框</summary>
        private void SelectedSteps_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_syncingSelection || _rebuilding) return;

            _selectedUids.Clear();
            if (_vm.SelectedSteps != null)
            {
                foreach (var step in _vm.SelectedSteps)
                {
                    if (step != null) _selectedUids.Add(step.Uid);
                }
            }
            HighlightSelection();
        }

        private void SyncSelectionFromViewModel()
        {
            _selectedUids.Clear();
            if (_vm.SelectedSteps != null)
            {
                foreach (var step in _vm.SelectedSteps)
                {
                    if (step != null) _selectedUids.Add(step.Uid);
                }
            }
            if (_selectedUids.Count == 0 && _vm.SelectedStep != null)
                _selectedUids.Add(_vm.SelectedStep.Uid);

            HighlightSelection();
        }

        private void HighlightSelection()
        {
            foreach (var pair in _nodes)
            {
                var ring = FindChild<Border>(pair.Value, "SelectionRing");
                if (ring == null) continue;
                ring.Visibility = _selectedUids.Contains(pair.Key) ? Visibility.Visible : Visibility.Collapsed;
            }

            // 有连线类型被设成「只显示选中步骤的」时，选中变化要重画连线
            if (AnyEdgeFilterActive)
                RebuildEdges();
        }

        #endregion

        #region 画布框选

        private void NodeLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_linkSource != null) return;
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.OriginalSource is not Canvas) return;   // 只有点在空白画布上才起框

            Focus();
            _banding = true;
            _bandStart = e.GetPosition(CanvasHost);
            _bandAdditive = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            if (_bandRect == null)
            {
                _bandRect = new Rectangle
                {
                    Stroke = LinkBrush,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Fill = new SolidColorBrush(Color.FromArgb(32, 43, 111, 214)),
                };
            }

            if (!OverlayLayer.Children.Contains(_bandRect))
                OverlayLayer.Children.Add(_bandRect);

            Canvas.SetLeft(_bandRect, _bandStart.X);
            Canvas.SetTop(_bandRect, _bandStart.Y);
            _bandRect.Width = 0;
            _bandRect.Height = 0;

            NodeLayer.CaptureMouse();
            e.Handled = true;
        }

        private void NodeLayer_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_banding || _bandRect == null) return;

            var point = e.GetPosition(CanvasHost);
            double x = Math.Min(point.X, _bandStart.X);
            double y = Math.Min(point.Y, _bandStart.Y);
            Canvas.SetLeft(_bandRect, x);
            Canvas.SetTop(_bandRect, y);
            _bandRect.Width = Math.Abs(point.X - _bandStart.X);
            _bandRect.Height = Math.Abs(point.Y - _bandStart.Y);
            e.Handled = true;
        }

        private void NodeLayer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_banding) return;

            _banding = false;
            NodeLayer.ReleaseMouseCapture();

            Rect band = Rect.Empty;
            if (_bandRect != null)
            {
                double left = Canvas.GetLeft(_bandRect);
                double top = Canvas.GetTop(_bandRect);
                if (!double.IsNaN(left) && !double.IsNaN(top))
                    band = new Rect(left, top, _bandRect.Width, _bandRect.Height);

                if (OverlayLayer.Children.Contains(_bandRect))
                    OverlayLayer.Children.Remove(_bandRect);
            }

            e.Handled = true;

            // 只是点了一下空白：清空选中
            if (band.Width < 4 && band.Height < 4)
            {
                if (!_bandAdditive)
                    ApplySelection(Array.Empty<AutomationStepBase>(), null);
                return;
            }

            var hits = new List<AutomationStepBase>();
            foreach (var pair in _nodes)
            {
                if (!TryGetNodeRect(pair.Value, out var rect)) continue;
                if (!rect.IntersectsWith(band)) continue;
                if (StepOf(pair.Value) is AutomationStepBase step)
                    hits.Add(step);
            }

            if (_bandAdditive)
            {
                var merged = GetSelectedSteps();
                foreach (var step in hits)
                {
                    if (!merged.Any(s => s.Uid == step.Uid))
                        merged.Add(step);
                }
                ApplySelection(merged, hits.FirstOrDefault() ?? _vm.SelectedStep);
            }
            else
            {
                ApplySelection(hits, hits.Count > 0 ? hits[0] : null);
            }
        }

        /// <summary>空白画布右键：按住 0.3 秒才弹出环状菜单，没按住就松开什么都不出</summary>
        private void NodeLayer_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_linkSource != null) return;
            if (e.OriginalSource is not Canvas) return;

            var point = e.GetPosition(CanvasHost);
            var nearest = FindNearestStep(point, out var nearestRect);

            // 新增步骤是插到选中步骤之后：点在最近节点左半边时，选它前一个，等价于插在它前面
            AutomationStepBase anchor = nearest;
            var steps = _vm.AutomationStepBases;
            if (nearest != null && steps != null)
            {
                int index = steps.IndexOf(nearest);
                if (index >= 0 && point.X < nearestRect.Left + nearestRect.Width / 2)
                    anchor = index > 0 ? steps[index - 1] : null;
            }

            ApplySelection(anchor == null ? Array.Empty<AutomationStepBase>() : new[] { anchor }, anchor);

            // 先记下位置，按住 0.3 秒才真的弹菜单
            _ringHoldPoint = e.GetPosition((IInputElement)RingLayer.Parent);
            _ringHoldPending = true;
            _ringHoldTimer.Stop();
            _ringHoldTimer.Start();
            e.Handled = true;
        }

        private void RingHoldTimer_Tick(object sender, EventArgs e)
        {
            _ringHoldTimer.Stop();
            if (!_ringHoldPending) return;
            _ringHoldPending = false;
            ShowCanvasRing(_ringHoldPoint);
        }

        private void RootGrid_MouseRightButtonUp(object sender, RoutedEventArgs e)
        {
            // 松开右键：还没到时间就什么都不出；已经弹出来了也一起收起
            _ringHoldTimer.Stop();
            _ringHoldPending = false;
            if (RingLayer.Visibility == Visibility.Visible)
            {
                CloseCanvasRing();
                e.Handled = true;
            }
        }

        #region 环状一级菜单

        private const double RingInnerRadius = 42;
        private const double RingOuterRadius = 104;
        private Point _ringHoldPoint;
        private bool _ringHoldPending;
        private readonly DispatcherTimer _ringHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };

        /// <summary>
        /// 空白处右键：以鼠标为中心画一个半透明扇形环，几个一级菜单就均分成几块。
        /// 鼠标停在某一块上即打开它的二级菜单并收起环。
        /// </summary>
        private void ShowCanvasRing(Point point)
        {
            CloseCanvasRing();

            var items = new (string Text, Action Open)[]
            {
                (LanguageService.GetLocalizedString("Add"), () => OpenRingMenu(_canvasMenu)),
                (LanguageService.GetLocalizedString("Edit"), () => OpenRingMenu(_canvasEditMenu)),
            };

            // 中心小点（提示环心就是右键位置）
            var centerDot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = LinkBrush,
                Opacity = 0.6,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(centerDot, point.X - 3);
            Canvas.SetTop(centerDot, point.Y - 3);
            RingLayer.Children.Add(centerDot);

            double sectorSpan = 360.0 / items.Length;
            const double gap = 3.0;
            double start = -90;

            for (int i = 0; i < items.Length; i++)
            {
                double from = start + i * sectorSpan + gap / 2;
                double to = start + (i + 1) * sectorSpan - gap / 2;
                double mid = (from + to) / 2;

                var sector = new Path
                {
                    Data = CreateRingSector(point, RingInnerRadius, RingOuterRadius, from, to),
                    Fill = new SolidColorBrush(Color.FromArgb(0x26, 0x2B, 0x6F, 0xD6)),
                    Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0x2B, 0x6F, 0xD6)),
                    StrokeThickness = 1.4,
                    Cursor = Cursors.Hand,
                };

                var label = new TextBlock
                {
                    Text = items[i].Text,
                    FontSize = 13,
                    Foreground = CreateFrozenBrush(0x2B, 0x6F, 0xD6),
                    IsHitTestVisible = false,
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                var anchor = PolarPoint(point, (RingInnerRadius + RingOuterRadius) / 2, mid);
                Canvas.SetLeft(label, anchor.X - label.DesiredSize.Width / 2);
                Canvas.SetTop(label, anchor.Y - label.DesiredSize.Height / 2);

                var open = items[i].Open;
                sector.MouseEnter += (_, __) =>
                {
                    sector.Fill = new SolidColorBrush(Color.FromArgb(0x59, 0x2B, 0x6F, 0xD6));
                    open();
                    CloseCanvasRing();
                };
                // 键盘/触屏：按一下就打开
                sector.MouseLeftButtonUp += (_, __) =>
                {
                    open();
                    CloseCanvasRing();
                };

                RingLayer.Children.Add(sector);
                RingLayer.Children.Add(label);
            }

            RingLayer.Visibility = Visibility.Visible;
        }

        private static Point PolarPoint(Point center, double radius, double degrees)
        {
            double rad = degrees * Math.PI / 180.0;
            return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
        }

        /// <summary>环形扇区（内外两段圆弧围成）</summary>
        private static Geometry CreateRingSector(Point center, double inner, double outer, double fromDeg, double toDeg)
        {
            var o1 = PolarPoint(center, outer, fromDeg);
            var o2 = PolarPoint(center, outer, toDeg);
            var i2 = PolarPoint(center, inner, toDeg);
            var i1 = PolarPoint(center, inner, fromDeg);
            bool largeArc = Math.Abs(toDeg - fromDeg) > 180;

            var figure = new PathFigure { StartPoint = o1, IsClosed = true, IsFilled = true };
            figure.Segments.Add(new ArcSegment { Point = o2, Size = new Size(outer, outer), SweepDirection = SweepDirection.Clockwise, IsLargeArc = largeArc });
            figure.Segments.Add(new LineSegment { Point = i2 });
            figure.Segments.Add(new ArcSegment { Point = i1, Size = new Size(inner, inner), SweepDirection = SweepDirection.Counterclockwise, IsLargeArc = largeArc });
            figure.Segments.Add(new LineSegment { Point = o1 });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();
            return geometry;
        }

        /// <summary>二级菜单（添加 / 编辑）在鼠标位置弹出</summary>
        private void OpenRingMenu(ContextMenu menu)
        {
            if (menu == null) return;

            menu.PlacementTarget = RingLayer;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private void RingLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 点空白处只收起环，不穿透到下层画布（避免误选/误拖动）
            CloseCanvasRing();
            e.Handled = true;
        }

        private void CloseCanvasRing()
        {
            RingLayer.Children.Clear();
            RingLayer.Visibility = Visibility.Collapsed;
        }

        #endregion

        private AutomationStepBase FindNearestStep(Point point, out Rect nearestRect)
        {
            nearestRect = Rect.Empty;
            AutomationStepBase nearest = null;
            double bestDistance = double.MaxValue;

            foreach (var pair in _nodes)
            {
                if (!TryGetNodeRect(pair.Value, out var rect)) continue;

                double dx = point.X - (rect.Left + rect.Width / 2);
                double dy = point.Y - (rect.Top + rect.Height / 2);
                double distance = dx * dx + dy * dy;
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                nearest = StepOf(pair.Value);
                nearestRect = rect;
            }

            return nearest;
        }

        #endregion

        #region 复制 / 剪切 / 粘贴

        private void CopySelection()
        {
            if (!SingletonLocator.Main.IsLoggedIn) return;

            var steps = GetSelectedSteps();
            if (steps.Count == 0) return;

            _clipboard.Clear();
            _clipboard.AddRange(steps);
            _hasClipboard = true;

            // 同步给表格视图的剪贴板（表格那边粘贴仍按原来的规则）
            _vm.PasteSteps.Clear();
            foreach (var step in steps)
                _vm.PasteSteps.Add(StepsViewModel.CloneStepWithNewUid(step));
            _vm.PasteStepCommand.RaiseCanExecuteChanged();
        }

        private async void CutSelection()
        {
            if (!SingletonLocator.Main.IsLoggedIn) return;

            CopySelection();
            if (!_hasClipboard) return;

            await _vm.RemoveStepsAsync(confirm: false);
        }

        private void PasteSelection()
        {
            if (!SingletonLocator.Main.IsLoggedIn) return;

            if (!_hasClipboard || _clipboard.Count == 0)
            {
                // 从表格里复制的：沿用现有的粘贴逻辑
                ((ICommand)_vm.PasteStepCommand).Execute(null);
                return;
            }

            var clones = new List<AutomationStepBase>();
            var uidMap = new Dictionary<Guid, Guid>();
            foreach (var source in _clipboard)
            {
                var clone = StepsViewModel.CloneStepWithNewUid(source);
                if (clone == null) continue;
                uidMap[source.Uid] = clone.Uid;
                clones.Add(clone);
            }

            if (clones.Count == 0) return;

            // 保持结构：块内步骤之间的引用一律改指向粘贴出来的副本
            foreach (var clone in clones)
            {
                if (clone.TrueGotoUid is Guid trueTarget && uidMap.TryGetValue(trueTarget, out var newTrue))
                    clone.TrueGotoUid = newTrue;
                if (clone.FalseGotoUid is Guid falseTarget && uidMap.TryGetValue(falseTarget, out var newFalse))
                    clone.FalseGotoUid = newFalse;

                if (clone is PopupStep popup && popup.CloseOnStepUid is Guid closeTarget
                    && uidMap.TryGetValue(closeTarget, out var newClose))
                {
                    popup.CloseOnStepUid = newClose;
                }

                if (clone is MultiThreadStartStep multiThread && multiThread.RangeEndUid is Guid rangeEnd
                    && uidMap.TryGetValue(rangeEnd, out var newRangeEnd))
                {
                    multiThread.RangeEndUid = newRangeEnd;
                }

                if (clone is BurnInConfigStep burnIn && burnIn.Config?.BurnFinishStepUid is Guid burnFinish
                    && uidMap.TryGetValue(burnFinish, out var newBurnFinish))
                {
                    burnIn.Config.BurnFinishStepUid = newBurnFinish;
                }
            }

            _vm.InsertStepsAfter(_vm.SelectedStep, clones);
            ApplySelection(clones, clones[0]);
        }

        #endregion

        #endregion

        #region 细粒度撤销 / 恢复

        /// <summary>供窗口级 Ctrl+Z 路由使用：流程图视图下统一走这里</summary>
        public bool CanUndoFromRouter() => _flowUndo.Count > 0
            || (_vm.UndoCommand != null && _vm.UndoCommand.CanExecute(null));

        public bool CanRedoFromRouter() => _flowRedo.Count > 0
            || (_vm.RedoCommand != null && _vm.RedoCommand.CanExecute(null));

        public void UndoFromRouter() => UndoFlow();

        public void RedoFromRouter() => RedoFlow();

        /// <summary>
        /// 记录一条细粒度撤销。注意条目里只保存步骤 Uid、撤销时才按 Uid 找回当前对象：
        /// 步骤列表被整体恢复（撤销/恢复增删）后所有步骤对象都会换成新的，
        /// 若捕获旧对象，之前的连线/标题修改就再也撤不了了。
        /// </summary>
        private void PushFlowUndoFor(AutomationStepBase step, Action<AutomationStepBase> undo, Action<AutomationStepBase> redo)
        {
            if (step == null) return;
            Guid uid = step.Uid;
            PushFlowUndo(
                () => { var s = FindStepByUid(uid); if (s != null) undo(s); },
                () => { var s = FindStepByUid(uid); if (s != null) redo(s); });
        }

        private AutomationStepBase FindStepByUid(Guid uid)
        {
            var steps = _vm.AutomationStepBases;
            if (steps == null) return null;
            foreach (var step in steps)
            {
                if (step.Uid == uid) return step;
            }
            return null;
        }

        private void PushFlowUndo(Action undo, Action redo)
        {
            _flowUndo.Add(new FlowUndoEntry { Undo = undo, Redo = redo, VmDepth = _vm.ListUndoCount });
            if (_flowUndo.Count > 100)
                _flowUndo.RemoveAt(0);
            _flowRedo.Clear();
        }

        /// <summary>
        /// 撤销：流程图条目与步骤列表条目共用一条时间线。
        /// 列表栈比当前条目入栈时更深，说明之后还有增删/移动/粘贴等结构改动，先撤那边。
        /// </summary>
        private void UndoFlow()
        {
            if (_flowUndo.Count > 0)
            {
                var top = _flowUndo[_flowUndo.Count - 1];
                if (_vm.ListUndoCount > top.VmDepth && _vm.UndoCommand.CanExecute(null))
                {
                    _vm.UndoCommand.Execute(null);
                    return;
                }

                _flowUndo.RemoveAt(_flowUndo.Count - 1);
                top.Undo?.Invoke();
                _flowRedo.Add(top);
                return;
            }

            if (_vm.UndoCommand.CanExecute(null))
                _vm.UndoCommand.Execute(null);
        }

        /// <summary>恢复：列表那边已撤销得比当前条目更晚时，先把列表恢复回来</summary>
        private void RedoFlow()
        {
            if (_flowRedo.Count > 0)
            {
                var top = _flowRedo[_flowRedo.Count - 1];
                if (top.VmDepth > _vm.ListUndoCount && _vm.RedoCommand.CanExecute(null))
                {
                    _vm.RedoCommand.Execute(null);
                    return;
                }

                _flowRedo.RemoveAt(_flowRedo.Count - 1);
                top.Redo?.Invoke();
                _flowUndo.Add(top);
                return;
            }

            if (_vm.RedoCommand.CanExecute(null))
                _vm.RedoCommand.Execute(null);
        }

        private void RestoreNodePosition(Guid uid, Point position, bool hadStore)
        {
            if (uid == Guid.Empty) return;

            if (hadStore)
                FlowLayoutStore.Set(uid, position);
            else
                FlowLayoutStore.Remove(uid);

            if (_nodes.TryGetValue(uid, out var node))
            {
                Canvas.SetLeft(node, position.X);
                Canvas.SetTop(node, position.Y);
            }

            UpdateCanvasSize();
            RebuildEdges();
        }

        private List<(Guid uid, bool hadStore, Point pos)> CapturePositions()
        {
            var snapshot = new List<(Guid, bool, Point)>();
            var steps = _vm.AutomationStepBases;
            if (steps == null) return snapshot;

            foreach (var step in steps)
            {
                bool hadStore = FlowLayoutStore.TryGet(step.Uid, out var position);
                if (!hadStore && _nodes.TryGetValue(step.Uid, out var node))
                {
                    double left = Canvas.GetLeft(node);
                    double top = Canvas.GetTop(node);
                    position = new Point(double.IsNaN(left) ? 0 : left, double.IsNaN(top) ? 0 : top);
                }

                snapshot.Add((step.Uid, hadStore, position));
            }

            return snapshot;
        }

        private void RestorePositions(List<(Guid uid, bool hadStore, Point pos)> snapshot)
        {
            if (snapshot == null) return;

            foreach (var (uid, hadStore, pos) in snapshot)
            {
                if (hadStore) FlowLayoutStore.Set(uid, pos);
                else FlowLayoutStore.Remove(uid);

                if (_nodes.TryGetValue(uid, out var node))
                {
                    Canvas.SetLeft(node, pos.X);
                    Canvas.SetTop(node, pos.Y);
                }
            }

            UpdateCanvasSize();
            RebuildEdges();
        }

        #endregion

        #region 节点内联编辑（双击名称 / 备注）

        private void LogToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            var step = StepOfElement(element);
            if (step == null) return;

            // 点击时绑定已经把 EnableLog 改掉了，这里记一条「切换记录日志」的撤销
            bool newValue = step.EnableLog;
            bool oldValue = !newValue;
            PushFlowUndoFor(step, s => s.EnableLog = oldValue, s => s.EnableLog = newValue);
        }

        /// <summary>锚定开关：钉住的节点自动整理时不移动（状态存在 FlowLayoutStore 里）</summary>
        private void PinToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            var step = StepOfElement(element);
            if (step == null) return;

            bool newValue = step.FlowPinned;
            bool oldValue = !newValue;
            PushFlowUndoFor(step, s => s.FlowPinned = oldValue, s => s.FlowPinned = newValue);
        }

        private void Node_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ContentControl node) return;
            if (_linkSource != null) return;
            if (!SingletonLocator.Main.IsLoggedIn) return;

            var point = e.GetPosition(node);
            string boxName = point.Y < 30 ? "NameBox" : "DescBox";
            BeginInlineEdit(node, boxName);
            e.Handled = true;
        }

        private void BeginInlineEdit(ContentControl node, string boxName)
        {
            if (_dragNode != null)
            {
                _dragNode.ReleaseMouseCapture();
                _dragNode = null;
                _dragging = false;
            }

            var box = FindChild<TextBox>(node, boxName);
            if (box == null || !box.IsReadOnly) return;

            _editStep = box.DataContext as AutomationStepBase;
            _editProperty = boxName == "NameBox" ? nameof(AutomationStepBase.Name) : nameof(AutomationStepBase.Description);
            _editOldText = _editStep == null
                ? null
                : (_editProperty == nameof(AutomationStepBase.Name) ? _editStep.Name : _editStep.Description);

            box.IsReadOnly = false;
            box.IsHitTestVisible = true;
            box.Background = Brushes.White;
            box.BorderBrush = LinkBrush;
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(2, 0, 2, 0);

            box.LostFocus += InlineEditBox_LostFocus;
            box.KeyDown += InlineEditBox_KeyDown;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                box.Focus();
                box.SelectAll();
            }), DispatcherPriority.Input);
        }

        private void InlineEditBox_LostFocus(object sender, RoutedEventArgs e) => EndInlineEdit(sender as TextBox);

        private void InlineEditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                EndInlineEdit(sender as TextBox);
                e.Handled = true;
            }
        }

        private void EndInlineEdit(TextBox box)
        {
            if (box == null || box.IsReadOnly || box.IsHitTestVisible == false) return;

            box.LostFocus -= InlineEditBox_LostFocus;
            box.KeyDown -= InlineEditBox_KeyDown;

            box.IsReadOnly = true;
            box.IsHitTestVisible = false;
            box.Background = Brushes.Transparent;
            box.BorderThickness = new Thickness(0);
            box.Padding = new Thickness(0);
            Keyboard.ClearFocus();

            // 标题 / 备注的修改单独记一条撤销
            var step = _editStep;
            var property = _editProperty;
            var oldText = _editOldText;
            _editStep = null;
            _editProperty = null;
            _editOldText = null;

            if (step == null || property == null) return;

            bool isName = property == nameof(AutomationStepBase.Name);
            string newText = isName ? step.Name : step.Description;
            if (string.Equals(newText, oldText, StringComparison.Ordinal)) return;

            PushFlowUndoFor(step,
                s => { if (isName) s.Name = oldText; else s.Description = oldText; },
                s => { if (isName) s.Name = newText; else s.Description = newText; });
        }

        #endregion

        #region 右侧摘要 / 缩略图

        private void UpdateNodePreview(AutomationStepBase step)
        {
            if (step == null || _rebuilding) return;
            if (!_nodes.TryGetValue(step.Uid, out var node)) return;

            var thumbBorder = FindChild<Border>(node, "ThumbBorder");
            var thumbImage = FindChild<Image>(node, "ThumbImage");
            var summary = FindChild<TextBlock>(node, "SummaryText");
            if (thumbBorder == null || thumbImage == null || summary == null) return;

            ImageSource image = null;
            if (step is ImageRecognitionBase imageStep)
            {
                try
                {
                    image = LoadThumbnail(imageStep.CroppedImageFullPath);
                }
                catch
                {
                    image = null;
                }
            }

            if (image != null)
            {
                thumbImage.Source = image;
                thumbBorder.Visibility = Visibility.Visible;
                summary.Visibility = Visibility.Collapsed;
            }
            else
            {
                thumbBorder.Visibility = Visibility.Collapsed;
                string text = BuildStepSummary(step);
                if (!string.Equals(summary.Text, text, StringComparison.Ordinal))
                    summary.Text = text;
                summary.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private static string BuildStepSummary(AutomationStepBase step)
        {
            string text = step switch
            {
                TypeTextStep t => t.TextToType,
                TypeTextMoreStep t => t.TextToType,
                TypeTextFromFileStep t => SafeFileName(t.FilePath),
                FocusWindowStep f => string.IsNullOrWhiteSpace(f.SelectedWindowTitle) ? f.WindowTitleKeyword : f.SelectedWindowTitle,
                PopupStep p => p.Title,
                MouseActionStep m => m.MouseActions == null ? null : $"动作 {m.MouseActions.Count}",
                GetInputStep g => g.InputMode.ToString(),
                UserInputStep u => u.HasFields ? "已配置字段" : null,
                MultiThreadStartStep m => $"线程 {m.ThreadCount}",
                SendSignalStep s => s.SignalName,
                ReceiveSignalStep r => r.SignalName,
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            text = text.Trim();
            return text.Length > 120 ? text.Substring(0, 120) + "…" : text;
        }

        private static string SafeFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                return System.IO.Path.GetFileName(path);
            }
            catch
            {
                return path;
            }
        }

        private static readonly Dictionary<string, BitmapImage> ThumbnailCache = new Dictionary<string, BitmapImage>();

        private static ImageSource LoadThumbnail(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            string key;
            try
            {
                if (!File.Exists(path)) return null;
                key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch
            {
                return null;
            }

            if (ThumbnailCache.TryGetValue(key, out var cached)) return cached;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.DecodePixelWidth = 96;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                if (ThumbnailCache.Count > 300) ThumbnailCache.Clear();
                ThumbnailCache[key] = bitmap;
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region 右键菜单

        private void BuildMenus()
        {
            _nodeMenu = CreateNodeMenu();
            _canvasMenu = CreateAddOnlyMenu();
            _canvasEditMenu = CreateCanvasEditMenu();
        }

        /// <summary>空白画布上右键：只列添加步骤，选完插到最近步骤的位置</summary>
        private ContextMenu CreateAddOnlyMenu()
        {
            var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
            FillAddItems(menu.Items);
            return menu;
        }

        /// <summary>环状菜单里的「编辑」二级菜单</summary>
        private ContextMenu CreateCanvasEditMenu()
        {
            var menu = new ContextMenu();

            menu.Items.Add(new MenuItem
            {
                Header = LanguageService.GetLocalizedString("Paste"),
                Command = new RelayCommand(PasteSelection),
            });
            menu.Items.Add(new MenuItem
            {
                Header = LanguageService.GetLocalizedString("Undo"),
                Command = new RelayCommand(UndoFlow),
            });
            menu.Items.Add(new MenuItem
            {
                Header = LanguageService.GetLocalizedString("Redo"),
                Command = new RelayCommand(RedoFlow),
            });
            menu.Items.Add(new Separator());

            var autoLayout = new MenuItem { Header = LanguageService.GetLocalizedString("FlowChart_AutoLayout") };
            autoLayout.Click += AutoLayout_Click;
            menu.Items.Add(autoLayout);

            menu.Items.Add(new MenuItem
            {
                Header = LanguageService.GetLocalizedString("SelectAll"),
                Command = new RelayCommand(SelectAllSteps),
            });

            return menu;
        }

        private ContextMenu CreateNodeMenu()
        {
            var menu = new ContextMenu();

            var add = new MenuItem { Header = LanguageService.GetLocalizedString("Add") };
            FillAddItems(add.Items);
            menu.Items.Add(add);

            var replace = new MenuItem { Header = LanguageService.GetLocalizedString("FlowChart_ReplaceWith") };
            FillReplaceItems(replace.Items);
            menu.Items.Add(replace);

            menu.Items.Add(new MenuItem
            {
                Header = LanguageService.GetLocalizedString("Delete"),
                Command = _vm.DelStepCommand,
            });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header = LanguageService.GetLocalizedString("Up"),
                Command = _vm.UpStepCommand,
            });
            menu.Items.Add(new MenuItem
            {
                Header = LanguageService.GetLocalizedString("Down"),
                Command = _vm.DownStepCommand,
            });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header = LanguageService.GetLocalizedString("Copy"),
                Command = _vm.CopyStepCommand,
            });
            menu.Items.Add(new MenuItem
            {
                Header = LanguageService.GetLocalizedString("Cut"),
                Command = _vm.CutStepCommand,
            });
            menu.Items.Add(new MenuItem
            {
                Header = LanguageService.GetLocalizedString("Paste"),
                Command = _vm.PasteStepCommand,
            });
            menu.Items.Add(new Separator());

            var clearJump = new MenuItem { Header = LanguageService.GetLocalizedString("FlowChart_ClearGoto") };
            clearJump.Click += ClearGoto_Click;
            menu.Items.Add(clearJump);

            var autoLayout = new MenuItem { Header = LanguageService.GetLocalizedString("FlowChart_AutoLayout") };
            autoLayout.Click += AutoLayout_Click;
            menu.Items.Add(autoLayout);

            return menu;
        }

        private void FillAddItems(ItemCollection items) => FillStepItems(items, replace: false);

        private void FillReplaceItems(ItemCollection items) => FillStepItems(items, replace: true);

        private void FillStepItems(ItemCollection items, bool replace)
        {
            foreach (var entry in SimpleStepTypes)
                items.Add(CreateStepItem(entry.Tag, entry.ResourceKey, replace));

            var multi = new MenuItem { Header = LanguageService.GetLocalizedString("MultiThreadGroup") };
            foreach (var entry in MultiThreadStepTypes)
                multi.Items.Add(CreateStepItem(entry.Tag, entry.ResourceKey, replace));
            items.Add(multi);
        }

        private MenuItem CreateStepItem(string tag, string resourceKey, bool replace)
        {
            var item = new MenuItem
            {
                Header = LanguageService.GetLocalizedString(resourceKey),
                Tag = tag,
            };

            if (replace) item.Click += ReplaceStepItem_Click;
            else item.Click += AddStepItem_Click;

            return item;
        }

        private void AddStepItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item || item.Tag is not string type) return;
            ((ICommand)_vm.AddStepCommand).Execute(type);
        }

        /// <summary>右键「替换为…」：新建一个该类型的步骤，沿用原步骤的 Uid 与跳转，就地替换</summary>
        private void ReplaceStepItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item || item.Tag is not string type) return;
            if (!SingletonLocator.Main.IsLoggedIn) return;

            var target = (sender as FrameworkElement)?.DataContext as AutomationStepBase ?? _vm.SelectedStep;
            if (target == null) return;

            // 烧录配置步骤全局仅允许一个
            if (type == "BurnInConfig" && _vm.AutomationStepBases != null
                && _vm.AutomationStepBases.Any(s => s is BurnInConfigStep && s.Uid != target.Uid))
            {
                WindowAsyncPopup.Show(
                    LanguageService.GetLocalizedString("BurnIn_OnlyOneAllowed"),
                    LanguageService.GetLocalizedString("Warning"),
                    PopupButtons.OK, MessageBoxImage.Warning);
                return;
            }

            var replacement = _vm.CreateStep(type);
            if (replacement == null) return;

            FlowLayoutStore.Remove(target.Uid);
            if (!_vm.ReplaceStep(target, replacement)) return;
            SelectStep(replacement);
        }

        private void Node_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ContentControl node || StepOf(node) is not AutomationStepBase step) return;

            // 已经多选时保留多选：只把右键的节点作为菜单目标，不把选择收缩成单选
            if (!_selectedUids.Contains(step.Uid))
                SelectStep(step);

            _nodeMenu.DataContext = step;
            _nodeMenu.PlacementTarget = node;
            _nodeMenu.Placement = PlacementMode.MousePoint;
            _nodeMenu.IsOpen = true;
            e.Handled = true;
        }

        private void ClearGoto_Click(object sender, RoutedEventArgs e)
        {
            var step = (sender as FrameworkElement)?.DataContext as AutomationStepBase ?? _vm.SelectedStep;
            if (step == null) return;

            _vm.SelectedStep = step;

            var oldTrue = step.TrueGotoUid;
            var oldFalse = step.FalseGotoUid;
            if (oldTrue == null && oldFalse == null) return;

            step.TrueGotoUid = null;
            step.FalseGotoUid = null;

            PushFlowUndoFor(step,
                s => { s.TrueGotoUid = oldTrue; s.FalseGotoUid = oldFalse; },
                s => { s.TrueGotoUid = null; s.FalseGotoUid = null; });

            RebuildEdges();
        }

        #endregion

        #region 出口连线（真 / 假跳转，可连回自身）

        private void Port_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement port) return;
            var step = StepOfElement(port);
            if (step == null) return;

            string tag = port.Tag as string;
            bool isReference = string.Equals(tag, "R", StringComparison.OrdinalIgnoreCase);
            bool isTrue = string.Equals(tag, "T", StringComparison.OrdinalIgnoreCase);
            BeginLink(step, isTrue, e.GetPosition(CanvasHost), isReference, null);
            e.Handled = true;
        }

        /// <summary>
        /// 拖动连线端点：从某根真/假连线末端开始拖，松手落在别的步骤上就改接到那一步
        /// （松手落在空白处则清空该分支的跳转）
        /// </summary>
        private void EdgeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement handle) return;
            if (handle.Tag is not EdgeHandleTag tag || tag.From == null) return;
            if (_vm.AutomationStepBases == null || !_vm.AutomationStepBases.Contains(tag.From)) return;

            BeginLink(tag.From, tag.IsTrue, e.GetPosition(CanvasHost), tag.Condition != null, tag.Condition);
            e.Handled = true;
        }

        /// <summary>开始拖一根连线：源步骤 + 分支 + 起点（键盘/端点手柄共用）</summary>
        private void BeginLink(AutomationStepBase step, bool isTrue, Point startPoint, bool isReference = false, Models.StepCondition condition = null)
        {
            if (step == null) return;
            if (!SingletonLocator.Main.IsLoggedIn) return;   // 未登录只看不改

            SelectStep(step);

            ClearLinkVisual();
            _linkSource = step;
            _linkIsTrue = isTrue;
            _linkIsReference = isReference;
            _linkCondition = condition;
            _linkStartPoint = startPoint;
            _linkMoved = false;

            // 源节点虚化，并在内部放一个自连锚点
            if (_nodes.TryGetValue(step.Uid, out var sourceNode))
            {
                _dimmedNode = sourceNode;
                sourceNode.Opacity = 0.45;
            }
            ShowSelfAnchor(step);

            _linkPath = new Path
            {
                Stroke = LinkBrush,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeDashArray = new DoubleCollection { 4, 3 },
            };
            OverlayLayer.Children.Add(_linkPath);

            var origin = PortPoint(step, isTrue);
            UpdateLinkPath(origin, origin);
            RootGrid.CaptureMouse();
        }

        private void RootGrid_MouseMove(object sender, MouseEventArgs e)
        {
            // 拖动折线段：整段沿垂直方向平移（贴着端口的那一段只挪里侧拐点）
            if (_dragSegKey != null && _dragSegRoute != null)
            {
                var segPoint = e.GetPosition(CanvasHost);
                var segDelta = segPoint - _dragSegMouseStart;
                double shift = _dragSegHorizontal ? segDelta.Y : segDelta.X;
                if (Math.Abs(shift) >= 0.5)
                {
                    var moved = MoveRouteSegment(_dragSegRoute, _dragSegIndex, _dragSegHorizontal, shift);
                    Services.FlowEdgeAnchorStore.Set(AnchorKey(_dragSegKey), RouteToAnchors(moved));
                    _dragSegMoved = true;
                    RebuildEdges();
                }
                e.Handled = true;
                return;
            }

            // 拖动连线端点：先尝试贴着节点边框挪，终点拖出节点后再变成改接
            if (_dragPortKey != null)
            {
                var portPoint = e.GetPosition(CanvasHost);
                var (side, fraction) = ProjectOnBorder(_dragPortNodeRect, portPoint);
                bool inside = _dragPortNodeRect.Contains(portPoint);

                if (inside || _dragPortIsStart)
                {
                    // 起点只能贴在本节点边框上；终点在节点范围内也按边框落点处理
                    Services.FlowPortStore.Set(_dragPortKey + (_dragPortIsStart ? "|A" : "|B"), side, fraction);
                    _dragPortMoved = true;
                    RebuildEdges();
                    e.Handled = true;
                    return;
                }

                // 终点被拖出节点：切换成「重新选择目标步骤」
                _dragPortRetargeting = true;
                var fromStep = _dragPortFrom;
                bool isTrue = _dragPortIsTrue;
                _dragPortKey = null;
                BeginLink(fromStep, isTrue, portPoint);
                e.Handled = true;
                return;
            }
            // 拖动连线锚点：实时改锚点位置并重画
            if (_dragAnchorKey != null && _dragAnchorIndex >= 0)
            {
                var anchorList = new List<Point>(Services.FlowEdgeAnchorStore.Get(_dragAnchorKey));
                if (_dragAnchorIndex < anchorList.Count)
                {
                    var at = e.GetPosition(CanvasHost);
                    anchorList[_dragAnchorIndex] = SnapAnchor(new Point(Math.Max(0, at.X), Math.Max(0, at.Y)));
                    Services.FlowEdgeAnchorStore.Set(_dragAnchorKey, anchorList);
                    RebuildEdges();
                }
                e.Handled = true;
                return;
            }

            if (_linkSource == null) return;

            var point = e.GetPosition(CanvasHost);
            if ((point - _linkStartPoint).Length > 6)
                _linkMoved = true;

            UpdateLinkPath(PortPoint(_linkSource, _linkIsTrue), point);

            bool overAnchor = _selfAnchorRect != Rect.Empty && _selfAnchorRect.Contains(point);
            if (_selfAnchorEllipse != null)
                _selfAnchorEllipse.Fill = overAnchor ? AnchorHoverBrush : Brushes.White;

            AutomationStepBase target = overAnchor ? _selfAnchorOwner : HitTestStep(point);
            if (!overAnchor && ReferenceEquals(target, _linkSource))
                target = null;   // 落在源节点本体上不算目标，只有中点锚点是自连

            SetDropTarget(target);
            e.Handled = true;
        }

        private void RootGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 结束折线段拖动：记一条撤销
            if (_dragSegKey != null)
            {
                string segKey = _dragSegKey;
                bool segMoved = _dragSegMoved;
                var segBefore = _dragSegBefore ?? new List<Point>();
                _dragSegKey = null;
                _dragSegRoute = null;
                _dragSegMoved = false;
                _dragSegBefore = null;
                RootGrid.ReleaseMouseCapture();
                HideSegmentHint();
                e.Handled = true;

                if (segMoved)
                {
                    var segAfter = new List<Point>(Services.FlowEdgeAnchorStore.Get(AnchorKey(segKey)));
                    PushFlowUndo(
                        () => { Services.FlowEdgeAnchorStore.Set(AnchorKey(segKey), segBefore); RebuildEdges(); },
                        () => { Services.FlowEdgeAnchorStore.Set(AnchorKey(segKey), segAfter); RebuildEdges(); });
                }
                RebuildEdges();
                return;
            }

            // 结束端点拖动：记一条撤销（起点/终点的边框落点）
            if (_dragPortKey != null)
            {
                string portDragKey = _dragPortKey;
                bool portIsStart = _dragPortIsStart;
                bool portMoved = _dragPortMoved;
                bool portHadBefore = _dragPortHadBefore;
                string portBeforeSide = _dragPortBeforeSide;
                double portBeforeFraction = _dragPortBeforeFraction;

                _dragPortKey = null;
                _dragPortMoved = false;
                RootGrid.ReleaseMouseCapture();
                e.Handled = true;

                if (portMoved)
                {
                    string portKey = portDragKey + (portIsStart ? "|A" : "|B");
                    Services.FlowPortStore.TryGet(portKey, out var afterSide, out var afterFraction);
                    PushFlowUndo(
                        () =>
                        {
                            if (portHadBefore && portBeforeSide != null) Services.FlowPortStore.Set(portKey, portBeforeSide, portBeforeFraction);
                            else Services.FlowPortStore.Remove(portKey);
                            RebuildEdges();
                        },
                        () =>
                        {
                            if (afterSide != null) Services.FlowPortStore.Set(portKey, afterSide, afterFraction);
                            RebuildEdges();
                        });
                }
                return;
            }

            // 终点拖出节点后改写成了重新选择目标：交给下面的连线结束逻辑处理
            _dragPortRetargeting = false;

            // 结束锚点拖动：记一条撤销
            if (_dragAnchorKey != null && _dragAnchorIndex >= 0)
            {
                string key = _dragAnchorKey;
                int index = _dragAnchorIndex;
                var before = _dragAnchorBefore ?? new List<Point>();
                var after = new List<Point>(Services.FlowEdgeAnchorStore.Get(key));
                _dragAnchorKey = null;
                _dragAnchorIndex = -1;
                _dragAnchorBefore = null;
                RootGrid.ReleaseMouseCapture();
                e.Handled = true;

                if (after.Count > index && before.Count > index
                    && (after[index] - before[index]).Length > 0.5)
                {
                    PushFlowUndo(
                        () => { Services.FlowEdgeAnchorStore.Set(key, before); RebuildEdges(); },
                        () => { Services.FlowEdgeAnchorStore.Set(key, after); RebuildEdges(); });
                }
                return;
            }

            if (_linkSource == null) return;

            var source = _linkSource;
            bool isTrue = _linkIsTrue;
            bool isReference = _linkIsReference;
            var condition = _linkCondition;
            bool moved = _linkMoved;
            var point = e.GetPosition(CanvasHost);

            bool overAnchor = _selfAnchorRect != Rect.Empty && _selfAnchorRect.Contains(point);
            var target = overAnchor ? source : HitTestStep(point);
            if (!overAnchor && ReferenceEquals(target, source))
                target = null;

            ClearLinkVisual();
            SetDropTarget(null);
            RootGrid.ReleaseMouseCapture();
            e.Handled = true;

            // 只是点了一下出口圆：不改跳转
            if (!moved) return;

            if (isReference) FinishReferenceLink(source, condition, target);
            else FinishLink(source, isTrue, target);
        }

        /// <summary>
        /// 引用步骤的连线：把「本步骤要引用哪一步」落成一条自定义条件规则。
        /// 新建（condition 为空）时新加一条规则，拖已有连线时改那条规则的引用目标，
        /// 改完打开结果判断窗口让用户配置具体引用什么。
        /// </summary>
        private void FinishReferenceLink(AutomationStepBase source, Models.StepCondition condition, AutomationStepBase target)
        {
            if (source == null) return;
            if (target == null) return;   // 拖到空白处不建立引用

            _vm.SelectedStep = source;
            source.ConditionMode = Models.ConditionMode.Custom;

            if (condition != null)
            {
                var oldUid = condition.StepUid;
                if (oldUid == target.Uid) return;
                condition.StepUid = target.Uid;
                PushFlowUndoFor(source,
                    s => { if (s.Conditions.Contains(condition)) condition.StepUid = oldUid; },
                    s => { if (s.Conditions.Contains(condition)) condition.StepUid = target.Uid; });
            }
            else
            {
                var rule = new Models.StepCondition
                {
                    ParentStepUid = source.Uid,
                    Source = Models.ConditionSource.Step,
                    Field = Models.ConditionField.Result,
                    StepUid = target.Uid,
                    Operator = Models.ConditionOperator.Equal,
                    Value = "true",
                };
                source.Conditions.Add(rule);
                PushFlowUndoFor(source,
                    s => s.Conditions.Remove(rule),
                    s => { if (!s.Conditions.Contains(rule)) s.Conditions.Add(rule); });
            }

            RebuildEdges();

            // 弹出结果判断窗口配置这条引用
            Views.WindowEditConditions.Edit(source);
            RebuildEdges();
        }

        private void FinishLink(AutomationStepBase source, bool isTrue, AutomationStepBase target)
        {
            if (source == null) return;

            _vm.SelectedStep = source;

            var oldTrue = source.TrueGotoUid;
            var oldFalse = source.FalseGotoUid;

            if (target == null)
            {
                if (isTrue) source.TrueGotoUid = null;
                else source.FalseGotoUid = null;
            }
            else
            {
                if (isTrue) source.TrueGotoUid = target.Uid;
                else source.FalseGotoUid = target.Uid;
            }

            if (oldTrue != source.TrueGotoUid || oldFalse != source.FalseGotoUid)
            {
                var newTrue = source.TrueGotoUid;
                var newFalse = source.FalseGotoUid;
                PushFlowUndoFor(source,
                    s => { s.TrueGotoUid = oldTrue; s.FalseGotoUid = oldFalse; },
                    s => { s.TrueGotoUid = newTrue; s.FalseGotoUid = newFalse; });
            }

            RebuildEdges();
        }

        private void ShowSelfAnchor(AutomationStepBase step)
        {
            HideSelfAnchor();
            if (!_nodes.TryGetValue(step.Uid, out var node)) return;
            if (!TryGetNodeRect(node, out var rect)) return;

            var center = new Point(rect.Left + NodeWidth / 2, rect.Top + NodeHeight / 2);
            _selfAnchorRect = new Rect(center.X - 11, center.Y - 11, 22, 22);
            _selfAnchorOwner = step;

            var grid = new Grid { Width = 22, Height = 22, ToolTip = LanguageService.GetLocalizedString("FlowChart_SelfAnchor_ToolTip", "拖到这里连回本步骤（自引用）") };
            var ellipse = new Ellipse
            {
                Stroke = LinkBrush,
                StrokeThickness = 1.6,
                Fill = Brushes.White,
                StrokeDashArray = new DoubleCollection { 3, 2 },
            };
            var glyph = new TextBlock
            {
                Text = "↺",
                FontSize = 11,
                Foreground = LinkBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.Children.Add(ellipse);
            grid.Children.Add(glyph);

            Canvas.SetLeft(grid, center.X - 11);
            Canvas.SetTop(grid, center.Y - 11);
            OverlayLayer.Children.Add(grid);

            _selfAnchor = grid;
            _selfAnchorEllipse = ellipse;
        }

        private void HideSelfAnchor()
        {
            if (_selfAnchor != null && OverlayLayer.Children.Contains(_selfAnchor))
                OverlayLayer.Children.Remove(_selfAnchor);
            _selfAnchor = null;
            _selfAnchorEllipse = null;
            _selfAnchorRect = Rect.Empty;
            _selfAnchorOwner = null;
        }

        private void UpdateLinkPath(Point start, Point end)
        {
            if (_linkPath == null) return;
            _linkPath.Data = new LineGeometry(start, end);
        }

        private void ClearLinkVisual()
        {
            if (_linkPath != null && OverlayLayer.Children.Contains(_linkPath))
                OverlayLayer.Children.Remove(_linkPath);
            _linkPath = null;
            _linkSource = null;
            _linkIsReference = false;
            _linkCondition = null;

            HideSelfAnchor();

            if (_dimmedNode != null)
            {
                _dimmedNode.Opacity = 1.0;
                _dimmedNode = null;
            }
        }

        private void SetDropTarget(AutomationStepBase step)
        {
            if (ReferenceEquals(_dropTarget, step)) return;

            if (_dropTarget != null && _nodes.TryGetValue(_dropTarget.Uid, out var oldNode))
            {
                var oldRing = FindChild<Border>(oldNode, "DropRing");
                if (oldRing != null) oldRing.Visibility = Visibility.Collapsed;
            }

            _dropTarget = step;

            if (step != null && _nodes.TryGetValue(step.Uid, out var node))
            {
                var ring = FindChild<Border>(node, "DropRing");
                if (ring != null) ring.Visibility = Visibility.Visible;
            }
        }

        private Point PortPoint(AutomationStepBase step, bool isTrue)
        {
            if (!_nodes.TryGetValue(step.Uid, out var node) || !TryGetNodeRect(node, out var rect))
                return new Point();
            return new Point(rect.Left + NodeSlotWidth - 10, rect.Top + rect.Height / 2 + (isTrue ? -10 : 10));
        }


        private AutomationStepBase HitTestStep(Point point)
        {
            foreach (var pair in _nodes)
            {
                if (!TryGetNodeRect(pair.Value, out var rect)) continue;
                if (rect.Contains(point))
                    return StepOf(pair.Value);
            }
            return null;
        }

        #endregion

        #region 连线绘制（顺序 / 跳转 / 开始结束）

        private void RebuildEdges()
        {
            HideSegmentHint();
            UpdateLayoutMetrics();
            EdgeLayer.Children.Clear();
            EdgeDimLayer.Children.Clear();
            HandleLayer.Children.Clear();
            if (_rebuilding || _nodes.Count == 0) return;

            var steps = _vm.AutomationStepBases;
            if (steps == null || steps.Count == 0) return;

            var byUid = new Dictionary<Guid, AutomationStepBase>();
            foreach (var step in steps) byUid[step.Uid] = step;

            var specs = new Dictionary<string, EdgeSpec>();

            EdgeSpec Ensure(AutomationStepBase from, AutomationStepBase to)
            {
                if (from == null || to == null) return null;
                string key = from.Uid.ToString("N") + ">" + to.Uid.ToString("N");
                if (!specs.TryGetValue(key, out var spec))
                {
                    spec = new EdgeSpec(from, to);
                    specs[key] = spec;
                }
                return spec;
            }

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var next = i + 1 < steps.Count ? steps[i + 1] : null;

                bool hasTrue = step.TrueGotoUid.HasValue && step.TrueGotoUid.Value != Guid.Empty;
                bool hasFalse = step.FalseGotoUid.HasValue && step.FalseGotoUid.Value != Guid.Empty;

                if (hasTrue && byUid.TryGetValue(step.TrueGotoUid.Value, out var trueTarget))
                    Ensure(step, trueTarget)?.MarkTrue();
                if (hasFalse && byUid.TryGetValue(step.FalseGotoUid.Value, out var falseTarget))
                    Ensure(step, falseTarget)?.MarkFalse();

                // 没有设置跳转的那一支，结果出来后就按顺序落到下一步
                if (!hasTrue) Ensure(step, next)?.MarkFallThrough();
                if (!hasFalse) Ensure(step, next)?.MarkFallThrough();
            }

            // 列表里紧挨着的两步：直线连接（横向或折行处竖向）
            for (int i = 0; i + 1 < steps.Count; i++)
            {
                string key = steps[i].Uid.ToString("N") + ">" + steps[i + 1].Uid.ToString("N");
                if (specs.TryGetValue(key, out var spec))
                    spec.IsSequential = true;
            }

            _edgeLane = 0;
            _bandLanes.Clear();
            _portSlots.Clear();
            _drawnSegments.Clear();
            _edgeRoutes.Clear();
            HideAnchorPreview();
            // 先画顺序连线，再画真假跳转（引用连线在最后单独画），端口按这个优先级占位
            foreach (var spec in specs.Values.OrderBy(s => s.IsSequential ? 0 : 1))
            {
                // 竖直排列不画顺序连线（含默认顺流的灰线），只留真假跳转与引用线
                if (IsVerticalArrange && !spec.IsTrue && !spec.IsFalse) continue;
                if (!ShouldShowEdge(spec)) continue;
                DrawEdge(spec);
            }

            // 引用步骤的连线：条件里引用了别的步骤（来源=引用步骤）就画一条紫色虚线
            int referenceIndex = 0;
            foreach (var step in steps)
            {
                if (step.Conditions == null || step.Conditions.Count == 0) continue;
                if (step.ConditionMode != Models.ConditionMode.Custom) continue;
                foreach (var condition in step.Conditions)
                {
                    if (condition.Source != Models.ConditionSource.Step) continue;
                    if (!condition.StepUid.HasValue || condition.StepUid.Value == Guid.Empty) continue;
                    if (!byUid.TryGetValue(condition.StepUid.Value, out var refTarget)) continue;
                    if (ReferenceEquals(step, refTarget)) continue;
                    if (!ShouldShowReferenceEdge(step, refTarget)) continue;

                    DrawReferenceEdge(step, refTarget, condition,
                        EdgeKey(step.Uid, refTarget.Uid, "R" + referenceIndex++));
                }
            }
        }

        /// <summary>连线过滤器：勾上=该类型全部显示；不勾=只显示与选中步骤相连的</summary>
        /// <summary>工具栏切换连线样式（折线 / 曲线），切换后立即按新样式重画</summary>
        private void EdgeStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EdgeStyleBox == null || EdgeStyleBox.SelectedIndex < 0) return;
            try { SingletonLocator.Settings.Step.FlowEdgeStyle = EdgeStyleBox.SelectedIndex; } catch { }
            UpdateLayoutMetrics();
            RebuildEdges();
        }

        private void EdgeFilter_Click(object sender, RoutedEventArgs e)
        {
            RebuildEdges();
        }

        /// <summary>锚点开关：关掉时隐藏已放的锚点，也不会在连线上预览 / 新增锚点（锚点本身仍参与走线）</summary>
        private void AnchorFilter_Click(object sender, RoutedEventArgs e)
        {
            if (!ShowAnchors) HideAnchorPreview();
            RebuildEdges();
        }

        private bool ShowAllTrue => ShowTrueToggle == null || ShowTrueToggle.IsChecked == true;

        private bool ShowAllFalse => ShowFalseToggle == null || ShowFalseToggle.IsChecked == true;

        private bool ShowAllReference => ShowRefToggle == null || ShowRefToggle.IsChecked == true;

        /// <summary>是否显示连线锚点（工具栏开关，默认显示）</summary>
        private bool ShowAnchors => ShowAnchorsToggle == null || ShowAnchorsToggle.IsChecked == true;

        /// <summary>焦点预览：只让与选中步骤相连的连线保持原色，其余变淡</summary>
        private bool FocusMode => FocusModeToggle == null || FocusModeToggle.IsChecked == true;

        private void FocusMode_Click(object sender, RoutedEventArgs e)
        {
            RebuildEdges();
        }

        /// <summary>
        /// 锚点按排列方式分开存：横向排列沿用原来的 Key（旧工程打开后锚点照旧），
        /// 竖直排列加 V| 前缀，两种排列的锚点位置互不干扰。
        /// </summary>
        private string AnchorKey(string edgeKey) => IsVerticalArrange ? "V|" + edgeKey : edgeKey;

        private bool AnyEdgeFilterActive => !ShowAllTrue || !ShowAllFalse || !ShowAllReference;

        /// <summary>顺序连线 / 顺流连线永远显示；真、假跳转按工具栏开关过滤</summary>
        private bool ShouldShowEdge(EdgeSpec spec)
        {
            if (!spec.IsTrue && !spec.IsFalse) return true;   // 顺序连线一直在

            bool filterOn = spec.IsTrue ? ShowAllTrue : ShowAllFalse;
            if (spec.IsTrue && spec.IsFalse)
                filterOn = ShowAllTrue || ShowAllFalse;

            if (filterOn) return true;
            return IsSelected(spec.From) || IsSelected(spec.To);
        }

        private bool ShouldShowReferenceEdge(AutomationStepBase from, AutomationStepBase to)
        {
            if (ShowAllReference) return true;
            return IsSelected(from) || IsSelected(to);
        }

        private bool IsSelected(AutomationStepBase step)
            => step != null && _selectedUids.Contains(step.Uid);

        /// <summary>画一根连线；跳转连线画完再在末端放一个可拖动的端点把手</summary>
        private void DrawEdge(EdgeSpec spec)
        {
            _drawingEdgeEnd = null;
            _drawingEdgeStart = null;
            // 焦点预览：没选中任何步骤时全部正常；选中了就只让与它相连的线保持原色
            _drawingDim = FocusMode && _selectedUids.Count > 0
                && !IsSelected(spec.From) && !IsSelected(spec.To);
            try { DrawEdgeCore(spec); }
            finally { _drawingDim = false; }

            // 只有「真」或「假」某一个分支的连线才需要改接；两个分支连到同一步时不区分真假，不给把手
            if ((!spec.IsTrue && !spec.IsFalse) || (spec.IsTrue && spec.IsFalse)) return;
            if (!(_drawingEdgeEnd is Point end)) return;
            if (ReferenceEquals(spec.From, spec.To)) return;

            AddEdgeHandle(end, spec.From, spec.To, spec.IsTrue, _lastEdgeKey, isStart: false);
            if (_drawingEdgeStart is Point start)
                AddEdgeHandle(start, spec.From, spec.To, spec.IsTrue, _lastEdgeKey, isStart: true);
        }

        private void DrawEdgeCore(EdgeSpec spec)
        {
            if (!_nodes.TryGetValue(spec.From.Uid, out var fromNode)) return;

            if (!_nodes.TryGetValue(spec.To.Uid, out var toNode)) return;
            if (!TryGetNodeRect(fromNode, out var fromRect) || !TryGetNodeRect(toNode, out var toRect)) return;

            bool explicitJump = spec.IsTrue || spec.IsFalse;
            Brush brush = spec.IsTrue && spec.IsFalse
                ? EdgeBothBrush
                : spec.IsTrue ? EdgeTrueBrush
                : spec.IsFalse ? EdgeFalseBrush
                : EdgeNormalBrush;
            // 顺序连线画成实心粗箭头（LabVIEW 那种粗线），真 / 假跳转保持细线
            double thickness = explicitJump ? 1.8 : 2.6;

            string label = spec.IsTrue && spec.IsFalse
                ? LanguageService.GetLocalizedString("FlowChart_EdgeBoth")
                : spec.IsTrue ? LanguageService.GetLocalizedString("FlowChart_EdgeTrue")
                : spec.IsFalse ? LanguageService.GetLocalizedString("FlowChart_EdgeFalse")
                : null;

            if (ReferenceEquals(spec.From, spec.To))
            {
                DrawSelfLoop(fromRect, brush, thickness, label);
                return;
            }

            _edgeLane++;

            // 折线 / 曲线都走同一套「最短路径 + 端点不重叠」的规划，只有渲染方式不同
            string kind = spec.IsTrue && spec.IsFalse ? "B" : spec.IsTrue ? "T" : spec.IsFalse ? "F" : "S";
            string edgeKey = EdgeKey(spec.From.Uid, spec.To.Uid, kind);
            _lastEdgeKey = edgeKey;
            // 竖直排列：真假跳转固定走节点右侧
            string forcedSide = IsVerticalArrange && explicitJump ? "R" : null;
            DrawRoutedEdge(fromRect, toRect, spec.From.Uid, spec.To.Uid, brush, thickness, label, false, edgeKey, forcedSide);
        }

        #region 统一走线（最短路径 + 同一节点边框上的端点不重叠）

        /// <summary>一条连线的唯一标识（起点&gt;终点&gt;类型），锚点按这个 Key 记录</summary>
        private static string EdgeKey(Guid fromUid, Guid toUid, string kind)
            => fromUid.ToString("N") + ">" + toUid.ToString("N") + ">" + kind;

        // 每条连线画出来的走线形状，供鼠标悬停时在线上显示锚点预览
        private readonly Dictionary<string, List<Point>> _edgeRoutes = new Dictionary<string, List<Point>>();
        private string _hoverEdgeKey;
        private Point _hoverEdgePoint;
        private Ellipse _anchorPreview;
        // 连线端点拖动：正在挪的是哪根线的哪一端、贴着哪个节点
        private string _dragPortKey;
        private bool _dragPortIsStart;
        private Guid _dragPortNodeUid;
        private Rect _dragPortNodeRect;
        private AutomationStepBase _dragPortFrom;
        private bool _dragPortIsTrue;
        private bool _dragPortMoved;
        private bool _dragPortRetargeting;
        private bool _dragPortHadBefore;
        private string _dragPortBeforeSide;
        private double _dragPortBeforeFraction;
        private string _dragAnchorKey;
        private int _dragAnchorIndex = -1;
        private List<Point> _dragAnchorBefore;

        // 拖动折线段（LabVIEW 那样：点在线上按住平移这一段）
        private const double SegmentGrabDistance = 6;      // 离线 ≤6px 算「在线上」，可以拖这段
        private const double AnchorPreviewDistance = 17;   // 6~17px 算「靠近线」，可以加锚点
        private string _dragSegKey;
        private List<Point> _dragSegRoute;
        private int _dragSegIndex;
        private bool _dragSegHorizontal;
        private Point _dragSegMouseStart;
        private List<Point> _dragSegBefore;
        private bool _dragSegMoved;
        private int _hoverSegmentIndex = -1;
        private Path _segHint;
        private static readonly Brush SegmentHintBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x2B, 0x6F, 0xD6));

        // 每个节点每条边已经用掉的端口数：同一侧的多条线按设置里的间隔依次排开
        private readonly Dictionary<string, int> _portSlots = new Dictionary<string, int>();

        /// <summary>同一节点边框上相邻两个端点的最小间隔（设置里可调，默认 10px）</summary>
        private double PortSpacing
        {
            get
            {
                double spacing = 10;
                try { spacing = SingletonLocator.Settings.Step.FlowPortSpacing; } catch { }
                if (spacing <= 0) spacing = 10;
                return Math.Min(80, Math.Max(6, spacing));
            }
        }

        /// <summary>0,1,-1,2,-2…（端口从中间往两边交替排）</summary>
        private static int SpreadIndex(int slot)
        {
            if (slot == 0) return 0;
            int n = (slot + 1) / 2;
            return slot % 2 == 1 ? n : -n;
        }

        private static double ClampTo(double value, double min, double max)
            => max < min ? min : Math.Min(max, Math.Max(min, value));

        /// <summary>取某条边（L/R/T/B）上的第 slot 个端点位置</summary>
        private Point PortPosition(Rect rect, string side, int slot)
        {
            double offset = SpreadIndex(slot) * PortSpacing;
            switch (side)
            {
                case "L":
                    return new Point(rect.Left, ClampTo(rect.Top + rect.Height / 2 + offset, rect.Top + 14, rect.Bottom - 14));
                case "R":
                    return new Point(rect.Right, ClampTo(rect.Top + rect.Height / 2 + offset, rect.Top + 14, rect.Bottom - 14));
                case "T":
                    return new Point(ClampTo(rect.Left + rect.Width / 2 + offset, rect.Left + 20, rect.Right - 20), rect.Top);
                default:
                    return new Point(ClampTo(rect.Left + rect.Width / 2 + offset, rect.Left + 20, rect.Right - 20), rect.Bottom);
            }
        }

        /// <summary>按边框上的比例取端点位置</summary>
        private static Point PortAtFraction(Rect rect, string side, double fraction)
        {
            double f = Math.Min(0.94, Math.Max(0.06, fraction));
            switch (side)
            {
                case "L": return new Point(rect.Left, rect.Top + rect.Height * f);
                case "R": return new Point(rect.Right, rect.Top + rect.Height * f);
                case "T": return new Point(rect.Left + rect.Width * f, rect.Top);
                default: return new Point(rect.Left + rect.Width * f, rect.Bottom);
            }
        }

        private static Vector PortDirection(string side) => side switch
        {
            "L" => new Vector(-1, 0),
            "R" => new Vector(1, 0),
            "T" => new Vector(0, -1),
            _ => new Vector(0, 1),
        };

        private int PeekPortSlot(Guid uid, string side)
        {
            _portSlots.TryGetValue(uid.ToString("N") + "|" + side, out int used);
            return used;
        }

        private void CommitPortSlot(Guid uid, string side)
        {
            string key = uid.ToString("N") + "|" + side;
            _portSlots.TryGetValue(key, out int used);
            _portSlots[key] = used + 1;
        }

        /// <summary>按中心偏移挑几条候选走线方向：优先最短，其次换一个方向绕开挡路的节点</summary>
        private static List<(string From, string To)> SideCandidates(Rect from, Rect to)
        {
            double dx = (to.Left + to.Width / 2) - (from.Left + from.Width / 2);
            double dy = (to.Top + to.Height / 2) - (from.Top + from.Height / 2);

            var horizontal = dx >= 0 ? ("R", "L") : ("L", "R");
            var vertical = dy >= 0 ? ("B", "T") : ("T", "B");

            var list = new List<(string, string)>();
            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                list.Add(horizontal);
                list.Add(vertical);
            }
            else
            {
                list.Add(vertical);
                list.Add(horizontal);
            }

            // 反向兜底：绕到节点另一侧
            list.Add((horizontal.Item1 == "R" ? "L" : "R", horizontal.Item2 == "L" ? "R" : "L"));
            list.Add((vertical.Item1 == "B" ? "T" : "B", vertical.Item2 == "T" ? "B" : "T"));
            return list;
        }

        /// <summary>两点之间的正交走线：各自先垂直离开边框一小段，再走 Z 形折线</summary>
        private static List<Point> BuildRoute(Rect from, Rect to, string sideFrom, string sideTo, int slotFrom, int slotTo, double midShift, double spacing, double? fracFrom = null, double? fracTo = null)
        {
            Point PortOf(Rect rect, string side, int slot)
            {
                double offset = SpreadIndex(slot) * spacing;
                switch (side)
                {
                    case "L": return new Point(rect.Left, ClampTo(rect.Top + rect.Height / 2 + offset, rect.Top + 14, rect.Bottom - 14));
                    case "R": return new Point(rect.Right, ClampTo(rect.Top + rect.Height / 2 + offset, rect.Top + 14, rect.Bottom - 14));
                    case "T": return new Point(ClampTo(rect.Left + rect.Width / 2 + offset, rect.Left + 20, rect.Right - 20), rect.Top);
                    default: return new Point(ClampTo(rect.Left + rect.Width / 2 + offset, rect.Left + 20, rect.Right - 20), rect.Bottom);
                }
            }

            const double stub = 16;

            var pA = fracFrom.HasValue ? PortAtFraction(from, sideFrom, fracFrom.Value) : PortOf(from, sideFrom, slotFrom);
            var pB = fracTo.HasValue ? PortAtFraction(to, sideTo, fracTo.Value) : PortOf(to, sideTo, slotTo);
            var dA = PortDirection(sideFrom);
            var dB = PortDirection(sideTo);
            var sA = new Point(pA.X + dA.X * stub, pA.Y + dA.Y * stub);
            var sB = new Point(pB.X + dB.X * stub, pB.Y + dB.Y * stub);

            var points = new List<Point> { pA, sA };
            if (dA.X != 0)
            {
                double midX = (sA.X + sB.X) / 2 + midShift;
                points.Add(new Point(midX, sA.Y));
                points.Add(new Point(midX, sB.Y));
            }
            else
            {
                double midY = (sA.Y + sB.Y) / 2 + midShift;
                points.Add(new Point(sA.X, midY));
                points.Add(new Point(sB.X, midY));
            }
            points.Add(sB);
            points.Add(pB);

            return SimplifyPath(points);
        }

        /// <summary>这段折线是否会穿过别的节点</summary>
        private bool RouteBlocked(IReadOnlyList<Point> points, Guid fromUid, Guid toUid)
        {
            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1];
                var b = points[i];
                var box = new Rect(
                    Math.Min(a.X, b.X) - 2,
                    Math.Min(a.Y, b.Y) - 2,
                    Math.Abs(b.X - a.X) + 4,
                    Math.Abs(b.Y - a.Y) + 4);

                foreach (var pair in _nodes)
                {
                    if (pair.Key == fromUid || pair.Key == toUid) continue;
                    if (!TryGetNodeRect(pair.Value, out var nodeRect)) continue;
                    if (nodeRect.IntersectsWith(box)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 画一条按最短路径规划的正交连线：选离目标最近的两条边、在边框上各占一个端点。
        /// 候选方向里优先选「不穿节点、也不和已画连线重叠」的；都被占就选重叠最少的。
        /// </summary>
        private List<Point> DrawRoutedEdge(Rect from, Rect to, Guid fromUid, Guid toUid,
            Brush brush, double thickness, string label, bool dashed, string edgeKey, string forcedSide = null)
        {
            List<Point> best = null;
            string bestFromSide = null, bestToSide = null;
            int bestSlotFrom = 0, bestSlotTo = 0;
            double bestScore = double.MaxValue;
            bool bestBlocked = true;

            double stagger = SpreadIndex(_edgeLane) * LaneStep;
            double spacing = PortSpacing;

            // 竖直排列等场景要求固定从某一侧进出：只试这一侧（起点终点都在同一侧）
            var sideCandidates = string.IsNullOrEmpty(forcedSide)
                ? SideCandidates(from, to)
                : new List<(string From, string To)> { (forcedSide, forcedSide) };

            void Consider(List<Point> route, string sideFrom, string sideTo, int slotFrom, int slotTo, bool blocked)
            {
                double overlap = MeasureOverlap(route);
                double score = overlap + (blocked ? 100000 : 0);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = route;
                    bestFromSide = sideFrom;
                    bestToSide = sideTo;
                    bestSlotFrom = slotFrom;
                    bestSlotTo = slotTo;
                    bestBlocked = blocked;
                }
            }

            // 用户手动拖过端点落点的，直接按那个落点走线
            bool hasStartPort = Services.FlowPortStore.TryGet(edgeKey + "|A", out var startSide, out var startFrac);
            bool hasEndPort = Services.FlowPortStore.TryGet(edgeKey + "|B", out var endSide, out var endFrac);
            bool hasPortOverride = hasStartPort || hasEndPort;

            if (hasPortOverride)
            {
                var fromSide = hasStartPort ? startSide : sideCandidates[0].From;
                var toSide = hasEndPort ? endSide : sideCandidates[0].To;
                var route = BuildRoute(from, to, fromSide, toSide, PeekPortSlot(fromUid, fromSide), PeekPortSlot(toUid, toSide),
                    stagger, spacing, hasStartPort ? startFrac : (double?)null, hasEndPort ? endFrac : (double?)null);
                Consider(route, fromSide, toSide, PeekPortSlot(fromUid, fromSide), PeekPortSlot(toUid, toSide), false);
            }

            foreach (var (sideFrom, sideTo) in sideCandidates)
            {
                if (hasPortOverride) break;   // 端点落点已固定，不再自动选边
                int slotFrom = PeekPortSlot(fromUid, sideFrom);
                int slotTo = PeekPortSlot(toUid, sideTo);
                var route = BuildRoute(from, to, sideFrom, sideTo, slotFrom, slotTo, stagger, spacing);
                bool blocked = RouteBlocked(route, fromUid, toUid);
                Consider(route, sideFrom, sideTo, slotFrom, slotTo, blocked);
                if (!blocked && bestScore <= 0.01) break;   // 已经完全不重叠，不用再试其它方向
            }

            // 四周都被中间节点堵住：从所有节点的上方/下方通道绕过去
            if (bestBlocked)
            {
                double dx = (to.Left + to.Width / 2) - (from.Left + from.Width / 2);
                string sideFrom = !string.IsNullOrEmpty(forcedSide) ? forcedSide : dx >= 0 ? "R" : "L";
                string sideTo = sideFrom;   // 同一个侧面绕过去，不会在节点上下沿冒出小尖

                foreach (bool above in new[] { true, false })
                {
                    int slotFrom = PeekPortSlot(fromUid, sideFrom);
                    int slotTo = PeekPortSlot(toUid, sideTo);
                    var route = BuildBandRoute(from, to, sideFrom, sideTo, slotFrom, slotTo, above);
                    bool blocked = RouteBlocked(route, fromUid, toUid);
                    Consider(route, sideFrom, sideTo, slotFrom, slotTo, blocked);
                    if (!blocked && bestScore <= 0.01) break;
                }
            }

            if (best == null || best.Count < 2) return null;

            // 占住这两个端点，后面的线会自动往外让
            int before = PeekPortSlot(fromUid, bestFromSide);
            if (before == bestSlotFrom) CommitPortSlot(fromUid, bestFromSide);
            int beforeTo = PeekPortSlot(toUid, bestToSide);
            if (beforeTo == bestSlotTo) CommitPortSlot(toUid, bestToSide);

            var anchors = Services.FlowEdgeAnchorStore.Get(AnchorKey(edgeKey));
            bool hasAnchors = anchors.Count > 0;

            // 有锚点：连线必须经过这些手动放下的锚点（正交连接起来）
            if (hasAnchors)
            {
                best = BuildRouteThroughAnchors(from, to, bestFromSide, bestToSide, bestSlotFrom, bestSlotTo, anchors);
                hasAnchors = true;
            }

            RegisterSegments(best);
            _edgeRoutes[edgeKey] = best;
            // 起点记下来：折线 / 曲线两种画法都能在起点挂上可拖动的端点把手
            _drawingEdgeStart = best[0];

            // 设置里选了「曲线」：有锚点时画成穿过多段曲线的平滑线（锚点就是转折处），否则画一条 S 形弧线
            if (SingletonLocator.Settings.Step.FlowEdgeStyle == 1)
            {
                var pA = PortPosition(from, bestFromSide, bestSlotFrom);
                var pB = PortPosition(to, bestToSide, bestSlotTo);
                var dA = PortDirection(bestFromSide);
                var dB = PortDirection(bestToSide);
                double distance = Math.Abs(pB.X - pA.X) + Math.Abs(pB.Y - pA.Y);
                double stub = Math.Max(30, Math.Min(90, distance * 0.4));

                // 别把弧线甩到画布外面去（上边沿、左边沿）
                if (dA.Y < 0) stub = Math.Min(stub, Math.Max(12, pA.Y - 10));
                if (dB.Y < 0) stub = Math.Min(stub, Math.Max(12, pB.Y - 10));
                if (dA.X < 0) stub = Math.Min(stub, Math.Max(12, pA.X - 10));
                if (dB.X < 0) stub = Math.Min(stub, Math.Max(12, pB.X - 10));

                var sA = new Point(pA.X + dA.X * stub, pA.Y + dA.Y * stub);
                var sB = new Point(pB.X + dB.X * stub, pB.Y + dB.Y * stub);

                if (hasAnchors)
                {
                    // 多段曲线：端点 → 引出点 → 各个锚点 → 引出点 → 端点，锚点作为曲线的必经转折
                    var curvePoints = new List<Point> { pA, sA };
                    curvePoints.AddRange(anchors);
                    curvePoints.Add(sB);
                    curvePoints.Add(pB);

                    AddSplineEdge(curvePoints, brush, thickness, label, dashed);

                    // 悬停/加锚点按「曲线的实际形状」来（采样点足够贴合曲线），保存时只存锚点位置，线会重新算
                    var curveSamples = SampleSpline(curvePoints);
                    _edgeRoutes[edgeKey] = curveSamples;
                    if (!dashed) AddEdgeInteractivePath(curveSamples, edgeKey);
                    AddAnchorDots(edgeKey, anchors);
                    return curveSamples;
                }

                AddBezierEdge(pA, sA, sB, pB, brush, thickness, label,
                    BezierPoint(pA, sA, sB, pB, 0.5), dashed);

                var arcSamples = SampleSpline(new[] { pA, sA, sB, pB });
                _edgeRoutes[edgeKey] = arcSamples;
                if (!dashed) AddEdgeInteractivePath(arcSamples, edgeKey);
                AddAnchorDots(edgeKey, anchors);
                return arcSamples;
            }

            AddPolylineEdge(best, brush, thickness, label, StaggeredLabelPoint(best), dashed);
            if (!dashed) AddEdgeInteractivePath(best, edgeKey);
            AddAnchorDots(edgeKey, anchors);
            return best;
        }

        /// <summary>
        /// 多段平滑曲线：用 Catmull-Rom 转三次贝塞尔，保证依次穿过每个点（锚点就是转折处）
        /// </summary>
        private void AddSplineEdge(IReadOnlyList<Point> points, Brush brush, double thickness, string label, bool dashed)
        {
            if (points == null || points.Count < 2) return;

            var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };

            for (int i = 0; i + 1 < points.Count; i++)
            {
                var p0 = points[Math.Max(0, i - 1)];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = points[Math.Min(points.Count - 1, i + 2)];

                var c1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);
                figure.Segments.Add(new BezierSegment(c1, c2, p2, true));
            }

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();

            var path = new Path
            {
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = geometry,
            };
            if (dashed)
                path.StrokeDashArray = new DoubleCollection { 5, 3 };
            EdgeTarget.Children.Add(path);

            var last = points[points.Count - 1];
            var before = points[points.Count - 2];
            AddArrowHead(last, last - before, brush);

            if (!string.IsNullOrEmpty(label))
                AddEdgeLabel(LabelsMidPoint(points), label, brush);
        }

        /// <summary>把多段曲线采样成密集折线：用于鼠标悬停定位与「在曲线上加锚点」</summary>
        private static List<Point> SampleSpline(IReadOnlyList<Point> points, int perSegment = 16)
        {
            var samples = new List<Point>();
            if (points == null || points.Count < 2) return samples;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                var p0 = points[Math.Max(0, i - 1)];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = points[Math.Min(points.Count - 1, i + 2)];

                var c1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);

                for (int step = 0; step < perSegment; step++)
                    samples.Add(BezierPoint(p1, c1, c2, p2, step / (double)perSegment));
            }

            samples.Add(points[points.Count - 1]);
            return samples;
        }

        /// <summary>多段曲线的标签位置：取中间那段的中间</summary>
        private static Point LabelsMidPoint(IReadOnlyList<Point> points)
        {
            int index = points.Count / 2;
            int previous = Math.Max(0, index - 1);
            return MidPoint(points[previous], points[index]);
        }

        /// <summary>经过锚点的走线：两端引出后依次用正交折线串起各个锚点</summary>
        private List<Point> BuildRouteThroughAnchors(Rect from, Rect to, string sideFrom, string sideTo,
            int slotFrom, int slotTo, IReadOnlyList<Point> anchors)
        {
            const double stub = 16;
            var pA = PortPosition(from, sideFrom, slotFrom);
            var pB = PortPosition(to, sideTo, slotTo);
            var dA = PortDirection(sideFrom);
            var dB = PortDirection(sideTo);
            var sA = new Point(pA.X + dA.X * stub, pA.Y + dA.Y * stub);
            var sB = new Point(pB.X + dB.X * stub, pB.Y + dB.Y * stub);

            var points = new List<Point> { pA, sA };
            var current = sA;

            foreach (var anchor in anchors)
            {
                AppendOrtho(points, current, anchor);
                current = anchor;
            }

            AppendOrtho(points, current, sB);
            points.Add(sB);
            points.Add(pB);

            return SimplifyPath(points);
        }

        /// <summary>把 a→b 用一段正交折线接上（先走长边方向再拐）</summary>
        private static void AppendOrtho(List<Point> points, Point a, Point b)
        {
            if (Math.Abs(a.X - b.X) < 0.5 || Math.Abs(a.Y - b.Y) < 0.5)
            {
                points.Add(b);
                return;
            }

            if (Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y))
                points.Add(new Point(b.X, a.Y));
            else
                points.Add(new Point(a.X, b.Y));

            points.Add(b);
        }

        /// <summary>连线的透明热区：鼠标靠近显示锚点预览，左键点击在该位置加一个锚点</summary>
        private void AddEdgeInteractivePath(IReadOnlyList<Point> points, string edgeKey)
        {
            if (points == null || points.Count < 2) return;

            var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
            for (int i = 1; i < points.Count; i++)
                figure.Segments.Add(new LineSegment(points[i], true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();

            var hit = new Path
            {
                Data = geometry,
                Stroke = Brushes.Transparent,
                StrokeThickness = 34,      // 宽热区：靠近但不贴线也能点到，用来加锚点
                Cursor = Cursors.Cross,
                Tag = edgeKey,
            };
            hit.MouseMove += EdgePath_MouseMove;
            hit.MouseLeave += EdgePath_MouseLeave;
            hit.MouseLeftButtonDown += EdgePath_MouseLeftButtonDown;
            HandleLayer.Children.Add(hit);
        }

        private void EdgePath_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string key) return;
            if (!_edgeRoutes.TryGetValue(key, out var route)) return;

            var point = e.GetPosition(CanvasHost);
            var projected = ProjectOnPath(route, point, out double distance, out int segmentIndex, out bool horizontal);
            if (projected == null) { HideAnchorPreview(); HideSegmentHint(); return; }

            // 在线上：这一段可以按住平移，鼠标形状提示方向
            if (distance <= SegmentGrabDistance)
            {
                HideAnchorPreview();
                _hoverEdgeKey = key;
                _hoverEdgePoint = projected.Value;
                _hoverSegmentIndex = segmentIndex;
                element.Cursor = horizontal ? Cursors.SizeNS : Cursors.SizeWE;
                ShowSegmentHint(key, route, segmentIndex);
                return;
            }

            // 靠近但不在线上：给锚点预览（锚点开关关掉时不加）
            HideSegmentHint();
            _hoverSegmentIndex = -1;
            element.Cursor = Cursors.Cross;
            if (!ShowAnchors) { HideAnchorPreview(); return; }
            if (distance > AnchorPreviewDistance) { HideAnchorPreview(); return; }

            _hoverEdgeKey = key;
            _hoverEdgePoint = SnapAnchor(point);
            ShowAnchorPreview(_hoverEdgePoint);
        }

        private void EdgePath_MouseLeave(object sender, MouseEventArgs e)
        {
            HideAnchorPreview();
            HideSegmentHint();
            _hoverSegmentIndex = -1;
            if (sender is FrameworkElement element) element.Cursor = Cursors.Cross;
        }

        /// <summary>连线上左键点击：把这个位置加进锚点列表（之后连线就固定在这一点）</summary>
        private void EdgePath_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string key) return;
            if (!_edgeRoutes.TryGetValue(key, out var route)) return;

            string anchorKey = AnchorKey(key);
            var point = e.GetPosition(CanvasHost);
            var projected = ProjectOnPath(route, point, out double distance, out int segmentIndex, out bool horizontal);
            if (projected == null) return;

            // 点在线上：拖动这一段（平移）；靠近但不在线上：加锚点
            if (distance <= SegmentGrabDistance)
            {
                BeginSegmentDrag(key, route, segmentIndex, horizontal, point);
                e.Handled = true;
                return;
            }

            if (!ShowAnchors || distance > AnchorPreviewDistance) return;

            var before = new List<Point>(Services.FlowEdgeAnchorStore.Get(anchorKey));
            // 锚点放在鼠标位置（不是投影点）：线会拐过去经过它
            var at = SnapAnchor(point);

            // 按点击位置在曲线上的先后插进锚点序列，避免新锚点总被排到最后导致绕线
            var after = new List<Point>(before);
            int insert = AnchorInsertIndex(route, at, before);
            if (insert < 0 || insert > after.Count) insert = after.Count;
            after.Insert(insert, at);
            Services.FlowEdgeAnchorStore.Set(anchorKey, after);

            PushFlowUndo(
                () => { Services.FlowEdgeAnchorStore.Set(anchorKey, before); RebuildEdges(); },
                () => { Services.FlowEdgeAnchorStore.Set(anchorKey, after); RebuildEdges(); });

            HideAnchorPreview();
            RebuildEdges();
            e.Handled = true;
        }

        private void ShowAnchorPreview(Point at)
        {
            if (_anchorPreview == null)
            {
                _anchorPreview = new Ellipse
                {
                    Width = 11,
                    Height = 11,
                    Fill = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                    Stroke = LinkBrush,
                    StrokeThickness = 1.6,
                    IsHitTestVisible = false,
                };
                HandleLayer.Children.Add(_anchorPreview);
            }

            Canvas.SetLeft(_anchorPreview, at.X - 5.5);
            Canvas.SetTop(_anchorPreview, at.Y - 5.5);
            _anchorPreview.Visibility = Visibility.Visible;
        }

        private void HideAnchorPreview()
        {
            _hoverEdgeKey = null;
            if (_anchorPreview != null)
            {
                HandleLayer.Children.Remove(_anchorPreview);
                _anchorPreview = null;
            }
        }

        /// <summary>已放下的锚点：小方块，按住拖动可改变连线走向，右键删除</summary>
        private void AddAnchorDots(string edgeKey, IReadOnlyList<Point> anchors)
        {
            if (!ShowAnchors) return;

            string anchorKey = AnchorKey(edgeKey);
            for (int i = 0; i < anchors.Count; i++)
            {
                int index = i;
                var dot = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = LinkBrush,        // 实心小点，比原来小一圈
                    Stroke = Brushes.White,
                    StrokeThickness = 1.2,
                    Cursor = Cursors.SizeAll,
                    ToolTip = LanguageService.GetLocalizedString("Flow_Anchor_ToolTip", "锚点：拖动可改变连线走向，右键删除"),
                };
                Canvas.SetLeft(dot, anchors[i].X - 3.5);
                Canvas.SetTop(dot, anchors[i].Y - 3.5);

                dot.MouseLeftButtonDown += (s, e) =>
                {
                    _dragAnchorKey = anchorKey;
                    _dragAnchorIndex = index;
                    _dragAnchorBefore = new List<Point>(Services.FlowEdgeAnchorStore.Get(anchorKey));
                    RootGrid.CaptureMouse();
                    e.Handled = true;
                };
                dot.MouseRightButtonDown += (s, e) =>
                {
                    var before = new List<Point>(Services.FlowEdgeAnchorStore.Get(anchorKey));
                    var after = new List<Point>(before);
                    if (index >= 0 && index < after.Count) after.RemoveAt(index);
                    Services.FlowEdgeAnchorStore.Set(anchorKey, after);
                    PushFlowUndo(
                        () => { Services.FlowEdgeAnchorStore.Set(anchorKey, before); RebuildEdges(); },
                        () => { Services.FlowEdgeAnchorStore.Set(anchorKey, after); RebuildEdges(); });
                    RebuildEdges();
                    e.Handled = true;
                };

                HandleLayer.Children.Add(dot);
            }
        }

        #region 折线段拖动

        /// <summary>按住在线上的一段：整段沿垂直方向平移，松手后记一条撤销</summary>
        private void BeginSegmentDrag(string edgeKey, IReadOnlyList<Point> route, int segmentIndex, bool horizontal, Point mousePoint)
        {
            if (!SingletonLocator.Main.IsLoggedIn) return;
            if (segmentIndex < 1 || segmentIndex >= route.Count) return;

            _dragSegKey = edgeKey;
            _dragSegRoute = new List<Point>(route);
            _dragSegIndex = segmentIndex;
            _dragSegHorizontal = horizontal;
            _dragSegMouseStart = mousePoint;
            _dragSegBefore = new List<Point>(Services.FlowEdgeAnchorStore.Get(AnchorKey(edgeKey)));
            _dragSegMoved = false;
            HideAnchorPreview();
            HideSegmentHint();
            RootGrid.CaptureMouse();
        }

        /// <summary>把折线里的某一段沿垂直方向平移；贴着节点端口的那一段只挪靠里的拐点</summary>
        private static List<Point> MoveRouteSegment(IReadOnlyList<Point> route, int segmentIndex, bool horizontal, double shift)
        {
            var moved = new List<Point>(route);
            int a = segmentIndex - 1;
            int b = segmentIndex;

            Point Shift(Point p) => horizontal ? new Point(p.X, p.Y + shift) : new Point(p.X + shift, p.Y);

            if (a > 0) moved[a] = Shift(moved[a]);
            if (b < moved.Count - 1) moved[b] = Shift(moved[b]);
            return moved;
        }

        /// <summary>
        /// 走线转锚点：只留中间拐点。长度 0 的段直接去掉（相邻点重合就合并），
        /// 还在同一条直线上的点也去掉，免得线上留下没意义的小点。
        /// </summary>
        private static List<Point> RouteToAnchors(IReadOnlyList<Point> route)
        {
            var anchors = new List<Point>();
            for (int i = 1; i < route.Count - 1; i++)
            {
                var p = route[i];
                if (anchors.Count > 0 && (p - anchors[anchors.Count - 1]).Length < 1.0) continue;
                anchors.Add(p);
            }

            // 去掉与前后两点共线的锚点（正交折线里只剩真正拐弯的点）
            for (int i = anchors.Count - 2; i >= 1; i--)
            {
                var prev = i - 1 >= 0 ? anchors[i - 1] : route[0];
                var next = i + 1 < anchors.Count ? anchors[i + 1] : route[route.Count - 1];
                bool sameY = Math.Abs(prev.Y - anchors[i].Y) < 0.5 && Math.Abs(anchors[i].Y - next.Y) < 0.5;
                bool sameX = Math.Abs(prev.X - anchors[i].X) < 0.5 && Math.Abs(anchors[i].X - next.X) < 0.5;
                if (sameY || sameX) anchors.RemoveAt(i);
            }

            return anchors;
        }

        /// <summary>悬停到某一段时高亮它，提示「这段可以拖」</summary>
        private void ShowSegmentHint(string edgeKey, IReadOnlyList<Point> route, int segmentIndex)
        {
            if (segmentIndex < 1 || segmentIndex >= route.Count) { HideSegmentHint(); return; }

            if (_segHint == null)
            {
                _segHint = new Path
                {
                    Stroke = SegmentHintBrush,
                    StrokeThickness = 7,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false,
                };
                OverlayLayer.Children.Add(_segHint);
            }

            var figure = new PathFigure { StartPoint = route[segmentIndex - 1], IsClosed = false };
            figure.Segments.Add(new LineSegment(route[segmentIndex], true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            _segHint.Data = geometry;
        }

        private void HideSegmentHint()
        {
            if (_segHint != null && OverlayLayer.Children.Contains(_segHint))
                OverlayLayer.Children.Remove(_segHint);
            _segHint = null;
        }

        #endregion
        /// <summary>把点投影到节点边框上：返回最靠近的那条边与沿线比例</summary>
        private static (string Side, double Fraction) ProjectOnBorder(Rect rect, Point point)
        {
            double dLeft = Math.Abs(point.X - rect.Left);
            double dRight = Math.Abs(point.X - rect.Right);
            double dTop = Math.Abs(point.Y - rect.Top);
            double dBottom = Math.Abs(point.Y - rect.Bottom);
            double min = Math.Min(Math.Min(dLeft, dRight), Math.Min(dTop, dBottom));

            double ClampFrac(double value) => Math.Min(0.94, Math.Max(0.06, value));
            double width = Math.Max(1, rect.Width);
            double height = Math.Max(1, rect.Height);

            if (min == dLeft) return ("L", ClampFrac((point.Y - rect.Top) / height));
            if (min == dRight) return ("R", ClampFrac((point.Y - rect.Top) / height));
            if (min == dTop) return ("T", ClampFrac((point.X - rect.Left) / width));
            return ("B", ClampFrac((point.X - rect.Left) / width));
        }

        /// <summary>新锚点插在第几个：按它在当前连线上的弧长位置与已有锚点比较</summary>
        private static int AnchorInsertIndex(IReadOnlyList<Point> route, Point click, IReadOnlyList<Point> anchors)
        {
            double clickPosition = ArcPosition(route, click);
            for (int i = 0; i < anchors.Count; i++)
            {
                if (ArcPosition(route, anchors[i]) > clickPosition) return i;
            }
            return anchors.Count;
        }

        /// <summary>点在折线上对应的弧长位置（取最近的一段）</summary>
        private static double ArcPosition(IReadOnlyList<Point> route, Point point)
        {
            double bestDistance = double.MaxValue;
            double bestPosition = 0;
            double accumulated = 0;

            for (int i = 1; i < route.Count; i++)
            {
                var a = route[i - 1];
                var b = route[i];
                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double lengthSquared = dx * dx + dy * dy;
                double t = lengthSquared < 1e-6
                    ? 0
                    : Math.Max(0, Math.Min(1, ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared));

                var projected = new Point(a.X + dx * t, a.Y + dy * t);
                double distance = (projected - point).Length;
                double length = Math.Sqrt(lengthSquared);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPosition = accumulated + length * t;
                }

                accumulated += length;
            }

            return bestPosition;
        }

        /// <summary>鼠标点到折线上的最近点</summary>
        private static Point? ProjectOnPath(IReadOnlyList<Point> points, Point p, out double distance)
            => ProjectOnPath(points, p, out distance, out _, out _);

        private static Point? ProjectOnPath(IReadOnlyList<Point> points, Point p, out double distance, out int segmentIndex, out bool horizontal)
        {
            Point? best = null;
            double bestDistance = double.MaxValue;
            int bestIndex = -1;
            bool bestHorizontal = false;

            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1];
                var b = points[i];
                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double lengthSquared = dx * dx + dy * dy;
                double t = lengthSquared < 1e-6
                    ? 0
                    : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared));

                var projected = new Point(a.X + dx * t, a.Y + dy * t);
                double d = (projected - p).Length;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = projected;
                    bestIndex = i;
                    bestHorizontal = Math.Abs(dx) >= Math.Abs(dy);
                }
            }

            distance = bestDistance;
            segmentIndex = bestIndex;
            horizontal = bestHorizontal;
            return best;
        }

        /// <summary>已画好的连线分段，用来判断新线会不会和它们重叠</summary>
        /// <summary>当前这条线画到哪一层：焦点预览时无关的线画到变淡层</summary>
        private bool _drawingDim;
        private Canvas EdgeTarget => _drawingDim ? EdgeDimLayer : EdgeLayer;

        private readonly List<(Point A, Point B)> _drawnSegments = new List<(Point, Point)>();

        private void RegisterSegments(IReadOnlyList<Point> route)
        {
            for (int i = 1; i < route.Count; i++)
                _drawnSegments.Add((route[i - 1], route[i]));
        }

        /// <summary>
        /// 这段走线和已画连线的重叠总长度（同一高度/同一竖线的平行段才算重叠，
        /// 间隔达到端口间隔的线视为分开）。
        /// </summary>
        private double MeasureOverlap(IReadOnlyList<Point> route)
        {
            double tolerance = Math.Max(2.5, PortSpacing * 0.4);
            double total = 0;

            for (int i = 1; i < route.Count; i++)
            {
                var a = route[i - 1];
                var b = route[i];
                bool horizontal = Math.Abs(a.Y - b.Y) < 0.5;

                if (horizontal)
                {
                    double x1 = Math.Min(a.X, b.X);
                    double x2 = Math.Max(a.X, b.X);
                    if (x2 - x1 < 1) continue;

                    foreach (var (c, d) in _drawnSegments)
                    {
                        if (Math.Abs(c.Y - d.Y) >= 0.5) continue;          // 对方不是水平段
                        if (Math.Abs(c.Y - a.Y) > tolerance) continue;     // 不在同一高度
                        double overlap = Math.Min(x2, Math.Max(c.X, d.X)) - Math.Max(x1, Math.Min(c.X, d.X));
                        if (overlap > 0) total += overlap;
                    }
                }
                else
                {
                    double y1 = Math.Min(a.Y, b.Y);
                    double y2 = Math.Max(a.Y, b.Y);
                    if (y2 - y1 < 1) continue;

                    foreach (var (c, d) in _drawnSegments)
                    {
                        if (Math.Abs(c.X - d.X) >= 0.5) continue;
                        if (Math.Abs(c.X - a.X) > tolerance) continue;
                        double overlap = Math.Min(y2, Math.Max(c.Y, d.Y)) - Math.Max(y1, Math.Min(c.Y, d.Y));
                        if (overlap > 0) total += overlap;
                    }
                }
            }

            return total;
        }

        /// <summary>
        /// 绕行通道：从节点侧面出去 → 沿节点外侧的上/下通道横走 → 再从目标侧面进来。
        /// 用侧面而不是上下沿，避免在节点边框上冒出一小截竖线。
        /// </summary>
        private List<Point> BuildBandRoute(Rect from, Rect to, string sideFrom, string sideTo,
            int slotFrom, int slotTo, bool above)
        {
            var pA = PortPosition(from, sideFrom, slotFrom);
            var pB = PortPosition(to, sideTo, slotTo);
            var dA = PortDirection(sideFrom);
            var dB = PortDirection(sideTo);

            const double stub = 18;
            var sA = new Point(pA.X + dA.X * stub, pA.Y + dA.Y * stub);
            var sB = new Point(pB.X + dB.X * stub, pB.Y + dB.Y * stub);

            int lane = AllocateLane(above ? "BANDTOP" : "BANDBOTTOM");
            double extra = lane * 10;
            double band = above
                ? Math.Max(10, Math.Min(from.Top, to.Top) - _gapY / 2 - extra)
                : Math.Max(from.Bottom, to.Bottom) + _gapY / 2 + extra;

            var points = new List<Point>
            {
                pA,
                sA,
                new Point(sA.X, band),
                new Point(sB.X, band),
                sB,
                pB,
            };

            return SimplifyPath(points);
        }

        #endregion

        private void DrawFallbackOrthogonal(Rect fromRect, Rect toRect, Brush brush, double thickness, string label)
        {
            double fromMidX = fromRect.Left + fromRect.Width / 2;
            double fromMidY = fromRect.Top + fromRect.Height / 2;
            double toMidX = toRect.Left + toRect.Width / 2;
            double toMidY = toRect.Top + toRect.Height / 2;

            bool horizontal = Math.Abs(toMidX - fromMidX) >= Math.Abs(toMidY - fromMidY);

            Point start, end;
            if (horizontal)
            {
                bool targetRight = toMidX >= fromMidX;
                start = new Point(targetRight ? fromRect.Right : fromRect.Left, fromMidY);
                end = new Point(targetRight ? toRect.Left : toRect.Right, toMidY);
            }
            else
            {
                bool targetBelow = toMidY >= fromMidY;
                start = new Point(fromMidX, targetBelow ? fromRect.Bottom : fromRect.Top);
                end = new Point(toMidX, targetBelow ? toRect.Top : toRect.Bottom);
            }

            AddConnectionEdge(start, end, brush, thickness, label);
        }

        /// <summary>自引用：在节点上方画一个小回环（走行间空隙，不会被相邻节点挡住）</summary>
        private void DrawSelfLoop(Rect rect, Brush brush, double thickness, string label)
        {
            // 竖直排列时自循环绕到节点右侧，跟右侧的真/假连线走同一边
            if (IsVerticalArrange)
            {
                double y1 = rect.Top + 24;
                double y2 = rect.Top + 72;
                var rightStart = new Point(rect.Right, y1);
                var rightEnd = new Point(rect.Right, y2);
                var rightC1 = new Point(rect.Right + 34, y1);
                var rightC2 = new Point(rect.Right + 34, y2);
                AddBezierEdge(rightStart, rightC1, rightC2, rightEnd, brush, thickness, label,
                    BezierPoint(rightStart, rightC1, rightC2, rightEnd, 0.5));
                return;
            }

            double x1 = rect.Left + 34;
            double x2 = rect.Left + 92;
            var start = new Point(x1, rect.Top);
            var end = new Point(x2, rect.Top);
            var c1 = new Point(x1, rect.Top - 34);
            var c2 = new Point(x2, rect.Top - 34);
            AddBezierEdge(start, c1, c2, end, brush, thickness, label, BezierPoint(start, c1, c2, end, 0.5));
        }

        /// <summary>
        /// 大致同一行 / 同一列的连线：横向连到目标靠源的那一侧（源在左就连目标的左边），
        /// 竖向接到目标的上下边。容差按半个节点算，不要求像素级完全对齐。
        /// </summary>
        private bool TryDrawDirectConnection(Rect from, Rect to, Guid fromUid, Guid toUid,
            bool avoidBlockingNodes, Brush brush, double thickness, string label)
        {
            double fromMidX = from.Left + from.Width / 2;
            double fromMidY = from.Top + from.Height / 2;
            double toMidX = to.Left + to.Width / 2;
            double toMidY = to.Top + to.Height / 2;

            if (Math.Abs(toMidY - fromMidY) <= SameRowTolerance)
            {
                if (avoidBlockingNodes)
                {
                    var box = new Rect(
                        Math.Min(from.Left, to.Left),
                        Math.Min(fromMidY, toMidY) - 4,
                        Math.Max(from.Right, to.Right) - Math.Min(from.Left, to.Left),
                        Math.Abs(toMidY - fromMidY) + 8);
                    if (IsBoxBlocked(box, fromUid, toUid)) return false;
                }

                // 同一目标上已经接过线时，整体上下让开一点，起点终点都不会重合
                double shift = avoidBlockingNodes ? EdgeShiftY(toUid) : 0;
                bool targetRight = toMidX >= fromMidX;
                var start = new Point(targetRight ? from.Right : from.Left, fromMidY + shift);
                var end = new Point(targetRight ? to.Left : to.Right, toMidY + shift);
                AddConnectionEdge(start, end, brush, thickness, label);
                return true;
            }

            if (Math.Abs(toMidX - fromMidX) <= SameColumnTolerance)
            {
                if (avoidBlockingNodes)
                {
                    var box = new Rect(
                        Math.Min(fromMidX, toMidX) - 4,
                        Math.Min(from.Top, to.Top),
                        Math.Abs(toMidX - fromMidX) + 8,
                        Math.Max(from.Bottom, to.Bottom) - Math.Min(from.Top, to.Top));
                    if (IsBoxBlocked(box, fromUid, toUid)) return false;
                }

                double shift = avoidBlockingNodes ? EdgeShiftX(toUid) : 0;
                bool targetBelow = toMidY >= fromMidY;
                var start = new Point(fromMidX + shift, targetBelow ? from.Bottom : from.Top);
                var end = new Point(toMidX + shift, targetBelow ? to.Top : to.Bottom);
                AddConnectionEdge(start, end, brush, thickness, label);
                return true;
            }

            return false;
        }

        /// <summary>横向直线：按目标已接了几根线整体上下让开</summary>
        private double EdgeShiftY(Guid toUid)
        {
            int lane = AllocateLane("H" + toUid.ToString("N"));
            return ((lane % 3) - 1) * 12;
        }

        /// <summary>竖向直线：按目标已接了几根线整体左右让开</summary>
        private double EdgeShiftX(Guid toUid)
        {
            int lane = AllocateLane("V" + toUid.ToString("N"));
            return ((lane % 3) - 1) * 12;
        }

        private bool IsBoxBlocked(Rect box, Guid fromUid, Guid toUid)
        {
            foreach (var pair in _nodes)
            {
                if (pair.Key == fromUid || pair.Key == toUid) continue;
                if (!TryGetNodeRect(pair.Value, out var rect)) continue;
                if (rect.IntersectsWith(box)) return true;
            }
            return false;
        }

        /// <summary>
        /// 同一行但中间夹着别的节点：从上/下方的空隙横过去，再贴着目标侧面拐进去，
        /// 这样连到的是目标的左侧（源在左时），也不会被中间的节点挡住。
        /// </summary>
        private bool TryDrawBandJumpToSide(Rect from, Rect to, Brush brush, double thickness, string label)
        {
            double fromMidX = from.Left + from.Width / 2;
            double fromMidY = from.Top + from.Height / 2;
            double toMidX = to.Left + to.Width / 2;
            double toMidY = to.Top + to.Height / 2;

            if (Math.Abs(toMidY - fromMidY) > SameRowTolerance) return false;

            bool targetRight = toMidX >= fromMidX;
            double bandAbove = Math.Min(from.Top, to.Top) - _gapY / 2;
            bool above = bandAbove >= 10;
            double band = above ? bandAbove : Math.Max(from.Bottom, to.Bottom) + _gapY / 2;
            double entryX = targetRight ? to.Left - 12 : to.Right + 12;

            var points = new List<Point>
            {
                new Point(fromMidX, above ? from.Top : from.Bottom),
                new Point(fromMidX, band),
                new Point(entryX, band),
                new Point(entryX, toMidY),
                new Point(targetRight ? to.Left : to.Right, toMidY),
            };

            points = SimplifyPath(points);
            if (points.Count < 2) return false;

            AddPolylineEdge(points, brush, thickness, label, StaggeredLabelPoint(points));
            return true;
        }

        /// <summary>水平/竖向的平滑连线：两端同高时就是直线，略偏时用中点控制点走个小 S</summary>
        private void AddSmoothEdge(Point start, Point end, Brush brush, double thickness, string label)
        {
            double midX = (start.X + end.X) / 2;
            double midY = (start.Y + end.Y) / 2;

            Point c1, c2;
            if (Math.Abs(end.X - start.X) >= Math.Abs(end.Y - start.Y))
            {
                c1 = new Point(midX, start.Y);
                c2 = new Point(midX, end.Y);
            }
            else
            {
                c1 = new Point(start.X, midY);
                c2 = new Point(end.X, midY);
            }

            AddBezierEdge(start, c1, c2, end, brush, thickness, label, OffLineLabelPoint(start, end));
        }

        /// <summary>按设置选连线样式：折线模式走方正的 Z 形折线，曲线模式走平滑曲线</summary>
        private void AddConnectionEdge(Point start, Point end, Brush brush, double thickness, string label)
        {
            if (SingletonLocator.Settings.Step.FlowEdgeStyle == 1)
            {
                AddSmoothEdge(start, end, brush, thickness, label);
                return;
            }

            double midX = (start.X + end.X) / 2;
            double midY = (start.Y + end.Y) / 2;

            List<Point> points;
            if (Math.Abs(end.X - start.X) >= Math.Abs(end.Y - start.Y))
            {
                points = new List<Point>
                {
                    start,
                    new Point(midX, start.Y),
                    new Point(midX, end.Y),
                    end,
                };
            }
            else
            {
                points = new List<Point>
                {
                    start,
                    new Point(start.X, midY),
                    new Point(end.X, midY),
                    end,
                };
            }

            points = SimplifyPath(points);
            AddPolylineEdge(points, brush, thickness, label, OffLineLabelPoint(start, end));
        }

        /// <summary>
        /// 真/假跳转：在行距、列距留出的通道里走正交折线，
        /// 从源节点上下边进入通道 → 沿通道横走 → 在目标列的竖直通道里上下 → 拐进目标节点，
        /// 全程不压到其他节点（节点也就不会把连线挡住）。
        /// 同一条通道里已经有过连线时自动往外让一格，多条跳转线不会叠成一条。
        /// </summary>
        private bool TryDrawGridJump(Rect from, Rect to, Guid fromUid, Guid toUid,
            Brush brush, double thickness, string label)
        {
            if (!TryInferCell(from, out int rowA, out int colA)) return false;
            if (!TryInferCell(to, out int rowB, out int colB)) return false;

            // 进出口不都落在正中间：同一条边上有几根线就从左到右依次错开，竖向的线不会叠在一起
            double ax = EdgeSlotX(from, "S" + fromUid.ToString("N"));
            double bx = EdgeSlotX(to, "E" + toUid.ToString("N"));

            var points = new List<Point>();

            if (rowB > rowA)
            {
                // 从源所在行下方通道横走，再从目标所在行上方通道拐进目标
                double bandA = BandBelow(rowA);
                double bandB = BandAbove(rowB);
                // 真假跳转统一走目标左侧的竖向通道，和引用连线的右侧通道分开
                double gutter = LeftGutter(colB, GutterShift(colB));

                points.Add(new Point(ax, from.Bottom));
                points.Add(new Point(ax, bandA));
                points.Add(new Point(gutter, bandA));
                points.Add(new Point(gutter, bandB));
                points.Add(new Point(bx, bandB));
                points.Add(new Point(bx, to.Top));
            }
            else if (rowB < rowA)
            {
                double bandA = BandAbove(rowA);
                double bandB = BandBelow(rowB);
                // 真假跳转统一走目标左侧的竖向通道，和引用连线的右侧通道分开
                double gutter = LeftGutter(colB, GutterShift(colB));

                points.Add(new Point(ax, from.Top));
                points.Add(new Point(ax, bandA));
                points.Add(new Point(gutter, bandA));
                points.Add(new Point(gutter, bandB));
                points.Add(new Point(bx, bandB));
                points.Add(new Point(bx, to.Bottom));
            }
            else
            {
                // 同一行：真假跳转统一走下方通道（引用连线走上方），多条时靠通道分道错开
                double band = BandBelow(rowA);

                points.Add(new Point(ax, from.Bottom));
                points.Add(new Point(ax, band));
                points.Add(new Point(bx, band));
                points.Add(new Point(bx, to.Bottom));
            }

            points = SimplifyPath(points);
            if (points.Count < 2) return false;

            AddPolylineEdge(points, brush, thickness, label, StaggeredLabelPoint(points));
            return true;
        }

        /// <summary>节点进出线位置：同一条边上的第 n 根线往右让 n 个槽位</summary>
        private double EdgeSlotX(Rect rect, string key)
        {
            int slot = AllocateLane(key) % 4;
            double width = Math.Max(0, rect.Width - 90);
            return rect.Left + 45 + slot * (width / 3.0);
        }

        /// <summary>
        /// 标签位置：落在自己那根线上，但沿着线的方向左右错开，
        /// 这样同一通道里多根线的标签不会叠在一起，也不用往上/往下占高度。
        /// </summary>
        private Point StaggeredLabelPoint(IReadOnlyList<Point> points)
        {
            var anchor = LabelAnchor(points, out bool horizontal, out Point a, out Point b);
            double t = ClampT();

            if (horizontal)
            {
                double dx = b.X - a.X;
                if (Math.Abs(dx) < 1) return anchor;
                return new Point(a.X + dx * t, a.Y);
            }

            double dy = b.Y - a.Y;
            if (Math.Abs(dy) < 1) return anchor;
            return new Point(a.X, a.Y + dy * t);
        }

        /// <summary>按连线序号取 0.24 / 0.5 / 0.76 三个错开位置</summary>
        private double ClampT()
        {
            double offset = ((_edgeLane % 3) - 1) * 0.26;
            return 0.5 + offset;
        }

        /// <summary>直线类连线的标签：落在线上，按序号沿线的方向错开</summary>
        private Point OffLineLabelPoint(Point start, Point end)
        {
            double t = ClampT();
            return new Point(
                start.X + (end.X - start.X) * t,
                start.Y + (end.Y - start.Y) * t);
        }

        /// <summary>该行下方的走线通道，同一条通道里已占用时往外让出 LaneStep 的整数倍</summary>
        private double BandBelow(int row)
        {
            int lane = AllocateLane("D" + row);
            double offset = Math.Min(lane * LaneStep, Math.Max(0, _gapY - 8));
            return Y0 + row * CellH + NodeHeight + _gapY / 2 + offset;
        }

        /// <summary>该行上方的走线通道（往上让）</summary>
        private double BandAbove(int row)
        {
            int lane = AllocateLane("U" + row);
            double offset = Math.Min(lane * LaneStep, Math.Max(0, _gapY - 8));
            return Y0 + row * CellH - _gapY / 2 - offset;
        }

        /// <summary>列通道里的竖向连线也左右让开一点，避免共道重叠</summary>
        private double GutterShift(int col)
        {
            int lane = AllocateLane("G" + col);
            double limit = Math.Max(0, _gapX / 2 - 5);
            double offset = ((lane % 3) - 1) * LaneStep;
            return Math.Max(-limit, Math.Min(limit, offset));
        }

        /// <summary>给某个通道分配下一个空位（返回已占用的条数）</summary>
        private int AllocateLane(string key)
        {
            _bandLanes.TryGetValue(key, out int used);
            _bandLanes[key] = used + 1;
            return used;
        }

        /// <summary>跳转的竖向通道：在目标列左右两条空隙里选离源节点更近的那条</summary>
        private double JumpGutter(int colB, double sourceX, double shift)
        {
            double leftGutter = X0 + colB * CellW - _gapX / 2;
            double rightGutter = X0 + colB * CellW + NodeWidth + _gapX / 2;

            double chosen = Math.Abs(sourceX - leftGutter) <= Math.Abs(sourceX - rightGutter)
                ? leftGutter
                : rightGutter;
            return chosen + shift;
        }

        /// <summary>目标列左侧的竖向通道（真假跳转固定走左边，避开引用连线的右边）</summary>
        private double LeftGutter(int colB, double shift)
        {
            double limit = Math.Max(0, _gapX / 2 - 5);
            double clamped = Math.Max(-limit, Math.Min(limit, shift));
            return Math.Max(6, X0 + colB * CellW - _gapX / 2 + clamped);
        }

        private bool TryInferCell(Rect rect, out int row, out int col)
        {
            col = (int)Math.Round((rect.Left - X0) / CellW);
            row = (int)Math.Round((rect.Top - Y0) / CellH);
            if (col < 0 || row < 0) return false;

            return Math.Abs(rect.Left - (X0 + col * CellW)) <= 2
                && Math.Abs(rect.Top - (Y0 + row * CellH)) <= 2;
        }

        private static List<Point> SimplifyPath(List<Point> points)
        {
            var result = new List<Point>(points.Count);
            foreach (var point in points)
            {
                if (result.Count == 0 || (result[result.Count - 1] - point).Length > 0.5)
                    result.Add(point);
            }
            return result;
        }

        /// <summary>标签放在最长的水平段中间，读起来最不挤</summary>
        private static Point LabelAnchor(IReadOnlyList<Point> points, out bool horizontal, out Point segA, out Point segB)
        {
            Point best = points[points.Count / 2];
            double bestLength = -1;
            horizontal = true;
            segA = best;
            segB = best;
            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1];
                var b = points[i];
                double dx = Math.Abs(b.X - a.X);
                double dy = Math.Abs(b.Y - a.Y);
                double length = Math.Max(dx, dy);
                if (length > bestLength)
                {
                    bestLength = length;
                    best = MidPoint(a, b);
                    horizontal = dx >= dy;
                    segA = a;
                    segB = b;
                }
            }
            return best;
        }

        private static Point MidPoint(Point a, Point b) => new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        private void DrawCurvedEdge(Rect fromRect, Rect toRect, Brush brush, double thickness, string label, bool dashed = false)
        {
            Point start, control1, control2, end;

            if (toRect.Top >= fromRect.Bottom - 8)
            {
                // 目标在下方：竖向 S 曲线
                start = new Point(fromRect.Left + fromRect.Width / 2, fromRect.Bottom);
                end = new Point(toRect.Left + toRect.Width / 2, toRect.Top);
                double midY = (start.Y + end.Y) / 2;
                control1 = new Point(start.X, midY);
                control2 = new Point(end.X, midY);
            }
            else
            {
                // 目标在上方（或同高）：从离目标更近的一侧绕过去
                bool rightSide = toRect.Left + toRect.Width / 2 >= fromRect.Left + fromRect.Width / 2;
                double baseX = rightSide ? Math.Max(fromRect.Right, toRect.Right) : Math.Min(fromRect.Left, toRect.Left);
                double bulge = Math.Max(56, Math.Abs(fromRect.Top - toRect.Top) * 0.35) * (rightSide ? 1 : -1);

                start = new Point(rightSide ? fromRect.Right : fromRect.Left, fromRect.Top + fromRect.Height / 2);
                end = new Point(rightSide ? toRect.Right : toRect.Left, toRect.Top + toRect.Height / 2);
                control1 = new Point(baseX + bulge, start.Y);
                control2 = new Point(baseX + bulge, end.Y);
            }

            AddBezierEdge(start, control1, control2, end, brush, thickness, label,
                BezierPoint(start, control1, control2, end, 0.5), dashed);
        }

        /// <summary>引用步骤的连线：正交折线，走「上方通道 + 目标右侧竖向通道」，末端从上边进，不压顺序连线</summary>
        private void DrawReferenceEdge(AutomationStepBase from, AutomationStepBase to, Models.StepCondition condition, string edgeKey)
        {
            if (!_nodes.TryGetValue(from.Uid, out var fromNode)) return;
            if (!_nodes.TryGetValue(to.Uid, out var toNode)) return;
            if (!TryGetNodeRect(fromNode, out var fromRect) || !TryGetNodeRect(toNode, out var toRect)) return;

            _drawingEdgeEnd = null;
            _edgeLane++;

            // 焦点预览：与选中步骤无关的引用线也变淡
            _drawingDim = FocusMode && _selectedUids.Count > 0
                && !IsSelected(from) && !IsSelected(to);
            try {
            // 引用出口在右侧，引用线按最短路径自动选边（不再强制从左边进出）
            var points = DrawRoutedEdge(fromRect, toRect, from.Uid, to.Uid, EdgeBothBrush, 1.5,
                LanguageService.GetLocalizedString("FlowReference", "引用"), true, edgeKey, null);
            if (points == null) return;

            AddReferenceHitArea(points, from, to, condition);
            }
            finally { _drawingDim = false; }
        }

        private void AddBezierEdge(Point start, Point control1, Point control2, Point end,
            Brush brush, double thickness, string label, Point labelPoint, bool dashed = false)
        {
            var path = new Path
            {
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = CreateBezierGeometry(start, control1, control2, end),
            };
            if (dashed)
                path.StrokeDashArray = new DoubleCollection { 5, 3 };
            EdgeTarget.Children.Add(path);

            AddArrowHead(end, end - control2, brush, Math.Max(9, thickness * 5));
            if (!string.IsNullOrEmpty(label))
                AddEdgeLabel(labelPoint, label, brush);
        }

        private void AddPolylineEdge(IReadOnlyList<Point> points, Brush brush, double thickness, string label, Point labelPoint, bool dashed = false)
        {
            var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
            for (int i = 1; i < points.Count; i++)
                figure.Segments.Add(new LineSegment(points[i], true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();

            var path = new Path
            {
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = geometry,
            };
            if (dashed)
                path.StrokeDashArray = new DoubleCollection { 5, 3 };
            EdgeTarget.Children.Add(path);

            _drawingEdgeStart = points[0];

            var end = points[points.Count - 1];
            var previous = points[points.Count - 2];
            AddArrowHead(end, end - previous, brush, Math.Max(9, thickness * 5));

            if (!string.IsNullOrEmpty(label))
                AddEdgeLabel(labelPoint, label, brush);
        }

        private void AddArrowHead(Point tip, Vector direction, Brush brush, double size = 9)
        {
            if (size < 1) size = 9;
            if (direction.Length < 0.01) direction = new Vector(0, 1);
            direction.Normalize();
            var back = tip - direction * size;
            var perpendicular = new Vector(-direction.Y, direction.X) * (size * 0.5);

            var arrow = new Polygon
            {
                Fill = brush,
                Points = new PointCollection { tip, back + perpendicular, back - perpendicular },
            };
            EdgeTarget.Children.Add(arrow);

            // 记下末端位置，跳转连线的拖动把手就挂在箭头三角形上
            _drawingEdgeEnd = tip;
        }

        /// <summary>
        /// 连线两端的拖动热区：按住它可以在节点边框上挪动这个端点；
        /// 终点拖出节点范围就变成重新选择目标节点（起点只能贴在本节点边框上挪）。
        /// </summary>
        private void AddEdgeHandle(Point at, AutomationStepBase from, AutomationStepBase to, bool isTrue, string edgeKey, bool isStart)
        {
            if (string.IsNullOrEmpty(edgeKey)) return;


            var hit = new Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = Brushes.Transparent,
                Cursor = Cursors.SizeAll,
                ToolTip = isStart
                    ? LanguageService.GetLocalizedString("FlowChart_DragStart_ToolTip", "按住可沿本节点边框移动起点")
                    : LanguageService.GetLocalizedString("FlowChart_DragEnd_ToolTip", "按住可在边框上移动终点；拖到别的步骤上改接，拖到空白处取消"),
                Tag = new EdgeHandleTag { From = from, IsTrue = isTrue, EdgeKey = edgeKey, IsStart = isStart },
            };
            Canvas.SetLeft(hit, at.X - 9);
            Canvas.SetTop(hit, at.Y - 9);
            hit.MouseLeftButtonDown += EdgePortHandle_MouseLeftButtonDown;
            HandleLayer.Children.Add(hit);
        }

        /// <summary>按下连线端点：开始「沿边框挪端点」，终点拖出节点后再变成改接</summary>
        private void EdgePortHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {

            if (sender is not FrameworkElement handle) return;
            if (handle.Tag is not EdgeHandleTag tag || tag.From == null || string.IsNullOrEmpty(tag.EdgeKey)) return;
            if (!SingletonLocator.Main.IsLoggedIn) return;

            var step = tag.From;
            Guid otherUid = tag.IsStart
                ? (step.TrueGotoUid == null ? Guid.Empty : Guid.Empty)   // 起点只用本节点
                : Guid.Empty;
            var nodeUid = tag.IsStart ? step.Uid : FindTargetUid(step, tag.IsTrue);
            if (nodeUid == Guid.Empty || !_nodes.TryGetValue(nodeUid, out var node)) return;
            if (!TryGetNodeRect(node, out var rect)) return;

            _dragPortKey = tag.EdgeKey;
            _dragPortIsStart = tag.IsStart;
            _dragPortNodeUid = nodeUid;
            _dragPortNodeRect = rect;
            _dragPortFrom = step;
            _dragPortIsTrue = tag.IsTrue;
            _dragPortMoved = false;
            _dragPortRetargeting = false;
            FlowPortStore.TryGet(tag.EdgeKey + (tag.IsStart ? "|A" : "|B"), out var beforeSide, out var beforeFrac);
            _dragPortBeforeSide = beforeSide;
            _dragPortBeforeFraction = beforeFrac;
            _dragPortHadBefore = beforeSide != null;

            RootGrid.CaptureMouse();
            e.Handled = true;
        }

        /// <summary>该分支当前指向的目标步骤 Uid</summary>
        private static Guid FindTargetUid(AutomationStepBase step, bool isTrue)
        {
            var uid = isTrue ? step.TrueGotoUid : step.FalseGotoUid;
            return uid.HasValue && uid.Value != Guid.Empty ? uid.Value : Guid.Empty;
        }

        /// <summary>
        /// 引用连线的整条线都是热区：透明加宽描边盖在整条线上，
        /// 左键按住可拖到别的步骤改引用，右键弹出 编辑/删除 菜单
        /// </summary>
        private void AddReferenceHitArea(IReadOnlyList<Point> points, AutomationStepBase from, AutomationStepBase to, Models.StepCondition condition)
        {
            var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
            for (int i = 1; i < points.Count; i++)
                figure.Segments.Add(new LineSegment(points[i], true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();

            var hit = new Path
            {
                Data = geometry,
                Stroke = Brushes.Transparent,
                StrokeThickness = 12,
                Cursor = Cursors.SizeAll,
                ToolTip = LanguageService.GetLocalizedString("FlowReference_DragEnd_ToolTip", "按住箭头拖动可改引用其他步骤；右键可编辑或删除"),
                Tag = new EdgeHandleTag { From = from, Condition = condition },
            };
            hit.MouseLeftButtonDown += EdgeHandle_MouseLeftButtonDown;
            hit.MouseRightButtonDown += (s, e) =>
            {
                ShowReferenceMenu(hit, from, condition);
                e.Handled = true;
            };
            HandleLayer.Children.Add(hit);
        }

        /// <summary>引用连线的右键菜单：编辑（打开结果判断窗口）/ 删除这条引用规则</summary>
        private void ShowReferenceMenu(FrameworkElement target, AutomationStepBase from, Models.StepCondition condition)
        {
            if (from == null || condition == null) return;

            var menu = new ContextMenu
            {
                PlacementTarget = target,
                Placement = PlacementMode.MousePoint,
            };

            menu.Items.Add(new MenuItem
            {
                Header = LanguageService.GetLocalizedString("Edit"),
                Command = new RelayCommand(() =>
                {
                    if (!from.Conditions.Contains(condition)) return;
                    Views.WindowEditConditions.Edit(from);
                    RebuildEdges();
                }),
            });

            menu.Items.Add(new MenuItem
            {
                Header = LanguageService.GetLocalizedString("Delete"),
                Command = new RelayCommand(() =>
                {
                    if (!from.Conditions.Contains(condition)) return;
                    from.Conditions.Remove(condition);
                    PushFlowUndoFor(from,
                        s => { if (!s.Conditions.Contains(condition)) s.Conditions.Add(condition); },
                        s => s.Conditions.Remove(condition));
                    RebuildEdges();
                }),
            });

            menu.IsOpen = true;
        }

        private void AddEdgeLabel(Point at, string text, Brush brush)
        {
            var label = new Border
            {
                Background = Brushes.White,
                BorderBrush = brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4, 0, 4, 0),
                Child = new TextBlock { Text = text, FontSize = 10, Foreground = brush },
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, at.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, at.Y - label.DesiredSize.Height / 2);
            EdgeTarget.Children.Add(label);
        }

        private static PathGeometry CreateBezierGeometry(Point start, Point control1, Point control2, Point end)
        {
            var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
            figure.Segments.Add(new BezierSegment(control1, control2, end, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();
            return geometry;
        }

        private static Point BezierPoint(Point p0, Point c1, Point c2, Point p3, double t)
        {
            double u = 1 - t;
            double a = u * u * u;
            double b = 3 * u * u * t;
            double c = 3 * u * t * t;
            double d = t * t * t;
            return new Point(
                a * p0.X + b * c1.X + c * c2.X + d * p3.X,
                a * p0.Y + b * c1.Y + c * c2.Y + d * p3.Y);
        }

        #endregion

        #region 工具方法

        private bool TryGetNodeRect(ContentControl node, out Rect rect)
        {
            rect = Rect.Empty;
            if (node == null) return false;

            double left = Canvas.GetLeft(node);
            double top = Canvas.GetTop(node);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            rect = new Rect(left, top, NodeSlotWidth, NodeHeight);
            return true;
        }

        /// <summary>节点对应的步骤：DataContext 与 Content 都兜底，避免命中测试因为继承的 DataContext 落空</summary>
        private static AutomationStepBase StepOf(ContentControl node)
            => (node?.DataContext as AutomationStepBase) ?? (node?.Content as AutomationStepBase);

        /// <summary>从模板内部的元素往上找所属的步骤节点（出口圆点、日志开关都在节点模板里）</summary>
        private static AutomationStepBase StepOfElement(DependencyObject element)
        {
            for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is ContentControl control && StepOf(control) is AutomationStepBase step)
                    return step;
            }
            return null;
        }

        private static T FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == name) return element;

                var found = FindChild<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static Brush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        #endregion

        /// <summary>连线端点把手携带的信息：从哪个步骤的那一支拖出来</summary>
        private sealed class EdgeHandleTag
        {
            public AutomationStepBase From { get; set; }
            public bool IsTrue { get; set; }
            /// <summary>引用连线：对应的那条条件规则（拖动时改它的引用目标）</summary>
            public Models.StepCondition Condition { get; set; }
            /// <summary>连线标识（端点拖动时按它记落点）</summary>
            public string EdgeKey { get; set; }
            public bool IsStart { get; set; }
        }

        /// <summary>同一个「从 → 到」上的连线（顺序下落 / 真跳转 / 假跳转）合并成一条</summary>
        private sealed class EdgeSpec
        {
            public EdgeSpec(AutomationStepBase from, AutomationStepBase to)
            {
                From = from;
                To = to;
            }

            public AutomationStepBase From { get; }
            public AutomationStepBase To { get; }
            public bool IsTrue { get; private set; }
            public bool IsFalse { get; private set; }
            public bool IsFallThrough { get; private set; }
            /// <summary>列表里紧挨着的两步（顺序边）</summary>
            public bool IsSequential { get; set; }

            public void MarkTrue() => IsTrue = true;
            public void MarkFalse() => IsFalse = true;
            public void MarkFallThrough() => IsFallThrough = true;
        }
    }
}
