using System.Text.Json;

namespace Tweakora.Modules.Apps;

public sealed class WinGetApp
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string Publisher { get; init; }
    public string IconSymbol { get; init; } = "Apps24";
    public bool IsInstalled { get; set; }
    public bool IsInstalling { get; set; }
    public int InstallProgress { get; set; }
}

public sealed class WinGetManager
{
    private readonly string _catalogPath;
    public bool IsSimulationMode { get; set; }

    public WinGetManager(string? catalogPath = null, bool isSimulationMode = true)
    {
        _catalogPath = catalogPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Catalog", "Apps", "apps.winget.json");
        IsSimulationMode = isSimulationMode;
    }

    public List<WinGetApp> LoadAppsCatalog()
    {
        if (!File.Exists(_catalogPath))
        {
            // Fallback path check if running from binary subfolder
            string relative = Path.Combine(Directory.GetCurrentDirectory(), "Catalog", "Apps", "apps.winget.json");
            if (File.Exists(relative))
            {
                return ParseJson(relative);
            }
            return new List<WinGetApp>();
        }

        return ParseJson(_catalogPath);
    }

    private static List<WinGetApp> ParseJson(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            var apps = JsonSerializer.Deserialize<List<WinGetApp>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return apps ?? new List<WinGetApp>();
        }
        catch
        {
            return new List<WinGetApp>();
        }
    }

    public async Task<bool> InstallAppAsync(WinGetApp app, IProgress<int>? progress = null)
    {
        app.IsInstalling = true;
        app.InstallProgress = 0;

        try
        {
            if (IsSimulationMode)
            {
                // Safe simulation mode: simulate download and install without touching system
                for (int i = 10; i <= 100; i += 20)
                {
                    await Task.Delay(80);
                    app.InstallProgress = i;
                    progress?.Report(i);
                }
                app.IsInstalled = true;
                return true;
            }
            else
            {
                // In VM real execution: winget command
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = $"install --id {app.Id} -e --accept-source-agreements --accept-package-agreements --silent",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    bool success = proc.ExitCode == 0;
                    app.IsInstalled = success;
                    return success;
                }
                return false;
            }
        }
        finally
        {
            app.IsInstalling = false;
        }
    }

    public static string GenerateBatchInstallScript(IEnumerable<WinGetApp> apps)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Tweakora WinGet Batch Installation Script");
        sb.AppendLine("# Run this in PowerShell (Administrator)");
        sb.AppendLine();
        foreach (var app in apps)
        {
            sb.AppendLine($"Write-Host 'Installing {app.Name}...' -ForegroundColor Cyan");
            sb.AppendLine($"winget install --id {app.Id} -e --accept-source-agreements --accept-package-agreements");
        }
        sb.AppendLine("Write-Host 'All selected packages processed.' -ForegroundColor Green");
        return sb.ToString();
    }
}
