using System.Text.Json;
using System.Text.RegularExpressions;
using WinVelo.Core.Models;

namespace WinVelo.Core.Safety;

/// <summary>
/// Enforces in code (not only in the catalog validator) that catalog tweaks can never switch off Windows Update,
/// BITS, the Microsoft Store or Windows Security. Used when the catalog is loaded (the tweak is rejected) and again
/// by every action handler right before it changes something, so even a hand-edited Catalog folder cannot do it.
/// </summary>
public static partial class ServicingGuard
{
    // HKLM\SYSTEM\CurrentControlSet\Services\<name> or ...\ControlSet001\Services\<name>
    [GeneratedRegex(@"^SYSTEM\\(CurrentControlSet|ControlSet\d{3})\\Services\\([^\\]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ServiceKeyRegex();

    /// <summary>Reason why setting a protected service to this startup type (or stopping it) is not allowed, or null.</summary>
    public static string? CheckService(string? serviceName, string? operation, string? startupType)
    {
        if (string.IsNullOrWhiteSpace(serviceName) || !ServicingProtectionHelper.IsProtectedService(serviceName)) return null;

        if (string.Equals(operation, "Stop", StringComparison.OrdinalIgnoreCase))
        {
            return $"'{serviceName}' is needed for Windows Update, the Microsoft Store or Windows Security and is never stopped by WinVelo.";
        }

        if (string.Equals(operation, "SetStartup", StringComparison.OrdinalIgnoreCase) && IsWeakerThanDefault(serviceName, startupType))
        {
            return $"'{serviceName}' is needed for Windows Update, the Microsoft Store or Windows Security; WinVelo never sets it to '{startupType}'.";
        }

        return null;
    }

    /// <summary>Reason why this registry write is not allowed, or null. Deleting a protected policy is always allowed.</summary>
    public static string? CheckRegistry(string? hive, string? key, string? valueName, string? operation, JsonElement? data)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        bool isSet = string.Equals(operation, "Set", StringComparison.OrdinalIgnoreCase);
        string normalizedKey = key.Trim('\\');

        // Writing Start=4 (disabled) or 3 (manual) directly into a protected service's key.
        var m = ServiceKeyRegex().Match(normalizedKey);
        if (m.Success && ServicingProtectionHelper.IsProtectedService(m.Groups[2].Value) && IsMachineHive(hive))
        {
            if (string.Equals(operation, "DeleteKey", StringComparison.OrdinalIgnoreCase))
            {
                return $"The service key of '{m.Groups[2].Value}' must not be deleted.";
            }
            if (isSet && string.Equals(valueName, "Start", StringComparison.OrdinalIgnoreCase))
            {
                string startup = ToNumber(data) switch { 2 => "Automatic", 3 => "Manual", 4 => "Disabled", _ => "Unknown" };
                if (startup == "Unknown" || IsWeakerThanDefault(m.Groups[2].Value, startup))
                {
                    return $"'{m.Groups[2].Value}' is needed for Windows Update, the Microsoft Store or Windows Security; its Start value cannot be set to {ToNumber(data)?.ToString() ?? "this value"}.";
                }
            }
        }

        // Policies that switch off Windows Update or the Store (setting them to 0 or deleting them is fine).
        if (isSet && ServicingProtectionHelper.IsProtectedPolicy(normalizedKey, valueName ?? string.Empty) && ToNumber(data) != 0)
        {
            return $"The policy '{valueName}' would turn off Windows Update or the Microsoft Store.";
        }

        return null;
    }

    /// <summary>Reason why this script is not allowed, or null.</summary>
    public static string? CheckScript(string? script)
    {
        if (string.IsNullOrWhiteSpace(script)) return null;

        foreach (string svc in ServicingProtectionConstants.ProtectedServices)
        {
            string name = Regex.Escape(svc);
            if (Regex.IsMatch(script, $@"\b(Set-Service|Stop-Service|Suspend-Service|sc(\.exe)?\s+(config|stop|delete))\b[^\r\n;|]*\b{name}\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(script, $@"\bGet-Service\b[^\r\n;|]*\b{name}\b[^\r\n;]*\|\s*(Stop-Service|Set-Service)\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(script, $@"Services\\{name}\b[^\r\n;]*\bStart\b", RegexOptions.IgnoreCase))
            {
                return $"The script changes '{svc}', which is needed for Windows Update, the Microsoft Store or Windows Security.";
            }
        }

        // Note: denying access to the Store's search database (store.db) only removes Store results from Start search;
        // it does not turn the Store off. CatalogValidator requires such tweaks to be High risk with side effects.

        return null;
    }

    /// <summary>All reasons why a catalog tweak must not be loaded. Used as a <c>CatalogLoader</c> validator.</summary>
    public static IEnumerable<string> CheckTweak(TweakDefinition tweak)
    {
        foreach (var action in tweak.Apply.Concat(tweak.Undo))
        {
            string? reason = CheckAction(action);
            if (reason != null) yield return reason;
        }
    }

    public static string? CheckAction(ActionItem action)
    {
        return action.Type switch
        {
            var t when t.Equals("Service", StringComparison.OrdinalIgnoreCase) => CheckService(action.ServiceName, action.Operation, action.StartupType),
            var t when t.Equals("Registry", StringComparison.OrdinalIgnoreCase) || t.Equals("Policy", StringComparison.OrdinalIgnoreCase)
                => CheckRegistry(action.Hive, action.Key, action.ValueName, action.Operation, action.Data),
            var t when t.Equals("PowerShell", StringComparison.OrdinalIgnoreCase) || t.Equals("Script", StringComparison.OrdinalIgnoreCase)
                => CheckScript(action.Script),
            _ => null
        };
    }

    private static bool IsWeakerThanDefault(string serviceName, string? startupType)
    {
        if (string.IsNullOrWhiteSpace(startupType)) return false;
        if (startupType.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) return true;

        string defaultStartup = ServicingProtectionConstants.ServiceDefaultStartups.TryGetValue(serviceName, out var d) ? d : "Manual";
        bool isAutomatic = startupType.StartsWith("Automatic", StringComparison.OrdinalIgnoreCase) || startupType.Equals("Auto", StringComparison.OrdinalIgnoreCase);
        return defaultStartup.Equals("Automatic", StringComparison.OrdinalIgnoreCase) && !isAutomatic;
    }

    private static bool IsMachineHive(string? hive) =>
        string.IsNullOrWhiteSpace(hive) ||
        hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase) ||
        hive.Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase);

    private static long? ToNumber(JsonElement? data)
    {
        if (data is not JsonElement e) return null;
        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out long n)) return n;
        if (e.ValueKind == JsonValueKind.String && long.TryParse(e.GetString(), out long s)) return s;
        return null;
    }
}
