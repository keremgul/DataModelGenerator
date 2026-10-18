using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Rules;

/// <summary>
/// Anlamı model kuralından belli olan alanların açıklamaları LLM'e sorulmadan
/// yazılır: birincil anahtar, yabancı anahtar ve kurumsal standart alanlar.
/// İş kuralından türeyen alanların açıklaması üretim adımında veya ayrı bir
/// açıklama turunda yazılır. Kullanıcının yazdığı açıklamanın üzerine yazılmaz.
/// </summary>
public static class FieldDescriptions
{
    /// <returns>Açıklaması hâlâ boş kalan alanların sayısı.</returns>
    public static int ApplyDefaults(DataModel model)
    {
        var missing = 0;

        foreach (var entity in model.Entities)
        {
            foreach (var attribute in entity.Attributes)
            {
                if (!string.IsNullOrWhiteSpace(attribute.Description)) continue;

                var description = DefaultFor(model, entity, attribute);
                if (description is null)
                {
                    missing++;
                    continue;
                }

                attribute.Description = description;
            }
        }

        return missing;
    }

    private static string? DefaultFor(DataModel model, ModelEntity entity, EntityAttribute attribute)
    {
        if (attribute.IsStandard || StandardFields.IsStandard(attribute.TechnicalName))
        {
            var definition = StandardFields.All.FirstOrDefault(d =>
                NamingRules.ToTechnicalName(d.Name).Equals(attribute.TechnicalName, StringComparison.OrdinalIgnoreCase));
            if (definition is not null) return definition.Description;
        }

        if (attribute.IsPrimaryKey)
            return $"Her {Readable(entity)} kaydını ayırt eden benzersiz numara";

        if (attribute.IsForeignKey && !string.IsNullOrWhiteSpace(attribute.ReferencesEntity))
        {
            var target = model.FindEntity(attribute.ReferencesEntity!);
            var name = target is null ? attribute.ReferencesEntity! : Readable(target);
            return $"Kaydın bağlı olduğu {name} kaydı";
        }

        return null;
    }

    /// <summary>Görünen ad teknik addan farklıysa (ör. "Müşteri") o tercih edilir.</summary>
    private static string Readable(ModelEntity entity) =>
        !string.IsNullOrWhiteSpace(entity.Name) &&
        !entity.Name.Equals(entity.TechnicalName, StringComparison.Ordinal)
            ? entity.Name.Trim().ToLowerInvariant()
            : entity.TechnicalName;
}
