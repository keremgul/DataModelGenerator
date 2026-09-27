namespace DataModelGenerator.Core.Models;

public enum RuleOrigin
{
    User,
    Clarification
}

public class BusinessRule
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public RuleOrigin Origin { get; set; } = RuleOrigin.User;

    public override string ToString() => $"{Id}: {Text}";
}

public class GlossaryTerm
{
    public string Term { get; set; } = string.Empty;
    public string TechnicalName { get; set; } = string.Empty;

    public override string ToString() => $"{Term} → {TechnicalName}";
}

public class ProjectInput
{
    public string Topic { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public List<BusinessRule> Rules { get; set; } = new();
    public List<GlossaryTerm> Glossary { get; set; } = new();

    public string NextRuleId(RuleOrigin origin)
    {
        var prefix = origin == RuleOrigin.Clarification ? "C" : "BR";
        var max = Rules
            .Where(r => r.Origin == origin && r.Id.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase))
            .Select(r => int.TryParse(r.Id[(prefix.Length + 1)..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"{prefix}-{max + 1}";
    }
}
