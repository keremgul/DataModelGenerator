using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Pipeline;

/// <summary>
/// Zincirin adımları. Kullanıcı cevapları veya reprompt sonrası pipeline baştan değil,
/// yalnızca etkilenen adımdan itibaren çalıştırılır.
/// </summary>
public enum PipelineStage
{
    /// <summary>Kurallar arası semantik ilişki analizi — çelişki ve tekrarlar burada yakalanır.</summary>
    RuleRelationAnalysis = 0,

    Extraction = 1,
    RelationshipInference = 2,
    AttributeDerivation = 3,
    NormalizationCheck = 4,
    GapDetection = 5,
    QuestionGeneration = 6,
    Serialization = 7
}

public static class PipelineStageMap
{
    /// <summary>Bir belirsizlik giderildiğinde yeniden çalıştırılması gereken ilk adım.</summary>
    /// <summary>Kullanıcıya gösterilen toplam adım sayısı.</summary>
    public static readonly int StageCount = Enum.GetValues<PipelineStage>().Length;

    public static PipelineStage AffectedStage(AmbiguityKind kind) => kind switch
    {
        AmbiguityKind.EntityExistence => PipelineStage.Extraction,
        AmbiguityKind.Cardinality => PipelineStage.RelationshipInference,
        AmbiguityKind.PrimaryKey => PipelineStage.AttributeDerivation,
        AmbiguityKind.ForeignKey => PipelineStage.AttributeDerivation,
        AmbiguityKind.DataType => PipelineStage.AttributeDerivation,
        AmbiguityKind.Requiredness => PipelineStage.AttributeDerivation,
        AmbiguityKind.Naming => PipelineStage.NormalizationCheck,
        _ => PipelineStage.NormalizationCheck
    };

    public static string DisplayName(PipelineStage stage) => stage switch
    {
        PipelineStage.RuleRelationAnalysis => "Kural ilişki analizi",
        PipelineStage.Extraction => "Varlık çıkarımı",
        PipelineStage.RelationshipInference => "İlişki ve kardinalite tespiti",
        PipelineStage.AttributeDerivation => "Alan ve tip türetimi",
        PipelineStage.NormalizationCheck => "Normalizasyon kontrolü",
        PipelineStage.GapDetection => "Belirsizlik tespiti",
        PipelineStage.QuestionGeneration => "Soru üretimi",
        PipelineStage.Serialization => "Çıktı üretimi",
        _ => stage.ToString()
    };
}
