using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WinVelo.App.ViewModels;
using WinVelo.Core.Engine;
using WinVelo.Core.Engine.Runners;
using WinVelo.Core.Models;
using WinVelo.Core.Operations;
using WinVelo.Modules.Advisor;
using WinVelo.Modules.Apps;
using WinVelo.Modules.Cleaner;
using WinVelo.Modules.Config;
using WinVelo.Modules.Memory;
using WinVelo.Modules.Network;
using WinVelo.Modules.Services;
using WinVelo.Modules.Startup;
using WinVelo.Modules.Tweaks;
using WinVelo.Modules.Updates;
using WinVelo.Simulation;
using Xunit;

namespace WinVelo.Tests;

public sealed class FirstRunWizardTests
{
    private (FirstRunWizardViewModel Wizard, FakeRestoreService Restore, FakeAppSettingsService Settings) CreateWizardFixture(bool completed = false)
    {
        var fakeRestore = new FakeRestoreService();
        var fakeSettings = new FakeAppSettingsService
        {
            Settings = new UserSettings
            {
                HasCompletedFirstRunWizard = completed,
                AppMode = "Simple"
            }
        };

        var wizard = new FirstRunWizardViewModel(fakeRestore, fakeSettings);
        return (wizard, fakeRestore, fakeSettings);
    }

    [Fact]
    public void FirstRunWizard_InitialState_Step1Metadata()
    {
        var (wizard, _, _) = CreateWizardFixture();

        Assert.Equal(1, wizard.CurrentStep);
        Assert.Equal("Welcome to WinVelo", wizard.StepTitle);
        Assert.Contains("does and does not do", wizard.StepDescription);
    }

    [Fact]
    public async Task FirstRunWizard_Step2_SystemRestoreDetectionAndEnabling()
    {
        var (wizard, restore, _) = CreateWizardFixture();
        restore.IsEnabled = false;

        wizard.NextStep(); // Go to Step 2
        Assert.Equal(2, wizard.CurrentStep);
        Assert.False(wizard.IsSystemRestoreEnabled);
        Assert.Contains("turned OFF", wizard.RestoreStatusMessage);

        await wizard.EnableSystemRestoreCommand.ExecuteAsync(null);

        Assert.True(wizard.IsSystemRestoreEnabled);
        Assert.True(restore.IsEnabled);
        Assert.Contains("successfully enabled", wizard.RestoreStatusMessage);
    }

    [Fact]
    public async Task FirstRunWizard_Step2_CreateAndVerifyRestorePoint()
    {
        var (wizard, restore, _) = CreateWizardFixture();
        restore.IsEnabled = true;

        wizard.NextStep(); // Step 2
        Assert.Equal(2, wizard.CurrentStep);

        await wizard.CreateAndVerifyRestorePointCommand.ExecuteAsync(null);

        Assert.True(wizard.HasVerifiedRestorePoint);
        Assert.Contains("Verified", wizard.RestoreVerificationMessage);
    }

    [Fact]
    public void FirstRunWizard_Step3_ModeSelectionAndFinishPersistence()
    {
        var (wizard, _, settings) = CreateWizardFixture();

        wizard.NextStep(); // Step 2
        wizard.NextStep(); // Step 3
        Assert.Equal(3, wizard.CurrentStep);
        Assert.True(wizard.IsSimpleMode);

        string? completedMode = null;
        wizard.WizardCompleted += mode => completedMode = mode;

        wizard.SelectAdvancedMode();
        Assert.False(wizard.IsSimpleMode);
        Assert.Equal("Advanced", wizard.SelectedMode);

        wizard.Finish();

        Assert.Equal("Advanced", completedMode);
        Assert.True(settings.GetSettings().HasCompletedFirstRunWizard);
        Assert.Equal("Advanced", settings.GetSettings().AppMode);
        Assert.False(settings.GetSettings().RecommendedOnly);
    }

    [Fact]
    public void MainViewModel_WizardFlow_FirstRunShownAndReopenable()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeSched = new FakeTaskSchedulerService();
        var fakeRestore = new FakeRestoreService();
        var fakeShell = new FakeShellService();
        var fakeSettings = new FakeAppSettingsService
        {
            Settings = new UserSettings { HasCompletedFirstRunWizard = false }
        };

        services.AddSingleton<WinVelo.Core.Interfaces.IRegistryService>(fakeReg);
        services.AddSingleton<WinVelo.Core.Interfaces.ISystemServicesService>(fakeSvc);
        services.AddSingleton<WinVelo.Core.Interfaces.ITaskSchedulerService>(fakeSched);
        services.AddSingleton<WinVelo.Core.Interfaces.ISystemRestoreService>(fakeRestore);
        services.AddSingleton<WinVelo.Core.Interfaces.IShellService>(fakeShell);
        services.AddSingleton<WinVelo.Core.Interfaces.IAppSettingsService>(fakeSettings);

        string journalDir = Path.Combine(AppContext.BaseDirectory, $"test-journal-wiz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(journalDir);
        services.AddSingleton(new RollbackJournalManager(journalDir));
        services.AddSingleton<IOperationRunner, OperationRunner>();
        services.AddSingleton<IActivityTracker, ActivityTracker>();

        services.AddSingleton<ICleanerService, CleanerService>();
        services.AddSingleton<ITweakService, TweakService>();
        services.AddSingleton<IAppInstallService, AppInstallService>();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddSingleton<IFixService, FixService>();
        services.AddSingleton<IUpdateModeService, UpdateModeService>();
        services.AddSingleton<IDnsService, DnsService>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IServicesManagerService, ServicesManagerService>();
        services.AddSingleton<IMemoryOptimizerService, MemoryOptimizerService>();
        services.AddSingleton<INetworkOptimizerService, NetworkOptimizerService>();
        services.AddSingleton<ISmartAdvisorService, SmartAdvisorService>();

        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<InstallViewModel>();
        services.AddSingleton<TweaksViewModel>();
        services.AddSingleton<ConfigViewModel>();
        services.AddSingleton<UpdatesViewModel>();
        services.AddSingleton<ToolboxViewModel>();
        services.AddSingleton<StartupViewModel>();
        services.AddSingleton<ToolsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<FirstRunWizardViewModel>();
        services.AddSingleton<MainViewModel>();

        var provider = services.BuildServiceProvider();
        var mainVm = provider.GetRequiredService<MainViewModel>();
        var wizard = provider.GetRequiredService<FirstRunWizardViewModel>();
        var tvm = provider.GetRequiredService<TweaksViewModel>();

        // First run wizard should be open initially
        Assert.True(mainVm.IsWizardOpen);

        // Completing wizard in Simple mode
        wizard.SelectSimpleMode();
        wizard.Finish();

        Assert.False(mainVm.IsWizardOpen);
        Assert.True(tvm.IsRecommendedOnly);
        Assert.False(tvm.ShowExperimentalTweaks);

        // Reopen wizard from Settings command
        mainVm.OpenFirstRunWizard();
        Assert.True(mainVm.IsWizardOpen);
        Assert.Equal(1, wizard.CurrentStep);
    }
}
