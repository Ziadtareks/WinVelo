using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using WinVelo.Core.Models;
using WinVelo.Core.Utilities;

namespace WinVelo.Core.Profiles;

internal static class RegistryExport
{
    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string Hex(byte[] bytes) => string.Join(',', bytes.Select(b => b.ToString("x2")));
    private static string Root(string hive) => hive switch
    {
        "HKCU" => "HKEY_CURRENT_USER",
        "HKLM" => "HKEY_LOCAL_MACHINE",
        "HKCR" => "HKEY_CLASSES_ROOT",
        "HKU" => "HKEY_USERS",
        "HKCC" => "HKEY_CURRENT_CONFIG",
        _ => throw new ArgumentException("Unsupported registry hive: " + hive)
    };
    public static string Format(ActionItem action)
    {
        var (hive, key) = RegistryPathHelper.Normalize(action.Hive ?? "", action.Key ?? "");
        string path = Root(hive) + "\\" + key;
        if (path.Contains(']') || path.Any(c => c is '\r' or '\n')) throw new ArgumentException("Invalid registry key.");
        if (action.Operation == "DeleteKey") return "[-" + path + "]";
        string section = "[" + path + "]" + Environment.NewLine;
        if (action.Operation == "CreateKey") return section;
        string name = string.IsNullOrEmpty(action.ValueName) ? "@" : "\"" + Escape(action.ValueName) + "\"";
        if (action.Operation == "DeleteValue") return section + name + "=-";
        if (action.Operation != "Set") return "; Omitted unsupported registry operation: " + action.Operation;
        if (action.Data is not JsonElement data) throw new ArgumentException("Missing registry data.");
        string kind = action.ValueKind ?? "DWord";
        string value;
        switch (kind.ToLowerInvariant())
        {
            case "dword": value = "dword:" + unchecked((uint)RegistryPathHelper.ParseDWord(data.ToString())).ToString("x8"); break;
            case "qword":
                long number = data.ValueKind == JsonValueKind.Number ? data.TryGetInt64(out long signed) ? signed : unchecked((long)data.GetUInt64()) : RegistryPathHelper.ParseQWord(data.GetString());
                byte[] bytes = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(bytes, number); value = "hex(b):" + Hex(bytes); break;
            case "binary":
            case "none":
                byte[] binary = data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().Select(v => v.GetByte()).ToArray() : Convert.FromBase64String(data.GetString() ?? "");
                value = (kind.Equals("None", StringComparison.OrdinalIgnoreCase) ? "hex(0):" : "hex:") + Hex(binary); break;
            case "multistring":
                string[] strings = data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().Select(v => v.GetString() ?? "").ToArray() : new[] { data.GetString() ?? "" };
                value = "hex(7):" + Hex(Encoding.Unicode.GetBytes(string.Join("\0", strings) + "\0\0")); break;
            case "expandstring": value = "hex(2):" + Hex(Encoding.Unicode.GetBytes((data.GetString() ?? "") + "\0")); break;
            case "string":
                string text = data.GetString() ?? "";
                value = text.Any(c => c is '\r' or '\n' or '\0') ? "hex(1):" + Hex(Encoding.Unicode.GetBytes(text + "\0")) : "\"" + Escape(text) + "\""; break;
            default: throw new ArgumentException("Unsupported registry value kind: " + kind);
        }
        return section + name + "=" + value;
    }
}
