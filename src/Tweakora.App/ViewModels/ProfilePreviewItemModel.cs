using CommunityToolkit.Mvvm.ComponentModel;
using Tweakora.Core.Models;

namespace Tweakora.App.ViewModels;

public sealed partial class ProfilePreviewItemModel : ObservableObject
{
    public required TweakDefinition Tweak { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Risk { get; init; }
    public required bool Reversible { get; init; }
    public required bool RequiresReboot { get; init; }
    public required bool RequiresExplorerRestart { get; init; }

    [ObservableProperty]
    private string _stateText = "Detecting...";

    [ObservableProperty]
    private string _stateBg = "#334155";

    [ObservableProperty]
    private string _stateFg = "#94A3B8";

    [ObservableProperty]
    private bool _isAlreadyApplied;

    [ObservableProperty]
    private bool _isSelected = true;

    public bool IsSelectable => !IsAlreadyApplied;

    public string ReversibleText => Reversible ? "Yes" : "No";
    public string RequiresRebootText => RequiresReboot ? "Yes" : "No";
    public string RequiresExplorerRestartText => RequiresExplorerRestart ? "Yes" : "No";

    public string RiskBadgeBg => Risk switch
    {
        "Safe" => "#064E3B",
        "Low" => "#1E3A8A",
        "Moderate" or "Medium" => "#451A03",
        "Caution" or "High" => "#450A0A",
        _ => "#1E222D"
    };

    public string RiskBadgeFg => Risk switch
    {
        "Safe" => "#10B981",
        "Low" => "#60A5FA",
        "Moderate" or "Medium" => "#F59E0B",
        "Caution" or "High" => "#EF4444",
        _ => "#94A3B8"
    };

    public string RiskBadgeBorder => Risk switch
    {
        "Safe" => "#059669",
        "Low" => "#3B82F6",
        "Moderate" or "Medium" => "#D97706",
        "Caution" or "High" => "#DC2626",
        _ => "#334155"
    };
}
