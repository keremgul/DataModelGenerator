using DataModelGenerator.Core.Validation;

namespace DataModelGenerator.Core.Models;

/// <summary>
/// Bir üretim turunun üç katmanlı çıktısı (model + özet + öneri) ile
/// belirsizlik listesi ve doğrulama sonuçları.
/// </summary>
public class ModelPackage
{
    public int RoundNumber { get; set; } = 1;
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public DataModel Model { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public List<Suggestion> Suggestions { get; set; } = new();
    public List<string> NormalizationNotes { get; set; } = new();
    public List<Ambiguity> Ambiguities { get; set; } = new();
    public List<ClarifyingQuestion> Questions { get; set; } = new();
    public List<ValidationIssue> ValidationIssues { get; set; } = new();
    public string MermaidCode { get; set; } = string.Empty;
    public string ModelJson { get; set; } = string.Empty;

    /// <summary>Son reprompt turunda modelin bildirdiği notlar (kapsam dışı kalan işler vb.).</summary>
    public List<string> RepromptNotes { get; set; } = new();

    /// <summary>Kaynaksız/belirsiz olduğu için modele alınmayan, cevap bekleyen öğeler.</summary>
    public List<PendingItem> PendingItems { get; set; } = new();

    /// <summary>Her alanın normalizasyon (NF) durumu.</summary>
    public List<Rules.NormalizationResult> NormalizationResults { get; set; } = new();

    /// <summary>Cevapların modelde karşılık bulup bulmadığı; bulmayanlar uyarı olarak gösterilir.</summary>
    public List<Rules.AnswerCompliance> AnswerCompliance { get; set; } = new();

    /// <summary>Business rule'lar arası semantik ilişkiler.</summary>
    public List<RuleRelation> RuleRelations { get; set; } = new();

    /// <summary>Son sohbet turu — direktifleri ve modelde oluşan gerçek değişikliği taşır.</summary>
    public ChatTurn? LastChatTurn { get; set; }

    /// <summary>
    /// Artımlı geliştirmede önceki ve yeni model arasındaki fark. Kullanıcıya
    /// neyin eklendiği, değiştiği ve kaldırıldığı bu özetle gösterilir.
    /// </summary>
    public ModelChangeSet? LastChanges { get; set; }

    /// <summary>Son turda kural listesine eklenen yeni kuralların kimlikleri.</summary>
    public List<string> AddedRuleIds { get; set; } = new();

    public IEnumerable<Rules.AnswerCompliance> UnsatisfiedAnswers =>
        AnswerCompliance.Where(a => !a.IsSatisfied);

    public int QuestionRoundsUsed { get; set; }

    /// <summary>Son "yeni soru üret" isteğinde kaç soru eklendiği.</summary>
    public int NewQuestionCount { get; set; }

    /// <summary>Son "yeni öneri üret" isteğinde kaç öneri eklendiği.</summary>
    public int NewSuggestionCount { get; set; }

    public IEnumerable<ClarifyingQuestion> OpenQuestions => Questions.Where(q => !q.IsApplied);
    public IEnumerable<ClarifyingQuestion> AppliedQuestions => Questions.Where(q => q.IsApplied);
    public IEnumerable<Suggestion> OpenSuggestions => Suggestions.Where(s => !s.IsApplied);

    public IEnumerable<Ambiguity> HighImpactAmbiguities =>
        Ambiguities.Where(a => a.Impact == AmbiguityImpact.High);
}
