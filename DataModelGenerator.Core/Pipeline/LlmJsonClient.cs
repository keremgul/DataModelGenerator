using System.Text.Json.Nodes;
using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Providers;
using DataModelGenerator.Core.Serialization;

namespace DataModelGenerator.Core.Pipeline;

/// <summary>
/// Pipeline adımlarının LLM çağrısını yapar ve yanıtı yapılandırılmış JSON'a çevirir.
/// Serbest metin zincirlemesi yapılmaz — her adım JSON alır, JSON verir.
/// </summary>
public class LlmJsonClient
{
    private const string UserInstruction =
        "Görevi uygula ve yalnızca istenen şemaya uygun tek bir JSON nesnesi döndür.";

    private readonly IModelProvider _provider;
    private readonly ProviderConnectionSettings _settings;
    private readonly string _modelId;

    public LlmJsonClient(IModelProvider provider, ProviderConnectionSettings settings, string modelId)
    {
        _provider = provider;
        _settings = settings;
        _modelId = modelId;
    }

    public async Task<JsonNode> SendAsync(PipelineStage stage, string prompt, CancellationToken ct)
    {
        string raw;
        try
        {
            raw = await _provider.GenerateAsync(_settings, _modelId, prompt, UserInstruction, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PipelineException(stage,
                $"{PipelineStageMap.DisplayName(stage)} adımında API hatası: {ex.Message}", string.Empty, ex);
        }

        if (string.IsNullOrWhiteSpace(raw))
            throw new PipelineException(stage,
                $"{PipelineStageMap.DisplayName(stage)} adımında model boş yanıt döndürdü.");

        var json = ModelJson.ExtractJsonObject(raw);
        try
        {
            return JsonNode.Parse(json)
                   ?? throw new PipelineException(stage, "Yanıt JSON'a çevrilemedi.", raw);
        }
        catch (Exception ex) when (ex is not PipelineException)
        {
            throw new PipelineException(stage,
                $"{PipelineStageMap.DisplayName(stage)} adımının yanıtı geçerli JSON değil: {ex.Message}", raw, ex);
        }
    }
}
