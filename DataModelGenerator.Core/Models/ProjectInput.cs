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
    public List<BusinessRule> Rules { get; set; } = new();
    public List<GlossaryTerm> Glossary { get; set; } = new();

    /// <summary>
    /// Kullanıcının yüklediği mevcut yapı dosyası. Buradaki tablolar referans
    /// alınır, yeniden üretilmez; yeni yapılar bunlarla ilişkilendirilir.
    /// </summary>
    public ExistingStructure ExistingStructure { get; set; } = new();

    /// <summary>Mevcut yapıdaki tablo adları — yabancı anahtar doğrulamasında kullanılır.</summary>
    public IReadOnlyList<string> ExistingStructureEntities => ExistingStructure.EntityNames;

    public static string PrefixOf(RuleOrigin origin) => origin switch
    {
        RuleOrigin.Clarification => "Q",
        RuleOrigin.Chat => "CHAT",
        _ => "BR"
    };

    public string NextRuleId(RuleOrigin origin)
    {
        var prefix = PrefixOf(origin);
        var existing = Rules
            .Where(r => r.Origin == origin && r.Id.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var max = existing
            .Select(r => int.TryParse(r.Id[(prefix.Length + 1)..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        // Mevcut kurallar sıfır dolgulu yazılmışsa (BR-01) yenisi de öyle olur;
        // aksi halde listede BR-01, BR-02, BR-3 gibi karışık numaralar oluşuyordu.
        var padded = existing.Any(r => r.Id[(prefix.Length + 1)..].StartsWith('0'));

        return padded ? $"{prefix}-{max + 1:00}" : $"{prefix}-{max + 1}";
    }
}
