namespace DataModelGenerator.Core.Models;

public enum PendingItemKind
{
    Entity,
    Attribute,
    Relationship
}

public enum PendingReason
{
    /// <summary>Hiçbir BR / soru cevabı / chat direktifine bağlanmamış.</summary>
    NoSource,

    /// <summary>Model öğeyi "belirsiz" olarak işaretledi.</summary>
    Uncertain,

    /// <summary>Normalizasyon kurallarından birini geçemedi.</summary>
    NormalizationBlocked
}

/// <summary>
/// Modele alınmayan öğe. Kaynağı olmayan, belirsiz veya normalizasyonu geçemeyen
/// her şey buraya düşer ve kullanıcıya soru olarak döner — modelde görünmez.
/// </summary>
public class PendingItem
{
    public string Id { get; set; } = string.Empty;
    public PendingItemKind Kind { get; set; }
    public PendingReason Reason { get; set; }

    /// <summary>Hangi varlık/alan: "Müşteri" veya "Müşteri.Adres".</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Neden modele alınmadığının açıklaması.</summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>Varsa ihlal edilen normalizasyon kuralı (ör. 1NF-1).</summary>
    public string? ViolatedRule { get; set; }

    /// <summary>Şüphenin doğduğu kaynaklar (BR-xx / Q-xx / CHAT-xx).</summary>
    public List<string> SourceRuleIds { get; set; } = new();

    /// <summary>Kullanıcıya sorulacak soru.</summary>
    public string Question { get; set; } = string.Empty;

    public List<string> SuggestedAnswers { get; set; } = new();

    public override string ToString() => $"[{Kind}] {Target}: {Explanation}";
}
