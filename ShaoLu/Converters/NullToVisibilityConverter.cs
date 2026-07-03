using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShaoLu.Converters
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isInverse = parameter != null && parameter.ToString() == "Inverse";
            bool isNull = value == null;

            if (isInverse)
                return isNull ? Visibility.Visible : Visibility.Collapsed;

            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}