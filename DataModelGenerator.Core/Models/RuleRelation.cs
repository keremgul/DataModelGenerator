namespace DataModelGenerator.Core.Models;

/// <summary>İki business rule arasındaki semantik ilişki.</summary>
public enum RuleRelationKind
{
    /// <summary>Aynı varlığı tanımlıyorlar — birlikte değerlendirilmeli.</summary>
    SameEntity,

    /// <summary>Biri diğerini detaylandırıyor.</summary>
    Elaborates,

    /// <summary>Biri diğerinin önkoşulu.</summary>
    DependsOn,

    /// <summary>Çelişiyorlar — çözülmeden modele yansımaz.</summary>
    Contradicts,

    /// <summary>Aynı şeyi söylüyorlar — tekrar.</summary>
    Duplicate,

    Unrelated
}

public class RuleRelation
{
    public string FirstRuleId { get; set; } = string.Empty;
    public string SecondRuleId { get; set; } = string.Empty;
    public RuleRelationKind Kind { get; set; } = RuleRelationKind.Unrelated;
    public string Explanation { get; set; } = string.Empty;

    /// <summary>Çelişki ve tekrarlar kullanıcıya soru olarak döner.</summary>
    public bool NeedsResolution =>
        Kind is RuleRelationKind.Contradicts or RuleRelationKind.Duplicate;

    public override string ToString() => $"{FirstRuleId} ↔ {SecondRuleId}: {Kind}";
}
