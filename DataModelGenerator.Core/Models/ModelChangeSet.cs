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

    public bool IsEmpty =>
        AddedEntities.Count == 0 && RemovedEntities.Count == 0 &&
        AddedAttributes.Count == 0 && RemovedAttributes.Count == 0 &&
        AddedRelationships.Count == 0 && RemovedRelationships.Count == 0;

    public int TotalChanges =>
        AddedEntities.Count + RemovedEntities.Count +
        AddedAttributes.Count + RemovedAttributes.Count +
        AddedRelationships.Count + RemovedRelationships.Count;

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
        }

        var beforeRelationships = before.Relationships.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterRelationships = after.Relationships.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        changes.AddedRelationships.AddRange(afterRelationships.Except(beforeRelationships, StringComparer.OrdinalIgnoreCase));
        changes.RemovedRelationships.AddRange(beforeRelationships.Except(afterRelationships, StringComparer.OrdinalIgnoreCase));

        return changes;

        static string Key(Relationship relationship) =>
            $"{relationship.FromEntity} → {relationship.ToEntity}";
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

        return sb.ToString().TrimEnd();

        static void Append(StringBuilder sb, string label, List<string> items)
        {
            if (items.Count == 0) return;
            sb.AppendLine($"{label} ({items.Count}): {string.Join(", ", items)}");
        }
    }
}
