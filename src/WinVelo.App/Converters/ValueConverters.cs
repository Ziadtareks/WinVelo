using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinVelo.App.Converters;

[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class StringToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasValue = value is string str && !string.IsNullOrWhiteSpace(str);
        if (Invert) hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : false;
    }
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Collapsed;
    }
}
