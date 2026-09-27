using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Validation;
using Xunit;

namespace DataModelGenerator.Tests;

public class ModelPipelineTests
{
    private static ProjectInput SampleInput() => new()
    {
        Topic = "Sipariş Yönetimi",
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

    [Fact]
    public async Task GenerateAsync_YediAdiminTamaminiSirayaCalistirir()
    {
        var (pipeline, provider) = Build();

        await pipeline.GenerateAsync(new PipelineContext { Input = SampleInput() });

        Assert.Equal(
            ["extraction", "relationships", "attributes", "normalization", "gaps", "questions", "summary"],
            provider.Stages);
    }

    [Fact]
    public async Task GenerateAsync_ModelOzetOneriVeBelirsizlikUretir()
    {
        var (pipeline, _) = Build();

        var package = await pipeline.GenerateAsync(new PipelineContext { Input = SampleInput() });

        Assert.Equal(2, package.Model.Entities.Count);
        Assert.Single(package.Model.Relationships);
        Assert.Contains("BR-1", package.Summary);
        Assert.NotEmpty(package.Suggestions);
        Assert.NotEmpty(package.Ambiguities);
        Assert.StartsWith("erDiagram", package.MermaidCode);
        Assert.Contains("CREATE TABLE", package.Ddl);
        Assert.Contains("\"entities\"", package.ModelJson);
    }

    [Fact]
    public async Task GenerateAsync_UretilenModelDogrulamadanGecer()
    {
        var (pipeline, _) = Build();

        var package = await pipeline.GenerateAsync(new PipelineContext { Input = SampleInput() });

        Assert.DoesNotContain(package.ValidationIssues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public async Task GenerateAsync_IzlenebilirligiKorur()
    {
        var (pipeline, _) = Build();

        var package = await pipeline.GenerateAsync(new PipelineContext { Input = SampleInput() });
        var customer = package.Model.FindEntity("Müşteri");

        Assert.NotNull(customer);
        Assert.Contains("BR-1", customer!.SourceRuleIds);
        Assert.All(customer.Attributes, a => Assert.NotEmpty(a.SourceRuleIds));
    }

    [Fact]
    public async Task GenerateAsync_YuksekEtkiliBelirsizlikIcinSoruUretir()
    {
        var (pipeline, _) = Build();

        var package = await pipeline.GenerateAsync(new PipelineContext { Input = SampleInput() });

        var question = Assert.Single(package.Questions);
        Assert.Equal(AmbiguityKind.Cardinality, question.Kind);
        Assert.NotEmpty(question.Options);
    }

    [Fact]
    public async Task GenerateAsync_VarlikCikarilamazsaAnlamliHataVerir()
    {
        var provider = new FakeModelProvider { AttributesResponse = "{}" };
        var pipeline = new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model");
        var input = SampleInput();

        // Varlık adımı boş dönerse akış ilk adımda durmalı.
        var emptyProvider = new EmptyExtractionProvider();
        var emptyPipeline = new ModelPipeline(emptyProvider, new ProviderConnectionSettings(), "fake-model");

        var ex = await Assert.ThrowsAsync<PipelineException>(() =>
            emptyPipeline.GenerateAsync(new PipelineContext { Input = input }));

        Assert.Equal(PipelineStage.Extraction, ex.Stage);
        Assert.NotNull(pipeline);
    }

    [Fact]
    public async Task ApplyAnswersAsync_CevabiKuralListesineEnjekteEder()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        var question = package.Questions[0];
        question.Answer = "Evet, her sipariş tek müşteriye aittir.";

        await pipeline.ApplyAnswersAsync(context, [question]);

        var injected = Assert.Single(context.Input.Rules, r => r.Origin == RuleOrigin.Clarification);
        Assert.Equal("C-1", injected.Id);
        Assert.Contains("Evet, her sipariş tek müşteriye aittir.", injected.Text);
    }

    [Fact]
    public async Task ApplyAnswersAsync_PipelineiBastanDegilEtkilenenAdimdanCalistirir()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        provider.Stages.Clear();
        package.Questions[0].Answer = "Evet";

        await pipeline.ApplyAnswersAsync(context, [package.Questions[0]]);

        Assert.DoesNotContain("extraction", provider.Stages);
        Assert.Equal("relationships", provider.Stages[0]);
    }

    [Fact]
    public async Task ApplyAnswersAsync_CevaplananBelirsizligiCozulmusIsaretler()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        var question = package.Questions[0];
        question.Answer = "Evet";

        var updated = await pipeline.ApplyAnswersAsync(context, [question]);

        var ambiguity = updated.Ambiguities.Single(a => a.Id == question.AmbiguityId);
        Assert.True(ambiguity.IsResolved);
        Assert.Equal(2, updated.RoundNumber);
    }

    [Fact]
    public async Task ApplyAnswersAsync_CevapYoksaHataVerir()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ApplyAnswersAsync(context, package.Questions));
    }

    [Fact]
    public async Task RepromptAsync_YalnizcaKapsamdakiVarligiModeleGonderir()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);

        provider.Prompts.Clear();
        provider.Stages.Clear();

        await pipeline.RepromptAsync(context, ["Sipariş"], "Tutar alanını DECIMAL(18,4) yap ve indirim oranı ekle.");

        var repromptPrompt = provider.Prompts[0];
        Assert.Contains("Sipariş", repromptPrompt);
        Assert.Contains("Tutar alanını DECIMAL(18,4) yap", repromptPrompt);
        Assert.DoesNotContain("AdSoyad", repromptPrompt);
    }

    [Fact]
    public async Task RepromptAsync_DegisikligiModeleUygularVeDigerVarligiKorur()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);

        var package = await pipeline.RepromptAsync(context, ["Sipariş"], "İndirim oranı ekle.");

        var order = package.Model.FindEntity("Sipariş");
        Assert.NotNull(order);
        Assert.Contains(order!.Attributes, a => a.TechnicalName == "IndirimOrani");
        Assert.Equal("DECIMAL(18,4)", order.Attributes.Single(a => a.TechnicalName == "Tutar").DataType);

        var customer = package.Model.FindEntity("Müşteri");
        Assert.NotNull(customer);
        Assert.Equal(2, customer!.Attributes.Count);
    }

    [Fact]
    public async Task RepromptAsync_VarlikCikariminiYenidenCalistirmaz()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);

        provider.Stages.Clear();
        await pipeline.RepromptAsync(context, ["Sipariş"], "İndirim oranı ekle.");

        Assert.DoesNotContain("extraction", provider.Stages);
        Assert.DoesNotContain("relationships", provider.Stages);
        Assert.DoesNotContain("attributes", provider.Stages);
        Assert.Equal("reprompt", provider.Stages[0]);
    }

    [Fact]
    public async Task RepromptAsync_TalebiKuralListesineNotEder()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);

        await pipeline.RepromptAsync(context, ["Sipariş"], "İndirim oranı ekle.");

        Assert.Contains(context.Input.Rules,
            r => r.Origin == RuleOrigin.Clarification && r.Text.Contains("İndirim oranı ekle."));
    }

    [Fact]
    public async Task RepromptAsync_KapsamBosVeyaTalepBossaHataVerir()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.RepromptAsync(context, ["Sipariş"], "   "));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.RepromptAsync(context, [], "bir şey değiştir"));
    }

    [Fact]
    public async Task GenerateAsync_NNIliskisiIcinAraTabloOnerirAmaOtomatikEklemez()
    {
        var provider = new FakeModelProvider
        {
            RelationshipsResponse = """
                {
                  "relationships": [
                    { "name": "içerir", "from": "Müşteri", "to": "Sipariş", "cardinality": "N-N", "required": true,
                      "description": "", "sourceRuleIds": ["BR-2"], "confidence": 0.8, "confidenceReason": "" }
                  ]
                }
                """
        };
        var pipeline = new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model");

        var package = await pipeline.GenerateAsync(new PipelineContext { Input = SampleInput() });

        Assert.Equal(2, package.Model.Entities.Count);
        Assert.Contains(package.Suggestions, s => s.Contains("ara tablo"));
        Assert.Contains(package.ValidationIssues, i => i.Code == "MANY_TO_MANY_WITHOUT_JUNCTION");
    }
}

/// <summary>Varlık çıkarımı boş dönen sağlayıcı — hata yolunu sınamak için.</summary>
internal class EmptyExtractionProvider : Core.Providers.IModelProvider
{
    public string ProviderKey => "empty";
    public string DisplayName => "Boş";
    public bool RequiresApiKey => false;
    public bool RequiresBaseUrl => false;
    public IReadOnlyList<ModelInfo> KnownModels => [];

    public Task<List<ModelInfo>> ListModelsAsync(ProviderConnectionSettings settings, CancellationToken ct = default) =>
        Task.FromResult(new List<ModelInfo>());

    public Task<string> GenerateAsync(ProviderConnectionSettings settings, string modelId,
        string systemPrompt, string userPrompt, CancellationToken ct = default) =>
        Task.FromResult("""{"entities":[]}""");

    public Task<ConnectionTestResult> TestConnectionAsync(ProviderConnectionSettings settings, string modelId,
        CancellationToken ct = default) =>
        Task.FromResult(ConnectionTestResult.Ok("OK"));
}
