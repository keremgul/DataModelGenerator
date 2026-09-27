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

    private static void EnsurePrimaryKey(ModelEntity entity, List<Ambiguity> notes)
    {
        if (entity.Attributes.Any(a => a.IsPrimaryKey)) return;

        var pkName = NamingRules.DefaultPrimaryKeyName(entity.TechnicalName);
        entity.Attributes.Insert(0, new EntityAttribute
        {
            Name = pkName,
            TechnicalName = pkName,
            IsPrimaryKey = true,
            IsRequired = true,
            IsUnique = true,
            SourceRuleIds = new List<string>(entity.SourceRuleIds),
            Confidence = 0.4,
            ConfidenceReason = "Kurallarda birincil anahtar belirtilmedi; vekil (surrogate) anahtar eklendi."
        });

        notes.Add(new Ambiguity
        {
            Kind = AmbiguityKind.PrimaryKey,
            Impact = AmbiguityImpact.High,
            Target = entity.Name,
            Description = $"'{entity.Name}' varlığı için birincil anahtar kurallardan çıkarılamadı; '{pkName}' vekil anahtarı eklendi.",
            Confidence = 0.4,
            AppliedDefault = pkName
        });
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
