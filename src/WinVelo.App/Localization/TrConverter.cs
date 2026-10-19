using System.Globalization;
using System.Windows.Data;

namespace WinVelo.App.Localization;

/// <summary>
/// Shows an English value (risk level, status, category name...) in the current language
/// without changing the value the view model works with.
/// </summary>
[ValueConversion(typeof(object), typeof(string))]
public sealed class TrConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return null;
        string text = value.ToString() ?? string.Empty;
        return parameter is string format && format.Contains("{0}", StringComparison.Ordinal)
            ? LocalizationManager.T(format, LocalizationManager.T(text))
            : LocalizationManager.TranslateMessage(text);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
