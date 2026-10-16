using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Rules;

/// <summary>
/// Beklemede listesindeki her öğeyi soruya çevirir. Kaynaksız, belirsiz veya
/// normalizasyonu geçemeyen bir öğe modele girmez; kullanıcı cevaplayana kadar
/// yalnızca beklemede listesinde ve soru listesinde görünür.
/// </summary>
public static class PendingQuestions
{
    /// <summary>
    /// Öğelere tur bağımsız kimlik verir ve karşılığı olmayanlar için soru üretir.
    /// Aynı öğe her turda yeniden doğduğu için soru ikinci kez eklenmez.
    /// </summary>
    public static int Sync(IReadOnlyList<PendingItem> pending, List<ClarifyingQuestion> questions, int round)
    {
        var added = 0;
        var index = 1;

        foreach (var item in pending)
        {
            item.Id = $"BEK-{index++}";

            if (string.IsNullOrWhiteSpace(item.Question)) continue;

            // Aynı öğe veya aynı metinli bir soru zaten varsa tekrar sorulmaz.
            if (questions.Any(q => q.AmbiguityId == item.Signature)) continue;
            if (questions.Any(q => SameText(q.Text, item.Question))) continue;

            questions.Add(new ClarifyingQuestion
            {
                Id = $"B{round}-{++added}",
                AmbiguityId = item.Signature,
                Kind = KindOf(item),
                Target = item.Target,
                Text = item.Question,
                Reason = BuildReason(item),
                Options = new List<string>(item.SuggestedAnswers)
            });
        }

        return added;
    }

    /// <summary>Sorunun hangi kuraldan doğduğu ve neden belirsiz kaldığı açıkça yazılır.</summary>
    private static string BuildReason(PendingItem item)
    {
        var source = item.SourceRuleIds.Count > 0
            ? $"Şüphe kaynağı: {string.Join(", ", item.SourceRuleIds)}."
            : "Şüphe kaynağı: hiçbir kurala bağlanamadı.";

        var violated = string.IsNullOrWhiteSpace(item.ViolatedRule)
            ? string.Empty
            : $" İhlal edilen kural: {item.ViolatedRule}.";

        return $"{source}{violated} Neden beklemede: {item.Explanation} " +
               "Cevaplanana kadar bu öğe modele eklenmez.";
    }

    private static AmbiguityKind KindOf(PendingItem item) => item.Kind switch
    {
        PendingItemKind.Entity => AmbiguityKind.EntityExistence,
        PendingItemKind.Relationship => AmbiguityKind.Cardinality,
        PendingItemKind.Attribute => AmbiguityKind.Requiredness,
        _ => AmbiguityKind.Other
    };

    private static bool SameText(string first, string second) =>
        string.Equals(Fold(first), Fold(second), StringComparison.OrdinalIgnoreCase);

    private static string Fold(string value) =>
        new(NamingRules.FoldTurkish(value).Where(char.IsLetterOrDigit).ToArray());
}
