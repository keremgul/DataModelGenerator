using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using Xunit;

namespace DataModelGenerator.Tests;

public class RuleRelationTests
{
    private static ProjectInput Input() => new()
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

    [Fact]
    public async Task IliskiAnaliziIlkAdimOlarakCalisir()
    {
        var (pipeline, provider) = Build();

        await pipeline.GenerateAsync(new PipelineContext { Input = Input() });

        Assert.Equal("rule-relations", provider.Stages[0]);
    }

    [Fact]
    public async Task CikarilanIliskilerBaglamaEklenir()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = Input() };

        await pipeline.GenerateAsync(context);

        var relation = Assert.Single(context.RuleRelations);
        Assert.Equal("BR-1", relation.FirstRuleId);
        Assert.Equal("BR-2", relation.SecondRuleId);
        Assert.Equal(RuleRelationKind.Elaborates, relation.Kind);
    }

    [Fact]
    public async Task IliskilerVarlikCikarimiPromptunaGonderilir()
    {
        var (pipeline, provider) = Build();

        await pipeline.GenerateAsync(new PipelineContext { Input = Input() });

        var extractionPrompt = provider.Prompts.First(p => p.Contains("VARLIK (entity) adaylarını"));
        Assert.Contains("BR-1 ↔ BR-2", extractionPrompt);
        Assert.Contains("detaylandırıyor", extractionPrompt);
    }

    [Fact]
    public async Task CelisenKurallarSoruOlarakDoner()
    {
        var (pipeline, provider) = Build();
        provider.RuleRelationsResponse = """
            {
              "relations": [
                { "first": "BR-1", "second": "BR-2", "kind": "celisir",
                  "explanation": "BR-1 tek sipariş derken BR-2 çoklu sipariş diyor" }
              ]
            }
            """;

        var context = new PipelineContext { Input = Input() };
        var package = await pipeline.GenerateAsync(context);

        var pending = Assert.Single(package.PendingItems, p => p.Target == "BR-1 ↔ BR-2");
        Assert.Contains("çelişiyor", pending.Question);
        Assert.Contains("BR-1 geçerli", pending.SuggestedAnswers);
        Assert.Contains(package.Ambiguities, a => a.Description.Contains("çelişiyor"));
    }

    [Fact]
    public async Task TekrarEdenKurallarSoruOlarakDoner()
    {
        var (pipeline, provider) = Build();
        provider.RuleRelationsResponse = """
            {
              "relations": [
                { "first": "BR-1", "second": "BR-2", "kind": "tekrar",
                  "explanation": "İkisi de müşteri numarasının tekilliğini söylüyor" }
              ]
            }
            """;

        var package = await pipeline.GenerateAsync(new PipelineContext { Input = Input() });

        var pending = Assert.Single(package.PendingItems, p => p.Target == "BR-1 ↔ BR-2");
        Assert.Contains("aynı şeyi söylüyor", pending.Question);
    }

    [Fact]
    public async Task UydurmaKuralKimligiTasiyanIliskiYoksayilir()
    {
        var (pipeline, provider) = Build();
        provider.RuleRelationsResponse = """
            {
              "relations": [
                { "first": "BR-1", "second": "BR-99", "kind": "celisir", "explanation": "uydurma" }
              ]
            }
            """;

        var context = new PipelineContext { Input = Input() };
        await pipeline.GenerateAsync(context);

        Assert.Empty(context.RuleRelations);
    }

    [Fact]
    public async Task TekKuralVarsaAnalizCalistirilmaz()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext
        {
            Input = new ProjectInput { Rules = { new BusinessRule { Id = "BR-1", Text = "Tek kural." } } }
        };

        await pipeline.GenerateAsync(context);

        Assert.DoesNotContain("rule-relations", provider.Stages);
    }
}
