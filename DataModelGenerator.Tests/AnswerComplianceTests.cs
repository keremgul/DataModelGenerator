using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Rules;
using Xunit;

namespace DataModelGenerator.Tests;

public class AnswerComplianceTests
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
    public void ModeldeKarsiligiOlanCevapUyumluSayilir()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[0].Attributes[1].SourceRuleIds.Add("Q-1");

        var question = new ClarifyingQuestion
        {
            Id = "Q1-1", Text = "Müşteri numarası tekil mi?", Answer = "Evet",
            IsApplied = true, InjectedRuleId = "Q-1"
        };

        var result = Assert.Single(AnswerComplianceChecker.Check(model, [question]));

        Assert.True(result.IsSatisfied);
        Assert.Contains("Müşteri", result.Evidence);
    }

    [Fact]
    public void ModeldeKarsiligiOlmayanCevapUyumsuzIsaretlenir()
    {
        var model = TestModels.CustomerOrder();

        var question = new ClarifyingQuestion
        {
            Id = "Q1-1", Text = "Adres ayrı tabloda mı tutulmalı?", Answer = "Evet",
            IsApplied = true, InjectedRuleId = "Q-7"
        };

        var result = Assert.Single(AnswerComplianceChecker.Check(model, [question]));

        Assert.False(result.IsSatisfied);
        Assert.Contains("görünmüyor", result.Evidence);
    }

    [Fact]
    public void UygulanmamisSorularDenetlenmez()
    {
        var question = new ClarifyingQuestion { Id = "Q1-1", Text = "Soru", Answer = "Cevap" };

        Assert.Empty(AnswerComplianceChecker.Check(TestModels.CustomerOrder(), [question]));
    }

    [Fact]
    public async Task UretimSonrasiUyumKontroluPaketeYansir()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = Input() };
        var package = await pipeline.GenerateAsync(context);

        package.Questions[0].Answer = "Evet, her sipariş tek müşteriye aittir.";
        var updated = await pipeline.ApplyAnswersAsync(context, [package.Questions[0]]);

        var compliance = Assert.Single(updated.AnswerCompliance);
        Assert.Equal("Q-1", compliance.RuleId);
        Assert.Equal("Evet, her sipariş tek müşteriye aittir.", compliance.Answer);
    }

    [Fact]
    public async Task CevaplarMustKisitBloguOlarakGonderilir()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = Input() };
        var package = await pipeline.GenerateAsync(context);

        package.Questions[0].Answer = "Evet, adet bilgisi tutulmalı";
        provider.Prompts.Clear();
        await pipeline.ApplyAnswersAsync(context, [package.Questions[0]]);

        var attributePrompt = provider.Prompts.First(p => p.Contains("ALAN (attribute) listesini"));
        Assert.Contains("BAĞLAYICI CEVAPLAR", attributePrompt);
        Assert.Contains("MUST", attributePrompt);
        Assert.Contains("Evet, adet bilgisi tutulmalı", attributePrompt);
    }
}
