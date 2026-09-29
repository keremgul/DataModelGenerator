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
    public async Task Reprompt_AcikcaBildirilenVarligiSiler()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);

        provider.RepromptResponseOverride = """
            {
              "entities": [], "relationships": [],
              "removedEntities": ["Sipariş"],
              "notes": ["Sipariş varlığı kaldırıldı"]
            }
            """;

        var package = await pipeline.RepromptAsync(context, ["Sipariş"], "Sipariş varlığını kaldır.");

        Assert.Null(package.Model.FindEntity("Sipariş"));
        Assert.NotNull(package.Model.FindEntity("Müşteri"));
    }

    [Fact]
    public async Task Reprompt_YanittaDonmeyenVarligiSilmez()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var before = await pipeline.GenerateAsync(context);
        var entityCount = before.Model.Entities.Count;

        // Yalnızca değişen varlık dönüyor; diğerleri korunmalı.
        provider.RepromptResponseOverride = """
            { "entities": [], "relationships": [], "notes": [] }
            """;

        var package = await pipeline.RepromptAsync(context, ["Sipariş"], "Küçük bir değişiklik yap.");

        Assert.Equal(entityCount, package.Model.Entities.Count);
        Assert.NotNull(package.Model.FindEntity("Sipariş"));
    }

    [Fact]
    public async Task Reprompt_IliskileriKendiliginden_Silmez()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var before = await pipeline.GenerateAsync(context);
        var relationshipCount = before.Model.Relationships.Count;

        provider.RepromptResponseOverride = """
            {
              "entities": [
                {
                  "name": "Sipariş", "sourceRuleIds": ["BR-2"],
                  "attributes": [
                    { "name": "Tutar", "required": true, "sourceRuleIds": ["BR-2"] }
                  ]
                }
              ],
              "relationships": [],
              "notes": []
            }
            """;

        var package = await pipeline.RepromptAsync(context, ["Sipariş"], "Tutar alanını güncelle.");

        Assert.Equal(relationshipCount, package.Model.Relationships.Count);
    }

    [Fact]
    public async Task Reprompt_AcikcaBildirilenIliskiyiSiler()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var before = await pipeline.GenerateAsync(context);
        var target = before.Model.Relationships[0];

        provider.RepromptResponseOverride = $$"""
            {
              "entities": [], "relationships": [],
              "removedRelationships": [{ "from": "{{target.FromEntity}}", "to": "{{target.ToEntity}}" }],
              "notes": []
            }
            """;

        var package = await pipeline.RepromptAsync(context, [], "Bu ilişkiyi kaldır.");

        Assert.DoesNotContain(package.Model.Relationships, r =>
            r.FromEntity == target.FromEntity && r.ToEntity == target.ToEntity);
        Assert.Contains(package.RepromptNotes, n => n.Contains("Kaldırılan ilişkiler"));
    }

    [Fact]
    public async Task Reprompt_SilinenVarligaIsaretEdenIliskiVeYabanciAnahtarTemizlenir()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);

        provider.RepromptResponseOverride = """
            {
              "entities": [], "relationships": [],
              "removedEntities": ["Müşteri"],
              "notes": []
            }
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
    public async Task Sorular_CevapUygulandigindaKendiliginden_YeniSoruUretilmez()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        // Yeni bir belirsizlik ortaya çıksa bile soru üretimi otomatik tetiklenmemeli.
        provider.GapsResponseOverride = """
            {
              "ambiguities": [
                { "target": "Musteri", "kind": "PrimaryKey", "impact": "High",
                  "description": "Yeni bir belirsizlik", "confidence": 0.3 }
              ]
            }
            """;

        package.Questions[0].Answer = "Evet";
        provider.Stages.Clear();
        var updated = await pipeline.ApplyAnswersAsync(context, [package.Questions[0]]);

        Assert.DoesNotContain("questions", provider.Stages);
        Assert.Empty(updated.OpenQuestions);
    }

    [Fact]
    public async Task Sorular_KullaniciIstediginde_YeniSoruUretilir()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        package.Questions[0].Answer = "Evet";
        await pipeline.ApplyAnswersAsync(context, [package.Questions[0]]);

        provider.GapsResponseOverride = """
            {
              "ambiguities": [
                { "target": "Musteri.MusteriNo", "kind": "PrimaryKey", "impact": "High",
                  "description": "Müşteri numarası doğal anahtar mı?", "confidence": 0.3 }
              ]
            }
            """;
        provider.QuestionsResponseOverride = """
            {
              "questions": [
                { "ambiguityId": "A2", "text": "Müşteri numarası doğal anahtar olarak mı kullanılmalı?",
                  "reason": "Birincil anahtar seçimini belirler", "options": ["Evet", "Hayır"] }
              ]
            }
            """;

        var updated = await pipeline.RequestQuestionsAsync(context);

        Assert.Equal(1, updated.NewQuestionCount);
        var open = Assert.Single(updated.OpenQuestions);
        Assert.Contains("doğal anahtar", open.Text);
        Assert.Single(updated.AppliedQuestions);
    }

    [Fact]
    public async Task Sorular_AyniSoruIkinciKezEklenmez()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        var alreadyAsked = package.Questions[0].Text;

        // Model aynı soruyu farklı bir belirsizlik kimliğiyle tekrar önerse bile eklenmemeli.
        provider.GapsResponseOverride = """
            {
              "ambiguities": [
                { "target": "Baska Hedef", "kind": "Cardinality", "impact": "High",
                  "description": "Aynı konu", "confidence": 0.3 }
              ]
            }
            """;
        provider.QuestionsResponseOverride = $$"""
            {
              "questions": [
                { "ambiguityId": "A2", "text": "{{alreadyAsked}}", "reason": "tekrar", "options": [] }
              ]
            }
            """;

        var updated = await pipeline.RequestQuestionsAsync(context);

        Assert.Equal(0, updated.NewQuestionCount);
        Assert.Single(updated.Questions);
    }

    [Fact]
    public async Task Sorular_YeniBelirsizlikYoksaKullaniciyaBildirilir()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);

        provider.GapsResponseOverride = """{"ambiguities": []}""";

        var updated = await pipeline.RequestQuestionsAsync(context);

        Assert.Equal(0, updated.NewQuestionCount);
        Assert.Contains(updated.NormalizationNotes, n => n.Contains("kararlı görünüyor"));
    }

    [Fact]
    public async Task Sorular_ModelUretilmedenYeniSoruIstenemez()
    {
        var (pipeline, _) = Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.RequestQuestionsAsync(new PipelineContext { Input = SampleInput() }));
    }
}
