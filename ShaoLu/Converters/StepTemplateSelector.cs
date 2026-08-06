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
        public DataTemplate TypeTextMoreTemplate { get; set; }
        public DataTemplate TypeTextFromFileTemplate { get; set; }
        public DataTemplate PopupTemplate { get; set; }
        public DataTemplate GetInputTemplate { get; set; }
        public DataTemplate MouseActionTemplate { get; set; }
        public DataTemplate StatisticsTemplate { get; set; }
        public DataTemplate BurnInConfigTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is AutomationStepBase step)
            {
                return step.Type switch
                {
                    StepType.Empty => EmptyTemplate,
                    StepType.ClickImage => ClickImageTemplate,
                    StepType.FindImage => FindImageTemplate,
                    StepType.ClickImages => EmptyTemplate,
                    StepType.FindImages => EmptyTemplate,
                    StepType.TypeText => TypeTextTemplate,
                    StepType.TypeTextMore => TypeTextMoreTemplate,
                    StepType.TypeTextFromFile => TypeTextFromFileTemplate,
                    StepType.Popup => PopupTemplate,
                    StepType.GetInput => GetInputTemplate,
                    StepType.MouseAction => MouseActionTemplate,
                    StepType.Statistics => StatisticsTemplate,
                    StepType.BurnInConfig => BurnInConfigTemplate,
                    _ => ClickImageTemplate,
                };
            }
            return base.SelectTemplate(item, container);
        }
    }
}
