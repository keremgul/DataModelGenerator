using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Rules;

/// <summary>
/// LLM çıktısını deterministik kurallara göre normalize eder: teknik adlandırma,
/// veri tipi eşlemesi ve düşük etkili varsayılanlar. Uygulanan her varsayılan
/// <see cref="Ambiguity"/> olarak kaydedilir; böylece özet metninde raporlanabilir.
/// </summary>
public class RuleEngine
{
    public List<Ambiguity> Normalize(DataModel model, ProjectInput input)
    {
        var notes = new List<Ambiguity>();
        var usedEntityNames = new List<string>();

        foreach (var entity in model.Entities)
        {
            if (string.IsNullOrWhiteSpace(entity.Name))
                entity.Name = entity.TechnicalName;

            var technical = NamingRules.ToTechnicalName(
                string.IsNullOrWhiteSpace(entity.TechnicalName) ? entity.Name : entity.TechnicalName,
                input.Glossary);

            var unique = NamingRules.MakeUnique(technical, usedEntityNames);
            if (!unique.Equals(technical, StringComparison.Ordinal))
            {
                notes.Add(new Ambiguity
                {
                    Kind = AmbiguityKind.Naming,
                    Impact = AmbiguityImpact.Low,
                    Target = entity.Name,
                    Description = $"'{technical}' teknik adı başka bir varlıkta kullanıldığı için '{unique}' olarak değiştirildi.",
                    Confidence = 0.6,
                    AppliedDefault = unique,
                    IsResolved = true
                });
            }

            entity.TechnicalName = unique;
            usedEntityNames.Add(unique);

            NormalizeAttributes(entity, input, notes);
            EnsurePrimaryKey(entity, notes);
        }

        ResolveReferences(model, notes);
        return notes;
    }

    /// <summary>
    /// Yabancı anahtar hedeflerini ve kardinaliteleri yeniden bağlar. İlişkilerden
    /// yeni FK alanları türetildikten sonra tekrar çağrılır.
    /// </summary>
    public List<Ambiguity> RelinkReferences(DataModel model)
    {
        var notes = new List<Ambiguity>();
        ResolveReferences(model, notes);
        return notes;
    }

    private static void NormalizeAttributes(ModelEntity entity, ProjectInput input, List<Ambiguity> notes)
    {
        var usedNames = new List<string>();

        foreach (var attribute in entity.Attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Name))
                attribute.Name = attribute.TechnicalName;

            var technical = NamingRules.ToTechnicalName(
                string.IsNullOrWhiteSpace(attribute.TechnicalName) ? attribute.Name : attribute.TechnicalName,
                input.Glossary);

            attribute.TechnicalName = NamingRules.MakeUnique(technical, usedNames);
            usedNames.Add(attribute.TechnicalName);

            if (attribute.IsPrimaryKey)
            {
                attribute.IsRequired = true;
                attribute.IsUnique = true;
            }
        }
    }

    /// <summary>
    /// Model kuralı: her tabloda "ID" adında tekil bir birincil anahtar bulunur.
    /// Modelin doğal anahtar olarak önerdiği alanlar tekil (unique) alana dönüştürülür.
    /// </summary>
    private static void EnsurePrimaryKey(ModelEntity entity, List<Ambiguity> notes)
    {
        var naturalKeys = entity.Attributes
            .Where(a => a.IsPrimaryKey && !a.TechnicalName.Equals(NamingRules.PrimaryKeyName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var naturalKey in naturalKeys)
        {
            naturalKey.IsPrimaryKey = false;
            naturalKey.IsUnique = true;
            naturalKey.IsRequired = true;

            notes.Add(new Ambiguity
            {
                Kind = AmbiguityKind.PrimaryKey,
                Impact = AmbiguityImpact.Low,
                Target = $"{entity.Name}.{naturalKey.Name}",
                Description = $"Model kuralı gereği birincil anahtar '{NamingRules.PrimaryKeyName}' alanıdır; " +
                              $"'{naturalKey.Name}' tekil (unique) alan olarak korundu.",
                Confidence = 0.8,
                AppliedDefault = "UNIQUE",
                IsResolved = true
            });
        }

        var identifier = entity.Attributes.FirstOrDefault(a =>
            a.TechnicalName.Equals(NamingRules.PrimaryKeyName, StringComparison.OrdinalIgnoreCase));

        if (identifier is null)
        {
            identifier = new EntityAttribute
            {
                Name = NamingRules.PrimaryKeyName,
                TechnicalName = NamingRules.PrimaryKeyName,
                SourceRuleIds = new List<string>(entity.SourceRuleIds),
                Confidence = 1.0,
                ConfidenceReason = "Model kuralı: her tabloda ID birincil anahtarı bulunur."
            };
            entity.Attributes.Insert(0, identifier);
        }

        identifier.IsPrimaryKey = true;
        identifier.IsRequired = true;
        identifier.IsUnique = true;
        identifier.IsForeignKey = false;
        identifier.ReferencesEntity = null;
        identifier.ReferencesAttribute = null;
    }

    /// <summary>FK hedeflerini kanonik varlık/alan adlarına bağlar.</summary>
    private static void ResolveReferences(DataModel model, List<Ambiguity> notes)
    {
        foreach (var entity in model.Entities)
        {
            foreach (var attribute in entity.Attributes.Where(a => a.IsForeignKey))
            {
                var target = string.IsNullOrWhiteSpace(attribute.ReferencesEntity)
                    ? null
                    : model.FindEntity(attribute.ReferencesEntity!);

                if (target is null)
                {
                    notes.Add(new Ambiguity
                    {
                        Kind = AmbiguityKind.ForeignKey,
                        Impact = AmbiguityImpact.High,
                        Target = $"{entity.Name}.{attribute.Name}",
                        Description = $"Yabancı anahtarın işaret ettiği '{attribute.ReferencesEntity}' varlığı modelde bulunamadı.",
                        Confidence = 0.3
                    });
                    continue;
                }

                attribute.ReferencesEntity = target.TechnicalName;

                var targetPk = target.PrimaryKey;
                if (targetPk is null) continue;

                var referenced = string.IsNullOrWhiteSpace(attribute.ReferencesAttribute)
                    ? null
                    : target.Attributes.FirstOrDefault(a =>
                        a.TechnicalName.Equals(attribute.ReferencesAttribute, StringComparison.OrdinalIgnoreCase) ||
                        a.Name.Equals(attribute.ReferencesAttribute, StringComparison.OrdinalIgnoreCase));

                if (referenced is null)
                {
                    attribute.ReferencesAttribute = targetPk.TechnicalName;
                    notes.Add(new Ambiguity
                    {
                        Kind = AmbiguityKind.ForeignKey,
                        Impact = AmbiguityImpact.Low,
                        Target = $"{entity.Name}.{attribute.Name}",
                        Description = $"Referans alanı belirtilmemiş; hedef varlığın birincil anahtarı '{targetPk.TechnicalName}' kullanıldı.",
                        Confidence = 0.6,
                        AppliedDefault = targetPk.TechnicalName,
                        IsResolved = true
                    });
                }
                else
                {
                    attribute.ReferencesAttribute = referenced.TechnicalName;
                }

                // Model kuralı: FK adı, referans verdiği tablonun adı + "ID".
                var expected = NamingRules.DefaultForeignKeyName(target.TechnicalName);
                if (!attribute.TechnicalName.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    var siblings = entity.Attributes
                        .Where(a => !ReferenceEquals(a, attribute))
                        .Select(a => a.TechnicalName)
                        .ToList();

                    attribute.TechnicalName = NamingRules.MakeUnique(expected, siblings);
                    attribute.Name = attribute.TechnicalName;
                }
            }
        }

        foreach (var relationship in model.Relationships)
        {
            var from = model.FindEntity(relationship.FromEntity);
            var to = model.FindEntity(relationship.ToEntity);
            if (from is not null) relationship.FromEntity = from.TechnicalName;
            if (to is not null) relationship.ToEntity = to.TechnicalName;

            if (relationship.Cardinality == Cardinality.Unknown)
            {
                relationship.Cardinality = Cardinality.OneToMany;
                notes.Add(new Ambiguity
                {
                    Kind = AmbiguityKind.Cardinality,
                    Impact = AmbiguityImpact.High,
                    Target = $"{relationship.FromEntity} → {relationship.ToEntity}",
                    Description = "Kardinalite kurallardan çıkarılamadı; geçici olarak 1-N varsayıldı.",
                    Confidence = 0.3,
                    AppliedDefault = "OneToMany"
                });
            }
        }
    }
}
