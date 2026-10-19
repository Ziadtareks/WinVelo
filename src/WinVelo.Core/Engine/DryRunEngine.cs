using WinVelo.Core.Models;

namespace WinVelo.Core.Engine;

public sealed class TweakPreview
{
    public required string TweakId { get; init; }
    public required string Name { get; init; }
    public required string Risk { get; init; }
    public List<ActionPreview> Actions { get; init; } = new();
}

public sealed class DryRunEngine
{
    private readonly IEnumerable<IActionHandler> _handlers;
    private readonly ExecutionContext _context;

    public DryRunEngine(IEnumerable<IActionHandler> handlers, ExecutionContext context)
    {
        _handlers = handlers;
        _context = context;
    }

    public List<TweakPreview> GeneratePreview(IEnumerable<TweakDefinition> tweaks)
    {
        List<TweakPreview> previews = new();

        foreach (TweakDefinition tweak in tweaks)
        {
            TweakPreview preview = new()
            {
                TweakId = tweak.Id,
                Name = tweak.Name,
                Risk = tweak.Risk
            };

            foreach (ActionItem action in tweak.Apply)
            {
                IActionHandler? handler = _handlers.FirstOrDefault(h => h.CanHandle(action.Type));
                if (handler != null)
                {
                    preview.Actions.Add(handler.Preview(action, _context));
                }
            }

            previews.Add(preview);
        }

        return previews;
    }
}
