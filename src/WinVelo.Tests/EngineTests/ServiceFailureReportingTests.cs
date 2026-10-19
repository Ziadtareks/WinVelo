using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
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

/// <summary>A service change that Windows refuses is a failure, never "success with a warning".</summary>
public sealed class ServiceFailureReportingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WinVeloSvcFail_" + Guid.NewGuid().ToString("N"));

    public ServiceFailureReportingTests()
    {
        TestModeManager.EnableTestMode();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Services that refuse every change, like a service protected by Tamper Protection.</summary>
    private sealed class RefusingServices : ISystemServicesService
    {
        private readonly FakeServicesService _inner = new();
        public bool ServiceExists(string serviceName) => _inner.ServiceExists(serviceName);
        public string GetStartupType(string serviceName) => _inner.GetStartupType(serviceName);
        public string GetState(string serviceName) => _inner.GetState(serviceName);
        public void SetStartupType(string serviceName, string startupType) => throw new UnauthorizedAccessException("Access is denied.");
        public void StartService(string serviceName) => throw new InvalidOperationException("Cannot start service.");
        public void StopService(string serviceName) => throw new InvalidOperationException("Cannot stop service.");
    }

    /// <summary>A registry that refuses writes, so the Start-value fallback fails too.</summary>
    private sealed class RefusingRegistry : IRegistryService
    {
        private readonly FakeRegistryService _inner = new();
        public bool KeyExists(string hive, string key) => _inner.KeyExists(hive, key);
        public bool ValueExists(string hive, string key, string valueName) => _inner.ValueExists(hive, key, valueName);
        public object? GetValue(string hive, string key, string valueName) => _inner.GetValue(hive, key, valueName);
        public string? GetValueKind(string hive, string key, string valueName) => _inner.GetValueKind(hive, key, valueName);
        public void SetValue(string hive, string key, string valueName, object data, string valueKind) => throw new UnauthorizedAccessException("Access is denied.");
        public void DeleteValue(string hive, string key, string valueName) => throw new UnauthorizedAccessException("Access is denied.");
        public void DeleteKey(string hive, string key) => throw new UnauthorizedAccessException("Access is denied.");
        public void CreateKey(string hive, string key) => throw new UnauthorizedAccessException("Access is denied.");
        public string ExportRegBackup(string hive, string key, string backupFolder) => string.Empty;
        public IEnumerable<string> GetValueNames(string hive, string key) => _inner.GetValueNames(hive, key);
        public IEnumerable<string> GetSubKeyNames(string hive, string key) => _inner.GetSubKeyNames(hive, key);
    }

    private ExecutionContext Context(ISystemServicesService services, IRegistryService registry) => new()
    {
        Registry = registry,
        Services = services,
        Tasks = new FakeTaskSchedulerService(),
        Shell = new FakeShellService(),
        Journal = new RollbackJournalManager(_dir),
        IsDryRun = false
    };

    [Theory]
    [InlineData("SetStartup", "Disabled", "Could not change service")]
    [InlineData("Start", null, "Could not start service")]
    [InlineData("Stop", null, "Could not stop service")]
    public async Task RefusedServiceChange_IsAFailure(string operation, string? startup, string expectedText)
    {
        var context = Context(new RefusingServices(), new RefusingRegistry());
        var result = await new ServiceActionHandler().ExecuteAsync(
            new ActionItem { Type = "Service", ServiceName = "Spooler", Operation = operation, StartupType = startup }, "t", 0, context);

        Assert.False(result.Success);
        Assert.StartsWith(expectedText, result.Message);
        Assert.DoesNotContain("[WARN]", result.Message);
    }

    [Fact]
    public async Task MissingService_IsStillASkip_NotAFailure()
    {
        var context = Context(new FakeServicesService(), new FakeRegistryService());
        var result = await new ServiceActionHandler().ExecuteAsync(
            new ActionItem { Type = "Service", ServiceName = "NoSuchServiceOnThisEdition", Operation = "SetStartup", StartupType = "Disabled" }, "t", 0, context);

        Assert.True(result.Success);
        Assert.Contains("not installed", result.Message);
    }

    [Fact]
    public async Task RefusedServiceUndo_IsAFailure_AndKeepsTheRecord()
    {
        var context = Context(new RefusingServices(), new RefusingRegistry());
        var record = new JournalActionRecord { TweakId = "t", ActionType = "Service", TargetIdentifier = "Spooler", PreviousData = "Automatic|Running", ExistedBefore = true };

        var result = await new ServiceActionHandler().UndoAsync(record, context);

        Assert.False(result.Success);
        Assert.StartsWith("Could not restore", result.Message);
        Assert.False(record.IsReverted);
    }

    [Fact]
    public void EveryServiceMessage_HasAnArabicPattern()
    {
        string? dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir!, "WinVelo.sln"))) dir = Path.GetDirectoryName(dir);
        var ar = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(dir!, "src", "WinVelo.App", "Localization", "Lang-ar.json")))!;

        var patterns = typeof(ServiceMessages).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.NotEmpty(patterns);
        foreach (string p in patterns)
        {
            Assert.True(ar.TryGetValue(p, out string? arabic), $"No Arabic for '{p}'.");
            foreach (Match m in Regex.Matches(p, @"\{\d\}")) Assert.Contains(m.Value, arabic);
        }
    }
}
