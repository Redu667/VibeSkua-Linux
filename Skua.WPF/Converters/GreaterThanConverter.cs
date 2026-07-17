using System;
using System.Globalization;
using System.Windows.Data;

namespace Skua.WPF.Converters;

public class GreaterThanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null && parameter is null)
            return true;

        if (value is null || parameter is null)
            return false;

        if (!TryConvertToInt(value, out int valueInt))
            return false;

        if (!TryConvertToInt(parameter, out int parameterInt))
            return false;

        return valueInt >= parameterInt;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static bool TryConvertToInt(object obj, out int result)
    {
        result = 0;

        if (obj is int intValue)
        {
            result = intValue;
            return true;
        }

        if (obj is string strValue && int.TryParse(strValue, out int parsedValue))
        {
            result = parsedValue;
            return true;
        }

        if (obj is IConvertible)
        {
            try
            {
                result = System.Convert.ToInt32(obj);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
}