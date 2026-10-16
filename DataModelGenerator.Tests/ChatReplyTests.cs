using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// Sohbet yalnızca ne yapıldığını bildirmez; modelin güncel haline bakarak
/// öneride de bulunur. Öneriler tekliftir, modele uygulanmaz.
/// </summary>
public class ChatReplyTests
{
    private static async Task<(ModelPipeline, FakeModelProvider, PipelineContext)> Generated()
    {
        var provider = new FakeModelProvider { AttributesResponse = FakeModelProvider.NormalizedAttributes };
        var pipeline = new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model");
        var context = new PipelineContext
        {
            Input = new ProjectInput
            {
                Rules =
                {
                    new BusinessRule { Id = "BR-1", Text = "Her müşterinin tekil bir müşteri numarası vardır." },
                    new BusinessRule { Id = "BR-2", Text = "Bir müşteri birden çok sipariş verebilir." }
                }
            }
        };

        await pipeline.GenerateAsync(context);
        provider.Stages.Clear();
        return (pipeline, provider, context);
    }

    [Fact]
    public async Task Tur_YanitVeOneriIcerir()
    {
        var (pipeline, _, context) = await Generated();

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşteri tablosunun adını Cari yap");

        Assert.Equal("İstediğin değişikliği uyguladım.", turn.Reply);
        Assert.True(turn.HasSuggestions);

        var suggestion = turn.Suggestions.Single();
        Assert.Contains("SiparisTarihi", suggestion.Text);
        Assert.NotEmpty(suggestion.Reason);
    }

    [Fact]
    public async Task Oneriler_ModeleUygulanmaz()
    {
        var (pipeline, _, context) = await Generated();
        var before = context.Model.FindEntity("Sipariş")!.Attributes.Count;

        await pipeline.SendChatAsync(context, "Müşteri tablosunun adını Cari yap");

        // Öneri "SiparisTarihi eklenebilir" diyor; model kendiliğinden eklememeli.
        var after = context.Model.FindEntity("Sipariş")!;
        Assert.Equal(before, after.Attributes.Count);
        Assert.DoesNotContain(after.Attributes, a => a.TechnicalName.Contains("Tarih"));
    }

    [Fact]
    public async Task OlmayanTabloyaAtifYapanOneri_Gosterilmez()
    {
        var (pipeline, provider, context) = await Generated();
        provider.ChatReplyResponse = """
            {
              "reply": "Tamam.",
              "suggestions": [
                { "text": "Depo tablosu eklenebilir", "reason": "...", "relatedEntities": ["Depo"] }
              ]
            }
            """;

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşteri tablosunun adını Cari yap");

        // Öneri metni kalır ama var olmayan tabloya yapılan atıf düşürülür.
        Assert.Empty(turn.Suggestions.Single().RelatedEntities);
    }

    [Fact]
    public async Task YanitGercekDiffeDayanir_ModelOzetiGonderilir()
    {
        var (pipeline, provider, context) = await Generated();

        await pipeline.SendChatAsync(context, "Müşteri tablosunun adını Cari yap");

        var chatPrompt = provider.Prompts.Single(p => p.Contains("Skill: Sohbet Yanıtı"));

        Assert.Contains("Eklenen tablo", chatPrompt);   // DIFF yerine geçti
        Assert.Contains("CARI", chatPrompt);            // güncel model gönderildi
        Assert.Contains("(uygulanamayan bir şey yok)", chatPrompt);
    }

    [Fact]
    public async Task YanitUretilemezse_TurYineDeGecerlidir()
    {
        var (pipeline, provider, context) = await Generated();
        provider.ChatReplyResponse = "bu JSON değil";

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşteri tablosunun adını Cari yap");

        Assert.Equal(ChatTurnStatus.Applied, turn.Status);
        Assert.Contains("Eklenen tablo", turn.Reply);
        Assert.NotNull(context.Model.FindEntity("Cari"));
    }

    [Fact]
    public async Task DiffBossa_UygulanmayanlarYanitaGirdiOlarakGonderilir()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """
            { "directives": [ { "operation": "remove-field", "entity": "Müşteri", "field": "ID" } ],
              "unsupported": [] }
            """;

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşteri kimliğini kaldır");

        Assert.Equal(ChatTurnStatus.NotApplied, turn.Status);

        var chatPrompt = provider.Prompts.Single(p => p.Contains("Skill: Sohbet Yanıtı"));
        Assert.Contains("(modelde hiçbir değişiklik olmadı)", chatPrompt);
        Assert.Contains("Birincil anahtar", chatPrompt);
    }
}
