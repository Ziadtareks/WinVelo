using System.Text.Json;
using WinVelo.Core.Engine.Handlers;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;

namespace WinVelo.Core.Engine;

/// <summary>Records toolbox mutations in the same journal used by tweak undo and history.</summary>
public static class ModuleJournal
{
    public static string DisplayName(string id)
    {
        if (id.StartsWith("tools.features.", StringComparison.Ordinal))
        {
            try
            {
                using var doc = Catalog.CatalogLoader.LoadDocument(Catalog.CatalogSource.Default, "Config/feature.json");
                if (doc != null && doc.RootElement.TryGetProperty(id["tools.features.".Length..], out var feature))
                    return feature.GetProperty("Content").GetString() ?? "Windows feature";
            }
            catch (JsonException) { }
            return "Windows feature";
        }
        if (id.StartsWith("tools.dns.", StringComparison.Ordinal)) return "DNS settings";
        return id switch
        {
            "tools.startup" => "Startup apps",
            "tools.updates" => "Windows Update policy",
            "tools.services" => "Windows services",
            "tools.network.nagle" => "Network packet settings",
            "tools.openssh" => "OpenSSH server",
            "tools.power-plan" => "Power plan",
            _ => id
        };
    }

    public static IRegistryService Registry(IRegistryService inner, RollbackJournalManager? journal, string operation)
        => journal == null ? inner : new TrackedRegistry(inner, journal, operation);
    public static ISystemServicesService Services(ISystemServicesService inner, RollbackJournalManager? journal, string operation)
        => journal == null ? inner : new TrackedServices(inner, journal, operation);

    private sealed class TrackedRegistry(IRegistryService inner, RollbackJournalManager journal, string operation) : IRegistryService
    {
        public bool KeyExists(string hive, string key) => inner.KeyExists(hive, key);
        public bool ValueExists(string hive, string key, string name) => inner.ValueExists(hive, key, name);
        public object? GetValue(string hive, string key, string name) => inner.GetValue(hive, key, name);
        public string? GetValueKind(string hive, string key, string name) => inner.GetValueKind(hive, key, name);
        public IEnumerable<string> GetValueNames(string hive, string key) => inner.GetValueNames(hive, key);
        public IEnumerable<string> GetSubKeyNames(string hive, string key) => inner.GetSubKeyNames(hive, key);
        public string ExportRegBackup(string hive, string key, string folder) => inner.ExportRegBackup(hive, key, folder);
        public void CreateKey(string hive, string key) => inner.CreateKey(hive, key);
        public void DeleteKey(string hive, string key) => throw new NotSupportedException("Toolbox operations must remove individual values, not entire registry trees.");
        public void SetValue(string hive, string key, string name, object data, string kind) => Write(hive, key, name, () => inner.SetValue(hive, key, name, data, kind));
        public void DeleteValue(string hive, string key, string name) => Write(hive, key, name, () => inner.DeleteValue(hive, key, name));
        private void Write(string hive, string key, string name, Action write)
        {
            journal.OperationGate.Wait();
            try
            {
                lock (journal)
                {
                    var normalized = Utilities.RegistryPathHelper.Normalize(hive, key);
                    string target = $"{normalized.Hive}\\{normalized.Key}\\{(name.Length == 0 ? "@" : name)}";
                    var baseline = journal.FindExistingBaselineRecord(operation, target);
                    bool exists = inner.ValueExists(hive, key, name);
                    var record = new JournalActionRecord
                    {
                        TweakId = operation,
                        ActionType = "Registry",
                        TargetIdentifier = target,
                        ExistedBefore = baseline?.ExistedBefore ?? exists,
                        PreviousValueKind = baseline?.PreviousValueKind ?? (exists ? inner.GetValueKind(hive, key, name) : null),
                        PreviousData = baseline != null ? baseline.PreviousData : exists ? RegistryActionHandler.SerializeData(inner.GetValue(hive, key, name)) : null,
                        KeyCreated = baseline?.KeyCreated ?? !inner.KeyExists(hive, key),
                        IsBaseline = baseline == null
                    };
                    journal.StartSession(); journal.RecordActionWriteAhead(record);
                    try { write(); }
                    finally { journal.CompleteSession(); }
                }
            }
            finally { journal.OperationGate.Release(); }
        }
    }

    private sealed class TrackedServices(ISystemServicesService inner, RollbackJournalManager journal, string operation) : ISystemServicesService
    {
        public bool ServiceExists(string name) => inner.ServiceExists(name);
        public string GetStartupType(string name) => inner.GetStartupType(name);
        public string GetState(string name) => inner.GetState(name);
        public void SetStartupType(string name, string type) => Write(name, () => inner.SetStartupType(name, type));
        public void StartService(string name) => Write(name, () => inner.StartService(name));
        public void StopService(string name) => Write(name, () => inner.StopService(name));
        private void Write(string name, Action write)
        {
            journal.OperationGate.Wait();
            try
            {
                lock (journal)
                {
                    var baseline = journal.FindExistingBaselineRecord(operation, name);
                    var record = new JournalActionRecord
                    {
                        TweakId = operation,
                        ActionType = "Service",
                        TargetIdentifier = name,
                        ExistedBefore = inner.ServiceExists(name),
                        PreviousValueKind = "ServiceConfig",
                        PreviousData = baseline?.PreviousData ?? $"{inner.GetStartupType(name)}|{inner.GetState(name)}",
                        IsBaseline = baseline == null
                    };
                    journal.StartSession(); journal.RecordActionWriteAhead(record);
                    try { write(); }
                    finally { journal.CompleteSession(); }
                }
            }
            finally { journal.OperationGate.Release(); }
        }
    }

    public static async Task<PowerShellResult> RunScriptAsync(IShellService shell, RollbackJournalManager? journal,
        string id, string script, IReadOnlyList<ActionItem> undo, int timeout = 60, CancellationToken cancellationToken = default)
    {
        if (journal != null) await journal.OperationGate.WaitAsync(cancellationToken);
        try
        {
            if (journal != null)
            {
                journal.StartSession();
                journal.RecordActionWriteAhead(new JournalActionRecord { TweakId = id, ActionType = "PowerShell", TargetIdentifier = "powershell://" + id, UndoActions = undo.ToList() });
            }
            var result = await shell.ExecutePowerShellAsync(script, timeout, cancellationToken);
            journal?.CompleteSession();
            return result;
        }
        finally { journal?.OperationGate.Release(); }
    }

    public static ActionItem UndoScript(string script, int timeout = 60) => new() { Type = "PowerShell", Operation = "Execute", Script = script, TimeoutSeconds = timeout };
}
