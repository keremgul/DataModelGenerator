using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// Sohbet, kullanıcının istediği değişikliği yapmalı: yerinde değişiklikler
/// (zorunluluk, kardinalite) "değişiklik yok" sayılmamalı, model ne yapılacağını
/// açıklayıp uygulamadığında bir kez daha istenmeli, soru sorulduğunda model
/// değiştirilmeden yanıt verilmeli.
/// </summary>
public class ChatCapabilityTests
{
    private static ProjectInput SampleInput() => new()
    {
        Rules =
        {
            new BusinessRule { Id = "BR-1", Text = "Her müşterinin tekil bir müşteri numarası vardır." },
            new BusinessRule { Id = "BR-2", Text = "Bir müşteri birden çok sipariş verebilir." }
        }
    };

    private static async Task<(ModelPipeline, FakeModelProvider, PipelineContext)> Generated()
    {
        var provider = new FakeModelProvider { AttributesResponse = FakeModelProvider.NormalizedAttributes };
        var pipeline = new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model");
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);
        provider.Stages.Clear();
        return (pipeline, provider, context);
    }

    [Fact]
    public async Task KardinaliteDegisikligi_UygulandiSayilir()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """
            { "directives": [ { "operation": "change-cardinality", "entity": "MUSTERI",
                                "targetEntity": "SIPARIS", "cardinality": "1-1" } ], "unsupported": [] }
            """;

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşteri ile sipariş bire bir olsun");

        Assert.Equal(ChatTurnStatus.Applied, turn.Status);
        Assert.Contains(turn.Changes.ModifiedRelationships, m => m.Contains("kardinalite"));
        Assert.DoesNotContain("reprompt", provider.Stages);
    }

    [Fact]
    public async Task AlanZorunlulugu_LLMsizDegistirilir()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """
            { "directives": [ { "operation": "update-field", "entity": "Müşteri", "field": "Soyad",
                                "required": false } ], "unsupported": [] }
            """;

        var (_, turn) = await pipeline.SendChatAsync(context, "Soyad zorunlu olmasın");

        Assert.Equal(ChatTurnStatus.Applied, turn.Status);
        Assert.False(context.Model.FindEntity("Müşteri")!.Attributes.First(a => a.TechnicalName == "SOYAD").IsRequired);
        Assert.Contains(turn.Changes.ModifiedAttributes, m => m.Contains("opsiyonel oldu"));
    }

    [Fact]
    public async Task ModelYalnizcaNotYazarsa_TalepBirKezDahaIstenirVeUygulanir()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """{"directives":[{"operation":"general","explanation":"Siparişe teslim tarihi eklenmeli"}],"unsupported":[]}""";

        // 1. deneme: model ne yapılacağını anlatıyor ama JSON'da uygulamıyor.
        provider.RepromptQueue.Enqueue("""
            { "entities": [], "notes": ["Sipariş tablosuna TeslimTarihi alanı eklenmeli"] }
            """);
        // 2. deneme: uyguluyor.
        provider.RepromptQueue.Enqueue("""
            { "entities": [ { "name": "Sipariş", "attributes": [
                { "name": "Teslim Tarihi", "sources": ["CHAT-1"], "confidence": "kesin" } ] } ], "notes": [] }
            """);

        var (_, turn) = await pipeline.SendChatAsync(context, "Siparişin ne zaman teslim edileceğini de tutalım");

        Assert.Equal(ChatTurnStatus.Applied, turn.Status);
        Assert.True(turn.Retried);
        Assert.Empty(turn.NotApplied);
        Assert.Equal(2, provider.Stages.Count(s => s == "reprompt"));
        Assert.Contains(context.Model.FindEntity("Sipariş")!.Attributes, a => a.TechnicalName == "TESLIMTARIHI");

        // İkinci istek ilk denemenin notunu geri verir.
        var retryPrompt = provider.Prompts.Last(p => p.Contains("DEĞİŞTİRİLECEK KAPSAM"));
        Assert.Contains("TeslimTarihi alanı eklenmeli", retryPrompt);
    }

    [Fact]
    public async Task RouterinTanimadigiKisim_AnlasilmadiDemedenModeleIletilir()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """{"directives":[],"unsupported":["Siparişe indirim oranı"]}""";

        var (_, turn) = await pipeline.SendChatAsync(context, "Siparişe indirim oranı ekle");

        Assert.Contains("reprompt", provider.Stages);
        Assert.Equal(ChatTurnStatus.Applied, turn.Status);
        Assert.Empty(turn.Unsupported);
    }

    [Fact]
    public async Task YeniTablo_EntityAlaninaYazilsaDaEklenir()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """
            { "directives": [ { "operation": "add-entity", "entity": "Fatura" } ], "unsupported": [] }
            """;
        provider.RepromptResponseOverride = """
            { "entities": [ { "name": "Fatura", "sources": ["CHAT-1"], "confidence": "kesin", "attributes": [
                { "name": "ID", "primaryKey": true, "sources": ["CHAT-1"], "confidence": "kesin" },
                { "name": "MUSTERIID", "foreignKey": true, "references": "MUSTERI", "sources": ["CHAT-1"], "confidence": "kesin" } ] } ] }
            """;

        var (_, turn) = await pipeline.SendChatAsync(context, "Fatura tablosu ekle");

        Assert.Single(turn.Directives);
        Assert.Equal("Fatura", turn.Directives[0].NewName);
        Assert.NotNull(context.Model.FindEntity("Fatura"));
    }

    [Fact]
    public async Task IliskiSkillYaniti_FromEntityAnahtarlariylaOkunur()
    {
        var (pipeline, provider, context) = await Generated();
        context.Model.Entities.Add(new ModelEntity
        {
            Name = "Adres", TechnicalName = "ADRES", SourceRuleIds = { "BR-1" },
            Attributes = { new EntityAttribute { Name = "ID", TechnicalName = "ID", IsPrimaryKey = true } }
        });
        provider.RouterResponse = """
            { "directives": [ { "operation": "add-relation", "entity": "MUSTERI", "targetEntity": "ADRES",
                                "cardinality": "1-N" } ], "unsupported": [] }
            """;
        // Skill şeması "fromEntity"/"toEntity" anahtarlarını kullanıyor.
        provider.AddRelationResponse = """
            { "entities": [], "relationships": [
                { "fromEntity": "MUSTERI", "toEntity": "ADRES", "cardinality": "1-N",
                  "sources": ["CHAT-1"], "confidence": "kesin" } ] }
            """;

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşterinin birden çok adresi olsun");

        Assert.Contains("add-relation", provider.Stages);
        Assert.DoesNotContain("reprompt", provider.Stages);
        Assert.Contains(context.Model.Relationships, r => r.FromEntity == "MUSTERI" && r.ToEntity == "ADRES");
        Assert.Contains(context.Model.FindEntity("ADRES")!.Attributes, a => a.IsForeignKey && a.ReferencesEntity == "MUSTERI");
    }

    [Fact]
    public async Task Soru_ModeliDegistirmedenYanitlanir()
    {
        var (pipeline, provider, context) = await Generated();
        var rulesBefore = context.Input.Rules.Count;
        var modelBefore = Core.Serialization.ModelJson.Serialize(context.Model);
        provider.RouterResponse = """
            { "directives": [ { "operation": "question", "explanation": "Sipariş tablosu ne tutuyor?" } ], "unsupported": [] }
            """;

        var (_, turn) = await pipeline.SendChatAsync(context, "Sipariş tablosu ne işe yarıyor?");

        Assert.Equal(ChatTurnStatus.Answered, turn.Status);
        Assert.Equal(rulesBefore, context.Input.Rules.Count);
        Assert.Equal(modelBefore, Core.Serialization.ModelJson.Serialize(context.Model));
        Assert.DoesNotContain("reprompt", provider.Stages);

        var chatPrompt = provider.Prompts.Last(p => p.Contains("Skill: Sohbet Yanıtı"));
        Assert.Contains("kullanıcı soru soruyor", chatPrompt);
    }

    [Fact]
    public async Task TabloDogalAdiylaBulunur()
    {
        var (_, _, context) = await Generated();

        Assert.NotNull(context.Model.FindEntity("Müşteri"));
        Assert.NotNull(context.Model.FindEntity("müşteri"));
        Assert.NotNull(context.Model.FindEntity("MUSTERI"));
    }
}
