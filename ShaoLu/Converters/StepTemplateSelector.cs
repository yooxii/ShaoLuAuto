using ShaoLu.Models;
using ShaoLu.Viewmodels.AutomationStep;
using System.Windows;
using System.Windows.Controls;

namespace ShaoLu.Converters
{
    public class StepTemplateSelector : DataTemplateSelector
    {
        public DataTemplate EmptyTemplate { get; set; }
        public DataTemplate ClickImageTemplate { get; set; }
        public DataTemplate TypeTextTemplate { get; set; }
        public DataTemplate FindImageTemplate { get; set; }
        public DataTemplate ClickImagesTemplate { get; set; }
        public DataTemplate FindImagesTemplate { get; set; }
        public DataTemplate TypeTextMoreTemplate { get; set; }
        public DataTemplate TypeTextFromFileTemplate { get; set; }
        public DataTemplate PopupTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is AutomationStepBase step)
            {
                return step.Type switch
                {
                    StepType.Empty => EmptyTemplate,
                    StepType.ClickImage => ClickImageTemplate,
                    StepType.FindImage => FindImageTemplate,
                    StepType.ClickImages => ClickImagesTemplate,
                    StepType.FindImages => FindImagesTemplate,
                    StepType.TypeText => TypeTextTemplate,
                    StepType.TypeTextMore => TypeTextMoreTemplate,
                    StepType.TypeTextFromFile => TypeTextFromFileTemplate,
                    StepType.Popup => PopupTemplate,
                    _ => ClickImageTemplate,
                };
            }
            return base.SelectTemplate(item, container);
        }
    }
}
