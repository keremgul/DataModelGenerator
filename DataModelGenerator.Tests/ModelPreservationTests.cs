using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// Soru cevabı, öneri ve sohbet mevcut modelin üzerine eklenir; modeli
/// baştan değiştiremez. Silme ancak kullanıcının talebi o öğeyi açıkça
/// kaldırmayı istiyorsa uygulanır.
/// </summary>
public class ModelPreservationTests
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

    private static ClarifyingQuestion Answer(PipelineContext context, AmbiguityKind kind, string answer)
    {
        var question = new ClarifyingQuestion
        {
            Id = $"QT-{context.Questions.Count + 1}",
            Kind = kind,
            Target = "Müşteri",
            Text = "Müşteri ayrı bir tablo mu?",
            Answer = answer
        };
        context.Questions.Add(question);
        return question;
    }

    [Fact]
    public async Task VarlikCevabi_YanittaDonmeyenTabloyuSilmez()
    {
        var (pipeline, provider, context) = await Generated();

        // Yeniden çıkarım yalnızca tek tablo döndürüyor.
        provider.EntitiesResponse = """
            { "entities": [ { "name": "Müşteri", "sources": ["BR-1"], "confidence": "kesin" } ] }
            """;

        await pipeline.ApplyAnswersAsync(context,
            [Answer(context, AmbiguityKind.EntityExistence, "Evet, ayrı tablo")]);

        Assert.Contains("extraction", provider.Stages);
        Assert.NotNull(context.Model.FindEntity("Sipariş"));
        Assert.Contains(context.Model.Relationships, r => r.ToEntity == "SIPARIS");
    }

    [Fact]
    public async Task IliskiCevabi_DonmeyenIliskiyiSilmezKardinaliteyiGunceller()
    {
        var (pipeline, provider, context) = await Generated();
        var before = context.Model.Relationships.Count;

        provider.RelationshipsResponse = """
            { "relationships": [
                { "name": "verir", "from": "Müşteri", "to": "Sipariş", "cardinality": "1-1",
                  "sources": ["BR-2"], "confidence": "kesin" } ] }
            """;

        await pipeline.ApplyAnswersAsync(context,
            [Answer(context, AmbiguityKind.Cardinality, "Her müşterinin tek siparişi olur")]);

        Assert.True(context.Model.Relationships.Count >= before);
        Assert.Contains(context.Model.Relationships, r => r.Cardinality == Cardinality.OneToOne);
    }

    [Fact]
    public async Task AlanCevabi_YanittaDonmeyenAlaniSilmez()
    {
        var (pipeline, provider, context) = await Generated();

        // Alan adımı yalnızca yeni bir alan döndürüyor; Ad/Soyad yanıtta yok.
        provider.AttributesResponse = """
            { "entities": [ { "name": "Müşteri", "attributes": [
                { "name": "Telefon", "required": false, "sources": ["BR-1"], "confidence": "kesin" } ] } ] }
            """;

        await pipeline.ApplyAnswersAsync(context,
            [Answer(context, AmbiguityKind.Requiredness, "Telefon da tutulsun")]);

        var musteri = context.Model.FindEntity("Müşteri")!;
        Assert.Contains(musteri.Attributes, a => a.TechnicalName == "AD");
        Assert.Contains(musteri.Attributes, a => a.TechnicalName == "SOYAD");
        Assert.Contains(musteri.Attributes, a => a.TechnicalName == "TELEFON");
    }

    [Fact]
    public async Task OneriUygulama_IstenmeyenTabloSilmesiniReddeder()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RepromptResponseOverride = """
            { "entities": [], "removedEntities": ["Sipariş"],
              "removedFields": [ { "entity": "Müşteri", "field": "Ad" } ], "notes": [] }
            """;

        context.Suggestions.Add(new Suggestion { Id = "S99", Text = "Müşteriye e-posta alanı eklenebilir" });
        context.Suggestions[^1].Decision = SuggestionDecision.Accepted;

        var package = await pipeline.ApplySuggestionsAsync(context, [context.Suggestions[^1]]);

        Assert.NotNull(package.Model.FindEntity("Sipariş"));
        Assert.Contains(package.Model.FindEntity("Müşteri")!.Attributes, a => a.TechnicalName == "AD");
        Assert.Contains(package.RepromptNotes, n => n.Contains("korunan"));
    }

    [Fact]
    public async Task Sohbet_AcikcaIstenenSilmeyiUygular()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """{"directives":[{"operation":"general","explanation":"Telefon yerine soyad kalksın"}],"unsupported":[]}""";
        provider.RepromptResponseOverride = """
            { "entities": [], "removedFields": [ { "entity": "Müşteri", "field": "Soyad" } ], "notes": [] }
            """;

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşteri tablosundaki soyad alanı gereksiz, kaldıralım");

        Assert.Equal(ChatTurnStatus.Applied, turn.Status);
        Assert.DoesNotContain(context.Model.FindEntity("Müşteri")!.Attributes, a => a.TechnicalName == "SOYAD");
    }

    [Fact]
    public async Task Sohbet_IstenmeyenSilmeyiReddederIstenenEklemeyiYapar()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """{"directives":[{"operation":"general","explanation":"E-posta ekle"}],"unsupported":[]}""";
        provider.RepromptResponseOverride = """
            { "entities": [ { "name": "Müşteri", "attributes": [
                { "name": "EPosta", "sources": ["CHAT-1"], "confidence": "kesin" } ] } ],
              "removedEntities": ["Sipariş"], "notes": [] }
            """;

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşteriye e-posta adresi ekle");

        Assert.Equal(ChatTurnStatus.Applied, turn.Status);
        Assert.NotNull(context.Model.FindEntity("Sipariş"));
        Assert.Contains(context.Model.FindEntity("Müşteri")!.Attributes, a => a.TechnicalName == "EPOSTA");
    }
}
