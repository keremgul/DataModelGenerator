namespace DataModelGenerator.Core.Models;

public enum SuggestionDecision
{
    Pending,
    Accepted,
    Rejected
}

/// <summary>
/// Modele dahil edilmemiş proaktif öneri. Kullanıcı kabul eder, reddeder veya
/// açıklama ekler; yalnızca kabul edilenler modele uygulanır.
/// </summary>
public class Suggestion
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public SuggestionDecision Decision { get; set; } = SuggestionDecision.Pending;

    /// <summary>Kullanıcının öneriye eklediği açıklama/koşul.</summary>
    public string Note { get; set; } = string.Empty;

    public bool IsApplied { get; set; }
    public int AppliedInRound { get; set; }

    public override string ToString() => Text;
}
