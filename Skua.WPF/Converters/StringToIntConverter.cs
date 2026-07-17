using System;
using System.Globalization;
using System.Windows.Data;

namespace Skua.WPF.Converters;

public class StringToIntConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        _ = int.TryParse(value.ToString(), out int result);
        return result;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string strValue)
        {
            if (string.IsNullOrWhiteSpace(strValue))
                return 0;
            if (int.TryParse(strValue, out int result))
                return result;
        }
        return 0;
    }
}