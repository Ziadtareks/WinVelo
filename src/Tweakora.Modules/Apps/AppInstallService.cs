using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;

namespace Tweakora.Modules.Apps;

public sealed partial class ApplicationItem : ObservableObject
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Link { get; init; } = string.Empty;
    public string WingetId { get; init; } = string.Empty;
    public string ChocoId { get; init; } = string.Empty;

    public string BrandColor { get; init; } = "#3B82F6";
    public string IconSymbol { get; init; } = "Apps24";
    public string ShortCode { get; init; } = "AP";
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

    public string IconBackground => HasIconImage ? "#20242D" : BrandColor;
    public string IconBorder => HasIconImage ? "#2F3642" : "Transparent";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateBadgeBackground))]
    [NotifyPropertyChangedFor(nameof(StateBadgeForeground))]
    [NotifyPropertyChangedFor(nameof(StateBadgeBorder))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateBadgeBackground))]
    [NotifyPropertyChangedFor(nameof(StateBadgeForeground))]
    [NotifyPropertyChangedFor(nameof(StateBadgeBorder))]
    private bool _hasUpdate;

    public string StateText => HasUpdate ? "Update available" : (IsInstalled ? "Installed" : "Not installed");

    public string StateBadgeBackground => HasUpdate ? "#451A03" : (IsInstalled ? "#064E3B" : "#1E222D");
    public string StateBadgeForeground => HasUpdate ? "#F59E0B" : (IsInstalled ? "#10B981" : "#94A3B8");
    public string StateBadgeBorder => HasUpdate ? "#D97706" : (IsInstalled ? "#059669" : "#334155");

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Ready";

    public string GetPackageId(string packageManager)
    {
        return packageManager.Equals("Chocolatey", StringComparison.OrdinalIgnoreCase)
            ? (!string.IsNullOrWhiteSpace(ChocoId) ? ChocoId : WingetId)
            : (!string.IsNullOrWhiteSpace(WingetId) ? WingetId : ChocoId);
    }
}

public interface IAppInstallService
{
    IReadOnlyList<ApplicationItem> LoadApplications();
    Task<OperationExecutionReport> InstallAppsAsync(
        IReadOnlyList<ApplicationItem> apps,
        string packageManager,
        IOperationRunner runner,
        CancellationToken cancellationToken = default);
    Task<OperationExecutionReport> UninstallAppsAsync(
        IReadOnlyList<ApplicationItem> apps,
        string packageManager,
        IOperationRunner runner,
        CancellationToken cancellationToken = default);
    Task<string> UpgradeAllAsync(string packageManager, CancellationToken cancellationToken = default);
    Task DetectInstalledStatusAsync(IEnumerable<ApplicationItem> apps, CancellationToken cancellationToken = default);
    Task<string> DetectBestPackageManagerAsync();
    Task<bool> IsPackageManagerAvailableAsync(string packageManager);
}

public sealed class AppInstallService : IAppInstallService
{
    private readonly IShellService _shell;

    public AppInstallService(IShellService shell)
    {
        _shell = shell;
    }

    public static (string Color, string Icon, string Code) GetBrandMeta(string key, string category)
    {
        return key.ToLowerInvariant() switch
        {
            "chrome" or "googlechrome" => ("#4285F4", "Globe24", "GC"),
            "brave" => ("#FB542B", "Globe24", "BR"),
            "firefox" or "mozillafirefox" => ("#FF7139", "Globe24", "FF"),
            "edge" or "microsoftedge" => ("#0078D7", "Globe24", "ME"),
            "discord" => ("#5865F2", "Chat24", "DC"),
            "telegram" => ("#26A5E4", "Send24", "TG"),
            "whatsapp" => ("#25D366", "Phone24", "WA"),
            "zoom" => ("#2D8CFF", "VideoChat24", "ZM"),
            "vlc" => ("#FF8800", "Video24", "VLC"),
            "spotify" => ("#1DB954", "MusicNote224", "SP"),
            "obs" or "obsstudio" => ("#302E31", "Record24", "OBS"),
            "handbrake" => ("#E05A47", "Video24", "HB"),
            "audacity" => ("#0000EB", "Speaker224", "AU"),
            "paintnet" => ("#0078D4", "DrawShape24", "PDN"),
            "sharex" => ("#00B4D8", "Crop24", "SX"),
            "7zip" => ("#2B579A", "FolderZip24", "7Z"),
            "winrar" => ("#8B5CF6", "FolderZip24", "RAR"),
            "powertoys" => ("#0078D4", "Wrench24", "PT"),
            "everything" => ("#E67E22", "FolderSearch24", "EV"),
            "wiztree" => ("#EAB308", "HardDrive24", "WT"),
            "qbittorrent" => ("#2B6CB0", "ArrowDownload24", "QB"),
            "anydesk" => ("#EF4444", "Desktop24", "AD"),
            "bitwarden" => ("#175DDC", "ShieldKeyhole24", "BW"),
            "bleachbit" => ("#22C55E", "Broom24", "BB"),
            "rufus" => ("#3B82F6", "UsbStick24", "RF"),
            "steam" => ("#171A21", "Games24", "ST"),
            "epicgames" => ("#2A2A2A", "Games24", "EG"),
            "goggalaxy" => ("#86269B", "Games24", "GOG"),
            "battlenet" => ("#00AEFF", "Games24", "BN"),
            "vscode" => ("#007ACC", "Code24", "VS"),
            "notepadplusplus" or "notepad++" => ("#90C53F", "DocumentText24", "NPP"),
            "git" => ("#F05032", "BranchFork24", "GIT"),
            "terminal" or "windowsterminal" => ("#4D4D4D", "WindowConsole20", "WT"),
            "python" or "python3" => ("#3776AB", "Code24", "PY"),
            "nodejs" or "node" => ("#339933", "Code24", "JS"),
            "githubdesktop" => ("#6E5494", "BranchFork24", "GH"),
            _ => category.ToLowerInvariant() switch
            {
                "browsers" => ("#3B82F6", "Globe24", key[..Math.Min(2, key.Length)].ToUpperInvariant()),
                "social" => ("#6366F1", "Chat24", key[..Math.Min(2, key.Length)].ToUpperInvariant()),
                "media" => ("#EC4899", "Video24", key[..Math.Min(2, key.Length)].ToUpperInvariant()),
                "gaming" => ("#8B5CF6", "Games24", key[..Math.Min(2, key.Length)].ToUpperInvariant()),
                "developer" => ("#10B981", "Code24", key[..Math.Min(2, key.Length)].ToUpperInvariant()),
                _ => ("#475569", "Apps24", key[..Math.Min(2, key.Length)].ToUpperInvariant())
            }
        };
    }

    public static string? GetBrandSvg(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "chrome" or "googlechrome" => "M12,2A10,10,0,1,0,22,12,10,10,0,0,0,12,2ZM12,7.5A4.5,4.5,0,1,1,7.5,12,4.5,4.5,0,0,1,12,7.5ZM4.25,12A7.75,7.75,0,0,1,12,4.25c2.35,0,4.45,1.05,5.88,2.71L12.5,15.75H8.75A4.5,4.5,0,0,1,4.25,12ZM12,19.75A7.75,7.75,0,0,1,5.32,15.1l4.74-8.21a4.5,4.5,0,0,1,5.83,4.61L12.1,19.74A7.7,7.7,0,0,1,12,19.75Zm6.68-4.65L14.79,8.5h4.96A7.73,7.73,0,0,1,19.75,12,7.7,7.7,0,0,1,18.68,15.1Z",
            "brave" => "M12,2L4,5v6.5C4,17.2,7.4,22.4,12,24c4.6-1.6,8-6.8,8-12.5V5L12,2ZM12,4.2l6,2.3v5C18,16.2,15.4,20.6,12,22c-3.4-1.4-6-5.8-6-10.5v-5L12,4.2ZM9.5,8C8.7,8,8,8.7,8,9.5S8.7,11,9.5,11s1.5-.7,1.5-1.5S10.3,8,9.5,8Zm5,0c-.8,0-1.5.7-1.5,1.5s.7,1.5,1.5,1.5,1.5-.7,1.5-1.5S15.3,8,14.5,8ZM12,13.5c-1.7,0-3,1-3,2.5h6C15,14.5,13.7,13.5,12,13.5Z",
            "firefox" or "mozillafirefox" => "M12,2C6.48,2,2,6.48,2,12s4.48,10,10,10,10-4.48,10-10S17.52,2,12,2Zm1,17.93c-3.95-.49-7-3.85-7-7.93,0-.62.08-1.21.21-1.79.09.28.2.55.33.81.74,1.47,2.15,2.54,3.84,2.83-.34-.48-.56-1.05-.61-1.67-.12-1.39.73-2.65,2.05-3.04-.4.81-.35,1.78.13,2.55.48.77,1.35,1.24,2.26,1.24.41,0,.82-.09,1.19-.28.98-.49,1.59-1.5,1.59-2.61,0-.4-.08-.79-.24-1.15.54.49.97,1.1,1.24,1.8.44,1.13.43,2.39-.03,3.52-.94,2.33-3.03,3.99-5.51,4.24-.15.01-.3.02-.45.02Z",
            "edge" or "microsoftedge" => "M12,2A10,10,0,0,0,2.1,10.5C2.8,7.3,5.6,5,9,5c4.4,0,7.5,3.6,7.5,8c0,2.5-1.3,4.7-3.3,6C17.6,18.8,21,15.7,21.8,11.8A10,10,0,0,0,12,2ZM4,12.5C4,8.9,7.1,6,11,6c1.1,0,2.1.3,3,.8-1.2.6-2,1.8-2,3.2,0,2.2,1.8,4,4,4,.4,0,.8-.1,1.2-.2C16.8,17.4,14.6,20,12,20,7.6,20,4,16.6,4,12.5Z",
            "discord" => "M19.54,4.16C18.09,3.49,16.54,2.99,14.92,2.7C14.71,3.08,14.47,3.58,14.3,3.98C12.57,3.72,10.85,3.72,9.15,3.98C8.98,3.58,8.74,3.08,8.53,2.7C6.91,2.99,5.36,3.49,3.91,4.16C1.02,8.47,0.24,12.67,0.62,16.82C2.54,18.23,4.39,19.09,6.21,19.66C6.66,19.05,7.06,18.39,7.4,17.7C6.73,17.45,6.1,17.13,5.5,16.75C5.66,16.63,5.82,16.51,5.97,16.39C9.66,18.09,13.7,18.09,17.36,16.39C17.52,16.51,17.68,16.63,17.84,16.75C17.24,17.13,16.61,17.45,15.94,17.7C16.28,18.39,16.68,19.05,17.13,19.66C18.95,19.09,20.81,18.23,22.72,16.82C23.19,12.02,21.95,7.85,19.54,4.16ZM7.46,14.18C6.36,14.18,5.45,13.17,5.45,11.93C5.45,10.69,6.34,9.68,7.46,9.68C8.6,9.68,9.5,10.69,9.48,11.93C9.48,13.17,8.6,14.18,7.46,14.18ZM15.89,14.18C14.79,14.18,13.88,13.17,13.88,11.93C13.88,10.69,14.77,9.68,15.89,9.68C17.03,9.68,17.93,10.69,17.91,11.93C17.91,13.17,17.03,14.18,15.89,14.18Z",
            "telegram" => "M12,2A10,10,0,1,0,22,12,10,10,0,0,0,12,2Zm4.83,6.88-1.92,9.05c-.14.65-.53.81-1.07.51l-2.93-2.16-1.41,1.36a.75.75,0,0,1-.6.29l.21-3,5.46-4.93c.24-.21-.05-.33-.37-.12L8.94,14.12l-2.9-.91c-.63-.2-.64-.63.13-.93l11.35-4.37C17.93,7.7,18.35,8,16.83,8.88Z",
            "whatsapp" => "M12,2A10,10,0,0,0,3.34,16.79L2,22l5.35-1.39A10,10,0,1,0,12,2Zm0,18.23a8.21,8.21,0,0,1-4.18-1.15l-.3-.18-3.11.82.83-3-.2-.31A8.24,8.24,0,1,1,12,20.23Zm4.52-6.17c-.25-.13-1.46-.72-1.69-.8s-.39-.13-.56.13-.64.8-.78.97-.29.19-.54.06a6.83,6.83,0,0,1-2-1.24,7.52,7.52,0,0,1-1.39-1.73c-.15-.25,0-.39.11-.51s.25-.29.37-.44a1.72,1.72,0,0,0,.25-.42.46.46,0,0,0,0-.44c-.06-.13-.56-1.35-.77-1.85s-.41-.42-.56-.43h-.48a.92.92,0,0,0-.67.31,2.81,2.81,0,0,0-.87,2.08,4.89,4.89,0,0,0,1,2.59,11.23,11.23,0,0,0,4.3,3.8c.61.26,1.09.42,1.46.54a3.52,3.52,0,0,0,1.61.1,2.63,2.63,0,0,0,1.73-1.22,2.15,2.15,0,0,0,.15-1.22C16.88,14.24,16.77,14.19,16.52,14.06Z",
            "zoom" => "M4,6A3,3,0,0,1,7,3H14A3,3,0,0,1,17,6V18A3,3,0,0,1,14,21H7A3,3,0,0,1,4,18ZM18,8.5L22,5.5V18.5L18,15.5Z",
            "vlc" => "M10.8,2.2c.4-.6,1.4-.6,1.8,0l2.2,4.8H8.6L10.8,2.2ZM7.7,8.5h8.6l1.3,3H6.4L7.7,8.5ZM5.4,13h13.2l1.6,4.5H3.8L5.4,13ZM2,19.5h20c.6,0,1,.4,1,1s-.4,1-1,1H2c-.6,0-1-.4-1-1S1.4,19.5,2,19.5Z",
            "spotify" => "M12,2A10,10,0,1,0,22,12,10,10,0,0,0,12,2ZM16.58,16.43a.74.74,0,0,1-1,.25c-2.77-1.69-6.26-2.07-10.37-1.13a.75.75,0,0,1-.33-1.46c4.5-.99,8.38-.56,11.45,1.34A.74.74,0,0,1,16.58,16.43ZM17.9,13.33a.93.93,0,0,1-1.28.31c-3.17-2-8-2.5-11.75-1.37a.93.93,0,1,1-.53-1.78c4.27-1.3,9.59-.72,13.25,1.56A.93.93,0,0,1,17.9,13.33ZM18,10.15C14.36,8,8.33,7.79,4.86,8.84a1.12,1.12,0,1,1-.66-2.14C8,5.48,14.63,5.72,18.84,8.22a1.12,1.12,0,1,1-1.16,1.93Z",
            "obs" or "obsstudio" => "M12,2A10,10,0,1,0,22,12,10,10,0,0,0,12,2ZM12,6a6,6,0,0,1,5.2,3H12a3,3,0,0,0-2.6,1.5L6.8,6.8A5.94,5.94,0,0,1,12,6ZM6,12a6,6,0,0,1,.8-3l2.6,4.5A3,3,0,0,0,12,15v4A6,6,0,0,1,6,12ZM14.6,13.5A3,3,0,0,0,12,12a3,3,0,0,0-1.5.4L7.9,7.9A6,6,0,0,1,17.2,12Z",
            "handbrake" => "M12,2L14,5H10L12,2ZM6,8h12l-1.5,6c-.5,2-2.2,3.5-4.5,3.5S7.9,16,7.5,14L6,8ZM11,17.5v3.5H8v1h8v-1H13V17.5Z",
            "audacity" => "M12,3A9,9,0,0,0,3,12v4a3,3,0,0,0,3,3h1v-7H5V12a7,7,0,0,1,14,0v0H17v7h1a3,3,0,0,0,3-3V12A9,9,0,0,0,12,3ZM8,11v3h1v-3H8ZM10,9v7h1V9H10ZM12,7v11h1V7H12ZM14,9v7h1V9H14Z",
            "paintnet" => "M12,2A10,10,0,0,0,2,12a10,10,0,0,0,10,10c1.38,0,2.5-1.12,2.5-2.5a2.5,2.5,0,0,0-.65-1.67c-.36-.4-.58-.93-.58-1.53,0-1.22.98-2.2,2.2-2.2H17a5,5,0,0,0,5-5C22,6.48,17.52,2,12,2ZM6.5,11.5A1.5,1.5,0,1,1,8,10,1.5,1.5,0,0,1,6.5,11.5Zm3-4A1.5,1.5,0,1,1,11,6,1.5,1.5,0,0,1,9.5,7.5Zm5,0A1.5,1.5,0,1,1,16,6,1.5,1.5,0,0,1,14.5,7.5Zm3,4A1.5,1.5,0,1,1,19,10,1.5,1.5,0,0,1,17.5,11.5Z",
            "sharex" => "M12,2L5,9h5v6H4l7,7l7-7h-5V9h6L12,2Z",
            "7zip" => "M3,5h18v14H3V5ZM5,7v3h6L8,17h2.5l3-7H5ZM15,10h4v2h-4v2h4v3h-6V10h2Z",
            "winrar" => "M4,4h16v3H4V4ZM4,9h16v3H4V9ZM4,14h16v3H4V14ZM4,19h16v2H4V19ZM13,3h3v18h-3V3Z",
            "powertoys" => "M3,3h8v8H3V3ZM13,3h8v8h-8V3ZM3,13h8v8H3V13ZM13,13h8v8h-8V13Z",
            "everything" => "M10,2a8,8,0,1,0,5.29,14l4.71,4.71a1,1,0,0,0,1.41-1.41l-4.71-4.71A8,8,0,0,0,10,2Zm0,2a6,6,0,1,1-6,6A6,6,0,0,1,10,4Z",
            "wiztree" => "M4,3h16v6H4V3ZM4,11h9v10H4V11ZM15,11h5v4h-5V11ZM15,17h5v4h-5V17Z",
            "qbittorrent" => "M12,2A10,10,0,1,0,22,12,10,10,0,0,0,12,2ZM8,7h2v5.5a2.5,2.5,0,0,1,2.5-2.5h.5v2h-.5a1.5,1.5,0,0,0-1.5,1.5V17H8V7Zm6,3h2v7h-2v-1.5A2.5,2.5,0,0,1,11.5,18H11v-2h.5a1.5,1.5,0,0,0,1.5-1.5V10Z",
            "anydesk" => "M8.5,4L2,14.5l3.5,5.5h6.5L8.5,14.5ZM15.5,4l-3.5,5.5l3.5,5.5h6.5L22,14.5ZM12,14.5l-3.5,5.5h7L12,14.5Z",
            "bitwarden" => "M12,2L4,5.5v6c0,5.55,3.84,10.74,8,12c4.16-1.26,8-6.45,8-12v-6L12,2ZM12,8a2,2,0,0,1,2,2c0,.74-.4,1.38-1,1.72v3.78h-2v-3.78A2,2,0,0,1,12,8Z",
            "bleachbit" => "M19.7,3.3a1,1,0,0,0-1.4,0L11,10.6l-.7-.7a1,1,0,0,0-1.4,1.4l.7.7-4.2,4.2A3,3,0,0,0,4.5,20.5l3.8-.8a3,3,0,0,0,2.1-2.1l4.2-4.2.7.7a1,1,0,0,0,1.4-1.4l-.7-.7l7.3-7.3A1,1,0,0,0,19.7,3.3Z",
            "rufus" => "M8,2h8v4h-8V2ZM9,3v2h2V3H9ZM13,3v2h2V3H13ZM7,7h10a1,1,0,0,1,1,1v13a1,1,0,0,1-1,1H7a1,1,0,0,1-1-1V8A1,1,0,0,1,7,7ZM13,11l-3,4h2v4l3-4h-2Z",
            "steam" => "M12,2A10,10,0,0,0,2.15,10.88L5.7,12.33a3,3,0,0,1,2.82-.44l2.1-3.07a4,4,0,1,1,4.88,4.88l-3.07,2.1a3,3,0,0,1-3.66,3.37L6.4,21A10,10,0,1,0,12,2ZM15.5,6A2.5,2.5,0,1,0,18,8.5,2.5,2.5,0,0,0,15.5,6ZM7,14a1.5,1.5,0,1,0,1.5,1.5A1.5,1.5,0,0,0,7,14Z",
            "epicgames" => "M12,2L3,5v9c0,5,4.5,8,9,9c4.5-1,9-4,9-9V5L12,2ZM15,14H9v-2h5v-2H9V8h6V6H7v10h8V14Z",
            "goggalaxy" => "M12,2A10,10,0,1,0,22,12,10,10,0,0,0,12,2ZM12,6A6,6,0,1,1,6,12,6,6,0,0,1,12,6ZM12,9A3,3,0,1,0,15,12,3,3,0,0,0,12,9Z",
            "battlenet" => "M12,2A10,10,0,0,0,2,12a10,10,0,0,0,10,10a10,10,0,0,0,10-10A10,10,0,0,0,12,2ZM7,9l4,3-4,3V9ZM13,6l4,5-5,3V6ZM14,16l-3-4 5-3v7Z",
            "vscode" => "M17.5,2.2L6.8,11.5L2.8,8.5L1.5,9.5L4.8,12L1.5,14.5L2.8,15.5L6.8,12.5L17.5,21.8L22.5,19.5V4.5L17.5,2.2ZM18.5,7.2L11.5,12L18.5,16.8V7.2Z",
            "notepadplusplus" or "notepad++" => "M6,2A2,2,0,0,0,4,4V20A2,2,0,0,0,6,22H18A2,2,0,0,0,20,20V8L14,2H6ZM13,3.5L18.5,9H13V3.5ZM7,12H17V14H7V12ZM7,16H14V18H7V16Z",
            "git" => "M21.7,10.3l-8-8a2.4,2.4,0,0,0-3.4,0L8.5,4.1l2.3,2.3a2.1,2.1,0,0,1,2.7.4,2.1,2.1,0,0,1,.4,2.6l2.2,2.2a2.1,2.1,0,0,1,2.6.4,2.4,2.4,0,0,1,0,3.4,2.4,2.4,0,0,1-3.4,0,2.1,2.1,0,0,1-.5-2.5l-2.1-2.1v5.2a2.4,2.4,0,0,1,.7,1.8,2.4,2.4,0,0,1-4.8,0,2.4,2.4,0,0,1,.7-1.8V9.3a2.4,2.4,0,0,1-.7-1.8,2.4,2.4,0,0,1,.7-1.8L2.3,10.3a2.4,2.4,0,0,0,0,3.4l8,8a2.4,2.4,0,0,0,3.4,0l8-8A2.4,2.4,0,0,0,21.7,10.3Z",
            "terminal" or "windowsterminal" => "M20,4H4A2,2,0,0,0,2,6V18A2,2,0,0,0,4,20H20A2,2,0,0,0,22,18V6A2,2,0,0,0,20,4ZM7.7,14.3L6.3,12.9L9.2,10L6.3,7.1L7.7,5.7L12,10L7.7,14.3ZM17,15H12V13H17V15Z",
            "python" or "python3" => "M11.9,2c-4,0-3.8,1.7-3.8,1.7l0,1.8h3.9v.6H5.2S2,5.7,2,9.8s2.8,3.9,2.8,3.9h1.7v-2.4c0-2.8,2.4-2.6,2.4-2.6h4.1V6.9S13.4,2,11.9,2Zm-1.8,1.2a.7.7,0,1,1,.7.7A.7.7,0,0,1,10.1,3.2ZM12.1,22c4,0,3.8-1.7,3.8-1.7l0-1.8h-3.9v-.6h6.8s3.2.4,3.2-3.7-2.8-3.9-2.8-3.9h-1.7v2.4c0,2.8-2.4,2.6-2.4,2.6h-4.1v1.8S10.6,22,12.1,22Zm1.8-1.2a.7.7,0,1,1-.7-.7A.7.7,0,0,1,13.9,20.8Z",
            "nodejs" or "node" => "M12,2L3.5,6.9v9.8L12,21.6l8.5-4.9V6.9L12,2ZM12,4.3l6.5,3.7v7.5L12,19.3l-6.5-3.7V8.1L12,4.3ZM12,7.5a4.5,4.5,0,1,0,4.5,4.5A4.5,4.5,0,0,0,12,7.5Z",
            "githubdesktop" => "M12,2A10,10,0,0,0,8.84,21.49c.5.09.68-.22.68-.48v-1.7c-2.78.6-3.37-1.34-3.37-1.34a2.65,2.65,0,0,0-1.11-1.46c-.91-.62.07-.61.07-.61a2.1,2.1,0,0,1,1.53,1,2.13,2.13,0,0,0,2.91.83,2.16,2.16,0,0,1,.64-1.34c-2.22-.25-4.55-1.11-4.55-4.94a3.87,3.87,0,0,1,1-2.68,3.6,3.6,0,0,1,.1-2.65s.84-.27,2.75,1a9.58,9.58,0,0,1,5,0c1.91-1.3,2.75-1,2.75-1a3.6,3.6,0,0,1,.1,2.65,3.87,3.87,0,0,1,1,2.68c0,3.84-2.34,4.68-4.57,4.93a2.39,2.39,0,0,1,.68,1.86v2.77c0,.27.18.58.69.48A10,10,0,0,0,12,2Z",
            _ => null
        };
    }

    public IReadOnlyList<ApplicationItem> LoadApplications()
    {
        string rootDir = AppContext.BaseDirectory;
        string appJsonPath = Path.Combine(rootDir, "Catalog", "Apps", "applications.json");

        if (!File.Exists(appJsonPath))
        {
            string? parent = Directory.GetParent(rootDir)?.FullName;
            while (parent != null)
            {
                string candidate = Path.Combine(parent, "Catalog", "Apps", "applications.json");
                if (File.Exists(candidate))
                {
                    appJsonPath = candidate;
                    break;
                }
                parent = Directory.GetParent(parent)?.FullName;
            }
        }

        List<ApplicationItem> list = new();
        if (File.Exists(appJsonPath))
        {
            try
            {
                string json = File.ReadAllText(appJsonPath);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var elem = prop.Value;
                    string name = elem.TryGetProperty("name", out var nP) ? nP.GetString() ?? prop.Name : prop.Name;
                    string cat = elem.TryGetProperty("category", out var cP) ? cP.GetString() ?? "Utilities" : "Utilities";
                    string desc = elem.TryGetProperty("description", out var dP) ? dP.GetString() ?? "" : "";
                    string link = elem.TryGetProperty("link", out var lP) ? lP.GetString() ?? "" : "";
                    string winget = elem.TryGetProperty("winget", out var wP) ? wP.GetString() ?? "" : "";
                    string choco = elem.TryGetProperty("choco", out var chP) ? chP.GetString() ?? "" : "";

                    var (color, icon, code) = GetBrandMeta(prop.Name, cat);
                    string? svg = GetBrandSvg(prop.Name);

                    list.Add(new ApplicationItem
                    {
                        Key = prop.Name,
                        Name = name,
                        Category = cat,
                        Description = desc,
                        Link = link,
                        WingetId = winget,
                        ChocoId = choco,
                        BrandColor = color,
                        IconSymbol = icon,
                        ShortCode = code,
                        SvgIconPath = svg
                    });
                }
            }
            catch { }
        }

        return list;
    }

    public async Task<string> DetectBestPackageManagerAsync()
    {
        // 1. Check WinGet first (Official Microsoft native engine for Windows 10/11)
        if (await IsPackageManagerAvailableAsync("WinGet"))
        {
            return "WinGet";
        }

        // 2. Check Chocolatey
        if (await IsPackageManagerAvailableAsync("Chocolatey"))
        {
            return "Chocolatey";
        }

        return "WinGet";
    }

    public async Task<bool> IsPackageManagerAvailableAsync(string packageManager)
    {
        try
        {
            if (packageManager.Equals("WinGet", StringComparison.OrdinalIgnoreCase))
            {
                string localAppWinget = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\WindowsApps\winget.exe");
                if (File.Exists(localAppWinget)) return true;

                var res = await _shell.ExecutePowerShellAsync("(Get-Command winget.exe -ErrorAction SilentlyContinue) -ne $null", 5);
                return res.Success && res.StdOut.Contains("True", StringComparison.OrdinalIgnoreCase);
            }
            else if (packageManager.Equals("Chocolatey", StringComparison.OrdinalIgnoreCase))
            {
                string chocoPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"chocolatey\bin\choco.exe");
                if (File.Exists(chocoPath)) return true;

                var res = await _shell.ExecutePowerShellAsync("(Get-Command choco.exe -ErrorAction SilentlyContinue) -ne $null", 5);
                return res.Success && res.StdOut.Contains("True", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Fallback gracefully
        }

        return packageManager.Equals("WinGet", StringComparison.OrdinalIgnoreCase);
    }

    public async Task DetectInstalledStatusAsync(IEnumerable<ApplicationItem> apps, CancellationToken cancellationToken = default)
    {
        try
        {
            // Rapid detection of installed applications via winget list
            var res = await _shell.ExecutePowerShellAsync("winget list --accept-source-agreements", 30);
            string output = res.StdOut;
            if (!string.IsNullOrWhiteSpace(output))
            {
                foreach (var app in apps)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (!string.IsNullOrWhiteSpace(app.WingetId) && output.Contains(app.WingetId, StringComparison.OrdinalIgnoreCase))
                    {
                        app.IsInstalled = true;
                        app.StatusText = "Installed";
                    }
                    else if (output.Contains(app.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        app.IsInstalled = true;
                        app.StatusText = "Installed";
                    }
                }
            }
        }
        catch
        {
            // Fallback gracefully without throwing
        }
    }

    public async Task<OperationExecutionReport> InstallAppsAsync(
        IReadOnlyList<ApplicationItem> apps,
        string packageManager,
        IOperationRunner runner,
        CancellationToken cancellationToken = default)
    {
        return await runner.RunAsync(
            $"Install Applications ({packageManager})",
            apps,
            async (app, ct) =>
            {
                string pkgId = app.GetPackageId(packageManager);
                if (string.IsNullOrWhiteSpace(pkgId))
                {
                    return new OperationItemResult
                    {
                        Id = app.Key,
                        Name = app.Name,
                        Status = OperationItemStatus.Skipped,
                        Reason = $"No package ID available for {packageManager}."
                    };
                }

                app.IsBusy = true;
                app.StatusText = "Installing...";

                string script = packageManager.Equals("Chocolatey", StringComparison.OrdinalIgnoreCase)
                    ? $"choco install \"{pkgId}\" -y --no-progress"
                    : $"winget install --id \"{pkgId}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";

                try
                {
                    var res = await _shell.ExecutePowerShellAsync(script, 300);
                    bool success = res.Success &&
                                   !res.StdOut.Contains("Error", StringComparison.OrdinalIgnoreCase) &&
                                   !res.StdOut.Contains("Failed", StringComparison.OrdinalIgnoreCase) &&
                                   !res.StdOut.Contains("Exit code: 1", StringComparison.OrdinalIgnoreCase);

                    if (success)
                    {
                        app.IsInstalled = true;
                        app.StatusText = "Installed";
                    }
                    else
                    {
                        app.StatusText = "Install Failed";
                    }

                    string reason = success
                        ? "Installed successfully."
                        : (!string.IsNullOrWhiteSpace(res.StdErr) ? res.StdErr.Trim() : (!string.IsNullOrWhiteSpace(res.StdOut) ? res.StdOut.Trim() : "Install failed."));

                    return new OperationItemResult
                    {
                        Id = app.Key,
                        Name = app.Name,
                        Status = success ? OperationItemStatus.Succeeded : OperationItemStatus.Failed,
                        Reason = reason
                    };
                }
                finally
                {
                    app.IsBusy = false;
                }
            },
            cancellationToken);
    }

    public async Task<OperationExecutionReport> UninstallAppsAsync(
        IReadOnlyList<ApplicationItem> apps,
        string packageManager,
        IOperationRunner runner,
        CancellationToken cancellationToken = default)
    {
        return await runner.RunAsync(
            $"Uninstall Applications ({packageManager})",
            apps,
            async (app, ct) =>
            {
                string pkgId = app.GetPackageId(packageManager);
                if (string.IsNullOrWhiteSpace(pkgId))
                {
                    return new OperationItemResult
                    {
                        Id = app.Key,
                        Name = app.Name,
                        Status = OperationItemStatus.Skipped,
                        Reason = $"No package ID available for {packageManager}."
                    };
                }

                app.IsBusy = true;
                app.StatusText = "Uninstalling...";

                string script = packageManager.Equals("Chocolatey", StringComparison.OrdinalIgnoreCase)
                    ? $"choco uninstall \"{pkgId}\" -y"
                    : $"winget uninstall --id \"{pkgId}\" --exact --silent --disable-interactivity";

                try
                {
                    var res = await _shell.ExecutePowerShellAsync(script, 180);
                    bool success = res.Success &&
                                   !res.StdOut.Contains("Error", StringComparison.OrdinalIgnoreCase) &&
                                   !res.StdOut.Contains("Failed", StringComparison.OrdinalIgnoreCase);

                    if (success)
                    {
                        app.IsInstalled = false;
                        app.StatusText = "Uninstalled";
                    }
                    else
                    {
                        app.StatusText = "Uninstall Failed";
                    }

                    string reason = success
                        ? "Uninstalled successfully."
                        : (!string.IsNullOrWhiteSpace(res.StdErr) ? res.StdErr.Trim() : (!string.IsNullOrWhiteSpace(res.StdOut) ? res.StdOut.Trim() : "Uninstall failed."));

                    return new OperationItemResult
                    {
                        Id = app.Key,
                        Name = app.Name,
                        Status = success ? OperationItemStatus.Succeeded : OperationItemStatus.Failed,
                        Reason = reason
                    };
                }
                finally
                {
                    app.IsBusy = false;
                }
            },
            cancellationToken);
    }

    public async Task<string> UpgradeAllAsync(string packageManager, CancellationToken cancellationToken = default)
    {
        string script = packageManager.Equals("Chocolatey", StringComparison.OrdinalIgnoreCase)
            ? "choco upgrade all -y"
            : "winget upgrade --all --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";

        var res = await _shell.ExecutePowerShellAsync(script, 600);
        return res.Success ? (string.IsNullOrWhiteSpace(res.StdOut) ? "Upgrade completed." : res.StdOut) : $"Upgrade failed: {res.StdErr}";
    }
}
