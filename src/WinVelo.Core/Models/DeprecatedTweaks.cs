using System;
using System.Collections.Generic;

namespace WinVelo.Core.Models;

/// <summary>
/// Registry of deprecated / removed tweaks that merely configured default Windows settings
/// (redundant with Undo on canonical tweaks). Prevents accidental alias mapping to opposite actions.
/// </summary>
public static class DeprecatedTweaks
{
    private static readonly Dictionary<string, string> RemovedOpposites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pref.mouseacceleration"] = "gaming.input.disable-mouse-acceleration",
        ["WPFToggleMouseAcceleration"] = "gaming.input.disable-mouse-acceleration",
        ["advanced.storage"] = "performance.system.storage-sense-disable",
        ["pref.taskbaralignment"] = "ui.taskbar.alignment-left",
        ["WPFToggleTaskbarAlignment"] = "ui.taskbar.alignment-left",
        ["pref.taskview"] = "ui.taskbar.disable-task-view-button",
        ["WPFToggleTaskview"] = "ui.taskbar.disable-task-view-button"
    };

    public static bool IsDeprecated(string id) =>
        !string.IsNullOrWhiteSpace(id) && RemovedOpposites.ContainsKey(id);

    public static bool TryGetCanonicalId(string id, out string canonicalId) =>
        RemovedOpposites.TryGetValue(id ?? string.Empty, out canonicalId!);

    public static bool TryGetRemovalMessage(string id, out string message)
    {
        if (!string.IsNullOrWhiteSpace(id) && RemovedOpposites.TryGetValue(id, out var canonicalId))
        {
            message = $"Tweak '{id}' was removed: use Undo on '{canonicalId}' instead.";
            return true;
        }
        message = string.Empty;
        return false;
    }
}
