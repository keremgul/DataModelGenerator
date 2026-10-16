using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Rules;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// Beklemede listesi: kaynaksız, belirsiz veya normalizasyonu geçemeyen öğe
/// modele girmez, otomatik olarak soruya dönüşür ve cevaplanana kadar beklemede kalır.
/// </summary>
public class PendingQuestionTests
{
    private static PendingItem Item(string target = "Müşteri.Adres") => new()
    {
        Kind = PendingItemKind.Attribute,
        Reason = PendingReason.NoSource,
        Target = target,
        Explanation = $"'{target}' alanı hiçbir kurala bağlanamadı.",
        SourceRuleIds = { "BR-01" },
        Question = $"'{target}' alanı gerçekten gerekli mi?",
        SuggestedAnswers = { "Gerekli — kuralı ekleyeceğim", "Gerekli değil, çıkar" }
    };

    [Fact]
    public void BeklemedeOge_SoruyaCevrilir()
    {
        var questions = new List<ClarifyingQuestion>();

        var added = PendingQuestions.Sync([Item()], questions, 1);

        Assert.Equal(1, added);
        Assert.Single(questions);
        Assert.Contains("gerçekten gerekli mi", questions[0].Text);
    }

    [Fact]
    public void SoruFormati_KaynakNedenVeOneriIcerir()
    {
        var questions = new List<ClarifyingQuestion>();
        PendingQuestions.Sync([Item()], questions, 1);

        var question = questions[0];

        Assert.Contains("Şüphe kaynağı: BR-01", question.Reason);
        Assert.Contains("hiçbir kurala bağlanamadı", question.Reason);
        Assert.Contains("modele eklenmez", question.Reason);
        Assert.Equal(2, question.Options.Count);
    }

    [Fact]
    public void IhlalEdilenNormalizasyonKurali_SorudaBelirtilir()
    {
        var item = Item();
        item.Reason = PendingReason.NormalizationBlocked;
        item.ViolatedRule = "1NF-1";

        var questions = new List<ClarifyingQuestion>();
        PendingQuestions.Sync([item], questions, 1);

        Assert.Contains("İhlal edilen kural: 1NF-1", questions[0].Reason);
        Assert.Equal("Normalizasyon (1NF-1)", item.ReasonText);
    }

    [Fact]
    public void AyniOge_IkinciTurdaTekrarSorulmaz()
    {
        var questions = new List<ClarifyingQuestion>();

        PendingQuestions.Sync([Item()], questions, 1);
        var second = PendingQuestions.Sync([Item()], questions, 2);

        Assert.Equal(0, second);
        Assert.Single(questions);
    }

    [Fact]
    public void OgelereTurBagimsizKimlikVerilir()
    {
        var items = new List<PendingItem> { Item("Müşteri.Adres"), Item("Sipariş.Notlar") };

        PendingQuestions.Sync(items, new List<ClarifyingQuestion>(), 1);

        Assert.Equal("BEK-1", items[0].Id);
        Assert.Equal("BEK-2", items[1].Id);
        Assert.NotEqual(items[0].Signature, items[1].Signature);
    }

    [Fact]
    public void SorusuOlmayanOge_SoruUretmez()
    {
        var item = Item();
        item.Question = string.Empty;

        var questions = new List<ClarifyingQuestion>();
        var added = PendingQuestions.Sync([item], questions, 1);

        Assert.Equal(0, added);
        Assert.Empty(questions);
        Assert.Equal("BEK-1", item.Id);
    }

    // -------------------------------------------------- pipeline uçtan uca

    [Fact]
    public async Task KaynaksizAlan_ModeleGirmezAmaSoruOlarakDoner()
    {
        var provider = new FakeModelProvider
        {
            // 'Faks No' hiçbir kurala bağlı değil: modele girmemeli.
            AttributesResponse = """
                {
                  "entities": [
                    {
                      "name": "Müşteri",
                      "attributes": [
                        { "name": "Müşteri No", "primaryKey": true, "sourceRuleIds": ["BR-1"], "confidence": 0.9 },
                        { "name": "Faks No", "sourceRuleIds": [], "confidence": 0.5 }
                      ]
                    }
                  ]
                }
                """
        };

        var pipeline = new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model");
        var context = new PipelineContext
        {
            Input = new ProjectInput
            {
                Rules =
                {
                    new BusinessRule { Id = "BR-1", Text = "Her müşterinin tekil numarası vardır." },
                    new BusinessRule { Id = "BR-2", Text = "Bir müşteri birden çok sipariş verebilir." }
                }
            }
        };

        var package = await pipeline.GenerateAsync(context);

        var musteri = package.Model.FindEntity("Müşteri")!;
        Assert.DoesNotContain(musteri.Attributes, a => a.TechnicalName.Contains("FAKS"));

        var pending = package.PendingItems.Single(p => p.Target.Contains("FAKSNO"));
        Assert.Equal(PendingReason.NoSource, pending.Reason);
        Assert.StartsWith("BEK-", pending.Id);
        Assert.Contains(package.Questions, q => q.AmbiguityId == pending.Signature);
    }
}
