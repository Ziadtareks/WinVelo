using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;
using Tweakora.Core.Utilities;

namespace Tweakora.Core.Engine;

public sealed class StateDetector
{
    private readonly IRegistryService _registry;
    private readonly ISystemServicesService _services;
    private readonly ITaskSchedulerService _tasks;
    private readonly ConcurrentDictionary<string, (TweakState State, DateTime CheckedAt)> _cache = new();
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(5);

    public StateDetector(
        IRegistryService registry,
        ISystemServicesService services,
        ITaskSchedulerService tasks)
    {
        _registry = registry;
        _services = services;
        _tasks = tasks;
    }

    public void InvalidateCache(string? tweakId = null)
    {
        if (tweakId == null)
        {
            _cache.Clear();
        }
        else
        {
            _cache.TryRemove(tweakId, out _);
        }
    }

    public Task<TweakState> DetectTweakStateAsync(TweakDefinition tweak, bool useCache = true)
    {
        if (useCache && _cache.TryGetValue(tweak.Id, out var cached))
        {
            if (DateTime.UtcNow - cached.CheckedAt < _cacheDuration)
            {
                return Task.FromResult(cached.State);
            }
        }

        return Task.Run(() =>
        {
            TweakState state = TweakState.Unknown;
            try
            {
                state = EvaluateTweakState(tweak);
            }
            catch
            {
                // Detection must fail safe to NotApplied, NEVER throw an error
                state = TweakState.NotApplied;
            }

            _cache[tweak.Id] = (state, DateTime.UtcNow);
            return state;
        });
    }

    public async Task<bool> IsTweakActiveAsync(TweakDefinition tweak, bool useCache = true)
    {
        var state = await DetectTweakStateAsync(tweak, useCache);
        return state == TweakState.Applied;
    }

    private TweakState EvaluateTweakState(TweakDefinition tweak)
    {
        if (tweak == null) return TweakState.Unknown;

        // 1. Explicit alternative conditions: if detect.any is specified, ANY match indicates active state
        // (e.g. Bing Search or Telemetry disabled via GPO vs user settings)
        if (tweak.Detect?.Any != null && tweak.Detect.Any.Count > 0)
        {
            bool anyPassed = tweak.Detect.Any.Any(EvaluateRule);
            return anyPassed ? TweakState.Applied : TweakState.NotApplied;
        }

        // 2. Derive detection directly from Apply actions if verifiable actions exist
        var verifiableActions = tweak.Apply?.Where(IsVerifiableAction).ToList() ?? new List<ActionItem>();

        if (verifiableActions.Count > 0)
        {
            int passedCount = 0;
            foreach (var action in verifiableActions)
            {
                if (EvaluateActionItem(action))
                {
                    passedCount++;
                }
            }

            if (passedCount == verifiableActions.Count)
            {
                return TweakState.Applied;
            }
            if (passedCount > 0)
            {
                return TweakState.Partial;
            }
            return TweakState.NotApplied;
        }

        // 3. If no verifiable Apply actions, check if explicit Detect block is defined
        if (HasDetectRules(tweak.Detect))
        {
            return EvaluateStructureToState(tweak.Detect);
        }

        // 4. For tweaks that cannot be verified (e.g. pure PowerShell with no inspectable state): return Unknown
        return TweakState.Unknown;
    }

    private static bool IsVerifiableAction(ActionItem action)
    {
        string type = action.Type ?? string.Empty;
        if (string.Equals(type, "registry", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "policy", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrEmpty(action.Hive) && !string.IsNullOrEmpty(action.Key);
        }
        if (string.Equals(type, "service", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrEmpty(action.ServiceName);
        }
        if (string.Equals(type, "task", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "scheduledtask", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrEmpty(action.TaskName);
        }
        return false;
    }

    private bool EvaluateActionItem(ActionItem action)
    {
        string type = action.Type ?? string.Empty;
        if (string.Equals(type, "registry", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "policy", StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateRegistryAction(action);
        }
        if (string.Equals(type, "service", StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateServiceAction(action);
        }
        if (string.Equals(type, "task", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "scheduledtask", StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateTaskAction(action);
        }
        return false;
    }

    private bool EvaluateRegistryAction(ActionItem action)
    {
        if (string.IsNullOrEmpty(action.Hive) || string.IsNullOrEmpty(action.Key)) return false;

        var (hive, key) = RegistryPathHelper.Normalize(action.Hive, action.Key);
        string valName = action.ValueName ?? string.Empty;
        string op = action.Operation ?? "Set";

        bool isRemoveEntry = action.Data.HasValue &&
            action.Data.Value.ValueKind == JsonValueKind.String &&
            string.Equals(action.Data.Value.GetString(), "<RemoveEntry>", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(op, "DeleteValue", StringComparison.OrdinalIgnoreCase) || isRemoveEntry)
        {
            if (!_registry.KeyExists(hive, key)) return true;
            return !_registry.ValueExists(hive, key, valName);
        }

        if (string.Equals(op, "DeleteKey", StringComparison.OrdinalIgnoreCase))
        {
            return !_registry.KeyExists(hive, key);
        }

        if (string.Equals(op, "CreateKey", StringComparison.OrdinalIgnoreCase))
        {
            return !_registry.KeyExists(hive, key);
        }

        // Default operation is "Set"
        if (!_registry.KeyExists(hive, key)) return false;

        // Special COM redirection / empty default value pattern (e.g. InprocServer32 key presence)
        if (string.IsNullOrEmpty(valName) &&
            action.Data.HasValue &&
            action.Data.Value.ValueKind == JsonValueKind.String &&
            string.IsNullOrEmpty(action.Data.Value.GetString()))
        {
            if (!_registry.ValueExists(hive, key, valName)) return true;
            object? defVal = _registry.GetValue(hive, key, valName);
            return defVal == null || string.Equals(defVal.ToString(), "", StringComparison.OrdinalIgnoreCase);
        }

        if (!_registry.ValueExists(hive, key, valName)) return false;

        object? actual = _registry.GetValue(hive, key, valName);
        return CompareJsonElementToData(action.Data, actual);
    }

    private bool EvaluateServiceAction(ActionItem action)
    {
        if (string.IsNullOrEmpty(action.ServiceName)) return false;
        if (!_services.ServiceExists(action.ServiceName))
        {
            return false;
        }

        string targetStartup = action.StartupType ?? string.Empty;
        if (string.IsNullOrEmpty(targetStartup))
        {
            string op = action.Operation ?? string.Empty;
            if (string.Equals(op, "Disable", StringComparison.OrdinalIgnoreCase))
            {
                targetStartup = "Disabled";
            }
            else if (string.Equals(op, "Enable", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(op, "Start", StringComparison.OrdinalIgnoreCase))
            {
                targetStartup = "Automatic";
            }
        }

        if (!string.IsNullOrEmpty(targetStartup))
        {
            string actualStartup = _services.GetStartupType(action.ServiceName);
            return string.Equals(actualStartup, targetStartup, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private bool EvaluateTaskAction(ActionItem action)
    {
        if (string.IsNullOrEmpty(action.TaskName)) return false;
        string path = action.TaskPath ?? "\\";
        if (!_tasks.TaskExists(path, action.TaskName)) return true; // If task doesn't exist, it cannot run

        bool isEnabled = _tasks.IsTaskEnabled(path, action.TaskName);
        string op = action.Operation ?? "Disable";
        if (string.Equals(op, "Disable", StringComparison.OrdinalIgnoreCase))
        {
            return !isEnabled;
        }
        if (string.Equals(op, "Enable", StringComparison.OrdinalIgnoreCase))
        {
            return isEnabled;
        }
        return true;
    }

    private static bool HasDetectRules(DetectStructure? detect)
    {
        if (detect == null) return false;
        if (detect.All != null && detect.All.Count > 0) return true;
        if (detect.Any != null && detect.Any.Count > 0) return true;
        var single = detect.AsSingleRule();
        return single != null && !string.IsNullOrEmpty(single.Type);
    }

    private TweakState EvaluateStructureToState(DetectStructure? detect)
    {
        if (detect == null) return TweakState.Unknown;

        if (detect.All != null && detect.All.Count > 0)
        {
            int passed = detect.All.Count(EvaluateRule);
            if (passed == detect.All.Count) return TweakState.Applied;
            if (passed > 0) return TweakState.Partial;
            return TweakState.NotApplied;
        }

        if (detect.Any != null && detect.Any.Count > 0)
        {
            bool anyPassed = detect.Any.Any(EvaluateRule);
            return anyPassed ? TweakState.Applied : TweakState.NotApplied;
        }

        var single = detect.AsSingleRule();
        if (single != null && !string.IsNullOrEmpty(single.Type))
        {
            bool passed = EvaluateRule(single);
            return passed ? TweakState.Applied : TweakState.NotApplied;
        }

        return TweakState.Unknown;
    }

    private bool EvaluateStructure(DetectStructure detect)
    {
        if (detect == null) return false;

        if (detect.All != null && detect.All.Count > 0)
        {
            return detect.All.All(EvaluateRule);
        }

        if (detect.Any != null && detect.Any.Count > 0)
        {
            return detect.Any.Any(EvaluateRule);
        }

        var single = detect.AsSingleRule();
        return single != null && EvaluateRule(single);
    }

    private bool EvaluateRule(DetectRule? rule)
    {
        if (rule == null || string.IsNullOrEmpty(rule.Type)) return false;

        try
        {
            return rule.Type switch
            {
                "Registry" or "Policy" => EvaluateRegistry(rule),
                "Service" => EvaluateService(rule),
                "ScheduledTask" or "Task" => EvaluateTask(rule),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private bool EvaluateRegistry(DetectRule rule)
    {
        if (string.IsNullOrEmpty(rule.Hive) || string.IsNullOrEmpty(rule.Key)) return false;

        var (hive, key) = RegistryPathHelper.Normalize(rule.Hive, rule.Key);
        string valName = rule.ValueName ?? string.Empty;

        bool isExpectedRemove = rule.ExpectedValue.HasValue &&
            rule.ExpectedValue.Value.ValueKind == JsonValueKind.String &&
            string.Equals(rule.ExpectedValue.Value.GetString(), "<RemoveEntry>", StringComparison.OrdinalIgnoreCase);

        bool keyExists = _registry.KeyExists(hive, key);
        if (!keyExists)
        {
            if (rule.MatchType == "NotExists") return true;
            if (rule.MatchType == "Equals" && isExpectedRemove) return true;
            if (rule.MatchType == "NotEquals") return true;
            return false;
        }

        if (string.IsNullOrEmpty(rule.ValueName))
        {
            if (rule.MatchType == "Exists") return keyExists;
            if (rule.MatchType == "NotExists") return !keyExists;
        }

        bool valExists = _registry.ValueExists(hive, key, valName);

        switch (rule.MatchType)
        {
            case "Exists":
                return valExists;

            case "NotExists":
                return !valExists;

            case "Equals":
                if (isExpectedRemove)
                {
                    return !valExists;
                }
                if (!valExists) return false;
                object? actual = _registry.GetValue(hive, key, valName);
                return CompareJsonElementToData(rule.ExpectedValue, actual);

            case "NotEquals":
                if (isExpectedRemove)
                {
                    return valExists;
                }
                if (!valExists) return true;
                object? current = _registry.GetValue(hive, key, valName);
                return !CompareJsonElementToData(rule.ExpectedValue, current);

            default:
                return false;
        }
    }

    private bool EvaluateService(DetectRule rule)
    {
        if (string.IsNullOrEmpty(rule.ServiceName)) return false;
        if (!_services.ServiceExists(rule.ServiceName)) return false;

        if (!string.IsNullOrEmpty(rule.ExpectedStartup))
        {
            string actualStartup = _services.GetStartupType(rule.ServiceName);
            if (!actualStartup.Equals(rule.ExpectedStartup, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrEmpty(rule.ExpectedState))
        {
            string actualState = _services.GetState(rule.ServiceName);
            if (!actualState.Equals(rule.ExpectedState, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private bool EvaluateTask(DetectRule rule)
    {
        if (string.IsNullOrEmpty(rule.TaskName)) return false;
        string path = rule.TaskPath ?? "\\";

        if (!_tasks.TaskExists(path, rule.TaskName)) return false;

        bool isEnabled = _tasks.IsTaskEnabled(path, rule.TaskName);
        return string.Equals(rule.ExpectedState, "Enabled", StringComparison.OrdinalIgnoreCase) ? isEnabled : !isEnabled;
    }

    private static bool CompareJsonElementToData(JsonElement? expected, object? actual)
    {
        if (!expected.HasValue && actual == null) return true;
        if (!expected.HasValue || actual == null) return false;

        JsonElement elem = expected.Value;

        if (elem.ValueKind == JsonValueKind.String &&
            string.Equals(elem.GetString(), "<RemoveEntry>", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TryExtractInt64(elem, out long expLong) && TryExtractInt64(actual, out long actLong))
        {
            return expLong == actLong;
        }

        if (elem.ValueKind == JsonValueKind.True || elem.ValueKind == JsonValueKind.False)
        {
            if (bool.TryParse(actual.ToString(), out bool b))
            {
                return elem.GetBoolean() == b;
            }
            if (TryExtractInt64(actual, out long actNum))
            {
                return (elem.GetBoolean() ? 1 : 0) == actNum;
            }
        }

        string? expStr = elem.ValueKind == JsonValueKind.String ? elem.GetString() : elem.GetRawText();
        string actStr = actual.ToString() ?? string.Empty;
        return string.Equals(expStr, actStr, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractInt64(object? value, out long result)
    {
        result = 0;
        if (value == null) return false;
        if (value is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number)
                return el.TryGetInt64(out result);
            if (el.ValueKind == JsonValueKind.String)
                return ParseLong(el.GetString(), out result);
            return false;
        }
        if (value is int i) { result = i; return true; }
        if (value is long l) { result = l; return true; }
        if (value is byte b) { result = b; return true; }
        if (value is short s) { result = s; return true; }
        return ParseLong(value.ToString(), out result);
    }

    private static bool ParseLong(string? s, out long result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out result);
        }
        return long.TryParse(s, out result);
    }
}
