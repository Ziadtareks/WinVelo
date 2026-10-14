using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Tweakora.Core.Models;

namespace Tweakora.Core.Utilities;

public static class TweakActionFormatter
{
    public static IReadOnlyList<string> FormatExactChanges(TweakDefinition? tweak)
    {
        if (tweak?.Apply == null || tweak.Apply.Count == 0)
            return Array.Empty<string>();

        var list = new List<string>(tweak.Apply.Count);
        foreach (var action in tweak.Apply)
        {
            list.Add(FormatAction(action));
        }
        return list;
    }

    public static string FormatAction(ActionItem action)
    {
        if (action == null) return string.Empty;

        string type = (action.Type ?? string.Empty).ToLowerInvariant();
        return type switch
        {
            "registry" => FormatRegistryAction(action),
            "service" => FormatServiceAction(action),
            "task" or "scheduledtask" => FormatTaskAction(action),
            "powershell" or "script" => FormatPowerShellAction(action),
            "appx" => FormatAppxAction(action),
            "feature" => FormatFeatureAction(action),
            "file" => FormatFileAction(action),
            _ => $"[{action.Type}] {action.Operation}"
        };
    }

    private static string FormatRegistryAction(ActionItem action)
    {
        string op = action.Operation?.ToLowerInvariant() ?? "set";
        string target = $"{action.Hive}\\{action.Key}";

        if (op == "deletevalue")
        {
            return $"{target}\\{action.ValueName} (Delete Value)";
        }
        if (op == "deletekey")
        {
            return $"{target} (Delete Key)";
        }

        string valName = string.IsNullOrEmpty(action.ValueName) ? "(Default)" : action.ValueName;
        string dataStr = action.Data?.ToString() ?? "null";
        string kindStr = !string.IsNullOrEmpty(action.ValueKind) ? $" ({action.ValueKind})" : string.Empty;

        return $"{target}\\{valName} = {dataStr}{kindStr}";
    }

    private static string FormatServiceAction(ActionItem action)
    {
        string startup = action.StartupType ?? action.Operation ?? "Configure";
        return $"Service '{action.ServiceName}' → StartupType: {startup}";
    }

    private static string FormatTaskAction(ActionItem action)
    {
        string path = action.TaskPath?.TrimEnd('\\') ?? string.Empty;
        string fullTask = string.IsNullOrEmpty(path) ? action.TaskName ?? "UnknownTask" : $"{path}\\{action.TaskName}";
        string op = action.Operation ?? "Configure";
        return $"Scheduled Task '{fullTask}' → {op}";
    }

    private static string FormatPowerShellAction(ActionItem action)
    {
        string summary = SummarizeScript(action.Script);
        return $"PowerShell: {summary}";
    }

    private static string FormatAppxAction(ActionItem action)
    {
        return $"AppX Package '{action.PackagePattern}' → {action.Operation ?? "Remove"}";
    }

    private static string FormatFeatureAction(ActionItem action)
    {
        return $"Optional Feature '{action.FeatureName}' → {action.Operation ?? "Configure"}";
    }

    private static string FormatFileAction(ActionItem action)
    {
        return $"File '{action.PathPattern}' in '{action.AllowedRoot}' → {action.Operation ?? "Delete"}";
    }

    public static string SummarizeScript(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return "Execute script block";

        var lines = script
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 0)
            return "Execute script block";

        // If the script starts with a comment, use the first comment as descriptive summary
        var commentLine = lines.FirstOrDefault(l => l.StartsWith("#") && l.Length > 2);
        if (commentLine != null)
        {
            return commentLine.TrimStart('#', ' ').Trim();
        }

        // Otherwise pick the first command line
        string first = lines[0];
        if (first.Length > 90)
            return first.Substring(0, 87) + "...";

        if (lines.Count > 1)
            return $"{first} (+ {lines.Count - 1} more commands)";

        return first;
    }
}
