using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// Sohbetin modelin güncel halini bilmesi: kendi eklediği alanı sonraki turda
/// bulabilmeli ve kaldırabilmeli.
/// </summary>
public class ChatModelAwarenessTests
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
    public async Task SohbetteEklenenAlan_SonrakiTurdaKaldirilabilir()
    {
        var (pipeline, provider, context) = await Generated();

        // 1. tur: alan ekle.
        provider.RouterResponse = """
            { "directives": [ { "operation": "add-field", "entity": "Müşteri", "newName": "Vergi No" } ],
              "unsupported": [] }
            """;
        provider.AddFieldResponse = """
            {
              "entities": [
                {
                  "name": "Müşteri",
                  "sourceRuleIds": ["BR-1"],
                  "attributes": [
                    { "name": "Müşteri No", "primaryKey": true, "required": true, "unique": true, "sources": ["BR-1"], "confidence": "kesin" },
                    { "name": "Ad", "required": true, "sources": ["BR-1"], "confidence": "kesin" },
                    { "name": "Soyad", "required": true, "sources": ["BR-1"], "confidence": "kesin" },
                    { "name": "Vergi No", "sources": ["CHAT-1"], "confidence": "kesin" }
                  ]
                }
              ],
              "relationships": [], "notes": []
            }
            """;

        var (_, added) = await pipeline.SendChatAsync(context, "Müşteriye vergi numarası ekle");

        Assert.Equal(ChatTurnStatus.Applied, added.Status);
        Assert.Contains(context.Model.FindEntity("Müşteri")!.Attributes, a => a.TechnicalName == "VERGINO");

        // 2. tur: aynı alanı kaldır — kalıp eşleşmesiyle, LLM'e gitmeden.
        provider.Stages.Clear();
        var (_, removed) = await pipeline.SendChatAsync(context, "Müşteri tablosundan VergiNo alanını kaldır");

        Assert.Equal(ChatTurnStatus.Applied, removed.Status);
        Assert.DoesNotContain(context.Model.FindEntity("Müşteri")!.Attributes, a => a.TechnicalName == "VERGINO");
    }

    [Fact]
    public async Task EklenenAlan_GorunenAdiylaDaKaldirilabilir()
    {
        var (pipeline, provider, context) = await Generated();
        var musteri = context.Model.FindEntity("Müşteri")!;
        musteri.Attributes.Add(new EntityAttribute
        {
            Name = "Vergi No", TechnicalName = "VERGINO", SourceRuleIds = { "BR-1" }
        });

        provider.RouterResponse = """
            { "directives": [ { "operation": "remove-field", "entity": "Müşteri", "field": "Vergi No" } ],
              "unsupported": [] }
            """;

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşteriden vergi numarasını çıkar");

        Assert.Equal(ChatTurnStatus.Applied, turn.Status);
        Assert.DoesNotContain(context.Model.FindEntity("Müşteri")!.Attributes, a => a.TechnicalName == "VERGINO");
    }

    [Fact]
    public async Task ModelYalnizcaYeniAlaniDonduğunde_MevcutAlanlarKorunur()
    {
        var (pipeline, provider, context) = await Generated();
        var before = context.Model.FindEntity("Müşteri")!.Attributes
            .Select(a => a.TechnicalName).ToList();

        provider.RouterResponse = """
            { "directives": [ { "operation": "add-field", "entity": "Müşteri", "newName": "Vergi No" } ],
              "unsupported": [] }
            """;

        // Gerçek modeller çoğu zaman yalnızca değişen alanı döndürür.
        provider.AddFieldResponse = """
            {
              "entities": [
                {
                  "name": "Müşteri",
                  "attributes": [
                    { "name": "Vergi No", "sources": ["CHAT-1"], "confidence": "kesin" }
                  ]
                }
              ],
              "relationships": [], "notes": []
            }
            """;

        await pipeline.SendChatAsync(context, "Müşteriye vergi numarası ekle");

        var after = context.Model.FindEntity("Müşteri")!.Attributes.Select(a => a.TechnicalName).ToList();

        Assert.All(before, name => Assert.Contains(name, after));
        Assert.Contains("VERGINO", after);
    }

    [Fact]
    public async Task AlanSilme_YalnizcaAcikcaBildirildigindeYapilir()
    {
        var (pipeline, provider, context) = await Generated();

        provider.RouterResponse = """{"directives":[],"unsupported":[]}""";
        provider.RepromptResponseOverride = """
            {
              "entities": [],
              "relationships": [],
              "removedFields": [{ "entity": "Müşteri", "field": "Soyad" }],
              "notes": []
            }
            """;

        var (package, turn) = await pipeline.SendChatAsync(context, "Soyad alanını kaldır");

        Assert.Equal(ChatTurnStatus.Applied, turn.Status);
        Assert.DoesNotContain(context.Model.FindEntity("Müşteri")!.Attributes, a => a.TechnicalName == "SOYAD");
        Assert.Contains(context.Model.FindEntity("Müşteri")!.Attributes, a => a.TechnicalName == "AD");
        Assert.Contains(package.RepromptNotes, n => n.Contains("Kaldırılan alanlar"));
    }

    [Fact]
    public async Task BirincilAnahtar_AcikcaBildirilseDeSilinmez()
    {
        var (pipeline, provider, context) = await Generated();

        provider.RouterResponse = """{"directives":[],"unsupported":[]}""";
        provider.RepromptResponseOverride = """
            {
              "entities": [], "relationships": [],
              "removedFields": [{ "entity": "Müşteri", "field": "ID" }],
              "notes": []
            }
            """;

        await pipeline.SendChatAsync(context, "Müşteri kimliğini kaldır");

        Assert.Contains(context.Model.FindEntity("Müşteri")!.Attributes, a => a.IsPrimaryKey);
    }

    [Fact]
    public async Task ModelOzeti_AlanlariGorunenVeTeknikAdiylaBildirir()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """{"directives":[],"unsupported":[]}""";
        provider.RepromptResponseOverride = """{"entities":[],"relationships":[],"notes":[]}""";

        await pipeline.SendChatAsync(context, "Model hakkında ne düşünüyorsun?");

        var routerPrompt = provider.Prompts.Single(p => p.Contains("Direktif Yönlendirici"));

        // Router, modelin güncel alanlarını hem teknik hem görünen adıyla görmeli.
        Assert.Contains("MUSTERI", routerPrompt);
        Assert.Contains("MUSTERINO", routerPrompt);
        Assert.Contains("Müşteri No", routerPrompt);
    }
}
