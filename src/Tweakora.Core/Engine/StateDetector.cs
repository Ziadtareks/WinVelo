using System;
using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, (bool Result, DateTime CheckedAt)> _cache = new();
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

    public Task<bool> IsTweakActiveAsync(TweakDefinition tweak, bool useCache = true)
    {
        if (useCache && _cache.TryGetValue(tweak.Id, out var cached))
        {
            if (DateTime.UtcNow - cached.CheckedAt < _cacheDuration)
            {
                return Task.FromResult(cached.Result);
            }
        }

        bool result = false;
        try
        {
            result = EvaluateStructure(tweak.Detect);
        }
        catch
        {
            // Detection must fail safe to false (Default), NEVER throw an error
            result = false;
        }

        _cache[tweak.Id] = (result, DateTime.UtcNow);
        return Task.FromResult(result);
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
