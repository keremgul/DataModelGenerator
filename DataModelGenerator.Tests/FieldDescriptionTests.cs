using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Rules;
using DataModelGenerator.Core.Serialization;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// Her alanın yalın Türkçe bir açıklaması olur ve ER diyagramında gösterilir.
/// </summary>
public class FieldDescriptionTests
{
    private static ProjectInput SampleInput() => new()
    {
        Rules =
        {
            new BusinessRule { Id = "BR-1", Text = "Her müşterinin tekil bir müşteri numarası vardır." },
            new BusinessRule { Id = "BR-2", Text = "Bir müşteri birden çok sipariş verebilir." }
        }
    };

    private static async Task<(ModelPipeline, FakeModelProvider, PipelineContext, ModelPackage)> Generated()
    {
        var provider = new FakeModelProvider { AttributesResponse = FakeModelProvider.NormalizedAttributes };
        var pipeline = new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model");
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);
        provider.Stages.Clear();
        return (pipeline, provider, context, package);
    }

    [Fact]
    public async Task Uretim_HerAlanaAciklamaYazar()
    {
        var (_, _, context, _) = await Generated();

        Assert.All(context.Model.Entities.SelectMany(e => e.Attributes),
            a => Assert.False(string.IsNullOrWhiteSpace(a.Description), $"{a.TechnicalName} açıklamasız"));
    }

    [Fact]
    public async Task AnahtarVeStandartAlanlar_LLMeSorulmadanAciklanir()
    {
        var (_, _, context, _) = await Generated();
        var siparis = context.Model.FindEntity("Sipariş")!;

        Assert.Contains("benzersiz", siparis.PrimaryKey!.Description);
        Assert.Contains("müşteri", siparis.Attributes.First(a => a.IsForeignKey).Description);
        Assert.Equal("Kaydın oluşturulma tarihi",
            siparis.Attributes.First(a => a.TechnicalName == "GIRISTARIHI").Description);
    }

    [Fact]
    public async Task AciklamaTuru_YalnizcaEksikAlanlariGonderir()
    {
        var (_, provider, _, _) = await Generated();
        var prompt = provider.Prompts.Last(p => p.Contains("Skill: Alan Açıklamaları"));
        var fields = prompt[prompt.LastIndexOf("### FIELDS", StringComparison.Ordinal)..];

        Assert.Contains("TUTAR", fields);
        Assert.DoesNotContain("GIRISTARIHI", fields);
        Assert.DoesNotContain("    - ID", fields);
    }

    [Fact]
    public async Task LLMinVerdigiAciklama_Korunur()
    {
        var provider = new FakeModelProvider
        {
            AttributesResponse = """
                { "entities": [
                  { "name": "Müşteri", "attributes": [
                    { "name": "ID", "primaryKey": true, "sources": ["BR-1"], "confidence": "kesin" },
                    { "name": "Müşteri No", "unique": true, "description": "Müşteriye verilen numara", "sources": ["BR-1"], "confidence": "kesin" } ] },
                  { "name": "Sipariş", "attributes": [
                    { "name": "ID", "primaryKey": true, "sources": ["BR-2"], "confidence": "kesin" } ] } ] }
                """
        };
        var pipeline = new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model");
        var context = new PipelineContext { Input = SampleInput() };

        await pipeline.GenerateAsync(context);

        Assert.Equal("Müşteriye verilen numara",
            context.Model.FindEntity("Müşteri")!.Attributes.First(a => a.TechnicalName == "MUSTERINO").Description);
    }

    [Fact]
    public async Task ErDiyagrami_AlanAciklamasiniGosterir()
    {
        var (_, _, _, package) = await Generated();

        Assert.Contains("alan TUTAR \"TUTAR açıklaması\"", package.MermaidCode);
        Assert.Contains("Kaydın oluşturulma tarihi", package.MermaidCode);
        Assert.DoesNotContain("BR-", package.MermaidCode);
    }

    [Fact]
    public void Mermaid_YabanciAnahtarHedefiniAciklamaylaBirlikteYazar()
    {
        var model = new DataModel
        {
            Entities =
            {
                new ModelEntity { Name = "MUSTERI", TechnicalName = "MUSTERI",
                    Attributes = { new EntityAttribute { Name = "ID", TechnicalName = "ID", IsPrimaryKey = true } } },
                new ModelEntity { Name = "SIPARIS", TechnicalName = "SIPARIS",
                    Attributes = { new EntityAttribute { Name = "MUSTERIID", TechnicalName = "MUSTERIID", IsForeignKey = true,
                        ReferencesEntity = "MUSTERI", Description = "Siparişi veren müşteri" } } }
            }
        };

        var code = MermaidSerializer.Serialize(model);

        Assert.Contains("alan MUSTERIID FK \"Siparişi veren müşteri · →MUSTERI\"", code);
    }

    [Fact]
    public async Task Sohbet_AlanAciklamasiniLLMsizDegistirir()
    {
        var (pipeline, provider, context, _) = await Generated();

        var (_, turn) = await pipeline.SendChatAsync(context,
            "Sipariş tablosundaki Tutar alanının açıklaması 'Siparişin toplam ödenecek tutarı' olsun");

        Assert.Equal(ChatTurnStatus.Applied, turn.Status);
        Assert.DoesNotContain("router", provider.Stages);
        Assert.Equal("Siparişin toplam ödenecek tutarı",
            context.Model.FindEntity("Sipariş")!.Attributes.First(a => a.TechnicalName == "TUTAR").Description);
        Assert.Contains(turn.Changes.ModifiedAttributes, m => m.Contains("TUTAR") && m.Contains("açıklama"));
    }

    [Fact]
    public async Task KullanicininYazdigiAciklama_SonrakiModelYanitindaEzilmez()
    {
        var (pipeline, provider, context, _) = await Generated();
        await pipeline.SendChatAsync(context,
            "Sipariş tablosundaki Tutar alanının açıklaması 'Ödenecek toplam' olsun");

        provider.RouterResponse = """{"directives":[{"operation":"general","explanation":"İndirim ekle"}],"unsupported":[]}""";
        provider.RepromptResponseOverride = """
            { "entities": [ { "name": "Sipariş", "attributes": [
                { "name": "Tutar", "description": "Modelin yazdığı başka açıklama", "sources": ["BR-2"], "confidence": "kesin" },
                { "name": "İndirim", "sources": ["CHAT-2"], "confidence": "kesin" } ] } ] }
            """;

        await pipeline.SendChatAsync(context, "Siparişe indirim alanı ekle");

        Assert.Equal("Ödenecek toplam",
            context.Model.FindEntity("Sipariş")!.Attributes.First(a => a.TechnicalName == "TUTAR").Description);
    }

    [Fact]
    public async Task AciklamasizEskiModel_BosTurUygulandiGorunmez()
    {
        var (pipeline, provider, context, _) = await Generated();
        foreach (var attribute in context.Model.Entities.SelectMany(e => e.Attributes))
            attribute.Description = string.Empty;

        provider.RouterResponse = """{"directives":[{"operation":"general","explanation":"?"}],"unsupported":[]}""";
        provider.RepromptResponseOverride = """{"entities":[],"relationships":[],"notes":[]}""";

        var (_, turn) = await pipeline.SendChatAsync(context, "Bir şey yap");

        Assert.Equal(ChatTurnStatus.NotApplied, turn.Status);
    }

    [Fact]
    public void VarsayilanAciklamalar_KullanicininAciklamasininUzerineYazmaz()
    {
        var model = new DataModel
        {
            Entities =
            {
                new ModelEntity { Name = "Müşteri", TechnicalName = "MUSTERI",
                    Attributes = { new EntityAttribute { Name = "ID", TechnicalName = "ID", IsPrimaryKey = true,
                        Description = "Kullanıcının yazdığı" } } }
            }
        };

        FieldDescriptions.ApplyDefaults(model);

        Assert.Equal("Kullanıcının yazdığı", model.Entities[0].Attributes[0].Description);
    }
}
