using System.Windows;
using Serilog;
using Tweakora.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Tweakora.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        ApplySystemBackdrop();
    }

    private void ApplySystemBackdrop()
    {
        try
        {
            // Windows 11 (build >= 22000) natively supports Mica
            if (Environment.OSVersion.Version.Build >= 22000)
            {
                WindowBackdropType = WindowBackdropType.Mica;
                Log.Information("Applied Mica window backdrop (Windows 11 build {Build})", Environment.OSVersion.Version.Build);
            }
            else
            {
                WindowBackdropType = WindowBackdropType.None;
                SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
                Log.Information("Windows 10 detected (build {Build}). Using solid backdrop fallback.", Environment.OSVersion.Version.Build);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to apply system backdrop effect, falling back to None");
            WindowBackdropType = WindowBackdropType.None;
            SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        }
    }
}
