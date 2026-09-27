using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Validation;

namespace DataModelGenerator.Core.Pipeline;

/// <summary>
/// Turlar arasında taşınan durum. Kısmi güncellemelerde adımlar bu nesnenin
/// üzerinde çalışır; etkilenmeyen varlıklar tekrar LLM'e gönderilmez.
/// </summary>
public class PipelineContext
{
    /// <summary>
    /// Soru–cevap döngüsünün üst sınırı. Bu tur sayısına ulaşıldığında yeni soru
    /// üretilmez; kalan belirsizlikler öneri olarak raporlanır.
    /// </summary>
    public const int MaxQuestionRounds = 3;

    public ProjectInput Input { get; set; } = new();
    public DataModel Model { get; set; } = new();
    public List<Ambiguity> Ambiguities { get; } = new();
    public List<ClarifyingQuestion> Questions { get; } = new();
    public List<string> NormalizationNotes { get; } = new();
    public List<string> Suggestions { get; } = new();
    public List<ValidationIssue> ValidationIssues { get; } = new();
    public string Summary { get; set; } = string.Empty;
    public int RoundNumber { get; set; }

    /// <summary>Kaç kez soru üretildiği. <see cref="MaxQuestionRounds"/> ile sınırlıdır.</summary>
    public int QuestionRounds { get; set; }

    public List<string> RepromptNotes { get; } = new();

    public bool QuestionLimitReached => QuestionRounds >= MaxQuestionRounds;

    /// <summary>Boşsa tüm varlıklar işlenir; doluysa yalnızca bu varlıklar LLM'e gönderilir.</summary>
    public HashSet<string> Scope { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsInScope(string entityName) =>
        Scope.Count == 0 || Scope.Contains(entityName);

    public ModelPackage ToPackage() => new()
    {
        RoundNumber = RoundNumber,
        Model = Model,
        Summary = Summary,
        Suggestions = new List<string>(Suggestions),
        NormalizationNotes = new List<string>(NormalizationNotes),
        Ambiguities = new List<Ambiguity>(Ambiguities),
        Questions = new List<ClarifyingQuestion>(Questions),
        ValidationIssues = new List<ValidationIssue>(ValidationIssues),
        RepromptNotes = new List<string>(RepromptNotes),
        QuestionRoundsUsed = QuestionRounds,
        QuestionLimitReached = QuestionLimitReached
    };
}

public class PipelineException : Exception
{
    public PipelineException(PipelineStage stage, string message, string rawOutput = "", Exception? inner = null)
        : base(message, inner)
    {
        Stage = stage;
        RawOutput = rawOutput;
    }

    public PipelineStage Stage { get; }
    public string RawOutput { get; }
}
