using ShaoLu.Utils;
using ShaoLu.Viewmodels;
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
        }

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
            AddOCRStep.CommandParameter = menuItem.CommandParameter;
            if (menuItem.CommandParameter is string type)
            {
                stepsViewModel.AddStepCommand.Execute(type);
            }
        }

        private void MenuItemAddImageStep_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = (MenuItem)sender;
            AddImageStep.CommandParameter = menuItem.CommandParameter;
            if (menuItem.CommandParameter is string type)
            {
                stepsViewModel.AddStepCommand.Execute(type);
            }
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
}
