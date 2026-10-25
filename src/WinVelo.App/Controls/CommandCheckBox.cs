using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace WinVelo.App.Controls;

/// <summary>A checkbox whose accessibility toggle follows the same command path as a user click.</summary>
public sealed class CommandCheckBox : CheckBox
{
    protected override AutomationPeer OnCreateAutomationPeer() => new CommandCheckBoxPeer(this);

    private sealed class CommandCheckBoxPeer(CommandCheckBox owner) : CheckBoxAutomationPeer(owner), IToggleProvider
    {
        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Toggle ? this : base.GetPattern(patternInterface);

        ToggleState IToggleProvider.ToggleState => owner.IsChecked switch
        {
            true => ToggleState.On,
            false => ToggleState.Off,
            _ => ToggleState.Indeterminate
        };

        void IToggleProvider.Toggle()
        {
            if (!IsEnabled()) throw new ElementNotEnabledException();
            // The stock WPF Toggle provider changes IsChecked without raising Click,
            // which bypasses an ICommand backed by an OS operation.
            owner.Dispatcher.InvokeAsync(owner.Activate);
        }
    }

    private void Activate()
    {
        if (IsEnabled) OnClick();
    }
}
