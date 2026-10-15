using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Rules;

public class DirectiveResult
{
    public bool Applied { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>Değişiklikten etkilenen tablolar — yalnızca bunlar yeniden normalize edilir.</summary>
    public List<string> AffectedEntities { get; set; } = new();

    public static DirectiveResult Fail(string message) => new() { Applied = false, Message = message };
}

/// <summary>
/// Hedefli değişiklikleri doğrudan model üzerinde uygular. Yeniden adlandırma,
/// alan/tablo kaldırma gibi işlemler için LLM çağrısı yapılmaz ve tüm pipeline
/// baştan çalıştırılmaz — yalnızca etkilenen parça güncellenir.
/// </summary>
public static class DirectiveExecutor
{
    public static DirectiveResult Execute(DataModel model, ChatDirective directive, string ruleId) =>
        directive.Operation switch
        {
            DirectiveOperation.RenameField => RenameField(model, directive, ruleId),
            DirectiveOperation.RemoveField => RemoveField(model, directive),
            DirectiveOperation.RenameEntity => RenameEntity(model, directive, ruleId),
            DirectiveOperation.RemoveEntity => RemoveEntity(model, directive),
            DirectiveOperation.RemoveRelation => RemoveRelation(model, directive),
            DirectiveOperation.ChangeCardinality => ChangeCardinality(model, directive, ruleId),
            _ => DirectiveResult.Fail($"'{directive.Operation}' işlemi doğrudan uygulanamıyor.")
        };

    private static DirectiveResult RenameField(DataModel model, ChatDirective directive, string ruleId)
    {
        var entity = model.FindEntity(directive.Entity);
        if (entity is null) return DirectiveResult.Fail($"'{directive.Entity}' tablosu bulunamadı.");

        var attribute = FindAttribute(entity, directive.Field);
        if (attribute is null)
            return DirectiveResult.Fail($"'{directive.Entity}' tablosunda '{directive.Field}' alanı bulunamadı.");

        if (string.IsNullOrWhiteSpace(directive.NewName))
            return DirectiveResult.Fail("Yeni alan adı belirtilmedi.");

        if (attribute.IsPrimaryKey)
            return DirectiveResult.Fail("Birincil anahtar adı model kuralı gereği 'ID' olmalıdır; değiştirilemez.");

        var newTechnical = NamingRules.ToTechnicalName(directive.NewName);
        if (entity.Attributes.Any(a => !ReferenceEquals(a, attribute) &&
                                       a.TechnicalName.Equals(newTechnical, StringComparison.OrdinalIgnoreCase)))
            return DirectiveResult.Fail($"'{newTechnical}' adında başka bir alan zaten var.");

        var oldTechnical = attribute.TechnicalName;
        attribute.Name = directive.NewName.Trim();
        attribute.TechnicalName = newTechnical;
        if (!attribute.SourceRuleIds.Contains(ruleId)) attribute.SourceRuleIds.Add(ruleId);

        // Bu alana işaret eden yabancı anahtarlar da güncellenir.
        foreach (var other in model.Entities)
        {
            foreach (var fk in other.Attributes.Where(a => a.IsForeignKey &&
                string.Equals(a.ReferencesEntity, entity.TechnicalName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.ReferencesAttribute, oldTechnical, StringComparison.OrdinalIgnoreCase)))
            {
                fk.ReferencesAttribute = newTechnical;
            }
        }

        return new DirectiveResult
        {
            Applied = true,
            Message = $"'{entity.Name}.{oldTechnical}' alanı '{newTechnical}' olarak değiştirildi.",
            AffectedEntities = [entity.TechnicalName]
        };
    }

    private static DirectiveResult RemoveField(DataModel model, ChatDirective directive)
    {
        var entity = model.FindEntity(directive.Entity);
        if (entity is null) return DirectiveResult.Fail($"'{directive.Entity}' tablosu bulunamadı.");

        var attribute = FindAttribute(entity, directive.Field);
        if (attribute is null)
            return DirectiveResult.Fail($"'{directive.Entity}' tablosunda '{directive.Field}' alanı bulunamadı.");

        if (attribute.IsPrimaryKey)
            return DirectiveResult.Fail("Birincil anahtar kaldırılamaz; her tabloda 'ID' bulunmak zorunda.");

        entity.Attributes.Remove(attribute);

        return new DirectiveResult
        {
            Applied = true,
            Message = $"'{entity.Name}.{attribute.TechnicalName}' alanı kaldırıldı.",
            AffectedEntities = [entity.TechnicalName]
        };
    }

    private static DirectiveResult RenameEntity(DataModel model, ChatDirective directive, string ruleId)
    {
        var entity = model.FindEntity(directive.Entity);
        if (entity is null) return DirectiveResult.Fail($"'{directive.Entity}' tablosu bulunamadı.");

        if (string.IsNullOrWhiteSpace(directive.NewName))
            return DirectiveResult.Fail("Yeni tablo adı belirtilmedi.");

        var newTechnical = NamingRules.ToTechnicalName(directive.NewName);
        if (model.Entities.Any(e => !ReferenceEquals(e, entity) &&
                                    e.TechnicalName.Equals(newTechnical, StringComparison.OrdinalIgnoreCase)))
            return DirectiveResult.Fail($"'{newTechnical}' adında başka bir tablo zaten var.");

        var oldTechnical = entity.TechnicalName;
        entity.Name = directive.NewName.Trim();
        entity.TechnicalName = newTechnical;
        if (!entity.SourceRuleIds.Contains(ruleId)) entity.SourceRuleIds.Add(ruleId);

        foreach (var relationship in model.Relationships)
        {
            if (string.Equals(relationship.FromEntity, oldTechnical, StringComparison.OrdinalIgnoreCase))
                relationship.FromEntity = newTechnical;
            if (string.Equals(relationship.ToEntity, oldTechnical, StringComparison.OrdinalIgnoreCase))
                relationship.ToEntity = newTechnical;
        }

        foreach (var other in model.Entities)
        {
            foreach (var fk in other.Attributes.Where(a => a.IsForeignKey &&
                string.Equals(a.ReferencesEntity, oldTechnical, StringComparison.OrdinalIgnoreCase)))
            {
                fk.ReferencesEntity = newTechnical;

                // FK adı model kuralı gereği hedef tablo adı + ID'dir.
                var expected = NamingRules.DefaultForeignKeyName(newTechnical);
                if (fk.TechnicalName.Equals(NamingRules.DefaultForeignKeyName(oldTechnical), StringComparison.OrdinalIgnoreCase))
                {
                    fk.TechnicalName = expected;
                    fk.Name = expected;
                }
            }
        }

        return new DirectiveResult
        {
            Applied = true,
            Message = $"'{oldTechnical}' tablosu '{newTechnical}' olarak değiştirildi.",
            AffectedEntities = [newTechnical]
        };
    }

    private static DirectiveResult RemoveEntity(DataModel model, ChatDirective directive)
    {
        var entity = model.FindEntity(directive.Entity);
        if (entity is null) return DirectiveResult.Fail($"'{directive.Entity}' tablosu bulunamadı.");

        var affected = model.Relationships
            .Where(r => Same(model, r.FromEntity, entity.TechnicalName) || Same(model, r.ToEntity, entity.TechnicalName))
            .SelectMany(r => new[] { r.FromEntity, r.ToEntity })
            .Where(name => !Same(model, name, entity.TechnicalName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var removed = Pipeline.ModelMerger.RemoveEntities(model, [entity.TechnicalName]);
        if (removed.Count == 0) return DirectiveResult.Fail($"'{directive.Entity}' kaldırılamadı.");

        return new DirectiveResult
        {
            Applied = true,
            Message = $"'{entity.Name}' tablosu, ilişkileri ve ona işaret eden yabancı anahtarlarla birlikte kaldırıldı.",
            AffectedEntities = affected
        };
    }

    private static DirectiveResult RemoveRelation(DataModel model, ChatDirective directive)
    {
        var removed = Pipeline.ModelMerger.RemoveRelationships(model,
            [(directive.Entity, directive.TargetEntity)]);

        if (removed.Count == 0)
            return DirectiveResult.Fail($"'{directive.Entity} → {directive.TargetEntity}' ilişkisi bulunamadı.");

        return new DirectiveResult
        {
            Applied = true,
            Message = $"İlişki kaldırıldı: {string.Join(", ", removed)}",
            AffectedEntities = [directive.Entity, directive.TargetEntity]
        };
    }

    private static DirectiveResult ChangeCardinality(DataModel model, ChatDirective directive, string ruleId)
    {
        if (directive.Cardinality == Cardinality.Unknown)
            return DirectiveResult.Fail("Yeni kardinalite belirtilmedi.");

        var relationship = model.Relationships.FirstOrDefault(r =>
            Same(model, r.FromEntity, directive.Entity) && Same(model, r.ToEntity, directive.TargetEntity));

        if (relationship is null)
            return DirectiveResult.Fail($"'{directive.Entity} → {directive.TargetEntity}' ilişkisi bulunamadı.");

        var previous = relationship.Cardinality;
        relationship.Cardinality = directive.Cardinality;
        if (!relationship.SourceRuleIds.Contains(ruleId)) relationship.SourceRuleIds.Add(ruleId);

        return new DirectiveResult
        {
            Applied = true,
            Message = $"Kardinalite {previous} → {directive.Cardinality} olarak değiştirildi.",
            AffectedEntities = [relationship.FromEntity, relationship.ToEntity]
        };
    }

    private static EntityAttribute? FindAttribute(ModelEntity entity, string name) =>
        entity.Attributes.FirstOrDefault(a =>
            a.TechnicalName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            a.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            NamingRules.ToTechnicalName(a.Name).Equals(NamingRules.ToTechnicalName(name), StringComparison.OrdinalIgnoreCase));

    private static bool Same(DataModel model, string left, string right)
    {
        var a = model.FindEntity(left)?.TechnicalName ?? left;
        var b = model.FindEntity(right)?.TechnicalName ?? right;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
