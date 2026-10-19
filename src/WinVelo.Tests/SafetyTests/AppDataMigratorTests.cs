using System;
using System.IO;
using WinVelo.Core.Safety;
using Xunit;

namespace WinVelo.Tests.SafetyTests;

public class AppDataMigratorTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _legacyDir;
    private readonly string _newDir;

    public AppDataMigratorTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "WinVelo_MigratorTests_" + Guid.NewGuid().ToString("N"));
        _legacyDir = Path.Combine(_testRoot, "Tweakora");
        _newDir = Path.Combine(_testRoot, "WinVelo");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void Migrate_FreshInstall_WhenLegacyFolderDoesNotExist_ReturnsFalseAndCreatesNoMarker()
    {
        // Arrange: Legacy folder does not exist
        Assert.False(Directory.Exists(_legacyDir));

        // Act
        bool result = AppDataMigrator.Migrate(_legacyDir, _newDir);

        // Assert
        Assert.False(result);
        Assert.False(File.Exists(Path.Combine(_newDir, AppDataMigrator.MarkerFileName)));
    }

    [Fact]
    public void Migrate_MigrationNeeded_CopiesFilesAndCreatesMarker()
    {
        // Arrange: Create legacy directory with files and subdirectories
        Directory.CreateDirectory(_legacyDir);
        string journalSubDir = Path.Combine(_legacyDir, "Journal");
        Directory.CreateDirectory(journalSubDir);

        string sampleFile = Path.Combine(journalSubDir, "sample-journal.json");
        File.WriteAllText(sampleFile, "{\"test\": \"data\"}");

        string rootFile = Path.Combine(_legacyDir, "config.json");
        File.WriteAllText(rootFile, "{\"version\": 1}");

        // Act
        bool result = AppDataMigrator.Migrate(_legacyDir, _newDir);

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(_newDir));

        // Marker created
        string markerPath = Path.Combine(_newDir, AppDataMigrator.MarkerFileName);
        Assert.True(File.Exists(markerPath));

        // Files copied
        string destJournal = Path.Combine(_newDir, "Journal", "sample-journal.json");
        Assert.True(File.Exists(destJournal));
        Assert.Equal("{\"test\": \"data\"}", File.ReadAllText(destJournal));

        string destConfig = Path.Combine(_newDir, "config.json");
        Assert.True(File.Exists(destConfig));
        Assert.Equal("{\"version\": 1}", File.ReadAllText(destConfig));

        // Safety: legacy files preserved, not deleted
        Assert.True(File.Exists(sampleFile));
        Assert.True(File.Exists(rootFile));
    }

    [Fact]
    public void Migrate_AlreadyMigrated_WhenMarkerExists_DoesNothingAndReturnsFalse()
    {
        // Arrange: New directory exists and marker exists
        Directory.CreateDirectory(_newDir);
        string markerPath = Path.Combine(_newDir, AppDataMigrator.MarkerFileName);
        File.WriteAllText(markerPath, "Migrated previously");

        // Legacy directory has files
        Directory.CreateDirectory(_legacyDir);
        string fileInLegacy = Path.Combine(_legacyDir, "new_after_marker.txt");
        File.WriteAllText(fileInLegacy, "new content");

        // Act
        bool result = AppDataMigrator.Migrate(_legacyDir, _newDir);

        // Assert
        Assert.False(result);
        Assert.False(File.Exists(Path.Combine(_newDir, "new_after_marker.txt")));
    }

    [Fact]
    public void Migrate_ErrorHandling_WhenFileLocked_DoesNotCrashAndContinues()
    {
        // Arrange: Create legacy directory with 2 files
        Directory.CreateDirectory(_legacyDir);
        string lockedFile = Path.Combine(_legacyDir, "locked.txt");
        string normalFile = Path.Combine(_legacyDir, "normal.txt");

        File.WriteAllText(lockedFile, "locked content");
        File.WriteAllText(normalFile, "normal content");

        bool loggedWarning = false;

        // Open lockedFile exclusively with FileShare.None so File.Copy fails
        using (FileStream fs = new(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // Act: Migrate with locked file present
            bool result = false;
            var exception = Record.Exception(() =>
            {
                result = AppDataMigrator.Migrate(
                    _legacyDir,
                    _newDir,
                    logCallback: (msg, ex) =>
                    {
                        if (ex != null) loggedWarning = true;
                    });
            });

            // Assert: Did not crash
            Assert.Null(exception);
            Assert.True(result);
            Assert.True(loggedWarning);

            // Normal file still copied successfully
            Assert.True(File.Exists(Path.Combine(_newDir, "normal.txt")));
            Assert.Equal("normal content", File.ReadAllText(Path.Combine(_newDir, "normal.txt")));

            // Marker file created
            Assert.True(File.Exists(Path.Combine(_newDir, AppDataMigrator.MarkerFileName)));
        }
    }
}
