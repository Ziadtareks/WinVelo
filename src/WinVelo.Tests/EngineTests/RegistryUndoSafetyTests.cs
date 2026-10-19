using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WinVelo.Core.Engine;
using WinVelo.Core.Engine.Handlers;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;
using WinVelo.Simulation;
using Xunit;
using ExecutionContext = WinVelo.Core.Engine.ExecutionContext;

namespace WinVelo.Tests.EngineTests;

/// <summary>Undo must restore exactly what was there before, and never remove more than WinVelo added.</summary>
public sealed class RegistryUndoSafetyTests : IDisposable
{
    private const string PolicyKey = @"SOFTWARE\Policies\WinVeloTest\NewKey";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WinVeloUndoSafety_" + Guid.NewGuid().ToString("N"));
    private readonly FakeRegistryService _registry = new();

    public RegistryUndoSafetyTests()
    {
        TestModeManager.EnableTestMode();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private TweakRunner Runner(IRegistryService registry)
    {
        var context = new ExecutionContext
        {
            Registry = registry,
            Services = new FakeServicesService(),
            Tasks = new FakeTaskSchedulerService(),
            Shell = new FakeShellService(),
            Journal = new RollbackJournalManager(_dir),
            IsDryRun = false,
            BackupDirectory = _dir
        };
        var detector = new StateDetector(registry, context.Services, context.Tasks);
        return new TweakRunner(new List<IActionHandler> { new RegistryActionHandler() }, context, new FakeRestoreService(), detector);
    }

    private static TweakDefinition SetTweak(string id, string key, string valueName, int data) => new()
    {
        Id = id,
        Name = id,
        Category = "test",
        Risk = "Safe",
        Detect = new DetectStructure
        {
            Type = "Registry", Hive = "HKLM", Key = key, ValueName = valueName, MatchType = "Equals",
            ExpectedValue = JsonDocument.Parse(data.ToString()).RootElement
        },
        Apply = new List<ActionItem>
        {
            new() { Type = "Registry", Hive = "HKLM", Key = key, ValueName = valueName, ValueKind = "DWord", Operation = "Set", Data = JsonDocument.Parse(data.ToString()).RootElement }
        },
        Undo = new List<ActionItem>
        {
            new() { Type = "Registry", Hive = "HKLM", Key = key, ValueName = valueName, Operation = "DeleteValue" }
        }
    };

    [Fact]
    public async Task UndoOfNewValue_DeletesIt_InsteadOfSettingZero()
    {
        var runner = Runner(_registry);
        var tweak = SetTweak("test.a", PolicyKey, "A", 1);

        await runner.ApplyBatchAsync(new[] { tweak });
        Assert.True(_registry.ValueExists("HKLM", PolicyKey, "A"));

        var undo = await runner.UndoTweakAsync(tweak);

        Assert.True(undo.Success, undo.Message);
        Assert.False(_registry.ValueExists("HKLM", PolicyKey, "A"));
        Assert.False(_registry.KeyExists("HKLM", PolicyKey)); // WinVelo created the key and it is empty again
    }

    [Fact]
    public async Task UndoOfTweakThatCreatedAKey_KeepsValuesOthersAddedToThatKey()
    {
        var runner = Runner(_registry);
        var first = SetTweak("test.first", PolicyKey, "A", 1);
        var second = SetTweak("test.second", PolicyKey, "B", 1);

        await runner.ApplyBatchAsync(new[] { first });                 // creates the key
        await runner.ApplyBatchAsync(new[] { second });                // adds B to it
        _registry.SetValue("HKLM", PolicyKey, "FromGroupPolicy", 7, "DWord"); // and something outside WinVelo

        var undo = await runner.UndoTweakAsync(first);

        Assert.True(undo.Success, undo.Message);
        Assert.False(_registry.ValueExists("HKLM", PolicyKey, "A"));
        Assert.True(_registry.ValueExists("HKLM", PolicyKey, "B"));
        Assert.True(_registry.ValueExists("HKLM", PolicyKey, "FromGroupPolicy"));
    }

    [Fact]
    public async Task UndoOfChangedValue_RestoresOriginalDataAndType()
    {
        _registry.SetValue("HKLM", PolicyKey, "A", "original text", "String");
        var runner = Runner(_registry);
        var tweak = SetTweak("test.a", PolicyKey, "A", 1);

        await runner.ApplyBatchAsync(new[] { tweak });
        await runner.UndoTweakAsync(tweak);

        Assert.Equal("original text", _registry.GetValue("HKLM", PolicyKey, "A"));
        Assert.Equal("String", _registry.GetValueKind("HKLM", PolicyKey, "A"));
    }

    [Fact]
    public async Task UndoOfOverwrittenMultiString_RestoresEveryLine()
    {
        _registry.SetValue("HKLM", PolicyKey, "List", new[] { "one", "two" }, "MultiString");
        var runner = Runner(_registry);
        var tweak = SetTweak("test.list", PolicyKey, "List", 1);

        await runner.ApplyBatchAsync(new[] { tweak });
        await runner.UndoTweakAsync(tweak);

        Assert.Equal(new[] { "one", "two" }, Assert.IsType<string[]>(_registry.GetValue("HKLM", PolicyKey, "List")));
    }

    [Fact]
    public async Task AccessDenied_IsReportedAsFailed_NotAsApplied()
    {
        var blocked = new BlockingRegistry(_registry, PolicyKey);
        var runner = Runner(blocked);

        var report = await runner.ApplyBatchAsync(new[] { SetTweak("test.blocked", PolicyKey, "A", 1) });

        var result = Assert.Single(report.Results);
        Assert.True(result.IsFailure, result.Message);
        Assert.Contains("blocked", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A registry where one key behaves like a key protected by Tamper Protection.</summary>
    private sealed class BlockingRegistry : IRegistryService
    {
        private readonly FakeRegistryService _inner;
        private readonly string _blockedKey;

        public BlockingRegistry(FakeRegistryService inner, string blockedKey)
        {
            _inner = inner;
            _blockedKey = blockedKey;
        }

        private void Check(string key)
        {
            if (string.Equals(key, _blockedKey, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Access is denied.");
        }

        public bool KeyExists(string hive, string key) => _inner.KeyExists(hive, key);
        public bool ValueExists(string hive, string key, string valueName) => _inner.ValueExists(hive, key, valueName);
        public object? GetValue(string hive, string key, string valueName) => _inner.GetValue(hive, key, valueName);
        public string? GetValueKind(string hive, string key, string valueName) => _inner.GetValueKind(hive, key, valueName);
        public void SetValue(string hive, string key, string valueName, object data, string valueKind) { Check(key); _inner.SetValue(hive, key, valueName, data, valueKind); }
        public void DeleteValue(string hive, string key, string valueName) { Check(key); _inner.DeleteValue(hive, key, valueName); }
        public void DeleteKey(string hive, string key) { Check(key); _inner.DeleteKey(hive, key); }
        public void CreateKey(string hive, string key) { Check(key); _inner.CreateKey(hive, key); }
        public string ExportRegBackup(string hive, string key, string backupFolder) => _inner.ExportRegBackup(hive, key, backupFolder);
        public IEnumerable<string> GetValueNames(string hive, string key) => _inner.GetValueNames(hive, key);
        public IEnumerable<string> GetSubKeyNames(string hive, string key) => _inner.GetSubKeyNames(hive, key);
    }
}
