using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Rules;

/// <summary>
/// skills/default-fields.md karşılığı: her tabloya kurumsal standart gereği
/// eklenen alanlar. Modelin üretmesi beklenmez, kod tarafında eklenir.
/// Kaynakları business rule değil standarttır; bu yüzden kaynak zorunluluğundan,
/// normalizasyondan ve soru üretiminden muaftırlar.
/// </summary>
public static class StandardFields
{
    /// <summary>Standart alanların kaynak kimliği — izlenebilirlik tablosunda görünür.</summary>
    public const string SourceId = "STD";

    public record Definition(string Name, string Description);

    public static readonly IReadOnlyList<Definition> All =
    [
        new("GIRISTARIHI", "Kaydın oluşturulma tarihi"),
        new("GIRISKULLANICIKODU", "Kaydı oluşturan kullanıcı"),
        new("ISLEMTARIHI", "Kaydın son değiştirilme tarihi"),
        new("ISLEMKULLANICIKODU", "Kaydı son değiştiren kullanıcı"),
        new("DURUMKODU", "Kaydın durumu")
    ];

    /// <summary>Verilen ad standart alanlardan biri mi?</summary>
    public static bool IsStandard(string technicalName)
    {
        if (string.IsNullOrWhiteSpace(technicalName)) return false;

        var normalized = NamingRules.ToTechnicalName(technicalName);
        return All.Any(f => NamingRules.ToTechnicalName(f.Name)
            .Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Eksik standart alanları her tabloya ekler ve adı eşleşen mevcut alanları
    /// standart olarak işaretler. Tekrar çağrılması alan çoğaltmaz.
    /// </summary>
    public static List<string> Apply(DataModel model)
    {
        var notes = new List<string>();

        foreach (var entity in model.Entities)
        {
            // Model kendiliğinden üretmişse tekrar eklenmez, işaretlenir.
            foreach (var attribute in entity.Attributes.Where(a => IsStandard(a.TechnicalName)))
            {
                attribute.IsStandard = true;
                if (!attribute.SourceRuleIds.Contains(SourceId)) attribute.SourceRuleIds.Add(SourceId);
            }

            var added = new List<string>();

            foreach (var definition in All)
            {
                var technical = NamingRules.ToTechnicalName(definition.Name);
                if (entity.Attributes.Any(a => a.TechnicalName.Equals(technical, StringComparison.OrdinalIgnoreCase)))
                    continue;

                entity.Attributes.Add(new EntityAttribute
                {
                    Name = definition.Name,
                    TechnicalName = technical,
                    IsStandard = true,
                    IsRequired = false,
                    Description = definition.Description,
                    SourceRuleIds = { SourceId },
                    ConfidenceReason = $"Kurumsal standart alan: {definition.Description}"
                });

                added.Add(technical);
            }

            if (added.Count > 0)
                notes.Add($"'{entity.TechnicalName}' tablosuna standart alanlar eklendi: {string.Join(", ", added)}.");
        }

        return notes;
    }

    /// <summary>Prompt'a gömülen kısa liste.</summary>
    public static string PromptText =>
        "STANDART ALANLAR (uygulama bunları her tabloya kendisi ekler — ÇIKTINDA YAZMA):\n" +
        string.Join("\n", All.Select(f => $"- {f.Name}: {f.Description}"));
}
