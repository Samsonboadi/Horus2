using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Test.Converters 
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                bool invert = parameter?.ToString()?.ToLower() == "inverse";
                if (invert) boolValue = !boolValue;
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                var invert = parameter?.ToString()?.ToLower() == "inverse";
                var result = visibility == Visibility.Visible;
                return invert ? !result : result;
            }
            return false;
        }
    }
}