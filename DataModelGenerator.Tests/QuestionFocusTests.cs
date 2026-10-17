using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Rules;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// Soruların odağı mantıksal yapıdır; alan tipi soruları elenir. Sorulmuş
/// sorular oturum boyunca saklanır ve benzerlik kontrolüyle tekrar edilmez.
/// </summary>
public class QuestionFocusTests
{
    private static ProjectInput SampleInput() => new()
    {
        Rules =
        {
            new BusinessRule { Id = "BR-1", Text = "Her müşterinin tekil bir müşteri numarası vardır." },
            new BusinessRule { Id = "BR-2", Text = "Bir müşteri birden çok sipariş verebilir." }
        }
    };

    private static (ModelPipeline, FakeModelProvider) Build()
    {
        var provider = new FakeModelProvider { AttributesResponse = FakeModelProvider.NormalizedAttributes };
        return (new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model"), provider);
    }

    // ------------------------------------------------------------ odak

    [Fact]
    public async Task AlanTipiBelirsizligi_ModeleGirmez()
    {
        var (pipeline, provider) = Build();
        provider.GapsResponseOverride = """
            {
              "ambiguities": [
                { "target": "MUSTERI.UNVAN", "kind": "DataType", "impact": "High",
                  "description": "Unvan alanı kaç karakter olmalı?", "confidence": 0.3 },
                { "target": "MUSTERI → SIPARIS", "kind": "Cardinality", "impact": "High",
                  "description": "Bir sipariş birden çok müşteriye ait olabilir mi?", "confidence": 0.3 }
              ]
            }
            """;

        var package = await pipeline.GenerateAsync(new PipelineContext { Input = SampleInput() });

        Assert.DoesNotContain(package.Ambiguities, a => a.Kind == AmbiguityKind.DataType);
        Assert.Contains(package.Ambiguities, a => a.Kind == AmbiguityKind.Cardinality);
        Assert.Contains(package.NormalizationNotes, n => n.Contains("alan tipi belirsizliği elendi"));
    }

    [Fact]
    public async Task TipKelimesiGecenAciklama_TuruDogruOlsaDaElenir()
    {
        var (pipeline, provider) = Build();
        provider.GapsResponseOverride = """
            {
              "ambiguities": [
                { "target": "SIPARIS.TUTAR", "kind": "Other", "impact": "High",
                  "description": "Tutar alanı DECIMAL mi olmalı, kaç ondalık basamak?", "confidence": 0.3 }
              ]
            }
            """;

        var package = await pipeline.GenerateAsync(new PipelineContext { Input = SampleInput() });

        Assert.DoesNotContain(package.Ambiguities, a => a.Description.Contains("ondalık basamak"));
    }

    [Fact]
    public async Task IsMantiginiEtkileyenKisit_Elenmez()
    {
        var (pipeline, provider) = Build();
        provider.GapsResponseOverride = """
            {
              "ambiguities": [
                { "target": "SIPARIS.TUTAR", "kind": "Other", "impact": "High",
                  "description": "Sipariş tutarı negatif olabilir mi?", "confidence": 0.3 }
              ]
            }
            """;

        var package = await pipeline.GenerateAsync(new PipelineContext { Input = SampleInput() });

        Assert.Contains(package.Ambiguities, a => a.Description.Contains("negatif olabilir mi"));
    }

    [Fact]
    public void PromptMantiksalYapiyaYonlendirir()
    {
        var prompts = new PromptLibrary();

        var questions = prompts.Get("questions");
        var gaps = prompts.Get("gaps");

        Assert.Contains("MANTIKSAL YAPI", questions);
        Assert.Contains("Alan tipi sorusu sorma", questions);
        Assert.Contains("Tarihçe/versiyonlama", gaps);
        Assert.Contains("Kardinalite", gaps);
    }

    // -------------------------------------------------------- benzerlik

    [Theory]
    [InlineData("Bir sipariş yalnızca tek bir müşteriye mi aittir?",
                "Bir siparişin yalnızca tek müşteriye ait olması gerekir mi?")]
    [InlineData("Müşteri adresi ayrı bir tabloda mı tutulmalı?",
                "Adres bilgisi müşteriden ayrı tabloda mı tutulmalıdır?")]
    public void AyniKonuFarkliKelimelerle_AyniSayilir(string first, string second) =>
        Assert.True(QuestionSimilarity.AreSame(first, second));

    [Theory]
    [InlineData("Bir sipariş yalnızca tek bir müşteriye mi aittir?",
                "Sipariş iptal edildiğinde kayıt siliniyor mu?")]
    [InlineData("Müşteri adresi ayrı bir tabloda mı tutulmalı?",
                "Fatura numarası tekil olmalı mı?")]
    public void FarkliKonular_AyniSayilmaz(string first, string second) =>
        Assert.False(QuestionSimilarity.AreSame(first, second));

    [Fact]
    public void SorulmusListesindekiBenzerSoru_Yakalanir()
    {
        string[] asked =
        [
            "Bir sipariş yalnızca tek bir müşteriye mi aittir?",
            "Fatura numarası tekil olmalı mı?"
        ];

        Assert.True(QuestionSimilarity.IsAlreadyAsked(
            "Bir siparişin tek bir müşteriye ait olması gerekir mi?", asked));

        Assert.False(QuestionSimilarity.IsAlreadyAsked(
            "Ürün stoğu negatif olabilir mi?", asked));
    }

    // ------------------------------------------------- oturum boyunca saklama

    [Fact]
    public async Task CevaplanmayanSoru_SonrakiTurdaTekrarUretilmez()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        var package = await pipeline.GenerateAsync(context);

        var asked = package.Questions[0].Text;

        // Model aynı soruyu farklı kelimelerle tekrar öneriyor.
        provider.GapsResponseOverride = """
            {
              "ambiguities": [
                { "target": "Baska Hedef", "kind": "Cardinality", "impact": "High",
                  "description": "Aynı konu", "confidence": 0.3 }
              ]
            }
            """;
        provider.QuestionsResponseOverride = """
            {
              "questions": [
                { "ambiguityId": "A2",
                  "text": "Bir siparişin yalnızca tek bir müşteriye ait olması gerekir mi?",
                  "reason": "tekrar", "options": [] }
              ]
            }
            """;

        var updated = await pipeline.RequestQuestionsAsync(context);

        Assert.Equal(0, updated.NewQuestionCount);
        Assert.Contains(updated.Questions, q => q.Text == asked);
    }

    [Fact]
    public async Task SorulmusSorular_PromptaTekrarSormaListesiOlarakGider()
    {
        var (pipeline, provider) = Build();
        var context = new PipelineContext { Input = SampleInput() };
        await pipeline.GenerateAsync(context);

        provider.GapsResponseOverride = """
            {
              "ambiguities": [
                { "target": "SIPARIS", "kind": "Requiredness", "impact": "High",
                  "description": "Sipariş iptal edildiğinde kayıt siliniyor mu?", "confidence": 0.3 }
              ]
            }
            """;

        provider.Prompts.Clear();
        await pipeline.RequestQuestionsAsync(context);

        var prompt = provider.Prompts.Single(p => p.Contains("HEDEFLİ SORULARI"));

        Assert.Contains("BUNLARI TEKRAR SORMA", prompt);
        Assert.Contains("cevap bekliyor", prompt);
    }
}
