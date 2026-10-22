using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using WinVelo.Core.Catalog;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Operations;

namespace WinVelo.Modules.Apps;

public sealed partial class AppxItem : ObservableObject
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string PackageId { get; init; }
    public string StoreId { get; init; } = "";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _status = "Unknown";
}

/// <summary>Explicit, current-user Appx operations. Removal has no automatic rollback.</summary>
public sealed class AppxService(IShellService shell)
{
    public IReadOnlyList<AppxItem> Load()
    {
        using var doc = CatalogLoader.LoadDocument(CatalogSource.Default, "Config/appx.json");
        if (doc == null) return Array.Empty<AppxItem>();
        return doc.RootElement.EnumerateObject().Select(p => new AppxItem
        {
            Key = p.Name,
            Name = p.Value.GetProperty("Content").GetString() ?? p.Name,
            PackageId = p.Value.GetProperty("PackageId").GetString() ?? "",
            StoreId = p.Value.TryGetProperty("StoreId", out var store) ? store.GetString() ?? "" : ""
        }).ToList();
    }
    private AppxItem Validate(AppxItem item)
    {
        var trusted = Load().FirstOrDefault(x => x.Key == item.Key && x.PackageId == item.PackageId);
        if (trusted == null || !Regex.IsMatch(trusted.PackageId, @"^[A-Za-z0-9._-]+$")) throw new ArgumentException("Unknown Appx package.");
        return trusted;
    }
    public async Task RefreshAsync(IEnumerable<AppxItem> items, CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            Validate(item);
            if (!shell.SupportsSystemQueries) { item.Status = "Simulation"; continue; }
            var result = await shell.ExecutePowerShellAsync($"[bool](Get-AppxPackage -Name '{item.PackageId}' -ErrorAction Stop)", 60, ct);
            item.Status = result.Success && bool.TryParse(result.StdOut.Trim(), out bool installed) ? installed ? "Installed" : "Not installed" : "Unknown";
        }
    }
    public Task<OperationExecutionReport> RemoveAsync(IReadOnlyList<AppxItem> items, bool confirmed, IOperationRunner runner, CancellationToken ct = default)
        => runner.RunAsync("Remove selected Appx packages (current user)", items, async (item, token) =>
        {
            Validate(item);
            if (!confirmed) return new OperationItemResult { Id = item.Key, Name = item.Name, Status = OperationItemStatus.Failed, Reason = "Explicit confirmation required. App removal can delete app data and cannot be undone automatically." };
            var result = await shell.ExecutePowerShellAsync($"Get-AppxPackage -Name '{item.PackageId}' -ErrorAction Stop | Remove-AppxPackage -ErrorAction Stop; if(Get-AppxPackage -Name '{item.PackageId}' -ErrorAction Stop){{throw 'Package is still installed.'}}", 300, token);
            item.Status = result.Success ? !shell.SupportsSystemQueries ? "Simulation" : "Not installed" : "Failed";
            if (result.Success) item.IsSelected = false;
            return new OperationItemResult { Id = item.Key, Name = item.Name, Status = result.Success ? OperationItemStatus.Succeeded : OperationItemStatus.Failed, Reason = result.Success ? "Removed for the current user. App data is not backed up." : result.StdErr };
        }, ct);
    public void OpenStore(AppxItem item)
    {
        var trusted = Validate(item);
        if (!Regex.IsMatch(trusted.StoreId, @"^[A-Za-z0-9]+$")) throw new ArgumentException("No Store listing for this package.");
        shell.OpenUrl("https://apps.microsoft.com/detail/" + trusted.StoreId);
    }
}
