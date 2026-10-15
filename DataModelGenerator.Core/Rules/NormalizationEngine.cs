using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Rules;

public enum NormalizationOutcome
{
    Passed,
    Decomposed,
    Blocked
}

public class NormalizationResult
{
    public string Entity { get; set; } = string.Empty;
    public string Attribute { get; set; } = string.Empty;
    public List<string> AppliedRules { get; set; } = new();
    public NormalizationOutcome Outcome { get; set; } = NormalizationOutcome.Passed;
    public string? ViolatedRule { get; set; }
    public string Explanation { get; set; } = string.Empty;

    /// <summary>İzlenebilirlik tablosunda gösterilen kısa NF durumu.</summary>
    public string StatusText => Outcome switch
    {
        NormalizationOutcome.Passed => "GEÇTİ",
        NormalizationOutcome.Decomposed => "AYRIŞTIRILDI",
        _ => $"ENGELLENDİ ({ViolatedRule})"
    };
}

/// <summary>
/// skills/NORMALIZATION_RULES.md kurallarını sırayla işletir. Bir kuralı geçemeyen
/// alan modele EKLENMEZ: beklemede listesine alınır ve kullanıcıya soru olarak döner.
/// Otomatik düzeltme yalnızca kuralın tek doğru çözümü olduğu durumda yapılır.
/// </summary>
public class NormalizationEngine
{
    private static readonly string[] CompositeHints =
        ["ad soyad", "adsoyad", "ad ve soyad", "adres", "tam ad", "isim soyisim"];

    private static readonly string[] MultiValueHints =
        ["listesi", "liste", "virgul", "virgül", "etiketler", "json", "dizi"];

    private static readonly string[] DerivedHints =
        ["toplam", "genel toplam", "yas", "yaş", "ortalama", "bakiye farki", "hesaplanan", "kalan gun", "kalan gün"];

    /// <summary>
    /// Modeli normalize eder. Engellenen alanlar modelden çıkarılır ve
    /// <paramref name="pending"/> listesine eklenir.
    /// </summary>
    public List<NormalizationResult> Apply(DataModel model, List<PendingItem> pending,
        IEnumerable<string>? onlyEntities = null)
    {
        var scope = onlyEntities?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<NormalizationResult>();

        foreach (var entity in model.Entities.ToList())
        {
            if (scope is not null && !scope.Contains(entity.TechnicalName) && !scope.Contains(entity.Name))
                continue;

            CheckPrimaryKey(entity, results, pending);
            CheckRepeatingGroups(entity, results, pending);

            foreach (var attribute in entity.Attributes.ToList())
            {
                var result = CheckAttribute(entity, attribute);
                results.Add(result);

                if (result.Outcome != NormalizationOutcome.Blocked) continue;

                entity.Attributes.Remove(attribute);
                pending.Add(new PendingItem
                {
                    Kind = PendingItemKind.Attribute,
                    Reason = PendingReason.NormalizationBlocked,
                    Target = $"{entity.Name}.{attribute.Name}",
                    ViolatedRule = result.ViolatedRule,
                    Explanation = result.Explanation,
                    SourceRuleIds = new List<string>(attribute.SourceRuleIds),
                    Question = BuildQuestion(entity, attribute, result),
                    SuggestedAnswers = BuildAnswers(result)
                });
            }
        }

        CheckForeignKeys(model, results, pending);
        return results;
    }

    private static void CheckPrimaryKey(ModelEntity entity, List<NormalizationResult> results, List<PendingItem> pending)
    {
        if (entity.PrimaryKeys.Any()) return;

        results.Add(new NormalizationResult
        {
            Entity = entity.Name,
            Attribute = "(birincil anahtar)",
            AppliedRules = { "PK-1" },
            Outcome = NormalizationOutcome.Blocked,
            ViolatedRule = "PK-1",
            Explanation = $"'{entity.Name}' tablosunun birincil anahtarı belirlenemedi."
        });

        pending.Add(new PendingItem
        {
            Kind = PendingItemKind.Entity,
            Reason = PendingReason.NormalizationBlocked,
            Target = entity.Name,
            ViolatedRule = "PK-1",
            Explanation = $"'{entity.Name}' tablosunun birincil anahtarı yok.",
            Question = $"'{entity.Name}' tablosunda kayıtları tekil olarak ne ayırt ediyor?",
            SuggestedAnswers = ["ID alanı eklensin", "Mevcut bir alan tekil anahtar olsun"]
        });
    }

    /// <summary>1NF-2: Telefon1, Telefon2 gibi tekrarlayan gruplar.</summary>
    private static void CheckRepeatingGroups(ModelEntity entity, List<NormalizationResult> results, List<PendingItem> pending)
    {
        var groups = entity.Attributes
            .Select(a => new { Attribute = a, Base = TrimTrailingDigits(a.TechnicalName) })
            .Where(x => x.Base.Length > 0 && x.Base != x.Attribute.TechnicalName)
            .GroupBy(x => x.Base, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in groups)
        {
            foreach (var member in group)
            {
                entity.Attributes.Remove(member.Attribute);
                results.Add(new NormalizationResult
                {
                    Entity = entity.Name,
                    Attribute = member.Attribute.Name,
                    AppliedRules = { "1NF-2" },
                    Outcome = NormalizationOutcome.Blocked,
                    ViolatedRule = "1NF-2",
                    Explanation = $"'{group.Key}' için tekrarlayan grup tespit edildi."
                });
            }

            pending.Add(new PendingItem
            {
                Kind = PendingItemKind.Attribute,
                Reason = PendingReason.NormalizationBlocked,
                Target = $"{entity.Name}.{group.Key}*",
                ViolatedRule = "1NF-2",
                Explanation = $"'{group.Key}1', '{group.Key}2' gibi tekrarlayan alanlar 1NF'i ihlal ediyor.",
                Question = $"'{entity.Name}' tablosunda birden çok {group.Key} bilgisi ayrı bir tabloda mı tutulmalı?",
                SuggestedAnswers = [$"Evet, ayrı {group.Key} tablosu oluştur", "Hayır, tek bir alan yeterli"]
            });
        }
    }

    private static NormalizationResult CheckAttribute(ModelEntity entity, EntityAttribute attribute)
    {
        var result = new NormalizationResult { Entity = entity.Name, Attribute = attribute.Name };
        var folded = NamingRules.FoldTurkish(attribute.Name).ToLowerInvariant();

        // AD-1: anahtar alanlar model kuralından doğar; diğerlerinin kaynağı olmalı.
        result.AppliedRules.Add("AD-1");
        if (!attribute.IsPrimaryKey && !attribute.IsForeignKey && attribute.SourceRuleIds.Count == 0)
        {
            result.Outcome = NormalizationOutcome.Blocked;
            result.ViolatedRule = "AD-1";
            result.Explanation = "Alanın kaynağı (BR/Q/CHAT) yok.";
            return result;
        }

        result.AppliedRules.Add("1NF-1");
        if (CompositeHints.Any(hint => folded.Contains(hint)))
        {
            result.Outcome = NormalizationOutcome.Blocked;
            result.ViolatedRule = "1NF-1";
            result.Explanation = $"'{attribute.Name}' bileşik görünüyor; atomik alanlara ayrılmalı veya atomik olduğu teyit edilmeli.";
            return result;
        }

        result.AppliedRules.Add("1NF-3");
        if (MultiValueHints.Any(hint => folded.Contains(hint)))
        {
            result.Outcome = NormalizationOutcome.Blocked;
            result.ViolatedRule = "1NF-3";
            result.Explanation = $"'{attribute.Name}' çok değerli görünüyor; tek alanda liste tutulamaz.";
            return result;
        }

        result.AppliedRules.Add("3NF-3");
        if (!attribute.IsPrimaryKey && DerivedHints.Any(hint => folded.Contains(hint)))
        {
            result.Outcome = NormalizationOutcome.Blocked;
            result.ViolatedRule = "3NF-3";
            result.Explanation = $"'{attribute.Name}' türetilebilir bir değer; saklanması gerekçelendirilmeli.";
            return result;
        }

        result.AppliedRules.Add("REL-4");
        var duplicate = entity.Attributes.FirstOrDefault(other =>
            !ReferenceEquals(other, attribute) &&
            NamingRules.FoldTurkish(other.Name).Equals(NamingRules.FoldTurkish(attribute.Name), StringComparison.OrdinalIgnoreCase));

        if (duplicate is not null && entity.Attributes.IndexOf(duplicate) < entity.Attributes.IndexOf(attribute))
        {
            result.Outcome = NormalizationOutcome.Blocked;
            result.ViolatedRule = "REL-4";
            result.Explanation = $"'{attribute.Name}' aynı bilgiyi tutan başka bir alanla çakışıyor.";
            return result;
        }

        return result;
    }

    /// <summary>REL-1: Yabancı anahtar var olan bir birincil anahtarı göstermeli.</summary>
    private static void CheckForeignKeys(DataModel model, List<NormalizationResult> results, List<PendingItem> pending)
    {
        foreach (var entity in model.Entities)
        {
            foreach (var foreignKey in entity.Attributes.Where(a => a.IsForeignKey).ToList())
            {
                var target = string.IsNullOrWhiteSpace(foreignKey.ReferencesEntity)
                    ? null
                    : model.FindEntity(foreignKey.ReferencesEntity!);

                if (target is not null && target.PrimaryKeys.Any())
                {
                    results.Add(new NormalizationResult
                    {
                        Entity = entity.Name,
                        Attribute = foreignKey.Name,
                        AppliedRules = { "REL-1" }
                    });
                    continue;
                }

                entity.Attributes.Remove(foreignKey);
                results.Add(new NormalizationResult
                {
                    Entity = entity.Name,
                    Attribute = foreignKey.Name,
                    AppliedRules = { "REL-1" },
                    Outcome = NormalizationOutcome.Blocked,
                    ViolatedRule = "REL-1",
                    Explanation = $"'{foreignKey.ReferencesEntity}' tablosu veya birincil anahtarı bulunamadı."
                });

                pending.Add(new PendingItem
                {
                    Kind = PendingItemKind.Attribute,
                    Reason = PendingReason.NormalizationBlocked,
                    Target = $"{entity.Name}.{foreignKey.Name}",
                    ViolatedRule = "REL-1",
                    Explanation = $"Yabancı anahtar '{foreignKey.ReferencesEntity}' tablosunu gösteriyor ama böyle bir tablo yok.",
                    SourceRuleIds = new List<string>(foreignKey.SourceRuleIds),
                    Question = $"'{entity.Name}' tablosu hangi tabloyla ilişkili olmalı?",
                    SuggestedAnswers = ["İlişki gerekli — hedef tabloyu belirteceğim", "İlişki gerekli değil"]
                });
            }
        }
    }

    private static string BuildQuestion(ModelEntity entity, EntityAttribute attribute, NormalizationResult result) =>
        result.ViolatedRule switch
        {
            "1NF-1" => $"'{entity.Name}.{attribute.Name}' alanı tek bir değer mi tutuyor, yoksa parçalara ayrılmalı mı?",
            "1NF-3" => $"'{entity.Name}.{attribute.Name}' alanında birden çok değer mi tutulacak? Ayrı tabloya alınmalı mı?",
            "3NF-3" => $"'{entity.Name}.{attribute.Name}' hesaplanabilir bir değer. Yine de saklanmalı mı?",
            "REL-4" => $"'{entity.Name}.{attribute.Name}' alanı başka bir alanla aynı bilgiyi tutuyor. Hangisi kalmalı?",
            _ => $"'{entity.Name}.{attribute.Name}' alanı için ne yapılmalı?"
        };

    private static List<string> BuildAnswers(NormalizationResult result) =>
        result.ViolatedRule switch
        {
            "1NF-1" => ["Atomik, olduğu gibi kalsın", "Parçalara ayır (ör. Ad / Soyad)"],
            "1NF-3" => ["Ayrı tabloya al", "Tek değer tutulacak, olduğu gibi kalsın"],
            "3NF-3" => ["Saklanmasın, hesaplansın", "Saklansın — gerekçesini yazacağım"],
            "REL-4" => ["İlk alan kalsın", "İkinci alan kalsın"],
            _ => ["Modele eklensin", "Modele eklenmesin"]
        };

    private static string TrimTrailingDigits(string value) =>
        value.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
}
