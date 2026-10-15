using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Rules;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// İzlenebilirlik tablosu: hangi alan hangi kurala ve hangi soru-cevaba dayanıyor,
/// iki yönlü gezinme (alan → kaynak, kaynak → alanlar) ve kısa rozetler.
/// </summary>
public class TraceabilityTableTests
{
    private static ProjectInput Input() => new()
    {
        Rules =
        {
            new BusinessRule { Id = "BR-01", Text = "Her müşterinin tekil bir kimliği vardır." },
            new BusinessRule { Id = "BR-02", Text = "Müşterinin vergi numarası saklanır." },
            new BusinessRule
            {
                Id = "Q-01",
                Origin = RuleOrigin.Clarification,
                Text = "Bir sipariş yalnızca tek bir müşteriye aittir."
            }
        }
    };

    private static ModelPackage Package()
    {
        var model = new DataModel();
        var musteri = new ModelEntity
        {
            Name = "Müşteri",
            TechnicalName = "Musteri",
            SourceRuleIds = { "BR-01" },
            Attributes =
            {
                new EntityAttribute { Name = "ID", TechnicalName = "ID", IsPrimaryKey = true },
                new EntityAttribute
                {
                    Name = "Vergi No", TechnicalName = "VergiNo", SourceRuleIds = { "BR-02" }
                },
                new EntityAttribute
                {
                    Name = "Sipariş Sahibi", TechnicalName = "SiparisSahibi", SourceRuleIds = { "Q-01" }
                }
            }
        };

        model.Entities.Add(musteri);

        return new ModelPackage
        {
            Model = model,
            Questions =
            {
                new ClarifyingQuestion
                {
                    Id = "S1",
                    Text = "Bir sipariş birden çok müşteriye ait olabilir mi?",
                    Answer = "Hayır, tek müşteriye aittir.",
                    InjectedRuleId = "Q-01",
                    IsApplied = true
                }
            },
            NormalizationResults =
            {
                new NormalizationResult
                {
                    Entity = "Musteri",
                    Attribute = "VergiNo",
                    Outcome = NormalizationOutcome.Passed,
                    Explanation = "Atomik ve tek anlamlı."
                }
            }
        };
    }

    [Fact]
    public void HerAlanIcinSatirUretir()
    {
        var table = TraceabilityTable.Build(Package(), Input());

        Assert.Equal(3, table.Entries.Count);
        Assert.Contains(table.Entries, e => e.Attribute == "VergiNo");
    }

    [Fact]
    public void KaynaksizAlan_VarliginKaynaginiDevralir()
    {
        var table = TraceabilityTable.Build(Package(), Input());

        var pk = table.Entries.Single(e => e.Attribute == "ID");
        Assert.Equal("BR-01", pk.Badges);
        Assert.Equal("PK", pk.KeyText);
    }

    [Fact]
    public void RozetKisa_UzunMetinTooltipte()
    {
        var table = TraceabilityTable.Build(Package(), Input());
        var row = table.Entries.Single(e => e.Attribute == "VergiNo");

        Assert.Equal("BR-02", row.Badges);
        Assert.Contains("Müşterinin vergi numarası saklanır.", row.Tooltip);
    }

    [Fact]
    public void SoruKaynagi_SoruVeCevapMetniniTasir()
    {
        var table = TraceabilityTable.Build(Package(), Input());
        var row = table.Entries.Single(e => e.Attribute == "SiparisSahibi");

        Assert.Contains("birden çok müşteriye", row.QuestionSummary);
        Assert.Contains("tek müşteriye aittir", row.AnswerSummary);
        Assert.True(row.Sources.Single().IsQuestion);
    }

    [Fact]
    public void TersYon_BirKaynaktanTureyenAlanlariListeler()
    {
        var table = TraceabilityTable.Build(Package(), Input());

        var derived = table.EntriesOf("BR-01").ToList();

        Assert.Single(derived);
        Assert.Equal("ID", derived[0].Attribute);
    }

    [Fact]
    public void KullanimSayisi_HerKaynakIcinHesaplanir()
    {
        var table = TraceabilityTable.Build(Package(), Input());

        Assert.Equal("1 alan", table.Sources.Single(s => s.RuleId == "BR-02").UsageText);
        Assert.Equal(3, table.Sources.Count);
    }

    [Fact]
    public void ModeleYansimamisKaynak_AyriListelenir()
    {
        var input = Input();
        input.Rules.Add(new BusinessRule { Id = "BR-03", Text = "Kargo firması bilgisi tutulur." });

        var table = TraceabilityTable.Build(Package(), input);

        Assert.Contains(table.UnusedSources, s => s.RuleId == "BR-03");
        Assert.Equal("modele yansımadı", table.Sources.Single(s => s.RuleId == "BR-03").UsageText);
    }

    [Fact]
    public void NormalizasyonDurumu_SatiraBaglanir()
    {
        var table = TraceabilityTable.Build(Package(), Input());
        var row = table.Entries.Single(e => e.Attribute == "VergiNo");

        Assert.Equal("GEÇTİ", row.NormalizationStatus);
        Assert.Contains("Atomik", row.Tooltip);
    }

    [Fact]
    public void KuralListesindeOlmayanKaynak_SessizceDusurulmez()
    {
        var package = Package();
        package.Model.Entities[0].Attributes[1].SourceRuleIds[0] = "BR-99";

        var table = TraceabilityTable.Build(package, Input());
        var row = table.Entries.Single(e => e.Attribute == "VergiNo");

        Assert.Equal("BR-99", row.Badges);
        Assert.Contains("bulunamadı", row.Tooltip);
    }

    [Fact]
    public void UzunMetin_TablodaKisaltilir()
    {
        var input = Input();
        input.Rules[1].Text = new string('x', 200);

        var table = TraceabilityTable.Build(Package(), input);
        var row = table.Entries.Single(e => e.Attribute == "VergiNo");

        Assert.True(row.RuleSummary.Length <= 70);
        Assert.EndsWith("…", row.RuleSummary);
    }
}
