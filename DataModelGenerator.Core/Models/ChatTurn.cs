namespace DataModelGenerator.Core.Models;

/// <summary>
/// Sohbetin modele değil kullanıcıya dönük önerisi. Bir tekliftir: kullanıcı
/// istemeden modele uygulanmaz.
/// </summary>
public class ChatSuggestion
{
    public string Text { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public List<string> RelatedEntities { get; set; } = new();

    public string TargetText => RelatedEntities.Count > 0
        ? string.Join(", ", RelatedEntities)
        : string.Empty;

    public override string ToString() => Text;
}

public enum ChatTurnStatus
{
    Applied,
    PartiallyApplied,
    NotApplied,
    Failed,

    /// <summary>Kullanıcı soru sordu; model değiştirilmedi, yanıt verildi.</summary>
    Answered
}

/// <summary>Bir sohbet turu: kullanıcı mesajı, yapılan iş ve gerçek diff.</summary>
public class ChatTurn
{
    public DateTime At { get; set; } = DateTime.Now;
    public string UserMessage { get; set; } = string.Empty;

    /// <summary>Bu turun kural listesine eklediği kaynak kimliği (CHAT-xx).</summary>
    public string RuleId { get; set; } = string.Empty;

    public List<ChatDirective> Directives { get; set; } = new();
    public List<string> Unsupported { get; set; } = new();
    public ChatTurnStatus Status { get; set; } = ChatTurnStatus.Applied;

    /// <summary>Modelin önceki ve sonraki hali arasındaki gerçek fark.</summary>
    public ModelChangeSet Changes { get; set; } = new();

    /// <summary>Uygulanamayan direktifler ve nedenleri.</summary>
    public List<string> NotApplied { get; set; } = new();

    /// <summary>Modelin bu tur için kullanıcıya yazdığı yanıt.</summary>
    public string Reply { get; set; } = string.Empty;

    /// <summary>Modelin güncel haline bakarak sunulan, henüz uygulanmamış teklifler.</summary>
    public List<ChatSuggestion> Suggestions { get; set; } = new();

    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>Mesaj yalnızca soru içeriyordu; modele dokunulmadı.</summary>
    public bool IsQuestionOnly { get; set; }

    /// <summary>İlk deneme modeli değiştirmedi, ikinci denemede uygulandı.</summary>
    public bool Retried { get; set; }

    public string StatusText => Status switch
    {
        ChatTurnStatus.Applied => "✓ Uygulandı",
        ChatTurnStatus.PartiallyApplied => "⚠ Kısmen uygulandı",
        ChatTurnStatus.NotApplied => "✗ Direktif uygulanmadı",
        ChatTurnStatus.Answered => "💬 Yanıtlandı",
        _ => "✗ Hata"
    };
}
