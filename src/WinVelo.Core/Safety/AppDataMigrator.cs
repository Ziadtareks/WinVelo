using System;
using System.IO;

namespace WinVelo.Core.Safety;

/// <summary>
/// Migrates application state and rollback journals from the legacy "Tweakora" directory
/// to the new "WinVelo" directory in %APPDATA%.
/// </summary>
public static class AppDataMigrator
{
    public const string MarkerFileName = ".migration_complete";

    /// <summary>The product's former name. Used to find and migrate data written by older builds.</summary>
    public const string LegacyProductName = "Tweakora";

    /// <summary>
    /// Migrates user data from the legacy Tweakora AppData directory to the new WinVelo directory.
    /// This operation is idempotent and safe: it only copies files (never deletes), records a marker file
    /// so it executes only once, and handles any IO/locking errors without throwing.
    /// </summary>
    /// <param name="legacyPath">Optional custom path to legacy directory (defaults to %APPDATA%\Tweakora).</param>
    /// <param name="newPath">Optional custom path to new directory (defaults to %APPDATA%\WinVelo).</param>
    /// <param name="logCallback">Optional callback for logging info/warning messages.</param>
    /// <returns>True if files were migrated, false if skipped or already migrated.</returns>
    public static bool Migrate(string? legacyPath = null, string? newPath = null, Action<string, Exception?>? logCallback = null)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            legacyPath ??= Path.Combine(appData, LegacyProductName);
            newPath ??= Path.Combine(appData, "WinVelo");

            string markerPath = Path.Combine(newPath, MarkerFileName);

            // 1. If marker already exists, migration was already completed.
            if (File.Exists(markerPath))
            {
                logCallback?.Invoke($"AppData migration already completed previously. Marker found at {markerPath}", null);
                return false;
            }

            // 2. If legacy directory does not exist, nothing to migrate.
            if (!Directory.Exists(legacyPath))
            {
                logCallback?.Invoke($"Legacy directory not found at {legacyPath}. No migration required.", null);
                return false;
            }

            // 3. Ensure target directory exists
            if (!Directory.Exists(newPath))
            {
                Directory.CreateDirectory(newPath);
            }

            // 4. Recursively copy files and directories without deleting anything from legacy
            CopyDirectoryRecursively(legacyPath, newPath, logCallback);

            // 5. Write marker file to ensure idempotency across subsequent runs
            try
            {
                File.WriteAllText(markerPath, $"Migrated on {DateTime.UtcNow:O} from {legacyPath}");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Failed to write migration marker at {markerPath}", ex);
            }

            logCallback?.Invoke($"Successfully migrated legacy data from {legacyPath} to {newPath}", null);
            return true;
        }
        catch (Exception ex)
        {
            logCallback?.Invoke("Unexpected error during AppData migration", ex);
            return false;
        }
    }

    private static void CopyDirectoryRecursively(string sourceDir, string targetDir, Action<string, Exception?>? logCallback)
    {
        try
        {
            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    string relative = Path.GetRelativePath(sourceDir, dirPath);
                    string destSubDir = Path.Combine(targetDir, relative);
                    if (!Directory.Exists(destSubDir))
                    {
                        Directory.CreateDirectory(destSubDir);
                    }
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"Failed to create directory {dirPath}", ex);
                }
            }

            foreach (string filePath in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                try
                {
                    string relative = Path.GetRelativePath(sourceDir, filePath);
                    string destFile = Path.Combine(targetDir, relative);
                    if (!File.Exists(destFile))
                    {
                        File.Copy(filePath, destFile, overwrite: false);
                    }
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"Failed to copy file {filePath}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"Error traversing directory {sourceDir}", ex);
        }
    }
}
