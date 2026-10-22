using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;
using WinVelo.Core.Utilities;

namespace WinVelo.Core.Engine;

public sealed class StateDetector
{
    private readonly IRegistryService _registry;
    private readonly ISystemServicesService _services;
    private readonly ITaskSchedulerService _tasks;
    private readonly IShellService? _shell;
    private readonly ConcurrentDictionary<string, (TweakState State, DateTime CheckedAt)> _cache = new();
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(5);

    public StateDetector(
        IRegistryService registry,
        ISystemServicesService services,
        ITaskSchedulerService tasks, IShellService? shell = null)
    {
        _registry = registry;
        _services = services;
        _tasks = tasks;
        _shell = shell;
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

        return Task.Run(async () =>
        {
            TweakState state = TweakState.Unknown;
            try
            {
                state = EvaluateTweakState(tweak);
                bool hasScripts = tweak.Apply.Any(a => a.Type.Equals("PowerShell", StringComparison.OrdinalIgnoreCase) || a.Type.Equals("Script", StringComparison.OrdinalIgnoreCase));
                if (hasScripts && !tweak.ScriptStateDerived && state is TweakState.Applied or TweakState.Unknown)
                {
                    bool? verified = null;
                    if (_shell != null && !string.IsNullOrWhiteSpace(tweak.VerifyScript))
                    {
                        var result = await _shell.ExecutePowerShellAsync(tweak.VerifyScript, 30);
                        if (result.Success && bool.TryParse(result.StdOut.Trim(), out bool value)) verified = value;
                    }
                    state = verified == true ? (state == TweakState.Unknown ? TweakState.Applied : state)
                        : state == TweakState.Applied ? TweakState.Partial : verified == false ? TweakState.NotApplied : TweakState.Unknown;
                }
            }
            catch
            {
                // Detection must fail safe to Unknown, NEVER throw an error
                state = TweakState.Unknown;
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

        // 1. Derive detection directly from Apply actions if verifiable actions exist
        var verifiableActions = tweak.Apply?.Where(IsVerifiableAction).ToList() ?? new List<ActionItem>();

        if (verifiableActions.Count > 0)
        {
            // Case A: If ALL verifiable actions are mutually alternative implementations for a single setting
            // declared in detect.any (e.g. pref.bingsearch setting DisableSearchBoxSuggestions vs BingSearchEnabled),
            // then any matching rule confirms the setting is Applied.
            if (tweak.Detect?.Any != null && tweak.Detect.Any.Count > 0 &&
                verifiableActions.All(a => tweak.Detect.Any.Any(r => IsRuleForAction(r, a))))
            {
                bool anyPassed = tweak.Detect.Any.Any(EvaluateRule);
                return anyPassed ? TweakState.Applied : TweakState.NotApplied;
            }

            // Case B: Composite tweak or tweak with discrete settings (e.g. essential.telemetry).
            // Each verifiable action must be evaluated. An action is satisfied if its primary key matches,
            // OR if an alternative rule in detect.any for the SAME setting (e.g. GPO key vs user/policy key
            // for the same valueName) is satisfied.
            int passedCount = 0;
            foreach (var action in verifiableActions)
            {
                if (EvaluateActionItem(action))
                {
                    passedCount++;
                }
                else if (tweak.Detect?.Any != null && tweak.Detect.Any.Any(r => IsAlternativeRuleForAction(r, action) && EvaluateRule(r)))
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

        // 2. If no verifiable Apply actions, check if explicit Detect block is defined
        if (HasDetectRules(tweak.Detect))
        {
            return EvaluateStructureToState(tweak.Detect);
        }

        // 3. For tweaks that cannot be verified (e.g. pure PowerShell with no inspectable state): return Unknown
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
            return _registry.KeyExists(hive, key);
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
            // A service that is not installed on this edition (e.g. CscService on Home) cannot run,
            // so it never keeps the tweak from being reported as applied.
            return true;
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

            case "BitsAllSet":
            case "BitsAnySet":
            case "BitsClear":
                // Bit flag values (e.g. DisabledComponents, NtfsDisableLastAccessUpdate, StickyKeys Flags):
                // expectedValue is the mask. A missing value never matches.
                if (!valExists) return false;
                if (!TryExtractInt64(rule.ExpectedValue, out long mask)) return false;
                if (!TryExtractInt64(_registry.GetValue(hive, key, valName), out long bits)) return false;
                mask &= 0xFFFFFFFFL;
                bits &= 0xFFFFFFFFL;
                return rule.MatchType switch
                {
                    "BitsAllSet" => (bits & mask) == mask,
                    "BitsAnySet" => (bits & mask) != 0,
                    _ => (bits & mask) == 0
                };

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
            return SameRegistryNumber(expLong, actLong);
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

    /// <summary>A DWORD written as -1 is read back as 4294967295 by some tools (and vice versa).</summary>
    private static bool SameRegistryNumber(long a, long b)
    {
        if (a == b) return true;
        bool aIs32 = a >= int.MinValue && a <= uint.MaxValue;
        bool bIs32 = b >= int.MinValue && b <= uint.MaxValue;
        return aIs32 && bIs32 && unchecked((uint)a) == unchecked((uint)b);
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

    private static bool IsRuleForAction(DetectRule rule, ActionItem action)
    {
        if (rule == null || action == null) return false;

        string actionType = action.Type ?? string.Empty;
        string ruleType = rule.Type ?? string.Empty;

        bool isActionReg = string.Equals(actionType, "registry", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(actionType, "policy", StringComparison.OrdinalIgnoreCase);
        bool isRuleReg = string.Equals(ruleType, "registry", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(ruleType, "policy", StringComparison.OrdinalIgnoreCase);

        if (isActionReg && isRuleReg)
        {
            string actVal = action.ValueName ?? string.Empty;
            string ruleVal = rule.ValueName ?? string.Empty;

            // Direct match on Hive, Key, and ValueName
            if (!string.IsNullOrEmpty(action.Hive) && !string.IsNullOrEmpty(rule.Hive) &&
                !string.IsNullOrEmpty(action.Key) && !string.IsNullOrEmpty(rule.Key))
            {
                var (actHive, actKey) = RegistryPathHelper.Normalize(action.Hive, action.Key);
                var (ruleHive, ruleKey) = RegistryPathHelper.Normalize(rule.Hive, rule.Key);
                if (string.Equals(actHive, ruleHive, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(actKey, ruleKey, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(actVal, ruleVal, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Same value name and target value (alternative location for the same setting)
            if (!string.IsNullOrEmpty(actVal) &&
                string.Equals(actVal, ruleVal, StringComparison.OrdinalIgnoreCase) &&
                ValuesMatch(action.Data, rule.ExpectedValue))
            {
                return true;
            }

            return false;
        }

        if (string.Equals(actionType, "service", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ruleType, "service", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrEmpty(action.ServiceName) &&
                   string.Equals(action.ServiceName, rule.ServiceName, StringComparison.OrdinalIgnoreCase);
        }

        if ((string.Equals(actionType, "task", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(actionType, "scheduledtask", StringComparison.OrdinalIgnoreCase)) &&
            (string.Equals(ruleType, "task", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(ruleType, "scheduledtask", StringComparison.OrdinalIgnoreCase)))
        {
            return !string.IsNullOrEmpty(action.TaskName) &&
                   string.Equals(action.TaskName, rule.TaskName, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsAlternativeRuleForAction(DetectRule rule, ActionItem action)
    {
        if (rule == null || action == null) return false;

        string actionType = action.Type ?? string.Empty;
        string ruleType = rule.Type ?? string.Empty;

        bool isActionReg = string.Equals(actionType, "registry", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(actionType, "policy", StringComparison.OrdinalIgnoreCase);
        bool isRuleReg = string.Equals(ruleType, "registry", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(ruleType, "policy", StringComparison.OrdinalIgnoreCase);

        if (isActionReg && isRuleReg)
        {
            string actVal = action.ValueName ?? string.Empty;
            string ruleVal = rule.ValueName ?? string.Empty;

            // An alternative rule for a registry action is one that targets the same ValueName and target value
            // in an alternative location (e.g. GPO key vs user/system policies for AllowTelemetry)
            if (!string.IsNullOrEmpty(actVal) &&
                string.Equals(actVal, ruleVal, StringComparison.OrdinalIgnoreCase) &&
                ValuesMatch(action.Data, rule.ExpectedValue))
            {
                return true;
            }

            // A bit-flag rule on the same value describes the setting's effect, whatever exact number is stored
            if (!string.IsNullOrEmpty(actVal) &&
                string.Equals(actVal, ruleVal, StringComparison.OrdinalIgnoreCase) &&
                (rule.MatchType ?? string.Empty).StartsWith("Bits", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ValuesMatch(JsonElement? actionData, JsonElement? ruleExpected)
    {
        if (!actionData.HasValue && !ruleExpected.HasValue) return true;
        if (!actionData.HasValue || !ruleExpected.HasValue) return false;

        if (TryExtractInt64(actionData.Value, out long aNum) && TryExtractInt64(ruleExpected.Value, out long rNum))
        {
            return SameRegistryNumber(aNum, rNum);
        }

        string? aStr = actionData.Value.ValueKind == JsonValueKind.String ? actionData.Value.GetString() : actionData.Value.GetRawText();
        string? rStr = ruleExpected.Value.ValueKind == JsonValueKind.String ? ruleExpected.Value.GetString() : ruleExpected.Value.GetRawText();
        return string.Equals(aStr, rStr, StringComparison.OrdinalIgnoreCase);
    }
}
