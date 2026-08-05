using ShaoLu.Services;
using System;
using System.Globalization;
using System.Windows.Data;

namespace ShaoLu.Converters
{
    /// <summary>
    /// 布尔值转"良品/不良"本地化文本
    /// </summary>
    public class BoolToGoodBadConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isGood = value is bool b && b;
            return isGood
                ? LanguageService.GetLocalizedString("BurnIn_Good")
                : LanguageService.GetLocalizedString("BurnIn_Bad");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
