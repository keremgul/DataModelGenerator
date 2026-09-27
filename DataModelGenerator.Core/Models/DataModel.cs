namespace DataModelGenerator.Core.Models;

public enum Cardinality
{
    Unknown,
    OneToOne,
    OneToMany,
    ManyToOne,
    ManyToMany
}

/// <summary>
/// Kavramsal modelde alan; veri tipi ve uzunluk taşımaz — bunlar fiziksel
/// tasarımda, ihtiyaca göre belirlenir.
/// </summary>
public class EntityAttribute
{
    public string Name { get; set; } = string.Empty;
    public string TechnicalName { get; set; } = string.Empty;
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
    public string? ReferencesEntity { get; set; }
    public string? ReferencesAttribute { get; set; }
    public bool IsRequired { get; set; }
    public bool IsUnique { get; set; }
    public List<string> SourceRuleIds { get; set; } = new();
    public double Confidence { get; set; } = 1.0;
    public string ConfidenceReason { get; set; } = string.Empty;

    public EntityAttribute Clone() => new()
    {
        Name = Name,
        TechnicalName = TechnicalName,
        IsPrimaryKey = IsPrimaryKey,
        IsForeignKey = IsForeignKey,
        ReferencesEntity = ReferencesEntity,
        ReferencesAttribute = ReferencesAttribute,
        IsRequired = IsRequired,
        IsUnique = IsUnique,
        SourceRuleIds = new List<string>(SourceRuleIds),
        Confidence = Confidence,
        ConfidenceReason = ConfidenceReason
    };
}

public class ModelEntity
{
    public string Name { get; set; } = string.Empty;
    public string TechnicalName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsJunction { get; set; }
    public List<EntityAttribute> Attributes { get; set; } = new();
    public List<string> SourceRuleIds { get; set; } = new();
    public double Confidence { get; set; } = 1.0;
    public string ConfidenceReason { get; set; } = string.Empty;

    public EntityAttribute? PrimaryKey => Attributes.FirstOrDefault(a => a.IsPrimaryKey);
    public IEnumerable<EntityAttribute> PrimaryKeys => Attributes.Where(a => a.IsPrimaryKey);

    public override string ToString() => Name;
}

public class Relationship
{
    public string Name { get; set; } = string.Empty;
    public string FromEntity { get; set; } = string.Empty;
    public string ToEntity { get; set; } = string.Empty;
    public Cardinality Cardinality { get; set; } = Cardinality.Unknown;
    public bool IsRequired { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public List<string> SourceRuleIds { get; set; } = new();
    public double Confidence { get; set; } = 1.0;
    public string ConfidenceReason { get; set; } = string.Empty;

    public override string ToString() => $"{FromEntity} → {ToEntity} ({Cardinality})";
}

public class DataModel
{
    public string Topic { get; set; } = string.Empty;
    public List<ModelEntity> Entities { get; set; } = new();
    public List<Relationship> Relationships { get; set; } = new();

    public ModelEntity? FindEntity(string name) =>
        Entities.FirstOrDefault(e =>
            e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            e.TechnicalName.Equals(name, StringComparison.OrdinalIgnoreCase));
}
