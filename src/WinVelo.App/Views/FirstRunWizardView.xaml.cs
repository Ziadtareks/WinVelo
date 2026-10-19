using System.Windows.Controls;
using System.Windows.Input;
using WinVelo.App.ViewModels;

namespace WinVelo.App.Views;

public partial class FirstRunWizardView : UserControl
{
    public FirstRunWizardView()
    {
        InitializeComponent();

        Loaded += (s, e) =>
        {
            Focus();
            Keyboard.Focus(this);
        };

        PreviewKeyDown += (s, e) =>
        {
            if (DataContext is not FirstRunWizardViewModel vm) return;

            if (e.Key == Key.Enter)
            {
                vm.NextStepCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                vm.FinishCommand.Execute(null);
                e.Handled = true;
            }
        };
    }
}
