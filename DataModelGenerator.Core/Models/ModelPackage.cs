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
