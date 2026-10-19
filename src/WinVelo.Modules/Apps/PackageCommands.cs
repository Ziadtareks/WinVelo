using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WinVelo.Core.Models;
using WinVelo.Core.Operations;
using WinVelo.Core.Utilities;

namespace WinVelo.Modules.Apps;

public enum PackageAction
{
    Install,
    Uninstall,
    Upgrade
}

public sealed record PackageOutcome(OperationItemStatus Status, string Message);

/// <summary>
/// Builds winget/choco argument lists and turns their exit codes into a clear result.
/// Arguments are always a list (passed with ProcessStartInfo.ArgumentList), never one command string.
/// </summary>
public static class PackageCommands
{
    // winget return codes (https://github.com/microsoft/winget-cli/blob/master/doc/windows/package-manager/winget/returnCodes.md).
    public const int WingetNoPackageFound = unchecked((int)0x8A150014);       // APPINSTALLER_CLI_ERROR_NO_APPLICATIONS_FOUND
    public const int WingetUpdateNotApplicable = unchecked((int)0x8A15002B);  // APPINSTALLER_CLI_ERROR_UPDATE_NOT_APPLICABLE
    public const int WingetAlreadyInstalled = unchecked((int)0x8A150061);     // APPINSTALLER_CLI_ERROR_PACKAGE_ALREADY_INSTALLED

    // Chocolatey passes through installer exit codes; these two mean "done, restart needed".
    private const int RebootInitiated = 1641;
    private const int RebootRequired = 3010;

    public static bool IsChocolatey(string packageManager) =>
        packageManager.Equals("Chocolatey", StringComparison.OrdinalIgnoreCase);

    public static string Executable(string packageManager) => IsChocolatey(packageManager) ? "choco.exe" : "winget.exe";

    /// <exception cref="ArgumentException">The id is not a valid package id for this package manager.</exception>
    public static IReadOnlyList<string> Arguments(string packageManager, PackageAction action, string packageId)
    {
        if (IsChocolatey(packageManager))
        {
            if (!PackageIds.IsValidChocolateyId(packageId))
            {
                throw new ArgumentException($"'{packageId}' is not a valid Chocolatey package id.", nameof(packageId));
            }

            return action switch
            {
                PackageAction.Install => new[] { "install", packageId, "-y", "--no-progress" },
                PackageAction.Uninstall => new[] { "uninstall", packageId, "-y" },
                _ => new[] { "upgrade", packageId, "-y", "--no-progress" }
            };
        }

        if (!PackageIds.IsValidWingetId(packageId))
        {
            throw new ArgumentException($"'{packageId}' is not a valid winget package id.", nameof(packageId));
        }

        return action switch
        {
            PackageAction.Install => new[]
            {
                "install", "--id", packageId, "--exact", "--source", PackageIds.WingetSource(packageId),
                "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"
            },
            PackageAction.Uninstall => new[]
            {
                "uninstall", "--id", packageId, "--exact", "--silent", "--accept-source-agreements", "--disable-interactivity"
            },
            _ => new[]
            {
                "upgrade", "--id", packageId, "--exact", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"
            }
        };
    }

    public static IReadOnlyList<string> UpgradeAllArguments(string packageManager) => IsChocolatey(packageManager)
        ? new[] { "upgrade", "all", "-y", "--no-progress" }
        : new[] { "upgrade", "--all", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" };

    public static PackageOutcome Interpret(string packageManager, PackageAction action, PowerShellResult result)
    {
        string tool = IsChocolatey(packageManager) ? "Chocolatey" : "winget";

        if (result.Cancelled)
        {
            return new(OperationItemStatus.Skipped, "Cancelled.");
        }

        if (result.FailedToStart)
        {
            return new(OperationItemStatus.Failed, IsChocolatey(packageManager)
                ? "Chocolatey (choco.exe) is not installed. Install it from https://chocolatey.org or choose winget."
                : "winget is not installed. Install or update \"App Installer\" from the Microsoft Store, then try again.");
        }

        if (result.TimedOut)
        {
            return new(OperationItemStatus.Failed, $"{tool} did not finish in time and was stopped. Check your internet connection and try again.");
        }

        int code = result.ExitCode;
        if (code == 0)
        {
            return new(OperationItemStatus.Succeeded, action switch
            {
                PackageAction.Install => "Installed successfully.",
                PackageAction.Uninstall => "Uninstalled successfully.",
                _ => "Upgrade completed."
            });
        }

        if (IsChocolatey(packageManager))
        {
            if (code == RebootRequired || code == RebootInitiated)
            {
                return new(OperationItemStatus.Succeeded, "Done. Restart Windows to finish.");
            }
        }
        else
        {
            if (code == WingetAlreadyInstalled && action == PackageAction.Install)
            {
                return new(OperationItemStatus.Succeeded, "Already installed.");
            }
            if (code == WingetUpdateNotApplicable && action == PackageAction.Upgrade)
            {
                return new(OperationItemStatus.Succeeded, "Already up to date.");
            }
            if (code == WingetNoPackageFound)
            {
                return new(OperationItemStatus.Failed, action == PackageAction.Uninstall
                    ? "winget could not find this app on your PC."
                    : "winget could not find this package. The package id may have changed.");
            }
        }

        string detail = LastLine(result.StdErr) ?? LastLine(result.StdOut) ?? string.Empty;
        string hex = code < 0 ? $" (0x{code:X8})" : string.Empty;
        return new(OperationItemStatus.Failed,
            $"{tool} failed with exit code {code}{hex}. {detail}".Trim() +
            " If you are offline, connect to the internet and try again.");
    }

    /// <summary>
    /// True when <paramref name="wingetListOutput"/> (the output of "winget list") shows the app:
    /// the id as a whole word, or a row that starts with the app name. A plain substring search would
    /// wrongly report "Git" as installed when only "GitHub Desktop" is.
    /// </summary>
    public static bool ListContains(string wingetListOutput, string? wingetId, string? name)
    {
        if (string.IsNullOrEmpty(wingetListOutput)) return false;

        if (!string.IsNullOrWhiteSpace(wingetId) &&
            Regex.IsMatch(wingetListOutput, @"(?<![\w.+-])" + Regex.Escape(wingetId) + @"(?![\w.+-])", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(name) &&
            Regex.IsMatch(wingetListOutput, @"(?m)^\s*" + Regex.Escape(name) + @"(\s{2,}|\s*…|\s*$)", RegexOptions.IgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string? LastLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            // winget draws progress bars with these characters; skip them.
            if (lines[i].Trim('-', '\\', '|', '/', ' ', '█', '▒').Length > 0)
            {
                return lines[i].Length > 300 ? lines[i].Substring(0, 300) : lines[i];
            }
        }
        return null;
    }
}
