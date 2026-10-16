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
            CheckCompositeKey(entity, results, pending);
            CheckRepeatingGroups(entity, results, pending);
            CheckTransitiveDependencies(entity, results, pending);

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
        CheckRelationships(model, results, pending);
        return results;
    }

    /// <summary>PK-1, PK-2 ve 1NF-4: anahtar var mı, null olamaz/tekil mi, satırı tekil ayırt ediyor mu.</summary>
    private static void CheckPrimaryKey(ModelEntity entity, List<NormalizationResult> results, List<PendingItem> pending)
    {
        var keys = entity.PrimaryKeys.ToList();

        if (keys.Count > 0)
        {
            // PK-2'nin tek doğru çözümü var: anahtar zorunlu ve tekildir, otomatik düzeltilir.
            var corrected = false;
            foreach (var key in keys.Where(k => !k.IsRequired || !k.IsUnique))
            {
                key.IsRequired = true;
                key.IsUnique = true;
                corrected = true;
            }

            results.Add(new NormalizationResult
            {
                Entity = entity.Name,
                Attribute = "(birincil anahtar)",
                AppliedRules = { "PK-1", "PK-2", "1NF-4", "BCNF-1" },
                Outcome = corrected ? NormalizationOutcome.Decomposed : NormalizationOutcome.Passed,
                Explanation = corrected
                    ? "Birincil anahtar zorunlu ve tekil olarak işaretlendi."
                    : "Anahtar tanımlı; satırlar tekil olarak ayırt edilebiliyor."
            });
            return;
        }

        results.Add(new NormalizationResult
        {
            Entity = entity.Name,
            Attribute = "(birincil anahtar)",
            AppliedRules = { "PK-1", "PK-2", "1NF-4" },
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

    /// <summary>
    /// 2NF-2 ve BCNF-1: bileşik anahtarlı tabloda anahtar olmayan alanların
    /// anahtarın tamamına bağlı olup olmadığı kurallardan çıkarılamaz — sorulur.
    /// Model kuralı tek sütunlu <c>ID</c> anahtarı dayattığı için bu durum
    /// yalnızca kullanıcı veya model açıkça bileşik anahtar tanımladığında oluşur.
    /// </summary>
    private static void CheckCompositeKey(ModelEntity entity, List<NormalizationResult> results, List<PendingItem> pending)
    {
        var keys = entity.PrimaryKeys.ToList();
        if (keys.Count < 2) return;

        var dependents = entity.Attributes
            .Where(a => !a.IsPrimaryKey && !a.IsForeignKey)
            .Select(a => a.Name)
            .ToList();

        if (dependents.Count == 0) return;

        results.Add(new NormalizationResult
        {
            Entity = entity.Name,
            Attribute = "(bileşik anahtar)",
            AppliedRules = { "2NF-1", "2NF-2", "BCNF-1", "BCNF-2" },
            Outcome = NormalizationOutcome.Blocked,
            ViolatedRule = "2NF-2",
            Explanation = $"'{entity.Name}' bileşik anahtarlı; anahtar olmayan alanların anahtarın " +
                          "tamamına mı yoksa bir parçasına mı bağlı olduğu kurallardan çıkarılamıyor."
        });

        pending.Add(new PendingItem
        {
            Kind = PendingItemKind.Entity,
            Reason = PendingReason.NormalizationBlocked,
            Target = entity.Name,
            ViolatedRule = "2NF-2",
            Explanation = $"'{entity.Name}' tablosunda bileşik anahtar var: " +
                          $"{string.Join(" + ", keys.Select(k => k.TechnicalName))}. " +
                          $"Anahtar olmayan alanlar: {string.Join(", ", dependents)}.",
            Question = $"'{entity.Name}' tablosundaki alanlar bileşik anahtarın tamamına mı bağlı?",
            SuggestedAnswers =
            [
                "Tamamına bağlı — olduğu gibi kalsın",
                "Bir parçasına bağlı alanlar var — ayrı tabloya taşınsın",
                "Tek sütunlu ID anahtarı kullanılsın"
            ]
        });
    }

    /// <summary>
    /// 3NF-2: geçişli bağımlılık. "IlKodu" ile "IlAdi" gibi, anahtar olmayan bir
    /// alanın başka bir anahtar olmayan alana bağlı olduğu çiftler yakalanır.
    /// </summary>
    private static void CheckTransitiveDependencies(ModelEntity entity, List<NormalizationResult> results,
        List<PendingItem> pending)
    {
        var candidates = entity.Attributes.Where(a => !a.IsPrimaryKey && !a.IsForeignKey).ToList();

        foreach (var dependent in candidates.ToList())
        {
            var stem = StemOf(dependent.TechnicalName, DescriptiveSuffixes);
            if (stem is null) continue;

            // Tablonun kendi adıyla aynı kökse geçişli değil: Musteri.MusteriAdi doğaldır.
            if (stem.Equals(NamingRules.FoldTurkish(entity.TechnicalName), StringComparison.OrdinalIgnoreCase))
                continue;

            var determinant = candidates.FirstOrDefault(other =>
                !ReferenceEquals(other, dependent) &&
                DeterminantSuffixes.Any(suffix =>
                    NamingRules.FoldTurkish(other.TechnicalName)
                        .Equals(stem + suffix, StringComparison.OrdinalIgnoreCase)));

            if (determinant is null) continue;

            entity.Attributes.Remove(dependent);
            results.Add(new NormalizationResult
            {
                Entity = entity.Name,
                Attribute = dependent.Name,
                AppliedRules = { "3NF-1", "3NF-2" },
                Outcome = NormalizationOutcome.Blocked,
                ViolatedRule = "3NF-2",
                Explanation = $"'{dependent.Name}', anahtar olmayan '{determinant.Name}' alanına bağlı görünüyor."
            });

            pending.Add(new PendingItem
            {
                Kind = PendingItemKind.Attribute,
                Reason = PendingReason.NormalizationBlocked,
                Target = $"{entity.Name}.{dependent.Name}",
                ViolatedRule = "3NF-2",
                Explanation = $"Geçişli bağımlılık: {entity.Name} anahtarı → {determinant.Name} → {dependent.Name}.",
                SourceRuleIds = new List<string>(dependent.SourceRuleIds),
                Question = $"'{dependent.Name}' bilgisi '{determinant.Name}' üzerinden mi belirleniyor? " +
                           "Ayrı bir tabloda mı tutulmalı?",
                SuggestedAnswers =
                [
                    $"Evet, ayrı tabloya taşınsın ve {determinant.Name} ile ilişkilendirilsin",
                    "Hayır, bağımsız bir alan — olduğu gibi kalsın"
                ]
            });
        }
    }

    /// <summary>REL-2 ve REL-3: kardinalite desteklenmeli, N-M ara tabloyla çözülmeli.</summary>
    private static void CheckRelationships(DataModel model, List<NormalizationResult> results, List<PendingItem> pending)
    {
        foreach (var relationship in model.Relationships.ToList())
        {
            var target = $"{relationship.FromEntity} → {relationship.ToEntity}";

            if (relationship.Cardinality == Cardinality.Unknown)
            {
                model.Relationships.Remove(relationship);
                results.Add(new NormalizationResult
                {
                    Entity = target,
                    Attribute = "(ilişki)",
                    AppliedRules = { "REL-2" },
                    Outcome = NormalizationOutcome.Blocked,
                    ViolatedRule = "REL-2",
                    Explanation = "Kardinalite hiçbir kural veya cevapla desteklenmiyor."
                });

                pending.Add(new PendingItem
                {
                    Kind = PendingItemKind.Relationship,
                    Reason = PendingReason.NormalizationBlocked,
                    Target = target,
                    ViolatedRule = "REL-2",
                    Explanation = $"'{target}' ilişkisinin kardinalitesi belirlenemedi.",
                    SourceRuleIds = new List<string>(relationship.SourceRuleIds),
                    Question = $"{relationship.FromEntity} ile {relationship.ToEntity} arasındaki ilişki hangisi?",
                    SuggestedAnswers = ["1-1", "1-N (bir kayda birden çok)", "N-M (çoka çok)"]
                });
                continue;
            }

            // REL-3: ara tablo deterministik katmanda açılır; hâlâ N-M kalmışsa sorulur.
            if (relationship.Cardinality == Cardinality.ManyToMany)
            {
                results.Add(new NormalizationResult
                {
                    Entity = target,
                    Attribute = "(ilişki)",
                    AppliedRules = { "REL-2", "REL-3" },
                    Outcome = NormalizationOutcome.Blocked,
                    ViolatedRule = "REL-3",
                    Explanation = "Çoka-çok ilişki ara tabloyla çözülmeli ama ara tablo oluşturulamadı."
                });

                pending.Add(new PendingItem
                {
                    Kind = PendingItemKind.Relationship,
                    Reason = PendingReason.NormalizationBlocked,
                    Target = target,
                    ViolatedRule = "REL-3",
                    Explanation = $"'{target}' çoka-çok ilişkisi için ara tablo oluşturulamadı.",
                    SourceRuleIds = new List<string>(relationship.SourceRuleIds),
                    Question = $"{relationship.FromEntity} ve {relationship.ToEntity} arasındaki bağlantı " +
                               "hangi bilgileri taşımalı?",
                    SuggestedAnswers = ["Sadece iki tarafın anahtarları", "Ek alanlar da var — belirteceğim"]
                });
                continue;
            }

            results.Add(new NormalizationResult
            {
                Entity = target,
                Attribute = "(ilişki)",
                AppliedRules = { "REL-2", "REL-3" }
            });
        }
    }

    private static readonly string[] DescriptiveSuffixes =
        ["adi", "ad", "unvani", "aciklamasi", "aciklama", "tanimi", "ismi"];

    private static readonly string[] DeterminantSuffixes =
        ["kodu", "kod", "id", "no", "numarasi"];

    /// <summary>Alan adının sonundaki ekini atıp kökü döndürür; ek yoksa null.</summary>
    private static string? StemOf(string technicalName, string[] suffixes)
    {
        var folded = NamingRules.FoldTurkish(technicalName).ToLowerInvariant();

        foreach (var suffix in suffixes.OrderByDescending(s => s.Length))
        {
            if (!folded.EndsWith(suffix, StringComparison.Ordinal)) continue;

            var stem = folded[..^suffix.Length];
            if (stem.Length >= 2) return stem;
        }

        return null;
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
