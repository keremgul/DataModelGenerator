using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Providers;

namespace DataModelGenerator.Tests;

/// <summary>
/// Pipeline'ı gerçek bir LLM olmadan sınamak için, prompt içeriğine göre
/// hazır JSON yanıtları döndüren sahte sağlayıcı.
/// </summary>
internal class FakeModelProvider : IModelProvider
{
    public string ProviderKey => "fake";
    public string DisplayName => "Sahte Sağlayıcı";
    public bool RequiresApiKey => false;
    public bool RequiresBaseUrl => false;
    public IReadOnlyList<ModelInfo> KnownModels => [new("fake-model", "fake-model")];

    public List<string> Stages { get; } = new();
    public List<string> Prompts { get; } = new();

    /// <summary>Alan adımının yanıtını test içinden değiştirebilmek için.</summary>
    public string AttributesResponse { get; set; } = DefaultAttributes;
    public string RelationshipsResponse { get; set; } = DefaultRelationships;
    public string? RepromptResponseOverride { get; set; }
    public string GapsResponseOverride { get; set; } = GapsResponse;
    public string QuestionsResponseOverride { get; set; } = QuestionsResponse;
    public string SummaryResponseOverride { get; set; } = SummaryResponse;

    public Task<List<ModelInfo>> ListModelsAsync(ProviderConnectionSettings settings, CancellationToken ct = default) =>
        Task.FromResult(new List<ModelInfo> { new("fake-model", "fake-model") });

    public Task<ConnectionTestResult> TestConnectionAsync(ProviderConnectionSettings settings, string modelId,
        CancellationToken ct = default) =>
        Task.FromResult(ConnectionTestResult.Ok("OK"));

    public Task<string> GenerateAsync(ProviderConnectionSettings settings, string modelId,
        string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        Prompts.Add(systemPrompt);

        if (systemPrompt.Contains("DEĞİŞTİRİLECEK KAPSAM"))
        {
            Stages.Add("reprompt");
            return Task.FromResult(RepromptResponseOverride ?? RepromptResponse);
        }
        if (systemPrompt.Contains("VARLIK (entity) adaylarını"))
        {
            Stages.Add("extraction");
            return Task.FromResult(DefaultEntities);
        }
        if (systemPrompt.Contains("İLİŞKİLERİ ve KARDİNALİTELERİ"))
        {
            Stages.Add("relationships");
            return Task.FromResult(RelationshipsResponse);
        }
        if (systemPrompt.Contains("ALAN (attribute) listesini"))
        {
            Stages.Add("attributes");
            return Task.FromResult(AttributesResponse);
        }
        if (systemPrompt.Contains("2NF/3NF"))
        {
            Stages.Add("normalization");
            return Task.FromResult("""{"violations":[],"notes":["Model 3NF uyumlu görünüyor."]}""");
        }
        if (systemPrompt.Contains("BELİRSİZ noktaları"))
        {
            Stages.Add("gaps");
            return Task.FromResult(GapsResponseOverride);
        }
        if (systemPrompt.Contains("HEDEFLİ SORULARI"))
        {
            Stages.Add("questions");
            return Task.FromResult(QuestionsResponseOverride);
        }
        if (systemPrompt.Contains("ÖZET ve ÖNERİ"))
        {
            Stages.Add("summary");
            return Task.FromResult(SummaryResponseOverride);
        }

        Stages.Add("bilinmeyen");
        return Task.FromResult("{}");
    }

    private const string DefaultEntities = """
        {
          "entities": [
            { "name": "Müşteri", "description": "Sipariş veren kişi", "sourceRuleIds": ["BR-1"], "confidence": 0.9, "confidenceReason": "BR-1'de açıkça geçiyor" },
            { "name": "Sipariş", "description": "Müşterinin verdiği sipariş", "sourceRuleIds": ["BR-2"], "confidence": 0.88, "confidenceReason": "BR-2'de geçiyor" }
          ]
        }
        """;

    private const string DefaultRelationships = """
        {
          "relationships": [
            { "name": "verir", "from": "Müşteri", "to": "Sipariş", "cardinality": "1-N", "required": true,
              "description": "Bir müşteri birden çok sipariş verir", "sourceRuleIds": ["BR-2"],
              "confidence": 0.8, "confidenceReason": "BR-2" }
          ]
        }
        """;

    private const string DefaultAttributes = """
        {
          "entities": [
            {
              "name": "Müşteri",
              "attributes": [
                { "name": "Müşteri No", "type": "BIGINT", "primaryKey": true, "required": true, "unique": true, "sourceRuleIds": ["BR-1"], "confidence": 0.9 },
                { "name": "Ad Soyad", "type": "VARCHAR(200)", "required": true, "sourceRuleIds": ["BR-1"], "confidence": 0.85 }
              ]
            },
            {
              "name": "Sipariş",
              "attributes": [
                { "name": "Sipariş No", "type": "BIGINT", "primaryKey": true, "required": true, "unique": true, "sourceRuleIds": ["BR-2"], "confidence": 0.9 },
                { "name": "Tutar", "type": "DECIMAL(18,2)", "required": true, "sourceRuleIds": ["BR-2"], "confidence": 0.8 },
                { "name": "Müşteri No", "type": "BIGINT", "foreignKey": true, "references": "Müşteri", "referencesAttribute": "MusteriNo", "required": true, "sourceRuleIds": ["BR-2"], "confidence": 0.8 }
              ]
            }
          ]
        }
        """;

    private const string GapsResponse = """
        {
          "ambiguities": [
            { "target": "Musteri → Siparis", "kind": "Cardinality", "impact": "High",
              "description": "Bir siparişin birden çok müşteriye ait olup olamayacağı belirtilmemiş", "confidence": 0.35 }
          ]
        }
        """;

    private const string QuestionsResponse = """
        {
          "questions": [
            { "ambiguityId": "A1", "text": "Bir sipariş yalnızca tek bir müşteriye mi aittir?",
              "reason": "Kardinaliteyi belirler", "options": ["Evet", "Hayır, ortak sipariş olabilir"] }
          ]
        }
        """;

    private const string SummaryResponse = """
        {
          "summary": "Müşteri varlığı BR-1'den, Sipariş varlığı BR-2'den türedi.",
          "suggestions": ["Sipariş durumu için ayrı bir durum varlığı düşünülebilir"]
        }
        """;

    private const string RepromptResponse = """
        {
          "entities": [
            {
              "name": "Sipariş",
              "description": "Müşterinin verdiği sipariş",
              "sourceRuleIds": ["BR-2"],
              "attributes": [
                { "name": "Sipariş No", "type": "BIGINT", "primaryKey": true, "required": true, "unique": true, "sourceRuleIds": ["BR-2"], "confidence": 0.9 },
                { "name": "Tutar", "type": "DECIMAL(18,4)", "required": true, "sourceRuleIds": ["BR-2"], "confidence": 0.9 },
                { "name": "İndirim Oranı", "type": "DECIMAL(5,2)", "required": false, "sourceRuleIds": ["BR-2"], "confidence": 0.7 },
                { "name": "Müşteri No", "type": "BIGINT", "foreignKey": true, "references": "Müşteri", "referencesAttribute": "MusteriNo", "required": true, "sourceRuleIds": ["BR-2"], "confidence": 0.8 }
              ]
            }
          ],
          "relationships": [],
          "notes": ["Müşteri varlığı kapsam dışında olduğu için değiştirilmedi"]
        }
        """;
}
