using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WinVelo.Core.Interfaces;

namespace WinVelo.Modules.Power;

public sealed class PowerPlanResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}

public interface IPowerPlanService
{
    /// <summary>Name and GUID of the active power plan, or null if powercfg could not be read.</summary>
    Task<(string Guid, string Name)?> GetActivePlanAsync(CancellationToken cancellationToken = default);

    /// <summary>WinUtil's "Ultimate Performance Profile - Enable": adds the hidden Ultimate Performance plan and makes it active.</summary>
    Task<PowerPlanResult> EnableUltimatePerformanceAsync(CancellationToken cancellationToken = default);

    /// <summary>WinUtil's "Ultimate Performance Profile - Disable": goes back to the plan that was active before and removes the plan WinVelo added.</summary>
    Task<PowerPlanResult> DisableUltimatePerformanceAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Ultimate Performance power plan, done with powercfg. Unlike WinUtil this never runs
/// "powercfg -restoredefaultschemes", so custom power plans the user made are kept.
/// The plan that was active before is remembered in a small file next to the settings, so Disable can go back to it.
/// </summary>
public sealed class PowerPlanService : IPowerPlanService
{
    /// <summary>Built-in template GUID of the Ultimate Performance scheme (hidden on most editions).</summary>
    public const string UltimateTemplateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    /// <summary>Built-in Balanced scheme, used when the previous plan is unknown or gone.</summary>
    public const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    private static readonly Regex GuidPattern = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);
    private static readonly Regex NamePattern = new(@"\(([^)]*)\)", RegexOptions.Compiled);

    private readonly IShellService _shell;
    private readonly string _stateFile;

    public PowerPlanService(IShellService shell, string stateFile)
    {
        _shell = shell;
        _stateFile = stateFile;
    }

    private sealed class SavedState
    {
        public string? PreviousGuid { get; set; }
        public string? CreatedGuid { get; set; }
    }

    public async Task<(string Guid, string Name)?> GetActivePlanAsync(CancellationToken cancellationToken = default)
    {
        var res = await _shell.RunProcessAsync("powercfg.exe", new[] { "/getactivescheme" }, 30, cancellationToken);
        if (!res.Success) return null;
        return ParseScheme(res.StdOut);
    }

    /// <summary>Reads "Power Scheme GUID: {guid}  (Name)" from powercfg output. The words differ by language; the GUID does not.</summary>
    public static (string Guid, string Name)? ParseScheme(string output)
    {
        var guid = GuidPattern.Match(output ?? string.Empty);
        if (!guid.Success) return null;
        var name = NamePattern.Match(output!, guid.Index + guid.Length);
        return (guid.Value.ToLowerInvariant(), name.Success ? name.Groups[1].Value.Trim() : string.Empty);
    }

    public async Task<PowerPlanResult> EnableUltimatePerformanceAsync(CancellationToken cancellationToken = default)
    {
        var active = await GetActivePlanAsync(cancellationToken);
        var state = LoadState();

        // Already added by WinVelo earlier and still there: just switch to it.
        if (state.CreatedGuid != null && await SchemeExistsAsync(state.CreatedGuid, cancellationToken))
        {
            if (active?.Guid == state.CreatedGuid)
            {
                return new PowerPlanResult { Success = true, Message = "Ultimate Performance is already the active power plan." };
            }
            state.PreviousGuid = active?.Guid ?? state.PreviousGuid;
            SaveState(state);
            return await SetActiveAsync(state.CreatedGuid, "Ultimate Performance is now the active power plan.", cancellationToken);
        }

        var dup = await _shell.RunProcessAsync("powercfg.exe", new[] { "-duplicatescheme", UltimateTemplateGuid }, 30, cancellationToken);
        if (!dup.Success)
        {
            return new PowerPlanResult
            {
                Success = false,
                Message = $"powercfg could not add the Ultimate Performance plan (exit code {dup.ExitCode})."
            };
        }

        var created = ParseScheme(dup.StdOut);
        if (created == null)
        {
            return new PowerPlanResult { Success = false, Message = "powercfg did not report the new plan's GUID." };
        }

        state.PreviousGuid = active?.Guid;
        state.CreatedGuid = created.Value.Guid;
        SaveState(state);

        return await SetActiveAsync(created.Value.Guid, "Ultimate Performance plan added and made active.", cancellationToken);
    }

    public async Task<PowerPlanResult> DisableUltimatePerformanceAsync(CancellationToken cancellationToken = default)
    {
        var state = LoadState();
        string target = state.PreviousGuid != null && state.PreviousGuid != state.CreatedGuid && await SchemeExistsAsync(state.PreviousGuid, cancellationToken)
            ? state.PreviousGuid
            : BalancedGuid;

        var switched = await SetActiveAsync(target, string.Empty, cancellationToken);
        if (!switched.Success) return switched;

        if (state.CreatedGuid != null && await SchemeExistsAsync(state.CreatedGuid, cancellationToken))
        {
            var del = await _shell.RunProcessAsync("powercfg.exe", new[] { "/delete", state.CreatedGuid }, 30, cancellationToken);
            if (!del.Success)
            {
                return new PowerPlanResult { Success = false, Message = $"Switched back, but powercfg could not delete the Ultimate Performance plan (exit code {del.ExitCode})." };
            }
        }

        DeleteState();
        return new PowerPlanResult
        {
            Success = true,
            Message = target == BalancedGuid ? "Switched to the Balanced power plan." : "Switched back to your previous power plan."
        };
    }

    private async Task<PowerPlanResult> SetActiveAsync(string guid, string successMessage, CancellationToken cancellationToken)
    {
        var res = await _shell.RunProcessAsync("powercfg.exe", new[] { "/setactive", guid }, 30, cancellationToken);
        return res.Success
            ? new PowerPlanResult { Success = true, Message = successMessage }
            : new PowerPlanResult { Success = false, Message = $"powercfg could not switch the power plan (exit code {res.ExitCode})." };
    }

    private async Task<bool> SchemeExistsAsync(string guid, CancellationToken cancellationToken)
    {
        var res = await _shell.RunProcessAsync("powercfg.exe", new[] { "/list" }, 30, cancellationToken);
        return res.Success && res.StdOut.Contains(guid, StringComparison.OrdinalIgnoreCase);
    }

    private SavedState LoadState()
    {
        try
        {
            if (File.Exists(_stateFile))
            {
                return JsonSerializer.Deserialize<SavedState>(File.ReadAllText(_stateFile)) ?? new SavedState();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new SavedState();
    }

    private void SaveState(SavedState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void DeleteState()
    {
        try { File.Delete(_stateFile); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
