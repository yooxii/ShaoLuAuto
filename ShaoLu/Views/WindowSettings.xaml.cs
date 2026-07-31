using ShaoLu.Viewmodels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShaoLu.Views
{
    /// <summary>
    /// WindowSettings.xaml 的交互逻辑
    /// </summary>
    public partial class WindowSettings : Window
    {
        private readonly SettingsWindowViewModel settingsVM = new SettingsWindowViewModel();
        public WindowSettings()
        {
            InitializeComponent();
            DataContext = settingsVM;

            settingsVM.WindowClosed += () => this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is SettingsCategory category)
            {
                settingsVM.SelectedCategory = category;

                // 如果有 SectionKey，滚动到对应区域
                if (!string.IsNullOrEmpty(category.SectionKey))
                {
                    ScrollToSection(category.SectionKey);
                }
            }
        }

        /// <summary>
        /// 根据 SectionKey 查找对应的 Expander 并滚动到可见位置
        /// </summary>
        private void ScrollToSection(string sectionKey)
        {
            var expander = FindElementByTag(SettingsPanel, sectionKey);
            if (expander is Expander exp)
            {
                exp.IsExpanded = true;
                exp.BringIntoView();
            }
            else if (expander is FrameworkElement fe)
            {
                fe.BringIntoView();
            }
        }

        /// <summary>
        /// 递归查找指定 Tag 的元素
        /// </summary>
        private static FrameworkElement FindElementByTag(DependencyObject parent, string tag)
        {
            if (parent == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is FrameworkElement fe && tag.Equals(fe.Tag))
                {
                    return fe;
                }

                var result = FindElementByTag(child, tag);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }
    }
}
