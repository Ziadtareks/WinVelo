using System.Reflection;

namespace WinVelo.Core.Catalog;

/// <summary>Where a catalog file was read from.</summary>
public enum CatalogOrigin
{
    Embedded,
    ExternalFolder
}

/// <summary>A problem found while reading the catalog. The app keeps running and skips only the broken part.</summary>
public sealed record CatalogIssue(string File, string? ItemId, string Message)
{
    public override string ToString() => ItemId == null ? $"{File}: {Message}" : $"{File} [{ItemId}]: {Message}";
}

/// <summary>
/// Reads catalog files (tweaks, presets, apps, config, translations).
/// The catalog is embedded in WinVelo.Core, so the app works even when no Catalog folder exists.
/// Advanced users can put a <c>Catalog</c> folder next to the exe: any file found there replaces the
/// built-in file with the same relative path (for example <c>Tweaks/extras.json</c>).
/// </summary>
public sealed class CatalogSource
{
    public const string FolderName = "Catalog";
    private const string ResourcePrefix = "Catalog/";

    private static readonly object _defaultLock = new();
    private static CatalogSource? _default;

    private readonly Assembly? _embeddedAssembly;
    private readonly Dictionary<string, string> _embeddedNames;
    private readonly List<CatalogIssue> _sourceIssues = new();

    /// <summary>Full path of the external Catalog folder in use, or null when only the built-in catalog is used.</summary>
    public string? ExternalPath { get; }

    /// <summary>Problems with the external folder itself (for example an empty Tweaks folder).</summary>
    public IReadOnlyList<CatalogIssue> SourceIssues => _sourceIssues;

    public bool UsesExternalFolder => ExternalPath != null;

    private CatalogSource(string? externalPath, Assembly? embeddedAssembly)
    {
        ExternalPath = externalPath;
        _embeddedAssembly = embeddedAssembly;
        _embeddedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (embeddedAssembly != null)
        {
            foreach (string name in embeddedAssembly.GetManifestResourceNames())
            {
                string normalized = Normalize(name);
                if (normalized.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    _embeddedNames[normalized.Substring(ResourcePrefix.Length)] = name;
                }
            }
        }
    }

    /// <summary>The catalog used by the app and the CLI: the folder next to the exe (if any) over the built-in files.</summary>
    public static CatalogSource Default
    {
        get
        {
            lock (_defaultLock)
            {
                return _default ??= Create(AppContext.BaseDirectory);
            }
        }
    }

    /// <summary>Replaces <see cref="Default"/>. Used by tests; pass null to go back to the normal lookup.</summary>
    public static void SetDefault(CatalogSource? source)
    {
        lock (_defaultLock)
        {
            _default = source;
        }
    }

    /// <summary>Built-in files plus an optional <c>Catalog</c> folder inside <paramref name="baseDirectory"/>.</summary>
    public static CatalogSource Create(string? baseDirectory)
    {
        string? external = null;
        var issues = new List<CatalogIssue>();
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            string candidate = Path.Combine(baseDirectory, FolderName);
            if (Directory.Exists(candidate))
            {
                external = Path.GetFullPath(candidate);
                string tweaks = Path.Combine(external, "Tweaks");
                if (Directory.Exists(tweaks) && !SafeHasJson(tweaks))
                {
                    issues.Add(new CatalogIssue("Catalog/Tweaks", null,
                        "The Tweaks folder next to the exe has no .json files. The built-in tweaks are used instead."));
                }
            }
        }

        var source = new CatalogSource(external, typeof(CatalogSource).Assembly);
        source._sourceIssues.AddRange(issues);
        return source;
    }

    /// <summary>Only the files of one folder, without the built-in catalog. Used by tests and tools.</summary>
    public static CatalogSource FromFolderOnly(string catalogFolder)
    {
        return new CatalogSource(Path.GetFullPath(catalogFolder), embeddedAssembly: null);
    }

    /// <summary>Only the built-in catalog.</summary>
    public static CatalogSource EmbeddedOnly() => new(null, typeof(CatalogSource).Assembly);

    /// <summary>Reads one file, for example "Apps/applications.json". Returns null when it exists nowhere.</summary>
    public string? ReadText(string relativePath)
    {
        string rel = Normalize(relativePath);
        if (!IsSafeRelativePath(rel)) return null;

        string? external = ExternalFilePath(rel);
        if (external != null && File.Exists(external))
        {
            return File.ReadAllText(external);
        }

        return ReadEmbedded(rel);
    }

    public bool Exists(string relativePath)
    {
        string rel = Normalize(relativePath);
        if (!IsSafeRelativePath(rel)) return false;
        string? external = ExternalFilePath(rel);
        return (external != null && File.Exists(external)) || _embeddedNames.ContainsKey(rel);
    }

    /// <summary>
    /// All .json files in a folder such as "Tweaks", sorted by path. A file in the external folder replaces the
    /// built-in file with the same relative path; built-in files without a replacement are still returned.
    /// </summary>
    public IReadOnlyList<CatalogFile> ReadFolder(string relativeFolder)
    {
        string folder = Normalize(relativeFolder).TrimEnd('/');
        var files = new SortedDictionary<string, CatalogFile>(StringComparer.OrdinalIgnoreCase);
        if (!IsSafeRelativePath(folder)) return Array.Empty<CatalogFile>();

        string prefix = folder + "/";
        foreach (var kv in _embeddedNames)
        {
            if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && kv.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                files[kv.Key] = new CatalogFile(kv.Key, CatalogOrigin.Embedded, () => ReadEmbedded(kv.Key) ?? string.Empty);
            }
        }

        if (ExternalPath != null)
        {
            string dir = Path.Combine(ExternalPath, folder.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(dir))
            {
                string[] externalFiles;
                try
                {
                    externalFiles = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _sourceIssues.Add(new CatalogIssue(folder, null, "Could not read the folder: " + ex.Message));
                    externalFiles = Array.Empty<string>();
                }

                foreach (string path in externalFiles)
                {
                    string rel = Normalize(Path.GetRelativePath(ExternalPath, path));
                    string fullPath = path;
                    files[rel] = new CatalogFile(rel, CatalogOrigin.ExternalFolder, () => File.ReadAllText(fullPath));
                }
            }
        }

        return files.Values.ToList();
    }

    private string? ExternalFilePath(string rel)
    {
        if (ExternalPath == null) return null;
        string full = Path.GetFullPath(Path.Combine(ExternalPath, rel.Replace('/', Path.DirectorySeparatorChar)));
        string root = ExternalPath.EndsWith(Path.DirectorySeparatorChar) ? ExternalPath : ExternalPath + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private string? ReadEmbedded(string rel)
    {
        if (_embeddedAssembly == null || !_embeddedNames.TryGetValue(rel, out string? name)) return null;
        using Stream? stream = _embeddedAssembly.GetManifestResourceStream(name);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    /// <summary>Rejects absolute paths and ".." segments so a relative catalog path can never leave the catalog folder.</summary>
    internal static bool IsSafeRelativePath(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel) || Path.IsPathRooted(rel) || rel.Contains(':')) return false;
        foreach (string part in rel.Split('/'))
        {
            if (part == "..") return false;
        }
        return true;
    }

    private static bool SafeHasJson(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>One catalog file. <see cref="ReadText"/> reads it on demand.</summary>
public sealed class CatalogFile
{
    private readonly Func<string> _read;

    public CatalogFile(string relativePath, CatalogOrigin origin, Func<string> read)
    {
        RelativePath = relativePath;
        Origin = origin;
        _read = read;
    }

    public string RelativePath { get; }
    public CatalogOrigin Origin { get; }
    public string ReadText() => _read();
}
