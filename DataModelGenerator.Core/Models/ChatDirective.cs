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

    /// <summary>LLM çağrısı gerektirmeden kod tarafında uygulanabilir mi?</summary>
    public bool IsDeterministic => Operation is
        DirectiveOperation.RenameField or
        DirectiveOperation.RemoveField or
        DirectiveOperation.RenameEntity or
        DirectiveOperation.RemoveEntity or
        DirectiveOperation.RemoveRelation or
        DirectiveOperation.ChangeCardinality;

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
        _ => Explanation
    };
}
