using System.Text.Json;
using WinVelo.Core.Models;

namespace WinVelo.Core.Catalog;

/// <summary>Items read from the catalog plus the problems that were skipped.</summary>
public sealed class CatalogLoadResult<T>
{
    public List<T> Items { get; } = new();
    public List<CatalogIssue> Issues { get; } = new();
}

/// <summary>
/// Loads tweaks and presets from a <see cref="CatalogSource"/>.
/// A file that is not valid JSON is skipped. Inside a valid file, a tweak that cannot be read
/// (or is rejected by <see cref="TweakValidators"/>) is skipped on its own; the other tweaks still load.
/// </summary>
public static class CatalogLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Extra checks run on every tweak before it is accepted. Each returns problems; any problem rejects the tweak.</summary>
    public static IList<Func<TweakDefinition, IEnumerable<string>>> TweakValidators { get; } = new List<Func<TweakDefinition, IEnumerable<string>>>();

    public static CatalogLoadResult<TweakDefinition> LoadTweaks(CatalogSource source)
    {
        var result = new CatalogLoadResult<TweakDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        result.Issues.AddRange(source.SourceIssues);

        foreach (var file in source.ReadFolder("Tweaks"))
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(file.ReadText(), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                result.Issues.Add(new CatalogIssue(file.RelativePath, null, "File skipped, it is not valid JSON: " + ex.Message));
                continue;
            }

            using (doc)
            {
                IEnumerable<JsonElement> elements = doc.RootElement.ValueKind switch
                {
                    JsonValueKind.Array => doc.RootElement.EnumerateArray(),
                    JsonValueKind.Object => new[] { doc.RootElement },
                    _ => Array.Empty<JsonElement>()
                };

                int index = 0;
                foreach (var element in elements)
                {
                    string label = TryGetId(element) ?? $"#{index}";
                    index++;

                    TweakDefinition? tweak;
                    try
                    {
                        tweak = element.Deserialize<TweakDefinition>(Options);
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or FormatException)
                    {
                        result.Issues.Add(new CatalogIssue(file.RelativePath, label, "Tweak skipped, it could not be read: " + ex.Message));
                        continue;
                    }

                    if (tweak == null || string.IsNullOrWhiteSpace(tweak.Id))
                    {
                        result.Issues.Add(new CatalogIssue(file.RelativePath, label, "Tweak skipped, it has no id."));
                        continue;
                    }

                    if (!seen.Add(tweak.Id))
                    {
                        result.Issues.Add(new CatalogIssue(file.RelativePath, tweak.Id, "Tweak skipped, another tweak already uses this id."));
                        continue;
                    }

                    var problems = TweakValidators.SelectMany(v => v(tweak)).ToList();
                    if (problems.Count > 0)
                    {
                        seen.Remove(tweak.Id);
                        result.Issues.Add(new CatalogIssue(file.RelativePath, tweak.Id, "Tweak skipped: " + string.Join(" ", problems)));
                        continue;
                    }

                    result.Items.Add(tweak);
                }
            }
        }

        return result;
    }

    public static CatalogLoadResult<PresetDefinition> LoadPresets(CatalogSource source)
    {
        var result = new CatalogLoadResult<PresetDefinition>();
        foreach (var file in source.ReadFolder("Presets"))
        {
            try
            {
                var preset = JsonSerializer.Deserialize<PresetDefinition>(file.ReadText(), Options);
                if (preset == null || string.IsNullOrWhiteSpace(preset.Id))
                {
                    result.Issues.Add(new CatalogIssue(file.RelativePath, null, "Preset skipped, it has no id."));
                    continue;
                }
                result.Items.Add(preset);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                result.Issues.Add(new CatalogIssue(file.RelativePath, null, "Preset skipped, it is not valid JSON: " + ex.Message));
            }
        }
        return result;
    }

    /// <summary>Parses a single JSON file such as "Apps/applications.json". Returns null (and adds an issue) when it is missing or broken.</summary>
    public static JsonDocument? LoadDocument(CatalogSource source, string relativePath, List<CatalogIssue>? issues = null)
    {
        string? text;
        try
        {
            text = source.ReadText(relativePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues?.Add(new CatalogIssue(relativePath, null, "Could not read the file: " + ex.Message));
            return null;
        }

        if (text == null)
        {
            issues?.Add(new CatalogIssue(relativePath, null, "File not found in the catalog."));
            return null;
        }

        try
        {
            return JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            issues?.Add(new CatalogIssue(relativePath, null, "File skipped, it is not valid JSON: " + ex.Message));
            return null;
        }
    }

    private static string? TryGetId(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.String)
        {
            return id.GetString();
        }
        return null;
    }
}
