using System;
using System.Collections.Generic;
using System.Linq;
using WinVelo.Core.Models;

namespace WinVelo.Core;

public enum CompatibilityIssueKind
{
    BuildTooOld,
    BuildTooNew,
    Edition
}

public sealed record CompatibilityIssue(
    CompatibilityIssueKind Kind,
    int CurrentBuild,
    int RequiredBuild,
    string CurrentEdition,
    IReadOnlyList<string> SupportedEditions)
{
    public string Describe() => Kind switch
    {
        CompatibilityIssueKind.BuildTooOld => $"Needs Windows build {RequiredBuild} or newer. This PC has build {CurrentBuild}.",
        CompatibilityIssueKind.BuildTooNew => $"Only works up to Windows build {RequiredBuild}. This PC has build {CurrentBuild}.",
        _ => $"Not supported on Windows {CurrentEdition}. Needs {string.Join(", ", SupportedEditions)}."
    };
}

/// <summary>
/// Checks a tweak's "windows" requirements (minBuild, maxBuild, editions) against the running PC.
/// It stays inactive until <see cref="Configure"/> is called, which the app and the CLI only do
/// when they work on the real system, so test mode and unit tests are never affected.
/// </summary>
public static class WindowsCompatibility
{
    private static int? _build;
    private static string _edition = string.Empty;

    public static bool IsConfigured => _build.HasValue;

    public static void Configure(int build, string? editionId)
    {
        _build = build > 0 ? build : null;
        _edition = NormalizeEdition(editionId);
    }

    public static void Reset()
    {
        _build = null;
        _edition = string.Empty;
    }

    /// <summary>Maps the registry EditionID (Core, Professional, EnterpriseS...) to the catalog names.</summary>
    public static string NormalizeEdition(string? editionId)
    {
        string id = (editionId ?? string.Empty).Trim();
        if (id.Length == 0) return string.Empty;
        if (id.StartsWith("Core", StringComparison.OrdinalIgnoreCase)) return "Home";
        if (id.StartsWith("Server", StringComparison.OrdinalIgnoreCase)) return "Server";
        if (id.StartsWith("IoTEnterprise", StringComparison.OrdinalIgnoreCase)) return "IoTEnterprise";
        if (id.StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase)) return "Enterprise";
        if (id.Contains("Education", StringComparison.OrdinalIgnoreCase)) return "Education";
        if (id.StartsWith("Professional", StringComparison.OrdinalIgnoreCase)) return "Pro";
        return id;
    }

    public static CompatibilityIssue? Check(TweakDefinition tweak)
    {
        if (!_build.HasValue || tweak?.Windows == null) return null;
        return Check(tweak.Windows, _build.Value, _edition);
    }

    public static CompatibilityIssue? Check(WindowsRequirements req, int build, string edition)
    {
        var editions = (IReadOnlyList<string>)(req.Editions ?? new List<string>());
        if (req.MinBuild > 0 && build < req.MinBuild)
            return new CompatibilityIssue(CompatibilityIssueKind.BuildTooOld, build, req.MinBuild, edition, editions);
        if (req.MaxBuild.HasValue && build > req.MaxBuild.Value)
            return new CompatibilityIssue(CompatibilityIssueKind.BuildTooNew, build, req.MaxBuild.Value, edition, editions);
        if (editions.Count > 0 && !string.IsNullOrEmpty(edition) &&
            !editions.Any(e => string.Equals(e, edition, StringComparison.OrdinalIgnoreCase) ||
                               (e.Equals("Enterprise", StringComparison.OrdinalIgnoreCase) && edition.Equals("IoTEnterprise", StringComparison.OrdinalIgnoreCase))))
            return new CompatibilityIssue(CompatibilityIssueKind.Edition, build, 0, edition, editions);
        return null;
    }
}
