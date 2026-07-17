using System;
using System.Globalization;
using System.Windows.Data;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;

namespace Skua.WPF.Converters;

public sealed class GrabberItemDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ItemBase item)
        {
            string baseText = item.ToString();

            IJunkService? junkService = Ioc.Default.GetService<IJunkService>();
            if (junkService != null && junkService.IsJunk(item.ID))
            {
                return baseText + " [Junk]";
            }

            return baseText;
        }

        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
