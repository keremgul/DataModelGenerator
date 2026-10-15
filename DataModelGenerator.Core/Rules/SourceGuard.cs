using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Rules;

/// <summary>
/// Halüsinasyon bariyeri: LLM çıktısı modele alınmadan önce burada süzülür.
/// Kaynağı olmayan (BR/Q/CHAT) veya "belirsiz" işaretli hiçbir öğe modele girmez;
/// gerekçesi ve kullanıcıya sorulacak soruyla birlikte beklemede listesine alınır.
/// Prompt talimatına güvenilmez — karar kod tarafında verilir.
/// </summary>
public static class SourceGuard
{
    /// <summary>Girilen kurallarda karşılığı olan kaynak kimlikleri.</summary>
    public static HashSet<string> KnownSources(ProjectInput input) =>
        input.Rules.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Varlıkları ve alanlarını süzer. Kabul edilenleri döndürür; reddedilenleri
    /// <paramref name="pending"/> listesine ekler.
    /// </summary>
    public static List<ModelEntity> FilterEntities(
        IEnumerable<ModelEntity> candidates, ProjectInput input, List<PendingItem> pending)
    {
        var known = KnownSources(input);
        var accepted = new List<ModelEntity>();

        foreach (var entity in candidates)
        {
            var sources = ValidSources(entity.SourceRuleIds, known);

            if (sources.Count == 0)
            {
                pending.Add(Build(PendingItemKind.Entity, PendingReason.NoSource, entity.Name,
                    $"'{entity.Name}' tablosu hiçbir business rule, soru cevabı veya direktife bağlanamadı.",
                    entity.SourceRuleIds,
                    $"'{entity.Name}' tablosu hangi kuraldan doğuyor? Modele girmesi gerekiyor mu?",
                    ["Gerekli — ilgili kuralı ekleyeceğim", "Gerekli değil, çıkar"]));
                continue;
            }

            entity.SourceRuleIds = sources;

            if (entity.Certainty == Certainty.Belirsiz)
            {
                pending.Add(Build(PendingItemKind.Entity, PendingReason.Uncertain, entity.Name,
                    $"'{entity.Name}' tablosu belirsiz olarak işaretlendi: {entity.ConfidenceReason}",
                    sources,
                    $"'{entity.Name}' ayrı bir tablo olmalı mı, yoksa başka bir tablonun alanı mı?",
                    ["Ayrı tablo olmalı", "Başka tablonun alanı olmalı"]));
                continue;
            }

            entity.Attributes = FilterAttributes(entity, known, pending);
            accepted.Add(entity);
        }

        return accepted;
    }

    private static List<EntityAttribute> FilterAttributes(
        ModelEntity entity, HashSet<string> known, List<PendingItem> pending)
    {
        var accepted = new List<EntityAttribute>();

        foreach (var attribute in entity.Attributes)
        {
            var target = $"{entity.Name}.{attribute.Name}";
            var sources = ValidSources(attribute.SourceRuleIds, known);

            // Anahtar alanları model kuralından doğar; kaynak aranmaz.
            if (attribute.IsPrimaryKey || attribute.IsForeignKey)
            {
                accepted.Add(attribute);
                continue;
            }

            if (sources.Count == 0)
            {
                pending.Add(Build(PendingItemKind.Attribute, PendingReason.NoSource, target,
                    $"'{target}' alanı hiçbir kurala bağlanamadı; kaynaksız alan modele eklenmez.",
                    attribute.SourceRuleIds,
                    $"'{entity.Name}' tablosunda '{attribute.Name}' alanı gerçekten gerekli mi? Hangi kuraldan doğuyor?",
                    ["Gerekli — ilgili kuralı ekleyeceğim", "Gerekli değil, çıkar"]));
                continue;
            }

            attribute.SourceRuleIds = sources;

            if (attribute.Certainty == Certainty.Belirsiz)
            {
                pending.Add(Build(PendingItemKind.Attribute, PendingReason.Uncertain, target,
                    $"'{target}' alanı belirsiz olarak işaretlendi: {attribute.ConfidenceReason}",
                    sources,
                    $"'{entity.Name}' tablosunda '{attribute.Name}' alanı bulunmalı mı?",
                    ["Evet, bulunmalı", "Hayır, çıkar"]));
                continue;
            }

            accepted.Add(attribute);
        }

        return accepted;
    }

    /// <summary>İlişkileri süzer; uçları modelde olmayan ilişkiler de elenir.</summary>
    public static List<Relationship> FilterRelationships(
        IEnumerable<Relationship> candidates, ProjectInput input, DataModel model, List<PendingItem> pending)
    {
        var known = KnownSources(input);
        var accepted = new List<Relationship>();

        foreach (var relationship in candidates)
        {
            var target = $"{relationship.FromEntity} → {relationship.ToEntity}";

            if (model.FindEntity(relationship.FromEntity) is null || model.FindEntity(relationship.ToEntity) is null)
                continue;

            var sources = ValidSources(relationship.SourceRuleIds, known);

            if (sources.Count == 0)
            {
                pending.Add(Build(PendingItemKind.Relationship, PendingReason.NoSource, target,
                    $"'{target}' ilişkisi hiçbir kurala bağlanamadı.",
                    relationship.SourceRuleIds,
                    $"{relationship.FromEntity} ile {relationship.ToEntity} arasında gerçekten ilişki var mı? Hangi kural bunu söylüyor?",
                    ["İlişki var — kuralı ekleyeceğim", "İlişki yok, çıkar"]));
                continue;
            }

            relationship.SourceRuleIds = sources;

            if (relationship.Certainty == Certainty.Belirsiz || relationship.Cardinality == Cardinality.Unknown)
            {
                pending.Add(Build(PendingItemKind.Relationship, PendingReason.Uncertain, target,
                    $"'{target}' ilişkisinin kardinalitesi kurallardan kesin olarak çıkarılamadı.",
                    sources,
                    $"{relationship.FromEntity} ile {relationship.ToEntity} arasındaki ilişki hangisi?",
                    ["1-1", "1-N (bir kayda birden çok)", "N-M (çoka çok)"]));
                continue;
            }

            accepted.Add(relationship);
        }

        return accepted;
    }

    private static List<string> ValidSources(IEnumerable<string> claimed, HashSet<string> known) =>
        claimed
            .Where(id => !string.IsNullOrWhiteSpace(id) && known.Contains(id.Trim()))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static PendingItem Build(PendingItemKind kind, PendingReason reason, string target,
        string explanation, IEnumerable<string> sources, string question, string[] answers) => new()
    {
        Kind = kind,
        Reason = reason,
        Target = target,
        Explanation = explanation,
        SourceRuleIds = sources.Where(s => !string.IsNullOrWhiteSpace(s)).ToList(),
        Question = question,
        SuggestedAnswers = answers.ToList()
    };
}
