using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace ShaoLu.Converters
{
    public class SelectedStepConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AutomationStepBase step)
            {
                return step.LineNo;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string step)
            {
                try { 
                    int lineno = int.Parse(step);
                    if (SingletonLocator.Steps.AutomationStepBases.Any(x => x.LineNo == lineno))
                        return SingletonLocator.Steps.AutomationStepBases.FirstOrDefault(x => x.LineNo == lineno);
                } 
                catch { return null; }
            }
            return null;
        }
    }
}
