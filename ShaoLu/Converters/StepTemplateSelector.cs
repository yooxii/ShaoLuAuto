using ShaoLu.Models;
using ShaoLu.Viewmodels.AutomationStep;
using System.Windows;
using System.Windows.Controls;

namespace ShaoLu.Converters
{
    public class StepTemplateSelector : DataTemplateSelector
    {
        public DataTemplate ImageTemplate { get; set; }
        public DataTemplate TextTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is AutomationStepBase step)
            {
                return step.Type switch
                {
                    StepType.ClickImage => ImageTemplate,
                    StepType.TypeText => TextTemplate,
                    _ => ImageTemplate,
                };
            }
            return base.SelectTemplate(item, container);
        }
    }
}
