using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Rules;

public class AnswerCompliance
{
    public string QuestionId { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;

    /// <summary>Cevabın modelde karşılığı bulundu mu?</summary>
    public bool IsSatisfied { get; set; }

    /// <summary>Karşılığın nerede bulunduğu veya neden bulunamadığı.</summary>
    public string Evidence { get; set; } = string.Empty;
}

/// <summary>
/// Kullanıcının verdiği her cevap bağlayıcı bir kısıttır. Model üretimi bittikten
/// sonra burada "bu cevap modelde karşılık buldu mu?" kontrolü yapılır; karşılığı
/// olmayan cevaplar kullanıcıya uyarı olarak listelenir.
/// </summary>
public static class AnswerComplianceChecker
{
    public static List<AnswerCompliance> Check(DataModel model, IEnumerable<ClarifyingQuestion> questions)
    {
        var results = new List<AnswerCompliance>();

        foreach (var question in questions.Where(q => q.IsApplied))
        {
            var result = new AnswerCompliance
            {
                QuestionId = question.Id,
                RuleId = question.InjectedRuleId ?? string.Empty,
                QuestionText = question.Text,
                Answer = question.Answer
            };

            // Birincil kanıt: cevabın eklediği kural, modelde bir öğenin kaynağı mı?
            var citedBy = FindCitations(model, result.RuleId).ToList();

            if (citedBy.Count > 0)
            {
                result.IsSatisfied = true;
                result.Evidence = $"Modelde {citedBy.Count} öğe bu cevaba dayanıyor: {string.Join(", ", citedBy.Take(5))}";
            }
            else
            {
                result.IsSatisfied = false;
                result.Evidence = "Cevap modelde hiçbir tablo, alan veya ilişkinin kaynağı olarak görünmüyor.";
            }

            results.Add(result);
        }

        return results;
    }

    private static IEnumerable<string> FindCitations(DataModel model, string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId)) yield break;

        foreach (var entity in model.Entities)
        {
            if (entity.SourceRuleIds.Contains(ruleId, StringComparer.OrdinalIgnoreCase))
                yield return entity.Name;

            foreach (var attribute in entity.Attributes)
            {
                if (attribute.SourceRuleIds.Contains(ruleId, StringComparer.OrdinalIgnoreCase))
                    yield return $"{entity.Name}.{attribute.Name}";
            }
        }

        foreach (var relationship in model.Relationships)
        {
            if (relationship.SourceRuleIds.Contains(ruleId, StringComparer.OrdinalIgnoreCase))
                yield return $"{relationship.FromEntity} → {relationship.ToEntity}";
        }
    }
}
