using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinVelo.Core.Catalog;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Operations;

namespace WinVelo.Modules.Config;

public sealed class FeatureDefinition
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public List<string> FeatureNames { get; init; } = new();
    public List<string> InvokeScripts { get; init; } = new();
    public bool IsSelected { get; set; }
    public bool IsInstalled { get; set; }
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
}

public sealed class FeatureService : IFeatureService
{
    private readonly IShellService _shell;

    public FeatureService(IShellService shell)
    {
        _shell = shell;
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
                        InvokeScripts = scripts
                    });
                }
            }
        }

        return list;
    }

    public async Task<OperationExecutionReport> EnableFeaturesAsync(
        IReadOnlyList<FeatureDefinition> features,
        IOperationRunner runner,
        CancellationToken cancellationToken = default)
    {
        return await runner.RunAsync(
            "Enable Windows Features",
            features,
            async (feature, ct) =>
            {
                foreach (string fn in feature.FeatureNames)
                {
                    string script = $"Enable-WindowsOptionalFeature -Online -FeatureName '{fn}' -All -NoRestart";
                    var res = await _shell.ExecutePowerShellAsync(script, 180);
                    if (!res.Success)
                    {
                        return new OperationItemResult
                        {
                            Id = feature.Key,
                            Name = feature.Name,
                            Status = OperationItemStatus.Failed,
                            Reason = !string.IsNullOrWhiteSpace(res.StdErr) ? res.StdErr.Trim() : $"Failed to enable feature '{fn}'"
                        };
                    }
                }

                foreach (string sc in feature.InvokeScripts)
                {
                    var res = await _shell.ExecutePowerShellAsync(sc, 180);
                    if (!res.Success)
                    {
                        return new OperationItemResult
                        {
                            Id = feature.Key,
                            Name = feature.Name,
                            Status = OperationItemStatus.Failed,
                            Reason = !string.IsNullOrWhiteSpace(res.StdErr) ? res.StdErr.Trim() : "Feature configuration script failed."
                        };
                    }
                }

                feature.IsInstalled = true;
                return new OperationItemResult
                {
                    Id = feature.Key,
                    Name = feature.Name,
                    Status = OperationItemStatus.Succeeded,
                    Reason = "Feature enabled successfully."
                };
            },
            cancellationToken);
    }

    public async Task<OperationExecutionReport> DisableFeaturesAsync(
        IReadOnlyList<FeatureDefinition> features,
        IOperationRunner runner,
        CancellationToken cancellationToken = default)
    {
        return await runner.RunAsync(
            "Disable Windows Features",
            features,
            async (feature, ct) =>
            {
                foreach (string fn in feature.FeatureNames)
                {
                    string script = $"Disable-WindowsOptionalFeature -Online -FeatureName '{fn}' -NoRestart";
                    var res = await _shell.ExecutePowerShellAsync(script, 180);
                    if (!res.Success)
                    {
                        return new OperationItemResult
                        {
                            Id = feature.Key,
                            Name = feature.Name,
                            Status = OperationItemStatus.Failed,
                            Reason = !string.IsNullOrWhiteSpace(res.StdErr) ? res.StdErr.Trim() : $"Failed to disable feature '{fn}'"
                        };
                    }
                }

                feature.IsInstalled = false;
                feature.IsSelected = false;
                return new OperationItemResult
                {
                    Id = feature.Key,
                    Name = feature.Name,
                    Status = OperationItemStatus.Succeeded,
                    Reason = "Feature disabled successfully."
                };
            },
            cancellationToken);
    }

    public async Task<bool> ConfigureOpenSshAsync(bool enable, CancellationToken cancellationToken = default)
    {
        if (enable)
        {
            string script = "Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0; Start-Service sshd; Set-Service -Name sshd -StartupType 'Automatic'";
            var res = await _shell.ExecutePowerShellAsync(script, 180);
            return res.Success;
        }
        else
        {
            string script = "Stop-Service sshd -ErrorAction SilentlyContinue; Set-Service -Name sshd -StartupType 'Disabled'; Remove-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0";
            var res = await _shell.ExecutePowerShellAsync(script, 180);
            return res.Success;
        }
    }
}
