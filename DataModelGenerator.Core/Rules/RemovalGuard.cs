namespace DataModelGenerator.Core.Rules;

/// <summary>
/// Model yanıtındaki silme listeleri (removedEntities / removedFields /
/// removedRelationships) ancak kullanıcının talebi o öğeyi açıkça kaldırmayı
/// istiyorsa uygulanır. Soru cevabı, öneri veya sohbet mevcut modelin üzerine
/// eklenir; LLM'in kendiliğinden "temizlik" yapıp modeli baştan değiştirmesi
/// bu süzgeçle engellenir. Karar prompt'a değil koda dayanır.
/// </summary>
public sealed class RemovalGuard
{
    /// <summary>
    /// Silme, yeniden adlandırma veya taşıma niyeti taşıyan ifadeler (Türkçe
    /// karakterler katlanmış, küçük harf). Yeniden adlandırma ve taşıma da
    /// eski öğenin kaldırılmasını gerektirir.
    /// </summary>
    private static readonly string[] RemovalHints =
    [
        "sil", "kaldir", "cikar", "gerek yok", "gereksiz", "olmasin", "tutulmasin",
        "istemiyorum", "iptal", "yerine", "tasi", "ayir", "ayristir", "birlestir",
        "adini", "adlandir", "remove", "delete", "drop", "rename", "merge", "move"
    ];

    /// <summary>Hiçbir silmeye izin vermez. <see cref="RemovalHints"/>'tan sonra tanımlanmalı: statik alanlar sırayla başlatılır.</summary>
    public static readonly RemovalGuard Deny = new(string.Empty, []);

    private readonly string _compact;
    private readonly HashSet<string> _words;
    private readonly bool _hasRemovalIntent;
    private readonly HashSet<string> _explicit;

    private RemovalGuard(string request, IEnumerable<string> explicitTargets)
    {
        var folded = NamingRules.FoldTurkish(request ?? string.Empty).ToLowerInvariant();

        _hasRemovalIntent = RemovalHints.Any(folded.Contains);
        _compact = Compact(folded);
        _words = folded
            .Split(new[] { ' ', ',', '.', ';', ':', '\n', '\r', '\t', '\'', '"', '(', ')', '-', '/' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(Compact)
            .ToHashSet(StringComparer.Ordinal);
        _explicit = explicitTargets
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => Compact(NamingRules.FoldTurkish(t).ToLowerInvariant()))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <param name="request">Kullanıcının bu turdaki talebi (mesaj, cevap, kabul edilen öneri…).</param>
    /// <param name="explicitTargets">Talepten bağımsız olarak kaldırılmasına izin verilen adlar (ör. ayrıştırmada taşınan alanlar).</param>
    public static RemovalGuard FromRequest(string? request, IEnumerable<string>? explicitTargets = null) =>
        new(request ?? string.Empty, explicitTargets ?? []);

    public bool AllowsEntity(string entity) =>
        IsExplicit(entity) || (_hasRemovalIntent && Mentions(entity));

    public bool AllowsField(string entity, string field) =>
        IsExplicit(field) || (_hasRemovalIntent && Mentions(field));

    /// <summary>
    /// İlişki silmek tablo veya alan silmekten hafiftir; kullanıcı "bu ilişkiyi
    /// kaldır" deyip tabloları adlandırmayabilir.
    /// </summary>
    public bool AllowsRelationship(string from, string to) =>
        _hasRemovalIntent && (Mentions(from) || Mentions(to) || _compact.Contains("iliski", StringComparison.Ordinal));

    private bool IsExplicit(string name) => _explicit.Contains(Compact(NamingRules.FoldTurkish(name).ToLowerInvariant()));

    /// <summary>
    /// Talep bu adı anıyor mu? Hem doğal ad ("vergi no") hem teknik ad
    /// ("VERGINO") aranır. Kısa adlar ("AD") başka kelimelerin içinde de geçtiği
    /// için yalnızca tam kelime olarak kabul edilir.
    /// </summary>
    private bool Mentions(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var target = Compact(NamingRules.FoldTurkish(name).ToLowerInvariant());
        if (target.Length == 0) return false;

        return target.Length < 4
            ? _words.Contains(target)
            : _compact.Contains(target, StringComparison.Ordinal);
    }

    private static string Compact(string value) =>
        new(value.Where(char.IsLetterOrDigit).ToArray());
}
