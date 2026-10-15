using System;
using System.IO;

namespace Tweakora.Core.Safety;

/// <summary>
/// Centralized resolver for storage directories across real host execution and isolated test/simulation runs.
/// Guarantees that test mode and UI/integration tests never touch %APPDATA%\WinVelo.
/// </summary>
public static class WinVeloStoragePaths
{
    public const string TestModeFolderName = "WinVelo-TestMode";

    public static string GetStorageRoot(bool isTestMode)
    {
        if (isTestMode)
        {
            return Path.Combine(Path.GetTempPath(), TestModeFolderName);
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinVelo");
    }

    public static string GetJournalDirectory(bool isTestMode)
    {
        return Path.Combine(GetStorageRoot(isTestMode), "Journal");
    }

    public static string GetSettingsPath(bool isTestMode)
    {
        if (isTestMode)
        {
            return Path.Combine(GetStorageRoot(true), "Settings", "settings.json");
        }

        return Path.Combine(GetStorageRoot(false), "settings.json");
    }

    public static string GetLogsDirectory(bool isTestMode)
    {
        if (isTestMode)
        {
            return Path.Combine(GetStorageRoot(true), "Logs");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinVelo", "Logs");
    }

    public static void InitializeStorage(bool isTestMode)
    {
        if (isTestMode)
        {
            string testRoot = GetStorageRoot(true);
            try
            {
                if (Directory.Exists(testRoot))
                {
                    Directory.Delete(testRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup for locked files in test runs
            }

            Directory.CreateDirectory(testRoot);
            Directory.CreateDirectory(GetJournalDirectory(true));
            Directory.CreateDirectory(Path.Combine(testRoot, "Settings"));
            Directory.CreateDirectory(GetLogsDirectory(true));
        }
        else
        {
            Directory.CreateDirectory(GetStorageRoot(false));
            Directory.CreateDirectory(GetJournalDirectory(false));
            Directory.CreateDirectory(GetLogsDirectory(false));
        }
    }
}
