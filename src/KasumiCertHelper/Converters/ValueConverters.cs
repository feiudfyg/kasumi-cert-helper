using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace KasumiCertHelper.Converters;

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool flag = value is bool b && b;
        if (Invert)
        {
            flag = !flag;
        }
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool hasText = value is string text && !string.IsNullOrWhiteSpace(text);
        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>
/// Feeds a pixel width from a <c>TableColumnLayout</c> into a <c>ColumnDefinition</c> so every
/// row of a table can follow the widths the user drags in the header.
/// </summary>
public sealed class DoubleToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => new GridLength(value is double width && width > 0 ? width : 0, GridUnitType.Pixel);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is GridLength length ? length.Value : 0d;
}
