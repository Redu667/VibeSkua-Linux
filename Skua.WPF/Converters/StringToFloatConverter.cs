using System;
using System.Globalization;
using System.Windows.Data;

namespace Skua.WPF.Converters;

public class StringToFloatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        _ = float.TryParse(value.ToString(), out float result);
        return result;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string strValue)
        {
            if (string.IsNullOrWhiteSpace(strValue))
                return 0;
            if (float.TryParse(strValue, out float result))
                return result;
            if (strValue.EndsWith('.'))
                return 0;
        }
        return 0;
    }
}