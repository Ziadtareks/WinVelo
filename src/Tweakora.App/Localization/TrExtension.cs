using System.Windows.Markup;

namespace Tweakora.App.Localization;

/// <summary>XAML: Text="{loc:Tr Home_Overview}" returns the text for the current language.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => LocalizationManager.Instance[Key];
}
