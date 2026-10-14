using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tweakora.Modules.Startup;

public enum StartupLocationType
{
    UserRun,
    SystemRun,
    SystemRun32,
    UserStartupFolder,
    CommonStartupFolder
}

public partial class StartupAppItem : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string Command { get; set; }
    public string CleanExePath { get; set; } = string.Empty;
    public string Publisher { get; set; } = "Unknown Publisher";
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = "User Registry";
    public string Location => Source; // Backward compatibility
    public StartupLocationType LocationType { get; set; } = StartupLocationType.UserRun;

    public string StartupApprovedHive { get; set; } = "HKCU";
    public string StartupApprovedKey { get; set; } = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public string StartupApprovedValueName { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    public string Impact { get; set; } = "Low";
    public double BootDelaySeconds { get; set; } = 0.2;
    public string FileSizeFormatted { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; } = 0;
    public bool IsEssentialSystemItem { get; set; } = false;

    public string IconColor { get; set; } = "#3B82F6";
    public string IconLetter { get; set; } = "A";
    public string IconSymbol { get; set; } = "Apps24";
    public string? SvgIconPath { get; set; } = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIconImage))]
    [NotifyPropertyChangedFor(nameof(HasSvgIcon))]
    [NotifyPropertyChangedFor(nameof(HasSymbolIcon))]
    [NotifyPropertyChangedFor(nameof(IconBackground))]
    [NotifyPropertyChangedFor(nameof(IconBorder))]
    private object? _iconImage;

    public bool HasIconImage => IconImage != null;
    public bool HasSvgIcon => IconImage == null && !string.IsNullOrWhiteSpace(SvgIconPath);
    public bool HasSymbolIcon => IconImage == null && string.IsNullOrWhiteSpace(SvgIconPath);

    public string IconBackground => HasIconImage ? "#20242D" : IconColor;
    public string IconBorder => HasIconImage ? "#2F3642" : "Transparent";

    [ObservableProperty]
    private bool _isToggling = false;

    public bool IsNotToggling => !IsToggling;
}

public sealed class StartupProgramItem : StartupAppItem
{
    // Concrete subclass maintaining exact compatibility with existing code
}
