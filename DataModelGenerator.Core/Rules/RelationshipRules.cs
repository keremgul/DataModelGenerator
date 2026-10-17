using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Rules;

/// <summary>Bir yabancı anahtar adının çözümlenmiş hali.</summary>
public record ForeignKeyMatch(string Role, string TargetEntity)
{
    public bool HasRole => !string.IsNullOrEmpty(Role);
}

/// <summary>
/// skills/relationship-naming.md kurallarını kod tarafında işletir.
/// &lt;TABLOADI&gt;ID kalıbına uyan alan ilişkinin kendisidir: ayrıca ilişki tanımı
/// beklenmez, mükerrer tanım elenir, hedefi bulunamayan referans uyarıya döner.
/// </summary>
public static class RelationshipRules
{
    /// <summary>
    /// Alan adını yabancı anahtar kalıbına göre çözer. Hedef tablo
    /// <paramref name="model"/> içinde (veya mevcut yapı dosyasında) bulunmalıdır.
    /// </summary>
    public static ForeignKeyMatch? Resolve(string attributeName, DataModel model,
        IEnumerable<string>? externalEntities = null)
    {
        var suffix = NamingRules.Policy.ForeignKeySuffix;
        var name = attributeName.Trim();

        if (name.Length <= suffix.Length) return null;
        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;

        // Birincil anahtarın kendisi yabancı anahtar değildir.
        if (name.Equals(NamingRules.Policy.PrimaryKeyName, StringComparison.OrdinalIgnoreCase)) return null;

        var withoutSuffix = name[..^suffix.Length].TrimEnd('_');
        if (withoutSuffix.Length == 0) return null;

        var candidates = model.Entities
            .Select(e => e.TechnicalName)
            .Concat(externalEntities ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(n => n.Length)
            .ToList();

        // Tam eşleşme: MUSTERIID → MUSTERI
        var exact = candidates.FirstOrDefault(n =>
            withoutSuffix.Equals(n, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return new ForeignKeyMatch(string.Empty, exact);

        // Rol önekli: GONDERENMUSTERIID → rol GONDEREN, hedef MUSTERI
        var suffixed = candidates.FirstOrDefault(n =>
            withoutSuffix.Length > n.Length &&
            withoutSuffix.EndsWith(n, StringComparison.OrdinalIgnoreCase));

        if (suffixed is null) return null;

        var role = withoutSuffix[..^suffixed.Length].TrimEnd('_');
        return new ForeignKeyMatch(role, suffixed);
    }

    /// <summary>
    /// Kalıba uyan alanları yabancı anahtar olarak işaretler, eksik ilişkileri
    /// kurar, mükerrerleri eler ve hedefi bulunamayan referansları bildirir.
    /// </summary>
    public static List<string> Apply(DataModel model, List<PendingItem> pending,
        IEnumerable<string>? externalEntities = null)
    {
        var external = externalEntities?.ToList() ?? [];
        var notes = new List<string>();

        MarkForeignKeys(model, pending, external, notes);
        EnsureRelationships(model, notes);
        RemoveDuplicateRelationships(model, notes);

        return notes;
    }

    private static void MarkForeignKeys(DataModel model, List<PendingItem> pending,
        List<string> external, List<string> notes)
    {
        foreach (var entity in model.Entities)
        {
            foreach (var attribute in entity.Attributes.Where(a => !a.IsPrimaryKey))
            {
                var match = Resolve(attribute.TechnicalName, model, external);

                if (match is null)
                {
                    // Model "yabancı anahtar" dediği halde hedefi çözülemiyorsa uyarılır.
                    if (attribute.IsForeignKey && !string.IsNullOrWhiteSpace(attribute.ReferencesEntity) &&
                        model.FindEntity(attribute.ReferencesEntity!) is null &&
                        !external.Contains(attribute.ReferencesEntity!, StringComparer.OrdinalIgnoreCase))
                        ReportMissingTarget(entity, attribute, attribute.ReferencesEntity!, pending, notes);

                    continue;
                }

                // Hedef yalnızca mevcut yapı dosyasında varsa referans geçerlidir
                // ama modelde ilişki çizilemez; uyarı verilmez.
                if (model.FindEntity(match.TargetEntity) is null)
                {
                    if (!external.Contains(match.TargetEntity, StringComparer.OrdinalIgnoreCase))
                    {
                        ReportMissingTarget(entity, attribute, match.TargetEntity, pending, notes);
                        continue;
                    }

                    attribute.IsForeignKey = true;
                    attribute.ReferencesEntity = match.TargetEntity;
                    attribute.ReferencesAttribute = NamingRules.Policy.PrimaryKeyName;

                    notes.Add($"'{entity.TechnicalName}.{attribute.TechnicalName}' mevcut yapıdaki " +
                              $"'{match.TargetEntity}' tablosuna referans veriyor.");
                    continue;
                }

                if (!attribute.IsForeignKey)
                {
                    attribute.IsForeignKey = true;
                    notes.Add($"'{entity.TechnicalName}.{attribute.TechnicalName}' adlandırma kuralı gereği " +
                              $"'{match.TargetEntity}' tablosuna yabancı anahtar sayıldı.");
                }

                attribute.ReferencesEntity = match.TargetEntity;
                attribute.ReferencesAttribute = NamingRules.Policy.PrimaryKeyName;
            }
        }
    }

    private static void ReportMissingTarget(ModelEntity entity, EntityAttribute attribute, string target,
        List<PendingItem> pending, List<string> notes)
    {
        notes.Add($"'{entity.TechnicalName}.{attribute.TechnicalName}' alanı '{target}' tablosuna işaret ediyor " +
                  "ama böyle bir tablo ne modelde ne de yüklenen mevcut yapıda var.");

        var signature = $"{entity.TechnicalName}.{attribute.TechnicalName}";
        if (pending.Any(p => p.Target == signature && p.ViolatedRule == "REL-1")) return;

        pending.Add(new PendingItem
        {
            Kind = PendingItemKind.Attribute,
            Reason = PendingReason.NormalizationBlocked,
            Target = signature,
            ViolatedRule = "REL-1",
            Explanation = $"'{attribute.TechnicalName}' alanı '{target}' tablosuna referans veriyor; bu tablo bulunamadı.",
            SourceRuleIds = new List<string>(attribute.SourceRuleIds),
            Question = $"'{target}' tablosu modele eklenmeli mi, yoksa " +
                       $"'{entity.TechnicalName}.{attribute.TechnicalName}' alanı kaldırılmalı mı?",
            SuggestedAnswers =
            [
                $"'{target}' tablosu eklensin",
                $"'{attribute.TechnicalName}' alanı kaldırılsın",
                "Alan başka bir tabloya işaret etmeli — belirteceğim"
            ]
        });
    }

    /// <summary>Yabancı anahtar varsa ilişki de vardır; eksikse kurulur.</summary>
    private static void EnsureRelationships(DataModel model, List<string> notes)
    {
        foreach (var entity in model.Entities)
        {
            foreach (var foreignKey in entity.Attributes.Where(a => a.IsForeignKey))
            {
                var target = string.IsNullOrWhiteSpace(foreignKey.ReferencesEntity)
                    ? null
                    : model.FindEntity(foreignKey.ReferencesEntity!);

                if (target is null) continue;

                // Aynı tablo çiftine rolü farklı birden çok ilişki meşrudur;
                // varlık kontrolü de role duyarlı yapılır.
                var match = Resolve(foreignKey.TechnicalName, model);
                var role = match?.HasRole == true ? match.Role : string.Empty;

                var exists = model.Relationships.Any(r =>
                    Same(r.FromEntity, target.TechnicalName) &&
                    Same(r.ToEntity, entity.TechnicalName) &&
                    Same(r.Role, role));

                if (exists) continue;

                model.Relationships.Add(new Relationship
                {
                    Name = role,
                    Role = role,
                    FromEntity = target.TechnicalName,
                    ToEntity = entity.TechnicalName,
                    Cardinality = Cardinality.OneToMany,
                    IsRequired = foreignKey.IsRequired,
                    SourceRuleIds = new List<string>(foreignKey.SourceRuleIds)
                });

                notes.Add($"'{entity.TechnicalName}.{foreignKey.TechnicalName}' alanından " +
                          $"'{target.TechnicalName}' → '{entity.TechnicalName}' ilişkisi türetildi.");
            }
        }
    }

    /// <summary>
    /// Aynı tablo çifti arasında farklı rollerle birden çok ilişki meşrudur;
    /// bu yüzden tekilleştirme kaynak + hedef + ilişki adına göre yapılır.
    /// </summary>
    private static void RemoveDuplicateRelationships(DataModel model, List<string> notes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<Relationship>();

        foreach (var relationship in model.Relationships)
        {
            var key = $"{relationship.FromEntity}|{relationship.ToEntity}|{relationship.Role}";
            if (!seen.Add(key)) duplicates.Add(relationship);
        }

        foreach (var duplicate in duplicates)
        {
            model.Relationships.Remove(duplicate);
            notes.Add($"Mükerrer ilişki tanımı elendi: {duplicate.FromEntity} → {duplicate.ToEntity}.");
        }
    }

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
