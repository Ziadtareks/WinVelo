using System.Text.Json;
using System.Text.RegularExpressions;
using WinVelo.Core.Catalog;
using WinVelo.Core.Models;

namespace WinVelo.Core;

/// <summary>
/// Optional display translations for catalog text (tweak names, descriptions, preset names and
/// exclusion reasons), read from Catalog/Localization/&lt;language&gt;.json.
/// Only display text is replaced: ids, risk levels, actions and detection rules are never touched.
/// The default language "en" leaves the catalog unchanged.
/// </summary>
public static class CatalogTranslations
{
    private static readonly object _lock = new();
    private static TranslationFile? _cached;
    private static string? _cachedPath;
    private static CatalogSource? _cachedSource;

    /// <summary>Language code set by the app at startup ("en" or "ar").</summary>
    public static string Language { get; set; } = "en";

    public static void Apply(IList<TweakDefinition> tweaks, CatalogSource catalog)
    {
        var file = Load(catalog);
        if (file?.Tweaks == null) return;

        foreach (var t in tweaks)
        {
            if (!file.Tweaks.TryGetValue(t.Id, out var tr) || tr == null) continue;
            if (!string.IsNullOrWhiteSpace(tr.Name)) t.Name = tr.Name;
            if (!string.IsNullOrWhiteSpace(tr.Description)) t.Description = tr.Description;
            if (!string.IsNullOrWhiteSpace(tr.WhatItDoes)) t.WhatItDoes = tr.WhatItDoes;
            if (!string.IsNullOrWhiteSpace(tr.SideEffects)) t.SideEffects = tr.SideEffects;
            if (!string.IsNullOrWhiteSpace(tr.EffectNote)) t.EffectNote = tr.EffectNote;
            if (!string.IsNullOrWhiteSpace(tr.Warnings)) t.Warnings = tr.Warnings;
            if (!string.IsNullOrWhiteSpace(tr.IrreversibleReason)) t.IrreversibleReason = tr.IrreversibleReason;
        }
    }

    public static void Apply(IList<PresetDefinition> presets, CatalogSource catalog)
    {
        var file = Load(catalog);
        if (file == null) return;

        foreach (var p in presets)
        {
            if (file.Presets != null && file.Presets.TryGetValue(p.Id, out var tr) && tr != null)
            {
                if (!string.IsNullOrWhiteSpace(tr.Name)) p.Name = tr.Name;
                if (!string.IsNullOrWhiteSpace(tr.Description)) p.Description = tr.Description;
            }

            foreach (var ex in p.Excluded)
            {
                ex.Reason = TranslateReason(file, ex.Reason);
            }
        }
    }

    private static string TranslateReason(TranslationFile file, string reason)
    {
        if (file.Reasons == null || string.IsNullOrEmpty(reason)) return reason;
        if (file.Reasons.TryGetValue(reason, out var direct) && !string.IsNullOrWhiteSpace(direct)) return direct;

        // Patterns such as "Conflicts with {0} (changes the same setting differently)".
        foreach (var kv in file.Reasons)
        {
            if (!kv.Key.Contains("{0}", StringComparison.Ordinal)) continue;
            string pattern = "^" + Regex.Escape(kv.Key).Replace("\\{0}", "(.+?)", StringComparison.Ordinal) + "$";
            var m = Regex.Match(reason, pattern);
            if (m.Success) return kv.Value.Replace("{0}", m.Groups[1].Value, StringComparison.Ordinal);
        }
        return reason;
    }

    private static TranslationFile? Load(CatalogSource catalog)
    {
        string lang = (Language ?? "en").Trim().ToLowerInvariant();
        if (lang == "en") return null;

        string relativePath = "Localization/" + lang + ".json";
        lock (_lock)
        {
            if (ReferenceEquals(_cachedSource, catalog) && _cachedPath == relativePath) return _cached;
            _cachedSource = catalog;
            _cachedPath = relativePath;
            _cached = null;
            try
            {
                string? text = catalog.ReadText(relativePath);
                if (text != null)
                {
                    _cached = JsonSerializer.Deserialize<TranslationFile>(text,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // A broken translation file only means the catalog stays in English.
                _cached = null;
            }
            return _cached;
        }
    }

    public sealed class TranslationFile
    {
        public Dictionary<string, TweakText>? Tweaks { get; set; }
        public Dictionary<string, PresetText>? Presets { get; set; }
        public Dictionary<string, string>? Reasons { get; set; }
    }

    public sealed class TweakText
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? WhatItDoes { get; set; }
        public string? SideEffects { get; set; }
        public string? EffectNote { get; set; }
        public string? Warnings { get; set; }
        public string? IrreversibleReason { get; set; }
    }

    public sealed class PresetText
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
    }
}
