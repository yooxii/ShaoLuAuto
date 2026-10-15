using ShaoLu.Viewmodels.AutomationStep;
using System.Windows;
using System.Windows.Controls;

namespace ShaoLu.Converters
{
    /// <summary>
    /// 步骤列表首列「多线程区域折叠开关」的模板选择器：只有「多线程开始」行使用折叠开关模板，
    /// 其余步骤类型使用空模板，避免在不含 IsRegionCollapsed 属性的步骤上建立绑定。
    /// </summary>
    public class RegionCollapseCellTemplateSelector : DataTemplateSelector
    {
        public DataTemplate ToggleTemplate { get; set; }

        public DataTemplate EmptyTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            return item is MultiThreadStartStep ? ToggleTemplate : EmptyTemplate;
        }
    }
}
