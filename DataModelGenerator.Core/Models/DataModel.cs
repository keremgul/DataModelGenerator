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
/// Modele giren her öğenin kesinlik derecesi. <see cref="Belirsiz"/> olan hiçbir
/// öğe modele eklenmez; soru listesine düşer.
/// </summary>
public enum Certainty
{
    Kesin,
    Belirsiz
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
    /// <summary>Bu alanı doğuran kaynaklar: BR-xx, Q-xx veya CHAT-xx. Boş olamaz.</summary>
    public List<string> SourceRuleIds { get; set; } = new();

    public Certainty Certainty { get; set; } = Certainty.Kesin;
    public double Confidence { get; set; } = 1.0;
    public string ConfidenceReason { get; set; } = string.Empty;

    /// <summary>
    /// Kurumsal standart gereği her tabloya eklenen alan (GIRISTARIHI,
    /// DURUMKODU…). Business rule'dan türemediği için kaynak zorunluluğundan,
    /// normalizasyondan ve soru üretiminden muaftır.
    /// </summary>
    public bool IsStandard { get; set; }

    /// <summary>
    /// Kullanıcıya gösterilen ad. Deterministik katmandan geçmemiş bir alan da
    /// standarda uygun görünsün diye politikadan geçirilir.
    /// </summary>
    public string Label => Rules.NamingRules.ToTechnicalName(
        string.IsNullOrWhiteSpace(TechnicalName) ? Name : TechnicalName);

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
        IsStandard = IsStandard,
        SourceRuleIds = new List<string>(SourceRuleIds),
        Certainty = Certainty,
        Confidence = Confidence,
        ConfidenceReason = ConfidenceReason
    };
}

public enum EntityOrigin
{
    /// <summary>Business rule'lardan bu üretimde türedi.</summary>
    New,

    /// <summary>Yüklenen mevcut yapı dosyasından geldi; yeniden üretilmez.</summary>
    Existing
}

public class ModelEntity
{
    public string Name { get; set; } = string.Empty;
    public string TechnicalName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsJunction { get; set; }
    public List<EntityAttribute> Attributes { get; set; } = new();
    public List<string> SourceRuleIds { get; set; } = new();
    public Certainty Certainty { get; set; } = Certainty.Kesin;
    public double Confidence { get; set; } = 1.0;
    public string ConfidenceReason { get; set; } = string.Empty;

    /// <summary>
    /// Tablo yüklenen mevcut yapıdan mı geliyor yoksa kurallardan mı türedi.
    /// İzlenebilirlik tablosunda ve diyagramda ayırt edilebilsin diye tutulur.
    /// </summary>
    public EntityOrigin Origin { get; set; } = EntityOrigin.New;

    public bool IsExisting => Origin == EntityOrigin.Existing;

    public EntityAttribute? PrimaryKey => Attributes.FirstOrDefault(a => a.IsPrimaryKey);
    public IEnumerable<EntityAttribute> PrimaryKeys => Attributes.Where(a => a.IsPrimaryKey);

    /// <summary>
    /// Kullanıcıya gösterilen ad. Deterministik katmandan geçmemiş bir model de
    /// (ör. LLM yanıtı süzülürken) standarda uygun görünsün diye politikadan geçirilir.
    /// </summary>
    public string Label => Rules.NamingRules.ToTechnicalName(
        string.IsNullOrWhiteSpace(TechnicalName) ? Name : TechnicalName);

    public override string ToString() => Label;
}

public class Relationship
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Aynı tablo çiftine birden çok ilişki varsa bunları ayıran rol
    /// (GONDEREN, ALICI…). Yabancı anahtar adındaki önekten türer. Boşsa
    /// çift arasında tek ilişki vardır. İlişkinin kimliği (kaynak, hedef, rol)
    /// üçlüsüdür — serbest metin olan Name değil.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    public string FromEntity { get; set; } = string.Empty;
    public string ToEntity { get; set; } = string.Empty;
    public Cardinality Cardinality { get; set; } = Cardinality.Unknown;
    public bool IsRequired { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public List<string> SourceRuleIds { get; set; } = new();
    public Certainty Certainty { get; set; } = Certainty.Kesin;
    public double Confidence { get; set; } = 1.0;
    public string ConfidenceReason { get; set; } = string.Empty;

    public override string ToString() => $"{FromEntity} → {ToEntity} ({Cardinality})";
}

public class DataModel
{
    public List<ModelEntity> Entities { get; set; } = new();
    public List<Relationship> Relationships { get; set; } = new();

    public ModelEntity? FindEntity(string name) =>
        Entities.FirstOrDefault(e =>
            e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            e.TechnicalName.Equals(name, StringComparison.OrdinalIgnoreCase));
}
