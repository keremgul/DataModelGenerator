using System.Text;

namespace DataModelGenerator.Core.Models;

/// <summary>
/// İki model anlık görüntüsü arasındaki somut fark. "Uygulandı" denilen bir
/// değişikliğin modele gerçekten yansıyıp yansımadığı buradan doğrulanır.
/// </summary>
public class ModelChangeSet
{
    public List<string> AddedEntities { get; } = new();
    public List<string> RemovedEntities { get; } = new();
    public List<string> AddedAttributes { get; } = new();
    public List<string> RemovedAttributes { get; } = new();
    public List<string> AddedRelationships { get; } = new();
    public List<string> RemovedRelationships { get; } = new();

    /// <summary>
    /// Yerinde değişen öğeler: zorunluluk, tekillik, anahtar, açıklama,
    /// kardinalite… Bunlar sayılmadığında "X alanını zorunlu yap" gibi bir
    /// talep uygulansa bile tur "değişiklik yok" diye işaretleniyordu.
    /// </summary>
    public List<string> ModifiedEntities { get; } = new();
    public List<string> ModifiedAttributes { get; } = new();
    public List<string> ModifiedRelationships { get; } = new();

    public bool IsEmpty => TotalChanges == 0;

    public int TotalChanges =>
        AddedEntities.Count + RemovedEntities.Count +
        AddedAttributes.Count + RemovedAttributes.Count +
        AddedRelationships.Count + RemovedRelationships.Count +
        ModifiedEntities.Count + ModifiedAttributes.Count + ModifiedRelationships.Count;

    public static ModelChangeSet Compare(DataModel before, DataModel after)
    {
        var changes = new ModelChangeSet();

        var beforeEntities = before.Entities.ToDictionary(e => e.TechnicalName, StringComparer.OrdinalIgnoreCase);
        var afterEntities = after.Entities.ToDictionary(e => e.TechnicalName, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, entity) in afterEntities.Where(e => !beforeEntities.ContainsKey(e.Key)))
            changes.AddedEntities.Add(entity.TechnicalName);

        foreach (var (name, entity) in beforeEntities.Where(e => !afterEntities.ContainsKey(e.Key)))
            changes.RemovedEntities.Add(entity.TechnicalName);

        foreach (var (name, afterEntity) in afterEntities)
        {
            if (!beforeEntities.TryGetValue(name, out var beforeEntity)) continue;

            var beforeAttributes = beforeEntity.Attributes.Select(a => a.TechnicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var afterAttributes = afterEntity.Attributes.Select(a => a.TechnicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var added in afterAttributes.Except(beforeAttributes, StringComparer.OrdinalIgnoreCase))
                changes.AddedAttributes.Add($"{afterEntity.TechnicalName}.{added}");

            foreach (var removed in beforeAttributes.Except(afterAttributes, StringComparer.OrdinalIgnoreCase))
                changes.RemovedAttributes.Add($"{afterEntity.TechnicalName}.{removed}");

            if (!string.Equals(beforeEntity.Description.Trim(), afterEntity.Description.Trim(), StringComparison.Ordinal))
                changes.ModifiedEntities.Add($"{afterEntity.TechnicalName} (açıklama)");

            foreach (var afterAttribute in afterEntity.Attributes)
            {
                var beforeAttribute = beforeEntity.Attributes.FirstOrDefault(a =>
                    a.TechnicalName.Equals(afterAttribute.TechnicalName, StringComparison.OrdinalIgnoreCase));
                if (beforeAttribute is null) continue;

                var details = AttributeDifferences(beforeAttribute, afterAttribute);
                if (details.Count > 0)
                    changes.ModifiedAttributes.Add(
                        $"{afterEntity.TechnicalName}.{afterAttribute.TechnicalName} ({string.Join(", ", details)})");
            }
        }

        var beforeRelationships = before.Relationships.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterRelationships = after.Relationships.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        changes.AddedRelationships.AddRange(afterRelationships.Except(beforeRelationships, StringComparer.OrdinalIgnoreCase));
        changes.RemovedRelationships.AddRange(beforeRelationships.Except(afterRelationships, StringComparer.OrdinalIgnoreCase));

        foreach (var afterRelationship in after.Relationships)
        {
            var beforeRelationship = before.Relationships.FirstOrDefault(r =>
                Key(r).Equals(Key(afterRelationship), StringComparison.OrdinalIgnoreCase));
            if (beforeRelationship is null) continue;

            var details = new List<string>();
            if (beforeRelationship.Cardinality != afterRelationship.Cardinality)
                details.Add($"kardinalite {beforeRelationship.Cardinality} → {afterRelationship.Cardinality}");
            if (beforeRelationship.IsRequired != afterRelationship.IsRequired)
                details.Add(afterRelationship.IsRequired ? "zorunlu oldu" : "opsiyonel oldu");

            if (details.Count > 0)
                changes.ModifiedRelationships.Add($"{Key(afterRelationship)} ({string.Join(", ", details)})");
        }

        return changes;

        static string Key(Relationship relationship) =>
            $"{relationship.FromEntity} → {relationship.ToEntity}";
    }

    private static List<string> AttributeDifferences(EntityAttribute before, EntityAttribute after)
    {
        var details = new List<string>();

        if (before.IsRequired != after.IsRequired)
            details.Add(after.IsRequired ? "zorunlu oldu" : "opsiyonel oldu");
        if (before.IsUnique != after.IsUnique)
            details.Add(after.IsUnique ? "tekil oldu" : "tekillik kalktı");
        if (before.IsPrimaryKey != after.IsPrimaryKey)
            details.Add(after.IsPrimaryKey ? "birincil anahtar oldu" : "birincil anahtar olmaktan çıktı");
        if (before.IsForeignKey != after.IsForeignKey ||
            !string.Equals(before.ReferencesEntity ?? "", after.ReferencesEntity ?? "", StringComparison.OrdinalIgnoreCase))
            details.Add(string.IsNullOrWhiteSpace(after.ReferencesEntity)
                ? "yabancı anahtar kalktı"
                : $"→ {after.ReferencesEntity}");
        if (!string.Equals(before.Description.Trim(), after.Description.Trim(), StringComparison.Ordinal))
            details.Add("açıklama");

        return details;
    }

    /// <summary>Kullanıcıya gösterilecek, ne olduğunu tek tek sayan özet.</summary>
    public string Describe()
    {
        if (IsEmpty) return "Modelde hiçbir değişiklik olmadı.";

        var sb = new StringBuilder();
        Append(sb, "Eklenen tablo", AddedEntities);
        Append(sb, "Kaldırılan tablo", RemovedEntities);
        Append(sb, "Eklenen alan", AddedAttributes);
        Append(sb, "Kaldırılan alan", RemovedAttributes);
        Append(sb, "Eklenen ilişki", AddedRelationships);
        Append(sb, "Kaldırılan ilişki", RemovedRelationships);
        Append(sb, "Değişen tablo", ModifiedEntities);
        Append(sb, "Değişen alan", ModifiedAttributes);
        Append(sb, "Değişen ilişki", ModifiedRelationships);

        return sb.ToString().TrimEnd();

        static void Append(StringBuilder sb, string label, List<string> items)
        {
            if (items.Count == 0) return;
            sb.AppendLine($"{label} ({items.Count}): {string.Join(", ", items)}");
        }
    }
}
