namespace DataModelGenerator.Core.Models;

public enum ChatTurnStatus
{
    Applied,
    PartiallyApplied,
    NotApplied,
    Failed
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

    public string StatusText => Status switch
    {
        ChatTurnStatus.Applied => "✓ Uygulandı",
        ChatTurnStatus.PartiallyApplied => "⚠ Kısmen uygulandı",
        ChatTurnStatus.NotApplied => "✗ Direktif uygulanmadı",
        _ => "✗ Hata"
    };
}
