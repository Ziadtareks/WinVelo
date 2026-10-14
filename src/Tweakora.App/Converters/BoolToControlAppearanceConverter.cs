using System;
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace Tweakora.App.Converters;

public sealed class BoolToControlAppearanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isTrue = value is bool b && b;
        return isTrue ? ControlAppearance.Primary : ControlAppearance.Secondary;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ControlAppearance appearance)
        {
            return appearance == ControlAppearance.Primary;
        }
        return false;
    }
}
