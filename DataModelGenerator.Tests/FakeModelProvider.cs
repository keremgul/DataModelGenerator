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

    /// <summary>Sıradaki reprompt çağrılarına sırayla verilecek yanıtlar; boşsa override/varsayılan.</summary>
    public Queue<string> RepromptQueue { get; } = new();

    public string EntitiesResponse { get; set; } = DefaultEntities;

    /// <summary>Null ise add-relation skill'i için yanıt verilmez (bilinmeyen).</summary>
    public string? AddRelationResponse { get; set; }
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
            if (RepromptQueue.Count > 0) return Task.FromResult(RepromptQueue.Dequeue());
            return Task.FromResult(RepromptResponseOverride ?? RepromptResponse);
        }
        if (systemPrompt.Contains("ELİNDE ÇALIŞAN BİR MODEL VAR"))
        {
            Stages.Add("extend");
            return Task.FromResult(ExtendResponse);
        }
        if (systemPrompt.Contains("Skill: Sohbet Yanıtı"))
        {
            Stages.Add("chat");
            return Task.FromResult(ChatReplyResponse);
        }
        if (systemPrompt.Contains("Direktif Yönlendirici"))
        {
            Stages.Add("router");
            return Task.FromResult(RouterResponse);
        }
        if (systemPrompt.Contains("Skill: Alan Ekle"))
        {
            Stages.Add("add-field");
            return Task.FromResult(AddFieldResponse);
        }
        if (AddRelationResponse is not null && systemPrompt.Contains("Skill: İlişki Ekle"))
        {
            Stages.Add("add-relation");
            return Task.FromResult(AddRelationResponse);
        }
                if (systemPrompt.Contains("Skill: Alan Açıklamaları"))
        {
            Stages.Add("describe");
            return Task.FromResult(DescribeResponseOverride ?? DescribeAll(systemPrompt));
        }
                if (systemPrompt.Contains("Business Rule İlişki Analizi"))
        {
            Stages.Add("rule-relations");
            return Task.FromResult(RuleRelationsResponse);
        }
        if (systemPrompt.Contains("VARLIK (entity) adaylarını"))
        {
            Stages.Add("extraction");
            return Task.FromResult(EntitiesResponse);
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

    /// <summary>Null ise FIELDS listesindeki her alana "<ALAN> açıklaması" yazılır.</summary>
    public string? DescribeResponseOverride { get; set; }

    /// <summary>Prompt'taki FIELDS bölümünü okuyup her alan için açıklama döndürür.</summary>
    private static string DescribeAll(string prompt)
    {
        var section = prompt[(prompt.LastIndexOf("### FIELDS", StringComparison.Ordinal) + "### FIELDS".Length)..];
        var items = new List<string>();
        var entity = string.Empty;

        foreach (var raw in section.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("- Tablo: "))
            {
                entity = line["- Tablo: ".Length..].Split(' ')[0];
                continue;
            }

            var trimmed = line.Trim();
            if (!trimmed.StartsWith("- ") || entity.Length == 0) continue;

            var field = trimmed[2..].Split(' ')[0];
            items.Add($$"""{"entity":"{{entity}}","field":"{{field}}","description":"{{field}} açıklaması"}""");
        }

        return $$"""{"descriptions":[{{string.Join(",", items)}}]}""";
    }

    /// <summary>Artımlı geliştirme: yalnızca yeni kuralın gerektirdiği delta.</summary>
    public string ExtendResponse { get; set; } = """
        {
          "entities": [
            {
              "name": "Fatura",
              "description": "Müşteriye kesilen ödeme belgesi",
              "sources": ["BR-3"],
              "confidence": "kesin",
              "attributes": [
                { "name": "ID", "primaryKey": true, "required": true, "unique": true, "sources": ["BR-3"], "confidence": "kesin" },
                { "name": "MUSTERIID", "foreignKey": true, "references": "MUSTERI", "referencesAttribute": "ID", "required": true, "sources": ["BR-3"], "confidence": "kesin" }
              ]
            }
          ],
          "relationships": [],
          "notes": []
        }
        """;

    /// <summary>Sohbet mesajının çevrileceği direktif listesi.</summary>
    public string RouterResponse { get; set; } = """{"directives":[],"unsupported":[]}""";

    /// <summary>Tur uygulandıktan sonra kullanıcıya yazılan yanıt ve öneriler.</summary>
    public string ChatReplyResponse { get; set; } = """
        {
          "reply": "İstediğin değişikliği uyguladım.",
          "suggestions": [
            {
              "text": "Sipariş tablosuna SiparisTarihi alanı eklenebilir",
              "reason": "Siparişin ne zaman oluştuğu hiçbir alanda tutulmuyor",
              "relatedEntities": ["Sipariş"]
            }
          ]
        }
        """;

    public string AddFieldResponse { get; set; } = """
        {
          "entities": [
            {
              "name": "Müşteri",
              "sourceRuleIds": ["BR-1"],
              "attributes": [
                { "name": "ID", "primaryKey": true, "required": true, "sources": ["BR-1"], "confidence": "kesin" },
                { "name": "Ad Soyad", "required": true, "sources": ["BR-1"], "confidence": "kesin" },
                { "name": "Vergi No", "sources": ["CHAT-1"], "confidence": "kesin" }
              ]
            }
          ],
          "relationships": [],
          "notes": []
        }
        """;

    public string RuleRelationsResponse { get; set; } = """
        {
          "relations": [
            { "first": "BR-1", "second": "BR-2", "kind": "detaylandirir",
              "explanation": "BR-2, BR-1'deki müşteriyi sipariş bağlamında detaylandırıyor" }
          ]
        }
        """;

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

    /// <summary>
    /// Normalizasyonu temiz geçen alan listesi. Varsayılan yanıttaki "Ad Soyad"
    /// 1NF-1 gereği beklemede listesine düşer; soru döngüsünü sınayan testler
    /// bunun gürültüsü olmadan çalışmak için bu yanıtı kullanır.
    /// </summary>
    public const string NormalizedAttributes = """
        {
          "entities": [
            {
              "name": "Müşteri",
              "attributes": [
                { "name": "Müşteri No", "primaryKey": true, "required": true, "unique": true, "sourceRuleIds": ["BR-1"], "confidence": 0.9 },
                { "name": "Ad", "required": true, "sourceRuleIds": ["BR-1"], "confidence": 0.85 },
                { "name": "Soyad", "required": true, "sourceRuleIds": ["BR-1"], "confidence": 0.85 }
              ]
            },
            {
              "name": "Sipariş",
              "attributes": [
                { "name": "Sipariş No", "primaryKey": true, "required": true, "unique": true, "sourceRuleIds": ["BR-2"], "confidence": 0.9 },
                { "name": "Tutar", "required": true, "sourceRuleIds": ["BR-2"], "confidence": 0.8 },
                { "name": "Müşteri No", "foreignKey": true, "references": "Müşteri", "referencesAttribute": "MusteriNo", "required": true, "sourceRuleIds": ["BR-2"], "confidence": 0.8 }
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
