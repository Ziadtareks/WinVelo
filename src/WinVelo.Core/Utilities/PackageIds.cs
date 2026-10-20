using System.Text.RegularExpressions;

namespace WinVelo.Core.Utilities;

/// <summary>
/// Format rules for package ids in Catalog/Apps/applications.json.
/// Ids are passed to winget/choco as separate process arguments (never through a shell), and these
/// rules are a second line of defence: anything that is not a plain id is refused.
/// </summary>
public static partial class PackageIds
{
    // winget-pkgs ids: Publisher.Package[.More], letters, digits and . + - _ (for example "Notepad++.Notepad++").
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9+_-]*(\.[A-Za-z0-9+_-]+)+$")]
    private static partial Regex WingetCommunityIdRegex();

    // Microsoft Store product ids: 12 upper-case letters and digits (for example "9NKSQGP7F2NH"),
    // or "XP" plus 12 more for Store listings of desktop apps (for example "XP8BT8DW290MPQ").
    [GeneratedRegex(@"^(?:[0-9A-Z]{12}|XP[0-9A-Z]{12})$")]
    private static partial Regex StoreIdRegex();

    // Chocolatey ids: lower/upper case letters, digits, '.', '-' and '_' (for example "notepadplusplus", "paint.net").
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")]
    private static partial Regex ChocolateyIdRegex();

    public static bool IsValidWingetId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 128 && (WingetCommunityIdRegex().IsMatch(id) || StoreIdRegex().IsMatch(id));

    /// <summary>True for a Microsoft Store id, which must be installed from the "msstore" source.</summary>
    public static bool IsStoreId(string? id) => !string.IsNullOrEmpty(id) && StoreIdRegex().IsMatch(id);

    public static bool IsValidChocolateyId(string? id) => !string.IsNullOrEmpty(id) && ChocolateyIdRegex().IsMatch(id);

    /// <summary>The winget source an id belongs to: "msstore" for Store ids, otherwise "winget".</summary>
    public static string WingetSource(string id) => IsStoreId(id) ? "msstore" : "winget";
}
