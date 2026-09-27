using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Pipeline;

/// <summary>
/// Zincirin adımları. Kullanıcı cevapları veya reprompt sonrası pipeline baştan değil,
/// yalnızca etkilenen adımdan itibaren çalıştırılır.
/// </summary>
public enum PipelineStage
{
    Extraction = 0,
    RelationshipInference = 1,
    AttributeDerivation = 2,
    NormalizationCheck = 3,
    GapDetection = 4,
    QuestionGeneration = 5,
    Serialization = 6
}

public static class PipelineStageMap
{
    /// <summary>Bir belirsizlik giderildiğinde yeniden çalıştırılması gereken ilk adım.</summary>
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
