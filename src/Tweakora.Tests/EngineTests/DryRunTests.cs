using System.Text.Json;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Handlers;
using Tweakora.Core.Fakes;
using Tweakora.Core.Models;
using Xunit;

namespace Tweakora.Tests.EngineTests;

public sealed class DryRunTests
{
    [Fact]
    public void GeneratePreview_DoesNotModifySystemAndShowsDifferences()
    {
        // Arrange
        FakeRegistryService registry = new();
        registry.SetValue("HKLM", "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection", "AllowTelemetry", 3, "DWord");

        FakeServicesService services = new();
        FakeTaskSchedulerService tasks = new();
        FakeShellService shell = new();
        RollbackJournalManager journal = new(Path.GetTempPath());

        Core.Engine.ExecutionContext context = new()
        {
            Registry = registry,
            Services = services,
            Tasks = tasks,
            Shell = shell,
            Journal = journal,
            IsDryRun = true
        };

        List<IActionHandler> handlers = new()
        {
            new RegistryActionHandler(),
            new ServiceActionHandler(),
            new TaskActionHandler(),
            new FileActionHandler()
        };

        DryRunEngine engine = new(handlers, context);

        TweakDefinition tweak = new()
        {
            Id = "privacy.telemetry.preview",
            Name = "Telemetry",
            Description = "Reduce data",
            Category = "privacy",
            Risk = "Safe",
            Impacts = new List<string> { "privacy" },
            Apply = new List<ActionItem>
            {
                new()
                {
                    Type = "Registry",
                    Hive = "HKLM",
                    Key = "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection",
                    ValueName = "AllowTelemetry",
                    ValueKind = "DWord",
                    Data = JsonDocument.Parse("0").RootElement,
                    Operation = "Set"
                }
            },
            Undo = new List<ActionItem>(),
            Source = JsonDocument.Parse("\"https://microsoft.com\"").RootElement,
            VerifiedOnBuild = "22631"
        };

        // Act
        List<TweakPreview> previews = engine.GeneratePreview(new[] { tweak });

        // Assert
        Assert.Single(previews);
        TweakPreview p = previews[0];
        Assert.Equal("privacy.telemetry.preview", p.TweakId);
        Assert.Single(p.Actions);
        Assert.Equal("3", p.Actions[0].CurrentState);
        Assert.Equal("0", p.Actions[0].ProposedState);

        // Verify fake registry was NOT modified by dry-run
        Assert.Equal(3, registry.GetValue("HKLM", "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection", "AllowTelemetry"));
    }
}
