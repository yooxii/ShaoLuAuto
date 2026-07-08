using ShaoLu.Models;
using ShaoLu.Viewmodels.AutomationStep;
using System.Windows;
using System.Windows.Controls;

namespace ShaoLu.Converters
{
    public class StepTemplateSelector : DataTemplateSelector
    {
        public DataTemplate ClickImageTemplate { get; set; }
        public DataTemplate TextTemplate { get; set; }
        public DataTemplate FindImageTemplate { get; set; }
        public DataTemplate PopupTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is AutomationStepBase step)
            {
                return step.Type switch
                {
                    StepType.ClickImage => ClickImageTemplate,
                    StepType.TypeText => TextTemplate,
                    StepType.FindImage => FindImageTemplate,
                    StepType.Popup => PopupTemplate,
                    _ => ClickImageTemplate,
                };
            }
            return base.SelectTemplate(item, container);
        }
    }
}
