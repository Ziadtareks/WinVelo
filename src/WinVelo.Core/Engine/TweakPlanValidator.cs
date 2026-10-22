using WinVelo.Core.Models;

namespace WinVelo.Core.Engine;

public static class TweakPlanValidator
{
    public static IReadOnlyList<string> FindConflicts(IEnumerable<TweakDefinition> tweaks)
    {
        var plan = tweaks.ToList();
        var selected = new HashSet<string>(plan.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        var errors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var duplicate in plan.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) errors.Add("Duplicate tweak: " + duplicate.Key);
        foreach (var tweak in plan)
        {
            foreach (var other in (tweak.OppositeOf ?? []).Concat(tweak.ConflictsWith ?? []))
                if (selected.Contains(other)) errors.Add($"Conflicting selection: {tweak.Id} and {other}. Select one setting.");
        }
        return errors.ToList();
    }
    public static (List<TweakDefinition> Ordered, List<string> Errors) OrderDependencies(IEnumerable<TweakDefinition> tweaks)
    {
        var items = tweaks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var ordered = new List<TweakDefinition>(); var errors = new List<string>(); var visited = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Visit(TweakDefinition tweak)
        {
            if (visited.TryGetValue(tweak.Id, out int state)) { if (state == 1) errors.Add("Dependency cycle at " + tweak.Id); return; }
            visited[tweak.Id] = 1;
            foreach (string dependency in tweak.DependsOn ?? []) if (items.TryGetValue(dependency, out var target)) Visit(target);
            visited[tweak.Id] = 2; ordered.Add(tweak);
        }
        foreach (var item in items.Values) Visit(item);
        return (ordered, errors);
    }

}
