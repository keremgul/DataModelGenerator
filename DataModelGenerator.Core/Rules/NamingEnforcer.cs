using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Rules;

/// <summary>
/// İsimlendirme standardını model üzerinde zorlar. Prompt'ta kuralı yazmak
/// yeterli değildir: model kurala uymayan bir ad döndürdüğünde burada düzeltilir.
/// Çalıştırılması idempotenttir — uyumlu bir ad ikinci geçişte değişmez.
/// </summary>
public static class NamingEnforcer
{
    /// <summary>
    /// Tüm tablo ve alan adlarını politikaya uydurur, referansları günceller ve
    /// yapılan düzeltmeleri döndürür.
    /// </summary>
    public static List<string> Enforce(DataModel model, NamingPolicy? policy = null)
    {
        policy ??= NamingRules.Policy;
        var corrections = new List<string>();

        var renamedEntities = EnforceEntityNames(model, policy, corrections);

        foreach (var entity in model.Entities)
            EnforceAttributeNames(entity, policy, corrections);

        RelinkReferences(model, renamedEntities);
        return corrections;
    }

    /// <returns>Eski teknik ad → yeni teknik ad eşlemesi.</returns>
    private static Dictionary<string, string> EnforceEntityNames(DataModel model, NamingPolicy policy,
        List<string> corrections)
    {
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var taken = new List<string>();

        foreach (var entity in model.Entities)
        {
            var source = string.IsNullOrWhiteSpace(entity.TechnicalName) ? entity.Name : entity.TechnicalName;
            var corrected = NamingRules.ToTechnicalName(source, policy);
            if (string.IsNullOrWhiteSpace(corrected)) continue;

            corrected = NamingRules.MakeUnique(corrected, taken);
            taken.Add(corrected);

            if (!corrected.Equals(entity.TechnicalName, StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(entity.TechnicalName))
                {
                    renamed[entity.TechnicalName] = corrected;
                    corrections.Add($"Tablo adı standarda uyduruldu: {entity.TechnicalName} → {corrected}");
                }

                entity.TechnicalName = corrected;
            }
        }

        return renamed;
    }

    private static void EnforceAttributeNames(ModelEntity entity, NamingPolicy policy, List<string> corrections)
    {
        var taken = new List<string>();

        foreach (var attribute in entity.Attributes)
        {
            // Birincil anahtarın adı politikada sabittir.
            var source = attribute.IsPrimaryKey
                ? policy.PrimaryKeyName
                : string.IsNullOrWhiteSpace(attribute.TechnicalName) ? attribute.Name : attribute.TechnicalName;

            var corrected = attribute.IsPrimaryKey
                ? policy.PrimaryKeyName
                : NamingRules.ToTechnicalName(source, policy);

            if (string.IsNullOrWhiteSpace(corrected)) continue;

            corrected = NamingRules.MakeUnique(corrected, taken);
            taken.Add(corrected);

            if (corrected.Equals(attribute.TechnicalName, StringComparison.Ordinal)) continue;

            if (!string.IsNullOrWhiteSpace(attribute.TechnicalName))
                corrections.Add(
                    $"Alan adı standarda uyduruldu: {entity.TechnicalName}.{attribute.TechnicalName} → {corrected}");

            attribute.TechnicalName = corrected;
        }
    }

    /// <summary>Tablo adı değiştiyse ona işaret eden her şey güncellenir.</summary>
    private static void RelinkReferences(DataModel model, Dictionary<string, string> renamedEntities)
    {
        foreach (var relationship in model.Relationships)
        {
            if (renamedEntities.TryGetValue(relationship.FromEntity, out var from))
                relationship.FromEntity = from;
            if (renamedEntities.TryGetValue(relationship.ToEntity, out var to))
                relationship.ToEntity = to;
        }

        foreach (var attribute in model.Entities.SelectMany(e => e.Attributes))
        {
            if (!string.IsNullOrWhiteSpace(attribute.ReferencesEntity) &&
                renamedEntities.TryGetValue(attribute.ReferencesEntity!, out var target))
                attribute.ReferencesEntity = target;

            if (!string.IsNullOrWhiteSpace(attribute.ReferencesAttribute))
                attribute.ReferencesAttribute = NamingRules.ToTechnicalName(attribute.ReferencesAttribute!);
        }
    }
}
