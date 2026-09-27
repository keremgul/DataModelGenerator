namespace DataModelGenerator.Core.Models;

public enum AmbiguityImpact
{
    Low,
    High
}

/// <summary>
/// Belirsizliğin hangi model kararını etkilediği. Etki seviyesi ve yeniden
/// çalıştırılacak pipeline adımı bu türe göre belirlenir.
/// </summary>
public enum AmbiguityKind
{
    Cardinality,
    PrimaryKey,
    ForeignKey,
    EntityExistence,
    DataType,
    Requiredness,
    Naming,
    Other
}

public class Ambiguity
{
    public string Id { get; set; } = string.Empty;
    public AmbiguityKind Kind { get; set; } = AmbiguityKind.Other;
    public AmbiguityImpact Impact { get; set; } = AmbiguityImpact.Low;
    public string Target { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Confidence { get; set; } = 1.0;
    public string? AppliedDefault { get; set; }
    public bool IsResolved { get; set; }

    /// <summary>Deterministik katmanın ürettiği kayıt — her turda yeniden hesaplanır.</summary>
    public bool FromRuleEngine { get; set; }

    public override string ToString() => $"[{Impact}] {Target}: {Description}";
}

public class ClarifyingQuestion
{
    public string Id { get; set; } = string.Empty;
    public string AmbiguityId { get; set; } = string.Empty;
    public AmbiguityKind Kind { get; set; } = AmbiguityKind.Other;
    public string Target { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public string Answer { get; set; } = string.Empty;

    /// <summary>Cevap modele uygulandıktan sonra true olur; soru geçmişte kalır, tekrar sorulmaz.</summary>
    public bool IsApplied { get; set; }

    public int AppliedInRound { get; set; }

    public bool IsAnswered => !string.IsNullOrWhiteSpace(Answer);
}
