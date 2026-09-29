using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Rules;
using DataModelGenerator.Core.Validation;
using Xunit;

namespace DataModelGenerator.Tests;

public class SuggestionAndModelRuleTests
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

    // --------------------------------------------------------- öneri akışı

    [Fact]
    public async Task Oneriler_KararVerilebilirNesneOlarakUretilir()
    {
        var (pipeline, _) = Build();

        var package = await pipeline.GenerateAsync(new PipelineContext { Input = SampleInput() });

        Assert.NotEmpty(package.Suggestions);
        Assert.All(package.Suggestions, s =>
        {
            Assert.Equal(SuggestionDecision.Pending, s.Decision);
            Assert.False(s.IsApplied);
            Assert.NotEmpty(s.Id);
        });
    }

    [Fact]
    public async Task Oneriler_YalnizcaKabulEdilenlerModeleGonderilir()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        var first = package.Suggestions[0];
        first.Decision = SuggestionDecision.Accepted;
        first.Note = "Yalnızca ana tablolara ekle";

        var rejected = package.Suggestions.Count > 1 ? package.Suggestions[1] : null;
        if (rejected is not null) rejected.Decision = SuggestionDecision.Rejected;

        provider.Prompts.Clear();
        await pipeline.ApplySuggestionsAsync(context, package.Suggestions);

        var prompt = provider.Prompts[0];
        Assert.Contains(first.Text[..20], prompt);
        Assert.Contains("Yalnızca ana tablolara ekle", prompt);
        if (rejected is not null) Assert.DoesNotContain(rejected.Text[..20], prompt);
    }

    [Fact]
    public async Task Oneriler_UygulandiktanSonraIsaretlenir()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        package.Suggestions[0].Decision = SuggestionDecision.Accepted;

        var updated = await pipeline.ApplySuggestionsAsync(context, package.Suggestions);

        var applied = updated.Suggestions.Single(s => s.Id == package.Suggestions[0].Id);
        Assert.True(applied.IsApplied);
        Assert.True(applied.AppliedInRound > 0);
    }

    [Fact]
    public async Task Oneriler_KabulEdilenYoksaHataVerir()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        foreach (var suggestion in package.Suggestions)
            suggestion.Decision = SuggestionDecision.Rejected;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ApplySuggestionsAsync(context, package.Suggestions));
    }

    [Fact]
    public async Task Oneriler_ReddedilenTekrarOnerilmez()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        var rejectedText = package.Suggestions[0].Text;
        package.Suggestions[0].Decision = SuggestionDecision.Rejected;
        package.Suggestions[^1].Decision = SuggestionDecision.Accepted;

        var updated = await pipeline.ApplySuggestionsAsync(context, package.Suggestions);

        var stillRejected = updated.Suggestions.Where(s => s.Text == rejectedText).ToList();
        Assert.Single(stillRejected);
        Assert.Equal(SuggestionDecision.Rejected, stillRejected[0].Decision);
        Assert.False(stillRejected[0].IsApplied);
    }

    [Fact]
    public async Task Oneriler_CevaplarOzetAdimina_Gonderilir()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        package.Questions[0].Answer = "Evet, adet bilgisi tutulmalı";
        await pipeline.ApplyAnswersAsync(context, [package.Questions[0]]);

        var summaryPrompt = provider.Prompts.Last(p => p.Contains("ÖZET ve ÖNERİ"));
        Assert.Contains("Evet, adet bilgisi tutulmalı", summaryPrompt);
        Assert.Contains(package.Questions[0].Text, summaryPrompt);
    }

    [Fact]
    public async Task Oneriler_KullaniciIstediginde_CevaplaraGoreUretilir()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        package.Questions[0].Answer = "Evet, adet bilgisi tutulmalı";
        await pipeline.ApplyAnswersAsync(context, [package.Questions[0]]);

        provider.SummaryResponseOverride = """
            {
              "summary": "Cevaplar uygulandı.",
              "suggestions": ["Ara tabloya adet alanı eklenmeli (kullanıcının adet cevabından doğdu)"]
            }
            """;

        var updated = await pipeline.RequestSuggestionsAsync(context);

        Assert.Equal(1, updated.NewSuggestionCount);
        Assert.Contains(updated.Suggestions, s => s.Text.Contains("adet alanı"));
    }

    [Fact]
    public async Task Oneriler_ReddedilenlerOzetPromptunda_Isaretlenir()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        var rejectedText = package.Suggestions[0].Text;
        package.Suggestions[0].Decision = SuggestionDecision.Rejected;

        provider.Prompts.Clear();
        await pipeline.RequestSuggestionsAsync(context);

        var summaryPrompt = provider.Prompts.Last(p => p.Contains("ÖZET ve ÖNERİ"));
        Assert.Contains("REDDETTİ", summaryPrompt);
        Assert.Contains(rejectedText[..20], summaryPrompt);
    }

    [Fact]
    public async Task Oneriler_ModelUretilmedenIstenemez()
    {
        var (pipeline, _) = Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.RequestSuggestionsAsync(new PipelineContext { Input = SampleInput() }));
    }

    // --------------------------------------------------- cevap düzenleme

    [Fact]
    public async Task Cevaplar_DuzenlendigindeYeniKuralEklenmezMevcutGuncellenir()
    {
        var (pipeline, _) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        var question = package.Questions[0];
        question.Answer = "İlk cevap";
        await pipeline.ApplyAnswersAsync(context, [question]);

        var afterFirst = context.Input.Rules.Count(r => r.Origin == RuleOrigin.Clarification);

        question.Answer = "Düzeltilmiş cevap";
        await pipeline.ApplyAnswersAsync(context, [question]);

        var clarifications = context.Input.Rules.Where(r => r.Origin == RuleOrigin.Clarification).ToList();
        Assert.Equal(afterFirst, clarifications.Count);
        Assert.Contains(clarifications, r => r.Text.Contains("Düzeltilmiş cevap"));
        Assert.DoesNotContain(clarifications, r => r.Text.Contains("İlk cevap"));
    }

    // ------------------------------------------------------ model kuralları

    [Fact]
    public async Task ModelKurallari_HerTablodaIDBirincilAnahtariOlur()
    {
        var (pipeline, _) = Build();

        var package = await pipeline.GenerateAsync(new PipelineContext { Input = SampleInput() });

        Assert.All(package.Model.Entities, entity =>
        {
            var pk = Assert.Single(entity.PrimaryKeys);
            Assert.Equal(NamingRules.PrimaryKeyName, pk.TechnicalName);
            Assert.True(pk.IsUnique);
        });
    }

    [Fact]
    public async Task ModelKurallari_YabanciAnahtarHedefTabloAdiArtiID()
    {
        var (pipeline, _) = Build();

        var package = await pipeline.GenerateAsync(new PipelineContext { Input = SampleInput() });

        var foreignKeys = package.Model.Entities.SelectMany(e => e.Attributes).Where(a => a.IsForeignKey).ToList();
        Assert.NotEmpty(foreignKeys);
        Assert.All(foreignKeys, fk =>
            Assert.Equal(NamingRules.DefaultForeignKeyName(fk.ReferencesEntity!), fk.TechnicalName));
    }

    [Fact]
    public void ModelKurallari_IliskisizTabloDogrulamadaYakalanir()
    {
        var model = TestModels.CustomerOrder();
        model.Entities.Add(new ModelEntity
        {
            Name = "Kampanya",
            TechnicalName = "Kampanya",
            Attributes =
            {
                new EntityAttribute { Name = "ID", TechnicalName = "ID", IsPrimaryKey = true, IsRequired = true, IsUnique = true }
            }
        });

        var issues = new ModelValidator().Validate(model);

        Assert.Contains(issues, i => i.Code == "UNRELATED_ENTITY" && i.Target == "Kampanya");
    }

    [Fact]
    public void ModelKurallari_IDDisiBirincilAnahtarDogrulamadaYakalanir()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[0].Attributes[0].IsPrimaryKey = false;
        model.Entities[0].Attributes[1].IsPrimaryKey = true;

        var issues = new ModelValidator().Validate(model);

        Assert.Contains(issues, i => i.Code == "PK_NOT_ID");
    }

    [Fact]
    public void ModelKurallari_CokaCokIliskiAraTabloyaCevrilir()
    {
        var model = TestModels.CustomerOrder();
        model.Relationships[0].Cardinality = Cardinality.ManyToMany;

        var created = ModelMerger.CreateJunctionTables(model);
        new RuleEngine().Normalize(model, new ProjectInput());
        ModelMerger.SyncForeignKeys(model);

        var junctionName = Assert.Single(created);
        var junction = model.FindEntity(junctionName);

        Assert.NotNull(junction);
        Assert.True(junction!.IsJunction);
        Assert.Equal(NamingRules.PrimaryKeyName, junction.PrimaryKey!.TechnicalName);
        Assert.Equal(2, junction.Attributes.Count(a => a.IsForeignKey));
        Assert.DoesNotContain(model.Relationships, r => r.Cardinality == Cardinality.ManyToMany);
    }

    [Fact]
    public void ModelKurallari_PromptMetniTumKurallariIcerir()
    {
        Assert.Contains("ID", ModelRules.PromptText);
        Assert.Contains("ilişkisiz tablo bulunmaz", ModelRules.PromptText);
        Assert.Contains("ara tabloyla", ModelRules.PromptText);
        Assert.Contains("PROJECALISAN", ModelRules.PromptText);
    }
}
