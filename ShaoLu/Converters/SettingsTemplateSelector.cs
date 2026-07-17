using ShaoLu.Models;
using System.Windows;
using System.Windows.Controls;

namespace ShaoLu.Converters
{
    public class SettingsTemplateSelector : DataTemplateSelector
    {
        public DataTemplate AppSettingsTemplate { get; set; }
        public DataTemplate StepSettingsTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item.GetType() == typeof(AppSettingsModel))
            {
                return AppSettingsTemplate;
            }
            if (item.GetType() == typeof(StepSettingsModel))
            {
                return StepSettingsTemplate;
            }
            return base.SelectTemplate(item, container);
        }
    }
}
