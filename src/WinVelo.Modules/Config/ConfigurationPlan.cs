using WinVelo.Core.Engine;
using WinVelo.Core.Models;
using WinVelo.Core.Profiles;
using WinVelo.Modules.Apps;
using WinVelo.Modules.Network;
using WinVelo.Modules.Updates;

namespace WinVelo.Modules.Config;

/// <summary>Resolves every section before execution so an invalid profile cannot be partially applied.</summary>
public sealed class ConfigurationPlan
{
    public List<TweakDefinition> ApplyTweaks { get; } = new();
    public List<TweakDefinition> UndoTweaks { get; } = new();
    public List<ApplicationItem> Apps { get; } = new();
    public List<FeatureDefinition> Features { get; } = new();
    public DnsProviderDefinition? Dns { get; private set; }
    public UpdateProfileMode? Updates { get; private set; }
    public List<string> Errors { get; } = new();

    public static ConfigurationPlan Resolve(WinVeloConfig config, IReadOnlyList<TweakDefinition> tweaks,
        IReadOnlyList<ApplicationItem> apps, IReadOnlyList<FeatureDefinition> features, IReadOnlyList<DnsProviderDefinition> providers)
    {
        var plan = new ConfigurationPlan();
        if (config.SchemaVersion != "1.0.0") plan.Errors.Add("Unsupported configuration schema: " + config.SchemaVersion);
        TweakDefinition? Find(string key) => tweaks.FirstOrDefault(t => t.Id.Equals(key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.Origin, key, StringComparison.OrdinalIgnoreCase) || (t.Aliases?.Contains(key, StringComparer.OrdinalIgnoreCase) ?? false));
        foreach (string key in config.SelectedTweaks ?? new())
        {
            var tweak = Find(key);
            if (tweak == null) plan.Errors.Add("Unknown tweak: " + key); else if (!plan.ApplyTweaks.Contains(tweak)) plan.ApplyTweaks.Add(tweak);
        }
        foreach (var toggle in config.StagedToggles ?? new())
        {
            var tweak = Find(toggle.Key);
            if (tweak == null) { plan.Errors.Add("Unknown toggle: " + toggle.Key); continue; }
            if (!toggle.Value && plan.ApplyTweaks.Contains(tweak)) plan.Errors.Add("Tweak selected for both apply and undo: " + tweak.Id);
            var list = toggle.Value ? plan.ApplyTweaks : plan.UndoTweaks;
            if (!list.Contains(tweak)) list.Add(tweak);
        }
        foreach (string key in config.SelectedApps ?? new())
        {
            var app = apps.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase) || a.WingetId.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (app == null) plan.Errors.Add("Unknown app: " + key); else if (!plan.Apps.Contains(app)) plan.Apps.Add(app);
        }
        foreach (string key in config.SelectedFeatures ?? new())
        {
            var feature = features.FirstOrDefault(f => f.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (feature == null) plan.Errors.Add("Unknown feature: " + key); else if (!plan.Features.Contains(feature)) plan.Features.Add(feature);
        }
        if (plan.Features.Any(f => f.Key == "WPFFeatureEnableLegacyRecovery") && plan.Features.Any(f => f.Key == "WPFFeatureDisableLegacyRecovery"))
            plan.Errors.Add("Select only one Legacy Recovery policy.");
        if (!string.IsNullOrWhiteSpace(config.SelectedDns))
        {
            plan.Dns = providers.FirstOrDefault(p => p.Id.Equals(config.SelectedDns, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(config.SelectedDns, StringComparison.OrdinalIgnoreCase));
            if (plan.Dns == null) plan.Errors.Add("Unknown DNS provider: " + config.SelectedDns);
            if (config.DnsAdapterIndex is <= 0) plan.Errors.Add("DNS adapter index must be positive.");
            if (config.UseDnsOverHttps && config.DnsAdapterIndex == null) plan.Errors.Add("DoH requires an explicit DNS adapter index.");
            if (config.UseDnsOverHttps && string.IsNullOrEmpty(plan.Dns?.DohTemplate)) plan.Errors.Add("Selected DNS provider does not offer a DoH template.");
        }
        else if (config.UseDnsOverHttps || config.DnsAdapterIndex != null) plan.Errors.Add("DNS options require a selected DNS provider.");
        if (!string.IsNullOrWhiteSpace(config.UpdatesMode))
        {
            if (Enum.TryParse<UpdateProfileMode>(config.UpdatesMode, true, out var mode) && Enum.IsDefined(mode)) plan.Updates = mode;
            else plan.Errors.Add("Unknown update mode: " + config.UpdatesMode);
        }
        plan.Errors.AddRange(TweakPlanValidator.FindConflicts(plan.ApplyTweaks));
        return plan;
    }
}
