using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinVelo.Core.Safety;
using WinVelo.Core.Utilities;
using WinVelo.Core.Catalog;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using WinVelo.Core.Engine;
using WinVelo.Core.Models;

namespace WinVelo.Modules.Config;

public sealed class FeatureDefinition : ObservableObject
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public List<string> FeatureNames { get; init; } = new();
    public List<string> InvokeScripts { get; init; } = new();
    public List<string> UndoScripts { get; init; } = new();
    public string? DetectScript { get; init; }
    public string? CaptureUndoScript { get; init; }
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    private bool _isInstalled;
    private string _status = "Unknown";
    public bool IsInstalled { get => _isInstalled; set { SetProperty(ref _isInstalled, value); Status = value ? "Enabled" : "Disabled"; } }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
}

public interface IFeatureService
{
    IReadOnlyList<FeatureDefinition> LoadFeatures();
    Task<OperationExecutionReport> EnableFeaturesAsync(
        IReadOnlyList<FeatureDefinition> features,
        IOperationRunner runner,
        CancellationToken cancellationToken = default);
    Task<OperationExecutionReport> DisableFeaturesAsync(
        IReadOnlyList<FeatureDefinition> features,
        IOperationRunner runner,
        CancellationToken cancellationToken = default);
    Task<bool> ConfigureOpenSshAsync(bool enable, CancellationToken cancellationToken = default);
    Task RefreshStatusAsync(IEnumerable<FeatureDefinition> features, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class FeatureService : IFeatureService
{
    private readonly IShellService _shell;
    private readonly RollbackJournalManager? _journal;

    public FeatureService(IShellService shell, RollbackJournalManager? journal = null)
    {
        _shell = shell;
        _journal = journal;
    }

    public IReadOnlyList<FeatureDefinition> LoadFeatures()
    {
        var list = new List<FeatureDefinition>();
        using var doc = CatalogLoader.LoadDocument(CatalogSource.Default, "Config/feature.json");
        if (doc != null && doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var elem = prop.Value;
                if (elem.ValueKind != JsonValueKind.Object) continue;
                string cat = elem.TryGetProperty("category", out var cP) ? cP.GetString() ?? "" : "";
                string type = elem.TryGetProperty("Type", out var tP) ? tP.GetString() ?? "" : "";

                if (cat == "Features" && type != "Button")
                {
                    string name = elem.TryGetProperty("Content", out var nP) ? nP.GetString() ?? prop.Name : prop.Name;
                    string desc = elem.TryGetProperty("Description", out var dP) ? dP.GetString() ?? name : name;

                    var feats = new List<string>();
                    if (elem.TryGetProperty("feature", out var fP) && fP.ValueKind == JsonValueKind.Array)
                    {
                        feats.AddRange(fP.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)));
                    }

                    var scripts = new List<string>();
                    if (elem.TryGetProperty("InvokeScript", out var sP) && sP.ValueKind == JsonValueKind.Array)
                    {
                        scripts.AddRange(sP.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)));
                    }

                    list.Add(new FeatureDefinition
                    {
                        Key = prop.Name,
                        Name = name,
                        Description = desc,
                        Category = cat,
                        FeatureNames = feats,
                        InvokeScripts = scripts,
                        UndoScripts = elem.TryGetProperty("UndoScript", out var undoP) && undoP.ValueKind == JsonValueKind.Array
                            ? undoP.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : new(),
                        DetectScript = elem.TryGetProperty("DetectScript", out var detectP) ? detectP.GetString() : null,
                        CaptureUndoScript = elem.TryGetProperty("CaptureUndoScript", out var captureP) ? captureP.GetString() : null
                    });
                }
            }
        }

        return list;
    }

    public Task<OperationExecutionReport> EnableFeaturesAsync(IReadOnlyList<FeatureDefinition> features, IOperationRunner runner, CancellationToken cancellationToken = default)
        => SetFeaturesAsync(features, true, runner, cancellationToken);
    public Task<OperationExecutionReport> DisableFeaturesAsync(IReadOnlyList<FeatureDefinition> features, IOperationRunner runner, CancellationToken cancellationToken = default)
        => SetFeaturesAsync(features, false, runner, cancellationToken);

    private Task<OperationExecutionReport> SetFeaturesAsync(IReadOnlyList<FeatureDefinition> features, bool enable, IOperationRunner runner, CancellationToken ct)
    {
        bool conflicting = features.Any(f => f.Key == "WPFFeatureEnableLegacyRecovery") && features.Any(f => f.Key == "WPFFeatureDisableLegacyRecovery");
        return runner.RunAsync(enable ? "Enable Windows Features" : "Disable Windows Features", features, async (feature, token) =>
        {
            OperationItemResult Failed(string reason) => new() { Id = feature.Key, Name = feature.Name, Status = OperationItemStatus.Failed, Reason = reason };
            if (conflicting) return Failed("Select only one Legacy Recovery policy.");
            var scripts = enable ? feature.InvokeScripts : feature.UndoScripts;
            if (feature.FeatureNames.Count == 0 && scripts.Count == 0) return Failed("This action has no disable operation. No changes were made.");
            try
            {
                var commands = feature.FeatureNames.Select(name => SafeCommands.OptionalFeature(enable, name) + " | Out-Null").Concat(scripts).ToList();
                foreach (string script in commands)
                {
                    string? blocked = ServicingGuard.CheckScript(script);
                    if (blocked != null) return Failed(blocked);
                }
                var undo = new List<ActionItem>();
                foreach (string name in feature.FeatureNames)
                {
                    token.ThrowIfCancellationRequested();
                    string restore = SafeCommands.OptionalFeature(!enable, name);
                    if (_shell.SupportsSystemQueries)
                    {
                        var state = await _shell.ExecutePowerShellAsync($"(Get-WindowsOptionalFeature -Online -FeatureName '{name}' -ErrorAction Stop).State.ToString()", 120);
                        if (!state.Success) return Failed(state.StdErr);
                        string original = state.StdOut.Trim();
                        if (original.Contains("Pending", StringComparison.OrdinalIgnoreCase)) return Failed("Restart Windows before changing this feature.");
                        if (original == "Enabled") restore = SafeCommands.OptionalFeature(true, name);
                        else if (original.StartsWith("Disabled", StringComparison.OrdinalIgnoreCase)) restore = SafeCommands.OptionalFeature(false, name) + (original == "DisabledWithPayloadRemoved" ? " -Remove" : "");
                        else return Failed("Could not determine the feature baseline: " + name);
                    }
                    undo.Add(ModuleJournal.UndoScript(restore, 1800));
                }
                if (feature.InvokeScripts.Count > 0 || feature.UndoScripts.Count > 0)
                {
                    var restore = enable ? feature.UndoScripts : feature.InvokeScripts;
                    if (_shell.SupportsSystemQueries && !string.IsNullOrWhiteSpace(feature.CaptureUndoScript))
                    {
                        var capture = await _shell.ExecutePowerShellAsync(feature.CaptureUndoScript, 60);
                        if (!capture.Success) return Failed("Could not capture feature baseline: " + capture.StdErr);
                        restore = JsonSerializer.Deserialize<List<string>>(capture.StdOut) ?? new();
                    }
                    if (restore.Count == 0 && !string.IsNullOrWhiteSpace(feature.CaptureUndoScript)) return Failed("Feature baseline was empty.");
                    undo.InsertRange(0, restore.Select(script => ModuleJournal.UndoScript(script, 1800)));
                }
                token.ThrowIfCancellationRequested();
                var result = await ModuleJournal.RunScriptAsync(_shell, _journal, "tools.features." + feature.Key, string.Join(";\n", commands), undo, 1800, token);
                if (!result.Success) return Failed(result.StdErr.Length > 0 ? result.StdErr : "Feature operation failed.");
                feature.IsSelected = false;
                await RefreshStatusAsync(new[] { feature }, token);
                if (_shell.SupportsSystemQueries && feature.Status != "Restart required" && feature.Status != (enable ? "Enabled" : "Disabled"))
                    return Failed("Commands completed but the requested state could not be verified: " + feature.Status);
                return new OperationItemResult { Id = feature.Key, Name = feature.Name, Status = OperationItemStatus.Succeeded, Reason = "Feature commands completed. " + feature.Status };
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException) { return Failed(ex.Message); }
        }, ct);
    }

    public async Task<bool> ConfigureOpenSshAsync(bool enable, CancellationToken cancellationToken = default)
    {
        const string capability = "OpenSSH.Server~~~~0.0.1.0";
        string restore = enable
            ? "if(Get-Service sshd -ErrorAction SilentlyContinue){Stop-Service sshd -Force -ErrorAction Stop}; Remove-WindowsCapability -Online -Name 'OpenSSH.Server~~~~0.0.1.0' -ErrorAction Stop | Out-Null"
            : "Add-WindowsCapability -Online -Name 'OpenSSH.Server~~~~0.0.1.0' -ErrorAction Stop | Out-Null";
        if (_shell.SupportsSystemQueries)
        {
            var capture = await _shell.ExecutePowerShellAsync("$c=Get-WindowsCapability -Online -Name 'OpenSSH.Server~~~~0.0.1.0' -ErrorAction Stop; $s=Get-Service sshd -ErrorAction SilentlyContinue; [pscustomobject]@{Installed=($c.State -eq 'Installed'); Exists=($null -ne $s); Start=if($s){$s.StartType.ToString()}else{''}; Running=($s -and $s.Status -eq 'Running')} | ConvertTo-Json -Compress", 120, cancellationToken);
            if (!capture.Success) return false;
            try
            {
                using var doc = JsonDocument.Parse(capture.StdOut);
                var root = doc.RootElement;
                if (root.GetProperty("Installed").GetBoolean())
                {
                    restore = "Add-WindowsCapability -Online -Name '" + capability + "' -ErrorAction Stop | Out-Null; ";
                    if (root.GetProperty("Exists").GetBoolean())
                    {
                        string type = root.GetProperty("Start").GetString() ?? "";
                        if (type is not ("Automatic" or "Manual" or "Disabled" or "AutomaticDelayedStart")) return false;
                        restore += $"Set-Service sshd -StartupType {type} -ErrorAction Stop; ";
                        restore += root.GetProperty("Running").GetBoolean() ? "Start-Service sshd -ErrorAction Stop" : "Stop-Service sshd -ErrorAction Stop";
                    }
                }
                else restore = "if(Get-Service sshd -ErrorAction SilentlyContinue){Stop-Service sshd -Force -ErrorAction Stop}; Remove-WindowsCapability -Online -Name '" + capability + "' -ErrorAction Stop | Out-Null";
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return false; }
        }
        string script = enable
            ? "Add-WindowsCapability -Online -Name '" + capability + "' -ErrorAction Stop | Out-Null; Set-Service sshd -StartupType Automatic -ErrorAction Stop; Start-Service sshd -ErrorAction Stop"
            : "if(Get-Service sshd -ErrorAction SilentlyContinue){Stop-Service sshd -Force -ErrorAction Stop}; Remove-WindowsCapability -Online -Name '" + capability + "' -ErrorAction Stop | Out-Null";
        var result = await ModuleJournal.RunScriptAsync(_shell, _journal, "tools.openssh", script, new[] { ModuleJournal.UndoScript(restore, 1800) }, 1800, cancellationToken);
        if (!result.Success || !_shell.SupportsSystemQueries) return result.Success;
        var verify = await _shell.ExecutePowerShellAsync("((Get-WindowsCapability -Online -Name '" + capability + "' -ErrorAction Stop).State -eq 'Installed')", 120, cancellationToken);
        return verify.Success && bool.TryParse(verify.StdOut.Trim(), out bool installed) && installed == enable;
    }

    public async Task RefreshStatusAsync(IEnumerable<FeatureDefinition> features, CancellationToken cancellationToken = default)
    {
        foreach (var feature in features)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TestModeManager.IsTestMode) { feature.Status = "Simulation"; continue; }
            string? script = feature.DetectScript;
            if (feature.FeatureNames.Count > 0)
            {
                if (feature.FeatureNames.Any(n => n.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.' and not '_'))) { feature.Status = "Unknown"; continue; }
                string names = string.Join(',', feature.FeatureNames.Select(n => "'" + n + "'"));
                script = $"@({names} | ForEach-Object {{ (Get-WindowsOptionalFeature -Online -FeatureName $_ -ErrorAction Stop).State.ToString() }}) | ConvertTo-Json -Compress";
            }
            if (string.IsNullOrWhiteSpace(script)) { feature.Status = "Unknown"; continue; }
            try
            {
                var result = await _shell.ExecutePowerShellAsync(script, 120, cancellationToken);
                if (!result.Success) { feature.Status = "Unknown"; continue; }
                if (feature.FeatureNames.Count > 0)
                {
                    using var state = JsonDocument.Parse(result.StdOut);
                    var states = state.RootElement.ValueKind == JsonValueKind.Array
                        ? state.RootElement.EnumerateArray().Select(v => v.GetString() ?? "").ToList() : new List<string> { state.RootElement.GetString() ?? "" };
                    if (states.Any(v => v.Contains("Pending", StringComparison.OrdinalIgnoreCase))) feature.Status = "Restart required";
                    else if (states.All(v => v == "Enabled")) feature.IsInstalled = true;
                    else if (states.All(v => v.StartsWith("Disabled", StringComparison.OrdinalIgnoreCase))) feature.IsInstalled = false;
                    else feature.Status = "Partial";
                }
                else if (bool.TryParse(result.StdOut.Trim(), out bool enabled)) feature.IsInstalled = enabled;
                else feature.Status = "Unknown";
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { feature.Status = "Unknown"; }
        }
    }
}
