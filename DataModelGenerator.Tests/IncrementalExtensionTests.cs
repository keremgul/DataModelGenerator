using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// Artımlı geliştirme: yeni kurallar mevcut modelin üzerine eklenir, model
/// sıfırdan üretilmez. Önceki tablo, alan ve ilişkiler korunur.
/// </summary>
public class IncrementalExtensionTests
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
    public async Task YeniKural_ModeliSifirdanUretmez()
    {
        var (pipeline, provider, context) = await Generated();

        await pipeline.ExtendAsync(context, ["Her sipariş için fatura kesilir."]);

        // Varlık ve alan çıkarımı baştan çalışmamalı.
        Assert.DoesNotContain("extraction", provider.Stages);
        Assert.DoesNotContain("relationships", provider.Stages);
        Assert.DoesNotContain("attributes", provider.Stages);
        Assert.Contains("extend", provider.Stages);
    }

    [Fact]
    public async Task OncekiTabloVeAlanlar_Korunur()
    {
        var (pipeline, _, context) = await Generated();

        var beforeEntities = context.Model.Entities.Select(e => e.TechnicalName).ToList();
        var beforeFields = context.Model.FindEntity("MUSTERI")!.Attributes
            .Select(a => a.TechnicalName).ToList();

        await pipeline.ExtendAsync(context, ["Her sipariş için fatura kesilir."]);

        foreach (var name in beforeEntities)
            Assert.NotNull(context.Model.FindEntity(name));

        var after = context.Model.FindEntity("MUSTERI")!.Attributes.Select(a => a.TechnicalName).ToList();
        Assert.All(beforeFields, f => Assert.Contains(f, after));
    }

    [Fact]
    public async Task YeniKuralinGerektirdigiTablo_Eklenir()
    {
        var (pipeline, _, context) = await Generated();

        await pipeline.ExtendAsync(context, ["Her sipariş için fatura kesilir."]);

        var fatura = context.Model.FindEntity("FATURA");
        Assert.NotNull(fatura);
        Assert.Contains(fatura!.Attributes, a => a.TechnicalName == "MUSTERIID" && a.IsForeignKey);
    }

    [Fact]
    public async Task YeniKurallar_KuralListesineEklenir()
    {
        var (pipeline, _, context) = await Generated();

        var package = await pipeline.ExtendAsync(context,
            ["Her sipariş için fatura kesilir.", "Fatura iptal edilebilir."]);

        Assert.Equal(2, package.AddedRuleIds.Count);
        Assert.Equal(4, context.Input.Rules.Count);
        Assert.Contains(context.Input.Rules, r => r.Text == "Fatura iptal edilebilir.");
    }

    [Fact]
    public async Task Fark_KullaniciyaOzetOlarakDoner()
    {
        var (pipeline, _, context) = await Generated();

        var package = await pipeline.ExtendAsync(context, ["Her sipariş için fatura kesilir."]);

        Assert.NotNull(package.LastChanges);
        Assert.Contains("FATURA", package.LastChanges!.AddedEntities);
        Assert.Contains("Eklenen tablo", package.LastChanges.Describe());
    }

    [Fact]
    public async Task MevcutModelVeYeniKurallar_PromptaBaglamOlarakGider()
    {
        var (pipeline, provider, context) = await Generated();

        await pipeline.ExtendAsync(context, ["Her sipariş için fatura kesilir."]);

        var prompt = provider.Prompts.Single(p => p.Contains("ELİNDE ÇALIŞAN BİR MODEL VAR"));

        Assert.Contains("MUSTERI", prompt);                           // mevcut model
        Assert.Contains("Her sipariş için fatura kesilir.", prompt);  // yeni kural
        Assert.Contains("BR-1", prompt);                              // uygulanmış kurallar
    }

    [Fact]
    public async Task DonmeyenTablo_Silinmez()
    {
        var (pipeline, provider, context) = await Generated();
        provider.ExtendResponse = """{"entities":[],"relationships":[],"notes":[]}""";

        var before = context.Model.Entities.Count;
        var package = await pipeline.ExtendAsync(context, ["Küçük bir not."]);

        Assert.Equal(before, package.Model.Entities.Count);
        Assert.True(package.LastChanges!.IsEmpty);
    }

    [Fact]
    public async Task ModelYoksa_AnlamliHataVerir()
    {
        var provider = new FakeModelProvider();
        var pipeline = new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExtendAsync(new PipelineContext(), ["Yeni kural."]));
    }

    [Fact]
    public async Task BosKuralListesi_AnlamliHataVerir()
    {
        var (pipeline, _, context) = await Generated();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExtendAsync(context, ["   "]));
    }
}
