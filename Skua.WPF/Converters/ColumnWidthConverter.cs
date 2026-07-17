using System;
using System.Globalization;
using System.Windows.Data;

namespace Skua.WPF.Converters;

public class ColumnWidthConverter : IMultiValueConverter
{
    // Layout constants (matching AccountManager.xaml)
    private const double DefaultItemWidth = 200.0;
    private const double MinimumItemWidth = 100.0;
    private const double ScrollViewerPaddingLeft = 4.0;
    private const double ScrollViewerPaddingRight = 4.0;
    private const double CardMarginRight = 4.0;
    private const double CardMarginBottom = 4.0;
    private const double ScrollbarWidth = 9.0;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // Check for unset or null values
        if (values == null || values.Length < 2 ||
            values[0] == System.Windows.DependencyProperty.UnsetValue ||
            values[1] == System.Windows.DependencyProperty.UnsetValue)
        {
            return DefaultItemWidth;
        }

        if (values[0] is double containerWidth && values[1] is int columns)
        {
            if (columns <= 0) columns = 1;
            if (containerWidth <= 0) return DefaultItemWidth;

            // Calculate item width based on available space
            // Formula: (containerWidth - scrollViewerPadding - cardMargins - scrollbarSpace) / columns

            double scrollViewerPadding = ScrollViewerPaddingLeft + ScrollViewerPaddingRight;
            double totalCardMargins = CardMarginRight * columns; // Right margin for each card
            double reservedSpace = scrollViewerPadding + totalCardMargins + ScrollbarWidth;

            double availableWidth = containerWidth - reservedSpace;
            double itemWidth = availableWidth / columns;

            return Math.Max(MinimumItemWidth, itemWidth);
        }

        return DefaultItemWidth;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
