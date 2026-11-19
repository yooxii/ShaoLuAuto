using ShaoLu.Models;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ShaoLu.Converters
{
    /// <summary>
    /// 按步骤类型返回节点配色：ConverterParameter="accent" 返回主色，
    /// "tint" 返回同色系的浅色底（用于页脚/角标）。
    /// </summary>
    public class StepTypeToBrushConverter : IValueConverter
    {
        private static readonly Color ImageColor = Color.FromRgb(0x2B, 0x6F, 0xD6);     // 图像识别
        private static readonly Color InputColor = Color.FromRgb(0x2F, 0xA3, 0x6B);     // 输入操作
        private static readonly Color CollectColor = Color.FromRgb(0xE0, 0x8A, 0x2E);   // 采集统计
        private static readonly Color FlowColor = Color.FromRgb(0x7B, 0x5C, 0xD6);      // 流程控制
        private static readonly Color InteractColor = Color.FromRgb(0x0F, 0x9E, 0xB1);  // 交互提示
        private static readonly Color OtherColor = Color.FromRgb(0x7D, 0x8A, 0x99);     // 其他

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var stepType = value is StepType type ? type : StepType.Empty;
            var color = ColorOf(stepType);

            if (string.Equals(parameter as string, "tint", StringComparison.OrdinalIgnoreCase))
                color = Color.FromArgb(0x22, color.R, color.G, color.B);

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();

        private static Color ColorOf(StepType type) => type switch
        {
            StepType.ClickImage or StepType.FindImage or StepType.ClickImages or StepType.FindImages
                => ImageColor,
            StepType.TypeText or StepType.TypeTextMore or StepType.TypeTextFromFile or StepType.MouseAction or StepType.GetInput
                => InputColor,
            StepType.ScanCode or StepType.AcbelUpload or StepType.Statistics or StepType.UserInput or StepType.BurnInConfig
                => CollectColor,
            StepType.MultiThread or StepType.MultiThreadEnd or StepType.SendSignal
                or StepType.ReceiveSignal or StepType.Interrupt or StepType.Empty
                => FlowColor,
            StepType.Popup or StepType.FocusWindow
                => InteractColor,
            _ => OtherColor,
        };
    }
}
