using System;
using System.Collections.Generic;
using WinVelo.Core.Models;

namespace WinVelo.Core.Catalog;

/// <summary>
/// Finds a tweak by its WinVelo id, its WinUtil origin key (for example WPFTweaksTelemetry) or one of its aliases.
/// Presets, imported configs, the CLI and the search box all use this, so a WinUtil name works everywhere.
/// </summary>
public static class TweakResolver
{
    public static bool Matches(TweakDefinition tweak, string idOrAlias)
    {
        if (string.IsNullOrWhiteSpace(idOrAlias)) return false;
        string key = idOrAlias.Trim();
        if (string.Equals(tweak.Id, key, StringComparison.OrdinalIgnoreCase)) return true;
        if (tweak.Origin != null && string.Equals(tweak.Origin, key, StringComparison.OrdinalIgnoreCase)) return true;
        if (tweak.Aliases != null)
        {
            foreach (string alias in tweak.Aliases)
            {
                if (string.Equals(alias, key, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    /// <summary>True when any of the keys names this tweak.</summary>
    public static bool MatchesAny(TweakDefinition tweak, ISet<string> keys)
    {
        if (keys.Contains(tweak.Id)) return true;
        if (tweak.Origin != null && keys.Contains(tweak.Origin)) return true;
        if (tweak.Aliases != null)
        {
            foreach (string alias in tweak.Aliases)
            {
                if (keys.Contains(alias)) return true;
            }
        }
        return false;
    }

    public static TweakDefinition? Find(IEnumerable<TweakDefinition> tweaks, string idOrAlias)
    {
        TweakDefinition? byAlias = null;
        foreach (var tweak in tweaks)
        {
            if (string.Equals(tweak.Id, idOrAlias?.Trim(), StringComparison.OrdinalIgnoreCase)) return tweak;
            if (byAlias == null && Matches(tweak, idOrAlias ?? string.Empty)) byAlias = tweak;
        }
        return byAlias;
    }
}
