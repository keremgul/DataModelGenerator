using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// Sohbet adımı: mesaj yapılandırılmış direktiflere çevrilir, basit işlemler
/// LLM'siz uygulanır ve sonuç modelin gerçek diff'iyle doğrulanır.
/// </summary>
public class ChatDirectiveTests
{
    private static ProjectInput SampleInput() => new()
    {
        Rules =
        {
            new BusinessRule { Id = "BR-1", Text = "Her müşterinin tekil bir müşteri numarası vardır." },
            new BusinessRule { Id = "BR-2", Text = "Bir müşteri birden çok sipariş verebilir." }
        }
    };

    private static (ModelPipeline Pipeline, FakeModelProvider Provider) Build()
    {
        var provider = new FakeModelProvider();
        return (new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model"), provider);
    }

    private static async Task<(ModelPipeline, FakeModelProvider, PipelineContext)> Generated()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);
        provider.Stages.Clear();
        return (pipeline, provider, context);
    }

    // -------------------------------------------------- LLM'siz kalıp eşleşmesi

    [Fact]
    public async Task TabloAdiDegistirme_ModelCagrisiYapmadanUygulanir()
    {
        var (pipeline, provider, context) = await Generated();

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşteri tablosunun adını Cari yap");

        Assert.DoesNotContain("router", provider.Stages);
        Assert.DoesNotContain("reprompt", provider.Stages);
        Assert.Equal(ChatTurnStatus.Applied, turn.Status);
        Assert.NotNull(context.Model.FindEntity("Cari"));
    }

    [Fact]
    public async Task AlanAdiDegisikligi_TumPipelineiBastanCalistirmaz()
    {
        var (pipeline, provider, context) = await Generated();

        await pipeline.SendChatAsync(context, "Sipariş tablosunun Tutar alanının adını NetTutar yap");

        // Model üretim adımlarının hiçbiri tekrar çalışmamalı.
        Assert.DoesNotContain("rule-relations", provider.Stages);
        Assert.DoesNotContain("extraction", provider.Stages);
        Assert.DoesNotContain("relationships", provider.Stages);
        Assert.DoesNotContain("attributes", provider.Stages);
        Assert.DoesNotContain("gaps", provider.Stages);
        Assert.DoesNotContain("questions", provider.Stages);

        // Geriye yalnızca özet/öneri tazelemesi kalır.
        Assert.Equal(["summary"], provider.Stages);
        Assert.Contains(context.Model.FindEntity("Sipariş")!.Attributes,
            a => a.TechnicalName == "NetTutar");
    }

    [Fact]
    public async Task AlanKaldirma_KalipEslesmesiyleUygulanir()
    {
        var (pipeline, provider, context) = await Generated();
        var musteri = context.Model.FindEntity("Müşteri")!;
        musteri.Attributes.Add(new EntityAttribute
        {
            Name = "FaksNo",
            TechnicalName = "FaksNo",
            SourceRuleIds = { "BR-1" }
        });

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşteri tablosundan FaksNo alanını kaldır");

        Assert.DoesNotContain("router", provider.Stages);
        Assert.Equal(ChatTurnStatus.Applied, turn.Status);
        Assert.DoesNotContain(context.Model.FindEntity("Müşteri")!.Attributes, a => a.TechnicalName == "FaksNo");
    }

    [Fact]
    public async Task Direktif_KaynakOlarakChatKimliginiEkler()
    {
        var (pipeline, _, context) = await Generated();

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşteri tablosunun adını Cari yap");

        Assert.Equal("CHAT-1", turn.RuleId);
        Assert.Contains(context.Input.Rules, r => r.Id == "CHAT-1" && r.Origin == RuleOrigin.Chat);
        Assert.Contains("CHAT-1", context.Model.FindEntity("Cari")!.SourceRuleIds);
    }

    [Fact]
    public async Task BirincilAnahtar_KaldirilmazVeGerekceBildirilir()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """
            { "directives": [ { "operation": "remove-field", "entity": "Müşteri", "field": "ID" } ],
              "unsupported": [] }
            """;

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşteri kimlik alanını at");

        Assert.Equal(ChatTurnStatus.NotApplied, turn.Status);
        Assert.Contains(turn.NotApplied, text => text.Contains("Birincil anahtar"));
        Assert.Contains(context.Model.FindEntity("Müşteri")!.Attributes, a => a.IsPrimaryKey);
    }

    // -------------------------------------------------- diff ile doğrulama

    [Fact]
    public async Task ModelHicbirSeyDegistirmezse_UygulanmadiRaporlanir()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """{"directives":[{"operation":"general","explanation":"Genel talep"}],"unsupported":[]}""";
        provider.RepromptResponseOverride = """{"entities":[],"relationships":[],"notes":[]}""";

        var (_, turn) = await pipeline.SendChatAsync(context, "Modeli bir gözden geçir");

        Assert.Equal(ChatTurnStatus.NotApplied, turn.Status);
        Assert.True(turn.Changes.IsEmpty);
        Assert.NotEmpty(turn.NotApplied);
    }

    [Fact]
    public async Task BirDirektifBasarisizsa_KismenUygulandiRaporlanir()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """
            {
              "directives": [
                { "operation": "rename-entity", "entity": "Müşteri", "newName": "Cari" },
                { "operation": "remove-field", "entity": "Sipariş", "field": "OlmayanAlan" }
              ],
              "unsupported": []
            }
            """;

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşteriyi Cari yap ve siparişten olmayan alanı sil");

        Assert.Equal(ChatTurnStatus.PartiallyApplied, turn.Status);
        Assert.Single(turn.NotApplied);
        Assert.NotNull(context.Model.FindEntity("Cari"));
    }

    [Fact]
    public async Task OlmayanTabloyaAtifYapanDirektif_Uygulanmaz()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """
            { "directives": [ { "operation": "remove-entity", "entity": "Depo" } ], "unsupported": [] }
            """;
        provider.RepromptResponseOverride = """{"entities":[],"relationships":[],"notes":[]}""";

        var (_, turn) = await pipeline.SendChatAsync(context, "Depo tablosunu kaldır");

        Assert.Empty(turn.Directives);
        Assert.Contains(turn.NotApplied, text => text.Contains("Depo"));
    }

    // -------------------------------------------------- skill yönlendirmesi

    [Fact]
    public async Task AlanEkleme_KendiSkillDosyasiylaCalisir()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """
            { "directives": [ { "operation": "add-field", "entity": "Müşteri", "newName": "Vergi No" } ],
              "unsupported": [] }
            """;

        var (_, turn) = await pipeline.SendChatAsync(context, "Müşteriye vergi numarası ekle");

        Assert.Contains("add-field", provider.Stages);
        Assert.DoesNotContain("reprompt", provider.Stages);
        Assert.Equal(ChatTurnStatus.Applied, turn.Status);
        Assert.Contains(context.Model.FindEntity("Müşteri")!.Attributes, a => a.TechnicalName == "VergiNo");
    }

    [Fact]
    public async Task SkillDosyasiOlmayanIslem_GenelRepromptYolunaDuser()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RouterResponse = """{"directives":[{"operation":"general","explanation":"Genel talep"}],"unsupported":[]}""";

        await pipeline.SendChatAsync(context, "Tüm tablolara oluşturma tarihi ekle");

        Assert.Contains("reprompt", provider.Stages);
    }

    [Fact]
    public async Task SohbetGecmisi_ContextteBirikir()
    {
        var (pipeline, provider, context) = await Generated();
        provider.RepromptResponseOverride = """{"entities":[],"relationships":[],"notes":[]}""";

        await pipeline.SendChatAsync(context, "Müşteri tablosunun adını Cari yap");
        await pipeline.SendChatAsync(context, "Cari tablosunun adını Musteri yap");

        Assert.Equal(2, context.ChatTurns.Count);
        Assert.Equal(["CHAT-1", "CHAT-2"], context.ChatTurns.Select(t => t.RuleId));
    }
}
