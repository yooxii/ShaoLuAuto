using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WPFDevelopers.Controls;

namespace ShaoLu.Views
{
    /// <summary>
    /// UserControlSteps.xaml 的交互逻辑
    /// </summary>
    public partial class UserControlSteps : UserControl
    {
        public StepsViewModel stepsViewModel = SingletonLocator.Steps;

        /// <summary>最近一次获得键盘焦点的区域是否在步骤详情面板（用于菜单快捷键路由）</summary>
        private bool _lastFocusInStepDetails;

        public UserControlSteps()
        {
            InitializeComponent();
            DataContext = stepsViewModel;
            AddHandler(Keyboard.GotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(OnGotKeyboardFocus), true);
            // 登录状态变化时刷新「用户输入」冻结字段的可编辑性
            Loaded += (_, __) => HookLoginState(true);
            Unloaded += (_, __) => HookLoginState(false);
            // 宽度变化时自动决定组别是全部展开还是折叠
            Loaded += (_, __) => RefreshToolbarGroupLayout();
            SizeChanged += (_, __) => RefreshToolbarGroupLayout();
        }

        #region 工具栏分组折叠（编辑 / 步骤 互斥）

        /// <summary>展开「编辑」组时收起「步骤」组</summary>
        private void EditGroupToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_groupLayoutBusy) return;
            // 不溢出时允许两组同时展开；溢出时才互斥
            if (!_toolbarOverflow) return;
            if (StepGroupToggle?.IsChecked == true)
                StepGroupToggle.IsChecked = false;
        }

        /// <summary>展开「步骤」组时收起「编辑」组（仅溢出时）</summary>
        private void StepGroupToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_groupLayoutBusy) return;
            if (!_toolbarOverflow) return;
            if (EditGroupToggle?.IsChecked == true)
                EditGroupToggle.IsChecked = false;
        }

        private bool _groupLayoutBusy;
        private bool _toolbarOverflow;

        /// <summary>
        /// 根据可用宽度决定组别展开状态：全部展开后若放不下（溢出）就折叠编辑组；
        /// 放得下则两组全部展开。布局期间绕过互斥，用户手动点击时仍互斥。
        /// </summary>
        private void RefreshToolbarGroupLayout()
        {
            if (_groupLayoutBusy || EditGroupToggle == null || StepGroupToggle == null) return;
            _groupLayoutBusy = true;
            try
            {
                EditGroupToggle.IsChecked = true;
                StepGroupToggle.IsChecked = true;
                UpdateLayout();

                if (GroupToolBarTray == null) return;
                GroupToolBarTray.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double needed = GroupToolBarTray.DesiredSize.Width;
                double available = ActualWidth;

                // 放不下就折叠编辑组，只保留步骤组；结果同时决定手动点击是否互斥
                _toolbarOverflow = needed > available - 4;
                if (_toolbarOverflow)
                    EditGroupToggle.IsChecked = false;
            }
            catch { }
            finally
            {
                _groupLayoutBusy = false;
            }
        }

        #endregion

        #region 登录状态 → 冻结字段刷新

        private bool _loginStateHooked;

        /// <summary>订阅/取消订阅主视图模型的登录状态变化</summary>
        private void HookLoginState(bool hook)
        {
            try
            {
                var main = SingletonLocator.Main;
                if (main == null) return;
                if (hook && !_loginStateHooked)
                {
                    main.PropertyChanged += OnMainPropertyChanged;
                    _loginStateHooked = true;
                    RefreshUserInputFieldLocks();
                }
                else if (!hook && _loginStateHooked)
                {
                    main.PropertyChanged -= OnMainPropertyChanged;
                    _loginStateHooked = false;
                }
            }
            catch { }
        }

        private void OnMainPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ShaoLu.Viewmodels.MainViewModel.IsLoggedIn))
                RefreshUserInputFieldLocks();
        }

        /// <summary>重新计算所有「用户输入」步骤字段的冻结/可编辑状态（登录后解锁、退出后重新锁定）</summary>
        public void RefreshUserInputFieldLocks()
        {
            try
            {
                var steps = stepsViewModel?.AutomationStepBases;
                if (steps == null) return;
                foreach (var step in steps)
                {
                    if (step is ShaoLu.Viewmodels.AutomationStep.UserInputStep userInput && userInput.Fields != null)
                    {
                        foreach (var inputField in userInput.Fields)
                            inputField?.RefreshLockState();
                    }
                }
            }
            catch { }
        }

        #endregion

        private void Refresh_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            UpdateLayout();
        }

        private void SplitButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var btn = (SplitButton)sender;
            if (btn.CommandParameter is string type)
            {
                stepsViewModel.AddStepCommand.Execute(type);
            }
        }

        private void MenuItemAddOtherStep_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = (MenuItem)sender;
            // 更新「发出这条菜单的 SplitButton」的 CommandParameter，
            // 这样菜单选择后主按钮显示什么、点下去就添加什么（此前固定写死 AddOCRStep，
            // 导致「多线程」这类其他 SplitButton 选完仍是第一个菜单项）
            var owner = ResolveOwnerSplitButton(menuItem);
            if (owner != null)
                owner.CommandParameter = menuItem.CommandParameter;
            if (menuItem.CommandParameter is string type)
            {
                stepsViewModel.AddStepCommand.Execute(type);
            }
        }

        private void MenuItemAddImageStep_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = (MenuItem)sender;
            var owner = ResolveOwnerSplitButton(menuItem);
            if (owner != null)
                owner.CommandParameter = menuItem.CommandParameter;
            if (menuItem.CommandParameter is string type)
            {
                stepsViewModel.AddStepCommand.Execute(type);
            }
        }

        /// <summary>找到菜单项所属的 SplitButton（菜单项被移入 ContextMenu，PlacementTarget 指向 SplitButton）</summary>
        private static SplitButton ResolveOwnerSplitButton(MenuItem menuItem)
        {
            if (menuItem == null) return null;
            if (menuItem.Parent is ContextMenu menu && menu.PlacementTarget is SplitButton owner)
                return owner;
            return null;
        }

        #region 撤销 / 恢复焦点路由

        /// <summary>当前焦点是否位于步骤详情面板内</summary>
        public bool IsFocusInStepDetails()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (focused != null && IsDescendantOf(focused, StepDetailsPanel)) return true;
            if (focused != null && IsDescendantOf(focused, StepsBox)) return false;
            // 焦点在菜单/工具栏等区域时，沿用最近一次编辑区域
            return _lastFocusInStepDetails;
        }

        public bool CanRouteUndo()
        {
            return IsFocusInStepDetails()
                ? stepsViewModel.UndoStepCommand.CanExecute(null)
                : stepsViewModel.UndoCommand.CanExecute(null);
        }

        public bool CanRouteRedo()
        {
            return IsFocusInStepDetails()
                ? stepsViewModel.RedoStepCommand.CanExecute(null)
                : stepsViewModel.RedoCommand.CanExecute(null);
        }

        /// <summary>按当前焦点区域执行撤销：步骤列表→列表撤销；步骤详情→步骤内撤销</summary>
        public void RouteUndo()
        {
            if (IsFocusInStepDetails())
                stepsViewModel.UndoStepCommand.Execute(null);
            else
                stepsViewModel.UndoCommand.Execute(null);
        }

        /// <summary>按当前焦点区域执行恢复：步骤列表→列表恢复；步骤详情→步骤内恢复</summary>
        public void RouteRedo()
        {
            if (IsFocusInStepDetails())
                stepsViewModel.RedoStepCommand.Execute(null);
            else
                stepsViewModel.RedoCommand.Execute(null);
        }

        private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var element = e.NewFocus as DependencyObject;
            if (element == null) return;
            if (IsDescendantOf(element, StepDetailsPanel))
                _lastFocusInStepDetails = true;
            else if (IsDescendantOf(element, StepsBox))
                _lastFocusInStepDetails = false;
        }

        private static bool IsDescendantOf(DependencyObject element, DependencyObject ancestor)
        {
            while (element != null)
            {
                if (ReferenceEquals(element, ancestor)) return true;
                element = GetParent(element);
            }
            return false;
        }

        private static DependencyObject GetParent(DependencyObject element)
        {
            try
            {
                if (element is Visual || element is System.Windows.Media.Media3D.Visual3D)
                    return VisualTreeHelper.GetParent(element);
            }
            catch { }
            return LogicalTreeHelper.GetParent(element);
        }

        #endregion
    }

    /// <summary>
    /// 「用户输入」步骤详情里的冻结字段编辑守卫。
    /// 步骤详情由 DataTemplate 渲染、没有代码后置，无法直接挂事件，
    /// 因此用附加属性把事件挂到模板内的 DataGrid / 值输入框上：
    /// - GuardLockedEdits（DataGrid）：开始编辑时拦截锁定字段；
    /// - GuardLockedValue（值输入框）：点击/键入时提示“登录后修改”（只读状态下仍允许选中复制）。
    /// </summary>
    public static class UserInputFieldLockGuard
    {
        #region DataGrid：拦截开始编辑

        public static readonly DependencyProperty GuardLockedEditsProperty =
            DependencyProperty.RegisterAttached("GuardLockedEdits", typeof(bool), typeof(UserInputFieldLockGuard),
                new PropertyMetadata(false, OnGuardLockedEditsChanged));

        public static void SetGuardLockedEdits(DependencyObject element, bool value) => element.SetValue(GuardLockedEditsProperty, value);

        public static bool GetGuardLockedEdits(DependencyObject element) => (bool)element.GetValue(GuardLockedEditsProperty);

        private static void OnGuardLockedEditsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is DataGrid grid)) return;
            grid.BeginningEdit -= OnGridBeginningEdit;
            if (e.NewValue is true) grid.BeginningEdit += OnGridBeginningEdit;
        }

        private static void OnGridBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row?.Item is UserInputField field && !field.CanEditValue)
            {
                e.Cancel = true;
                PromptNeedLogin();
            }
        }

        #endregion

        #region 值输入框：点击/键盘操作时提示

        public static readonly DependencyProperty GuardLockedValueProperty =
            DependencyProperty.RegisterAttached("GuardLockedValue", typeof(bool), typeof(UserInputFieldLockGuard),
                new PropertyMetadata(false, OnGuardLockedValueChanged));

        public static void SetGuardLockedValue(DependencyObject element, bool value) => element.SetValue(GuardLockedValueProperty, value);

        public static bool GetGuardLockedValue(DependencyObject element) => (bool)element.GetValue(GuardLockedValueProperty);

        private static void OnGuardLockedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is FrameworkElement element)) return;
            element.PreviewMouseLeftButtonDown -= OnLockedValueMouseDown;
            element.PreviewKeyDown -= OnLockedValueKeyDown;
            if (e.NewValue is true)
            {
                element.PreviewMouseLeftButtonDown += OnLockedValueMouseDown;
                element.PreviewKeyDown += OnLockedValueKeyDown;
            }
        }

        private static void OnLockedValueMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is UserInputField field)) return;
            if (field.CanEditValue) return;
            // 只读单元格仍允许选中复制，仅在鼠标首次进入（尚未获得键盘焦点）时提示一次
            if (!element.IsKeyboardFocusWithin) PromptNeedLogin();
        }

        private static void OnLockedValueKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is UserInputField field)) return;
            if (field.CanEditValue) return;
            // 阅读/复制类按键不提示（IsReadOnly 本身已阻止修改）
            if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down
                || e.Key == Key.Tab || e.Key == Key.Home || e.Key == Key.End)
                return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                && (e.Key == Key.C || e.Key == Key.A))
                return;
            PromptNeedLogin();
        }

        #endregion

        /// <summary>最近一次提示时间（1.5 秒节流，避免连续操作弹出多个提示窗口）</summary>
        private static DateTime _lastPromptAt = DateTime.MinValue;

        /// <summary>提示需要登录后才能修改冻结字段</summary>
        private static void PromptNeedLogin()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastPromptAt).TotalSeconds < 1.5) return;
            _lastPromptAt = now;

            WindowAsyncPopup.Show(
                LanguageService.GetLocalizedString("UserInput_FrozenNeedLogin", "该字段已冻结，请登录后再修改"),
                LanguageService.GetLocalizedString("Warning", "提示"),
                PopupButtons.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 步骤详情里每个设置段自带横向滚动容器；为了让最外层（详情面板）的竖向滚动顺畅，
    /// 段内容器的鼠标滚轮不自行消费，而是转交给上层可竖向滚动的 ScrollViewer。
    /// </summary>
    public static class StepScrollAssist
    {
        public static readonly DependencyProperty BubbleWheelToParentProperty =
            DependencyProperty.RegisterAttached(
                "BubbleWheelToParent",
                typeof(bool),
                typeof(StepScrollAssist),
                new PropertyMetadata(false, OnBubbleWheelToParentChanged));

        public static void SetBubbleWheelToParent(DependencyObject element, bool value)
            => element?.SetValue(BubbleWheelToParentProperty, value);

        public static bool GetBubbleWheelToParent(DependencyObject element)
            => element != null && (bool)element.GetValue(BubbleWheelToParentProperty);

        private static void OnBubbleWheelToParentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element) return;

            element.PreviewMouseWheel -= OnPreviewMouseWheel;
            if (e.NewValue is true)
                element.PreviewMouseWheel += OnPreviewMouseWheel;
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || sender is not DependencyObject element) return;

            // 内层容器自身能纵向滚动时，交给它自己处理（当前段内容器都是禁用纵向滚动）
            if (element is ScrollViewer inner
                && inner.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
                && inner.ScrollableHeight > 0)
            {
                return;
            }

            var parent = FindParentVerticalScrollViewer(element);
            if (parent == null) return;

            // 标记为已处理，然后把同一滚轮事件转给外层容器
            e.Handled = true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = element,
            });
        }

        private static ScrollViewer FindParentVerticalScrollViewer(DependencyObject element)
        {
            var current = element as DependencyObject;
            while (current != null)
            {
                current = VisualTreeHelper.GetParent(current);
                if (current is ScrollViewer viewer
                    && viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
                    && viewer.ScrollableHeight > 0)
                {
                    return viewer;
                }
            }
            return null;
        }
    }
}
