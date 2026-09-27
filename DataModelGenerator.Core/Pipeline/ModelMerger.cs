using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Rules;

namespace DataModelGenerator.Core.Pipeline;

/// <summary>
/// Kısmi güncellemelerin modele birleştirilmesi ve ilişki ↔ yabancı anahtar
/// tutarlılığının deterministik olarak sağlanması.
/// </summary>
public static class ModelMerger
{
    public static void UpsertEntity(DataModel model, ModelEntity entity)
    {
        var existing = model.FindEntity(entity.Name) ?? model.FindEntity(entity.TechnicalName);
        if (existing is null)
        {
            model.Entities.Add(entity);
            return;
        }

        existing.Name = entity.Name;
        existing.Description = string.IsNullOrWhiteSpace(entity.Description) ? existing.Description : entity.Description;
        existing.IsJunction = entity.IsJunction || existing.IsJunction;
        existing.Confidence = entity.Confidence;
        existing.ConfidenceReason = entity.ConfidenceReason;
        existing.SourceRuleIds = MergeIds(existing.SourceRuleIds, entity.SourceRuleIds);

        if (entity.Attributes.Count > 0)
            existing.Attributes = entity.Attributes;
    }

    public static void UpsertRelationship(DataModel model, Relationship relationship)
    {
        var existing = model.Relationships.FirstOrDefault(r =>
            SameEntity(model, r.FromEntity, relationship.FromEntity) &&
            SameEntity(model, r.ToEntity, relationship.ToEntity));

        if (existing is null)
        {
            model.Relationships.Add(relationship);
            return;
        }

        existing.Name = relationship.Name;
        existing.Cardinality = relationship.Cardinality;
        existing.IsRequired = relationship.IsRequired;
        existing.Description = relationship.Description;
        existing.Confidence = relationship.Confidence;
        existing.ConfidenceReason = relationship.ConfidenceReason;
        existing.SourceRuleIds = MergeIds(existing.SourceRuleIds, relationship.SourceRuleIds);
    }

    /// <summary>
    /// İlişkilerin gerektirdiği yabancı anahtarları ekler ve karşılığı olmayan
    /// yabancı anahtarlar için ilişki üretir. N-N ilişkiler için ara tablo
    /// otomatik oluşturulmaz — bu bir öneri olarak raporlanır.
    /// </summary>
    public static void SyncForeignKeys(DataModel model)
    {
        foreach (var relationship in model.Relationships)
        {
            if (relationship.Cardinality == Cardinality.ManyToMany) continue;

            var from = model.FindEntity(relationship.FromEntity);
            var to = model.FindEntity(relationship.ToEntity);
            if (from is null || to is null) continue;

            var (parent, child) = relationship.Cardinality == Cardinality.ManyToOne
                ? (to, from)
                : (from, to);

            var parentKey = parent.PrimaryKey;
            if (parentKey is null) continue;

            var alreadyMapped = child.Attributes.Any(a =>
                a.IsForeignKey &&
                string.Equals(a.ReferencesEntity, parent.TechnicalName, StringComparison.OrdinalIgnoreCase));

            if (alreadyMapped) continue;

            var name = NamingRules.MakeUnique(
                NamingRules.DefaultForeignKeyName(parent.TechnicalName),
                child.Attributes.Select(a => a.TechnicalName).ToList());

            child.Attributes.Add(new EntityAttribute
            {
                Name = name,
                TechnicalName = name,
                DataType = parentKey.DataType,
                RawType = parentKey.DataType,
                IsForeignKey = true,
                ReferencesEntity = parent.TechnicalName,
                ReferencesAttribute = parentKey.TechnicalName,
                IsRequired = relationship.IsRequired,
                IsUnique = relationship.Cardinality == Cardinality.OneToOne,
                SourceRuleIds = new List<string>(relationship.SourceRuleIds),
                Confidence = 0.7,
                ConfidenceReason = $"'{relationship.Name}' ilişkisinden deterministik olarak türetildi."
            });
        }

        foreach (var entity in model.Entities.ToList())
        {
            foreach (var attribute in entity.Attributes.Where(a => a.IsForeignKey).ToList())
            {
                var parent = string.IsNullOrWhiteSpace(attribute.ReferencesEntity)
                    ? null
                    : model.FindEntity(attribute.ReferencesEntity!);
                if (parent is null) continue;

                var covered = model.Relationships.Any(r =>
                    (SameEntity(model, r.FromEntity, parent.TechnicalName) && SameEntity(model, r.ToEntity, entity.TechnicalName)) ||
                    (SameEntity(model, r.ToEntity, parent.TechnicalName) && SameEntity(model, r.FromEntity, entity.TechnicalName)));

                if (covered) continue;

                model.Relationships.Add(new Relationship
                {
                    Name = "içerir",
                    FromEntity = parent.TechnicalName,
                    ToEntity = entity.TechnicalName,
                    Cardinality = attribute.IsUnique ? Cardinality.OneToOne : Cardinality.OneToMany,
                    IsRequired = attribute.IsRequired,
                    Description = $"'{entity.Name}.{attribute.Name}' yabancı anahtarından türetildi.",
                    SourceRuleIds = new List<string>(attribute.SourceRuleIds),
                    Confidence = attribute.Confidence,
                    ConfidenceReason = "Yabancı anahtar alanından deterministik olarak türetildi."
                });
            }
        }

        model.Relationships.RemoveAll(r =>
            model.FindEntity(r.FromEntity) is null || model.FindEntity(r.ToEntity) is null);
    }

    private static bool SameEntity(DataModel model, string left, string right)
    {
        var a = model.FindEntity(left)?.TechnicalName ?? left;
        var b = model.FindEntity(right)?.TechnicalName ?? right;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> MergeIds(List<string> first, List<string> second) =>
        first.Concat(second)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
