using System.Text.Json.Serialization;

namespace Tweakora.Core.Models;

public sealed class WindowsRequirements
{
    [JsonPropertyName("minBuild")]
    public int MinBuild { get; set; }

    [JsonPropertyName("maxBuild")]
    public int? MaxBuild { get; set; }

    [JsonPropertyName("editions")]
    public List<string>? Editions { get; set; }

    public bool IsCompatible(int currentBuild, string currentEdition)
    {
        if (currentBuild < MinBuild) return false;
        if (MaxBuild.HasValue && currentBuild > MaxBuild.Value) return false;
        if (Editions != null && Editions.Count > 0)
        {
            return Editions.Any(e => currentEdition.Contains(e, StringComparison.OrdinalIgnoreCase));
        }
        return true;
    }
}
