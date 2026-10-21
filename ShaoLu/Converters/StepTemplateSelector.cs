using ShaoLu.Models;
using ShaoLu.Viewmodels.AutomationStep;
using System.Windows;
using System.Windows.Controls;

namespace ShaoLu.Converters
{
    public class StepTemplateSelector : DataTemplateSelector
    {
        public DataTemplate EmptyTemplate { get; set; }
        public DataTemplate EmptyStepTemplate { get; set; }
        public DataTemplate ClickImageTemplate { get; set; }
        public DataTemplate TypeTextTemplate { get; set; }
        public DataTemplate FindImageTemplate { get; set; }
        public DataTemplate TypeTextMoreTemplate { get; set; }
        public DataTemplate TypeTextFromFileTemplate { get; set; }
        public DataTemplate PopupTemplate { get; set; }
        public DataTemplate AcbelUploadTemplate { get; set; }
        public DataTemplate GetInputTemplate { get; set; }
        public DataTemplate MouseActionTemplate { get; set; }
        public DataTemplate StatisticsTemplate { get; set; }
        public DataTemplate BurnInConfigTemplate { get; set; }
        public DataTemplate FocusWindowTemplate { get; set; }
        public DataTemplate MultiThreadTemplate { get; set; }
        public DataTemplate MultiThreadEndTemplate { get; set; }
        public DataTemplate SendSignalTemplate { get; set; }
        public DataTemplate ReceiveSignalTemplate { get; set; }
        public DataTemplate InterruptTemplate { get; set; }
        public DataTemplate UserInputTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is AutomationStepBase step)
            {
                return step.Type switch
                {
                    StepType.Empty => EmptyStepTemplate,
                    StepType.ClickImage => ClickImageTemplate,
                    StepType.FindImage => FindImageTemplate,
                    StepType.ClickImages => EmptyTemplate,
                    StepType.FindImages => EmptyTemplate,
                    StepType.TypeText => TypeTextTemplate,
                    StepType.TypeTextMore => TypeTextMoreTemplate,
                    StepType.TypeTextFromFile => TypeTextFromFileTemplate,
                    StepType.Popup => PopupTemplate,
                    StepType.AcbelUpload => AcbelUploadTemplate,
                    StepType.GetInput => GetInputTemplate,
                    StepType.MouseAction => MouseActionTemplate,
                    StepType.Statistics => StatisticsTemplate,
                    StepType.BurnInConfig => BurnInConfigTemplate,
                    StepType.FocusWindow => FocusWindowTemplate,
                    StepType.MultiThread => MultiThreadTemplate,
                    StepType.MultiThreadEnd => MultiThreadEndTemplate,
                    StepType.SendSignal => SendSignalTemplate,
                    StepType.ReceiveSignal => ReceiveSignalTemplate,
                    StepType.Interrupt => InterruptTemplate,
                    StepType.UserInput => UserInputTemplate,
                    _ => ClickImageTemplate,
                };
            }
            return base.SelectTemplate(item, container);
        }
    }
}
