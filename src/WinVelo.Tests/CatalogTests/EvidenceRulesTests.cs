using System.Text.Json.Nodes;
using Xunit;

namespace WinVelo.Tests.CatalogTests;

/// <summary>CatalogValidator.ValidateEvidenceRules: the evidence label must match where a tweak lives and what it cites.</summary>
public sealed class EvidenceRulesTests
{
    private static int Errors(string json)
    {
        int errors = 0;
        CatalogValidator.Program.ValidateEvidenceRules(JsonNode.Parse(json)!, "test", ref errors);
        return errors;
    }

    [Fact]
    public void Anecdotal_OnlyInExperimental()
    {
        Assert.Equal(0, Errors("""{ "evidence": "anecdotal", "category": "Experimental", "source": "https://example.com" }"""));
        Assert.Equal(1, Errors("""{ "evidence": "anecdotal", "category": "performance", "source": "https://example.com" }"""));
    }

    [Fact]
    public void Documented_NeedsAnHttpsSource()
    {
        Assert.Equal(0, Errors("""{ "evidence": "documented", "category": "privacy", "sources": ["https://learn.microsoft.com/x"] }"""));
        Assert.Equal(1, Errors("""{ "evidence": "documented", "category": "privacy", "source": "" }"""));
    }

    [Fact]
    public void Sources_MustBeWebLinks()
    {
        Assert.Equal(1, Errors("""{ "evidence": "situational", "category": "system", "sources": ["file:///C:/x.html"] }"""));
        Assert.Equal(1, Errors("""{ "evidence": "situational", "category": "system", "source": "see forum post" }"""));
    }
}
