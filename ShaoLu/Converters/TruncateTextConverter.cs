using System;
using System.Globalization;
using System.Windows.Data;

namespace ShaoLu.Converters
{
    /// <summary>
    /// 长文本截断转换器：超过最大长度（默认100字符）时截断并追加省略号
    /// </summary>
    public class TruncateTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string;
            if (string.IsNullOrEmpty(text)) return text;

            int maxLength = 100;
            if (parameter is string param && int.TryParse(param, out int parsed))
            {
                maxLength = parsed;
            }

            return Utils.TextHelper.Truncate(text, maxLength);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
