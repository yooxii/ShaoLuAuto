using ShaoLu.Models;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShaoLu.Converters
{
    /// <summary>
    /// 将 ConditionMode 转换为 Visibility
    /// Custom => Visible, Default => Collapsed
    /// </summary>
    public class ConditionModeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ConditionMode mode && mode == ConditionMode.Custom)
                return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
