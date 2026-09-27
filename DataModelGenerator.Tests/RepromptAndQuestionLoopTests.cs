using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using Xunit;

namespace DataModelGenerator.Tests;

public class RepromptAndQuestionLoopTests
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

    // ------------------------------------------------------------ reprompt

    [Fact]
    public async Task Reprompt_DonmeyenKapsamVarligiModelden_Silinir()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);

        // Model "Sipariş"i döndürmüyor → silinmesi isteniyor demektir.
        provider.RepromptResponseOverride = """
            { "entities": [], "relationships": [], "notes": ["Sipariş varlığı kaldırıldı"] }
            """;

        var package = await pipeline.RepromptAsync(context, ["Sipariş"], "Sipariş varlığını kaldır.");

        Assert.Null(package.Model.FindEntity("Sipariş"));
        Assert.NotNull(package.Model.FindEntity("Müşteri"));
    }

    [Fact]
    public async Task Reprompt_SilinenVarligaIsaretEdenIliskiVeYabanciAnahtarTemizlenir()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);

        provider.RepromptResponseOverride = """
            { "entities": [], "relationships": [], "notes": [] }
            """;

        var package = await pipeline.RepromptAsync(context, ["Müşteri"], "Müşteri varlığını kaldır.");

        Assert.Null(package.Model.FindEntity("Müşteri"));
        Assert.DoesNotContain(package.Model.Relationships,
            r => r.FromEntity.Contains("Musteri") || r.ToEntity.Contains("Musteri"));

        var order = package.Model.FindEntity("Sipariş");
        Assert.NotNull(order);
        Assert.DoesNotContain(order!.Attributes, a => a.ReferencesEntity == "Musteri");
    }

    [Fact]
    public async Task Reprompt_KapsamDisiVarligaDokunmaz()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);

        provider.RepromptResponseOverride = """
            { "entities": [], "relationships": [], "notes": [] }
            """;

        var package = await pipeline.RepromptAsync(context, ["Sipariş"], "Sipariş varlığını kaldır.");

        // Kapsamda olmayan Müşteri, yanıtta dönmese de silinmemeli.
        Assert.NotNull(package.Model.FindEntity("Müşteri"));
    }

    [Fact]
    public async Task Reprompt_ModelNotlariPakete_Tasinir()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);

        provider.RepromptResponseOverride = """
            {
              "entities": [],
              "relationships": [],
              "notes": ["KAPSAM DIŞI DEĞİŞİKLİK GEREKİYOR: Ürün varlığına kod alanı eklenmeli"]
            }
            """;

        var package = await pipeline.RepromptAsync(context, ["Sipariş"], "Kod alanı ekle.");

        Assert.Contains(package.RepromptNotes, n => n.Contains("KAPSAM DIŞI"));
        Assert.Contains(package.RepromptNotes, n => n.Contains("kaldırılan varlıklar"));
    }

    [Fact]
    public async Task Reprompt_YeniVarlikEklenebilir()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);

        provider.RepromptResponseOverride = """
            {
              "entities": [
                {
                  "name": "Sipariş", "sourceRuleIds": ["BR-2"],
                  "attributes": [
                    { "name": "Sipariş No", "primaryKey": true, "required": true, "unique": true, "sourceRuleIds": ["BR-2"] }
                  ]
                },
                {
                  "name": "Sipariş Kalemi", "sourceRuleIds": ["BR-2"],
                  "attributes": [
                    { "name": "Kalem No", "primaryKey": true, "required": true, "unique": true, "sourceRuleIds": ["BR-2"] }
                  ]
                }
              ],
              "relationships": [],
              "notes": []
            }
            """;

        var package = await pipeline.RepromptAsync(context, ["Sipariş"], "Sipariş kalemi ekle.");

        Assert.NotNull(package.Model.FindEntity("Sipariş Kalemi"));
        Assert.NotNull(package.Model.FindEntity("Sipariş"));
    }

    // ------------------------------------------------------- soru döngüsü

    [Fact]
    public async Task Sorular_UygulandigindaSilinmezIsaretlenir()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        var question = package.Questions[0];
        question.Answer = "Evet";

        var updated = await pipeline.ApplyAnswersAsync(context, [question]);

        var applied = Assert.Single(updated.AppliedQuestions);
        Assert.True(applied.IsApplied);
        Assert.Equal("Evet", applied.Answer);
        Assert.True(applied.AppliedInRound > 0);
    }

    [Fact]
    public async Task Sorular_CozulmusBelirsizlikIcinTekrarSorulmaz()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        package.Questions[0].Answer = "Evet";
        var updated = await pipeline.ApplyAnswersAsync(context, [package.Questions[0]]);

        Assert.Empty(updated.OpenQuestions);
    }

    [Fact]
    public async Task Sorular_TurSinirinaUlasincaYeniSoruUretilmez()
    {
        var provider = new FakeModelProvider();
        var pipeline = new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model");
        var context = new PipelineContext { Input = SampleInput() };

        await pipeline.GenerateAsync(context);

        // Her turda yeni bir belirsizlik + soru üreten bir sağlayıcı taklit edilir.
        for (var round = 2; round <= PipelineContext.MaxQuestionRounds + 2; round++)
        {
            provider.GapsResponseOverride = $$"""
                {
                  "ambiguities": [
                    { "target": "Hedef{{round}}", "kind": "Cardinality", "impact": "High",
                      "description": "Tur {{round}} belirsizliği", "confidence": 0.3 }
                  ]
                }
                """;
            provider.QuestionsResponseOverride = $$"""
                {
                  "questions": [
                    { "ambiguityId": "A{{round}}", "text": "Tur {{round}} sorusu", "reason": "test", "options": [] }
                  ]
                }
                """;

            var open = context.Questions.Where(q => !q.IsApplied).ToList();
            if (open.Count == 0) break;

            foreach (var q in open) q.Answer = "Evet";
            await pipeline.ApplyAnswersAsync(context, open);
        }

        Assert.True(context.QuestionRounds <= PipelineContext.MaxQuestionRounds,
            $"Soru turu sınırı aşıldı: {context.QuestionRounds}");
        Assert.True(context.QuestionLimitReached);
        Assert.Empty(context.Questions.Where(q => !q.IsApplied));
    }

    [Fact]
    public async Task Sorular_SinirAsildigindaPakette_Raporlanir()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        context.QuestionRounds = PipelineContext.MaxQuestionRounds;

        var package = await pipeline.GenerateAsync(context);

        Assert.True(package.QuestionLimitReached);
        Assert.Empty(package.OpenQuestions);
        Assert.Contains(package.NormalizationNotes, n => n.Contains("Soru turu sınırına"));
    }
}
