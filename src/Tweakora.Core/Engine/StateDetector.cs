using System.Collections.Concurrent;
using System.Text.Json;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;

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

        bool result = EvaluateStructure(tweak.Detect);
        _cache[tweak.Id] = (result, DateTime.UtcNow);
        return Task.FromResult(result);
    }

    private bool EvaluateStructure(DetectStructure detect)
    {
        if (detect.All != null && detect.All.Count > 0)
        {
            return detect.All.All(EvaluateRule);
        }

        if (detect.Any != null && detect.Any.Count > 0)
        {
            return detect.Any.Any(EvaluateRule);
        }

        return EvaluateRule(detect.AsSingleRule());
    }

    private bool EvaluateRule(DetectRule rule)
    {
        switch (rule.Type)
        {
            case "Registry":
                return EvaluateRegistry(rule);
            case "Service":
                return EvaluateService(rule);
            case "ScheduledTask":
                return EvaluateTask(rule);
            default:
                return false;
        }
    }

    private bool EvaluateRegistry(DetectRule rule)
    {
        if (string.IsNullOrEmpty(rule.Hive) || string.IsNullOrEmpty(rule.Key)) return false;

        bool keyExists = _registry.KeyExists(rule.Hive, rule.Key);
        if (!keyExists)
        {
            return rule.MatchType == "NotExists";
        }

        string valName = rule.ValueName ?? string.Empty;
        bool valExists = _registry.ValueExists(rule.Hive, rule.Key, valName);

        switch (rule.MatchType)
        {
            case "Exists":
                return valExists;
            case "NotExists":
                return !valExists;
            case "Equals":
                if (!valExists) return false;
                object? actual = _registry.GetValue(rule.Hive, rule.Key, valName);
                return CompareJsonElementToData(rule.ExpectedValue, actual);
            case "NotEquals":
                if (!valExists) return true;
                object? current = _registry.GetValue(rule.Hive, rule.Key, valName);
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
        return rule.ExpectedState == "Enabled" ? isEnabled : !isEnabled;
    }

    private static bool CompareJsonElementToData(JsonElement? expected, object? actual)
    {
        if (!expected.HasValue && actual == null) return true;
        if (!expected.HasValue || actual == null) return false;

        JsonElement elem = expected.Value;

        if (elem.ValueKind == JsonValueKind.Number)
        {
            if (elem.TryGetInt64(out long expLong) && Convert.ToInt64(actual) == expLong)
                return true;
            if (elem.TryGetDouble(out double expDouble) && Math.Abs(Convert.ToDouble(actual) - expDouble) < 0.0001)
                return true;
        }
        else if (elem.ValueKind == JsonValueKind.String)
        {
            return string.Equals(elem.GetString(), actual.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        else if (elem.ValueKind == JsonValueKind.True || elem.ValueKind == JsonValueKind.False)
        {
            return elem.GetBoolean() == Convert.ToBoolean(actual);
        }

        return false;
    }
}
