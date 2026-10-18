namespace DataModelGenerator.Core.Models;

public enum DirectiveOperation
{
    RenameField,
    AddField,
    RemoveField,
    RenameEntity,
    AddEntity,
    RemoveEntity,
    AddRelation,
    RemoveRelation,
    ChangeCardinality,
    SplitEntity,

    /// <summary>Alanın veya tablonun açıklamasını yazar/değiştirir.</summary>
    SetDescription,

    /// <summary>Alanın zorunluluk/tekillik özelliğini değiştirir.</summary>
    UpdateField,

    /// <summary>
    /// Model değişikliği değil, soru: "bu tablo ne işe yarıyor?" gibi. Modele
    /// dokunulmaz; yanıt sohbet skill'i tarafından yazılır.
    /// </summary>
    Question,

    /// <summary>Hedefli bir işleme girmeyen, modelin tamamını ilgilendiren talep.</summary>
    General
}

/// <summary>
/// Sohbet mesajından çıkarılan yapılandırılmış değişiklik. Basit işlemler
/// (yeniden adlandırma, alan kaldırma) LLM'e gitmeden doğrudan kodla uygulanır.
/// </summary>
public class ChatDirective
{
    public DirectiveOperation Operation { get; set; } = DirectiveOperation.General;
    public string Entity { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
    public string TargetEntity { get; set; } = string.Empty;
    public Cardinality Cardinality { get; set; } = Cardinality.Unknown;
    public List<string> Fields { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;

    /// <summary><see cref="DirectiveOperation.SetDescription"/> için yazılacak açıklama.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary><see cref="DirectiveOperation.UpdateField"/>: null ise değişmez.</summary>
    public bool? Required { get; set; }
    public bool? Unique { get; set; }

    /// <summary>LLM çağrısı gerektirmeden kod tarafında uygulanabilir mi?</summary>
    public bool IsDeterministic => Operation is
        DirectiveOperation.RenameField or
        DirectiveOperation.RemoveField or
        DirectiveOperation.RenameEntity or
        DirectiveOperation.RemoveEntity or
        DirectiveOperation.RemoveRelation or
        DirectiveOperation.ChangeCardinality or
        DirectiveOperation.SetDescription or
        DirectiveOperation.UpdateField;

    public override string ToString() => Operation switch
    {
        DirectiveOperation.RenameField => $"{Entity}.{Field} → {NewName}",
        DirectiveOperation.RemoveField => $"{Entity}.{Field} kaldır",
        DirectiveOperation.RenameEntity => $"{Entity} → {NewName}",
        DirectiveOperation.RemoveEntity => $"{Entity} kaldır",
        DirectiveOperation.AddField => $"{Entity}.{NewName} ekle",
        DirectiveOperation.AddRelation => $"{Entity} → {TargetEntity} ilişkisi ekle",
        DirectiveOperation.RemoveRelation => $"{Entity} → {TargetEntity} ilişkisini kaldır",
        DirectiveOperation.ChangeCardinality => $"{Entity} → {TargetEntity} kardinalite {Cardinality}",
        DirectiveOperation.SplitEntity => $"{Entity} → {NewName} ayrıştır",
        DirectiveOperation.SetDescription => string.IsNullOrWhiteSpace(Field)
            ? $"{Entity} açıklaması: {Description}"
            : $"{Entity}.{Field} açıklaması: {Description}",
        DirectiveOperation.UpdateField => $"{Entity}.{Field}" +
            (Required is null ? "" : Required.Value ? " zorunlu" : " opsiyonel") +
            (Unique is null ? "" : Unique.Value ? " tekil" : " tekil değil"),
        DirectiveOperation.Question => $"Soru: {Explanation}",
        _ => Explanation
    };
}
