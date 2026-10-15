namespace DataModelGenerator.Core.Models;

public enum RuleOrigin
{
    /// <summary>Kullanıcının girdiği business rule — BR-xx.</summary>
    User,

    /// <summary>Bir soruya verilen cevap — Q-xx.</summary>
    Clarification,

    /// <summary>Sohbetten gelen direktif — CHAT-xx.</summary>
    Chat
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

    public static string PrefixOf(RuleOrigin origin) => origin switch
    {
        RuleOrigin.Clarification => "Q",
        RuleOrigin.Chat => "CHAT",
        _ => "BR"
    };

    public string NextRuleId(RuleOrigin origin)
    {
        var prefix = PrefixOf(origin);
        var max = Rules
            .Where(r => r.Origin == origin && r.Id.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase))
            .Select(r => int.TryParse(r.Id[(prefix.Length + 1)..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"{prefix}-{max + 1}";
    }
}
