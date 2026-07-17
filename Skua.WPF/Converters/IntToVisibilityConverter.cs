using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Skua.WPF.Converters;

public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int intValue = (int)value;
        bool isVisible = intValue > 0;

        if (parameter is string param && param.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            isVisible = !isVisible;

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (Visibility)value == Visibility.Visible ? 1 : 0;
    }
}
