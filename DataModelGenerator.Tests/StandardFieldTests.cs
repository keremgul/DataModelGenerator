using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Rules;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// Kurumsal standart alanlar her tabloya kod tarafında eklenir; kaynak
/// zorunluluğundan, normalizasyondan ve soru üretiminden muaftır.
/// </summary>
public class StandardFieldTests
{
    private static readonly string[] Expected =
        ["GIRISTARIHI", "GIRISKULLANICIKODU", "ISLEMTARIHI", "ISLEMKULLANICIKODU", "DURUMKODU"];

    private static DataModel ModelWith(params EntityAttribute[] attributes)
    {
        var entity = new ModelEntity { Name = "MUSTERI", TechnicalName = "MUSTERI", SourceRuleIds = { "BR-01" } };
        entity.Attributes.Add(new EntityAttribute
        {
            Name = "ID", TechnicalName = "ID", IsPrimaryKey = true, IsRequired = true, IsUnique = true
        });
        entity.Attributes.AddRange(attributes);
        return new DataModel { Entities = { entity } };
    }

    // ----------------------------------------------------------- ekleme

    [Fact]
    public void BesStandartAlanHerTabloyaEklenir()
    {
        var model = ModelWith();
        model.Entities.Add(new ModelEntity { Name = "SIPARIS", TechnicalName = "SIPARIS" });

        StandardFields.Apply(model);

        foreach (var entity in model.Entities)
            Assert.All(Expected, name =>
                Assert.Contains(entity.Attributes, a => a.TechnicalName == name));
    }

    [Fact]
    public void StandartAlanlar_IsStandardVeSTDKaynagiTasir()
    {
        var model = ModelWith();
        StandardFields.Apply(model);

        var field = model.Entities[0].Attributes.Single(a => a.TechnicalName == "DURUMKODU");

        Assert.True(field.IsStandard);
        Assert.Contains(StandardFields.SourceId, field.SourceRuleIds);
        Assert.False(field.IsPrimaryKey);
        Assert.False(field.IsForeignKey);
    }

    [Fact]
    public void TekrarCagrildiginda_AlanCogaltmaz()
    {
        var model = ModelWith();

        StandardFields.Apply(model);
        var afterFirst = model.Entities[0].Attributes.Count;
        StandardFields.Apply(model);

        Assert.Equal(afterFirst, model.Entities[0].Attributes.Count);
    }

    [Fact]
    public void ModelinKendiUrettigiStandartAlan_TekrarEklenmezIsaretlenir()
    {
        var model = ModelWith(new EntityAttribute
        {
            Name = "Giriş Tarihi", TechnicalName = "GIRISTARIHI", SourceRuleIds = { "BR-01" }
        });

        StandardFields.Apply(model);

        var matches = model.Entities[0].Attributes.Where(a => a.TechnicalName == "GIRISTARIHI").ToList();
        Assert.Single(matches);
        Assert.True(matches[0].IsStandard);
        Assert.Contains(StandardFields.SourceId, matches[0].SourceRuleIds);
    }

    [Fact]
    public void IsStandard_AdiTaniyor()
    {
        Assert.True(StandardFields.IsStandard("GIRISTARIHI"));
        Assert.True(StandardFields.IsStandard("Giriş Tarihi"));
        Assert.False(StandardFields.IsStandard("MUSTERINO"));
    }

    // -------------------------------------------------------- muafiyetler

    [Fact]
    public void StandartAlan_KaynaksizDiyeElenmez()
    {
        var entity = new ModelEntity { Name = "MUSTERI", TechnicalName = "MUSTERI", SourceRuleIds = { "BR-01" } };
        entity.Attributes.Add(new EntityAttribute { Name = "DURUMKODU", TechnicalName = "DURUMKODU" });

        var input = new ProjectInput { Rules = { new BusinessRule { Id = "BR-01", Text = "Müşteri vardır." } } };
        var pending = new List<PendingItem>();

        var accepted = SourceGuard.FilterEntities([entity], input, pending);

        Assert.Contains(accepted[0].Attributes, a => a.TechnicalName == "DURUMKODU");
        Assert.Empty(pending);
    }

    [Fact]
    public void StandartAlan_NormalizasyonKurallarindanMuaf()
    {
        var model = ModelWith();
        StandardFields.Apply(model);

        var pending = new List<PendingItem>();
        var results = new NormalizationEngine().Apply(model, pending);

        Assert.All(Expected, name =>
            Assert.Contains(model.Entities[0].Attributes, a => a.TechnicalName == name));

        var result = results.Single(r => r.Attribute == "DURUMKODU");
        Assert.Contains("STD-1", result.AppliedRules);
        Assert.Equal(NormalizationOutcome.Passed, result.Outcome);
    }

    /// <summary>GIRISTARIHI ile ISLEMTARIHI tekrarlayan grup sayılmamalı.</summary>
    [Fact]
    public void StandartAlanlar_TekrarlayanGrupSayilmaz()
    {
        var model = ModelWith();
        StandardFields.Apply(model);

        var pending = new List<PendingItem>();
        new NormalizationEngine().Apply(model, pending);

        Assert.DoesNotContain(pending, p => p.ViolatedRule == "1NF-2");
    }

    // ------------------------------------------------------ soru üretimi

    [Fact]
    public async Task StandartAlanIcinSoruUretilmez()
    {
        var provider = new FakeModelProvider
        {
            AttributesResponse = FakeModelProvider.NormalizedAttributes,
            GapsResponseOverride = """
                {
                  "ambiguities": [
                    { "target": "MUSTERI.DURUMKODU", "kind": "Requiredness", "impact": "High",
                      "description": "DURUMKODU alanı zorunlu mu?", "confidence": 0.3 },
                    { "target": "MUSTERI → SIPARIS", "kind": "Cardinality", "impact": "High",
                      "description": "Bir sipariş birden çok müşteriye ait olabilir mi?", "confidence": 0.3 }
                  ]
                }
                """
        };

        var pipeline = new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model");
        var package = await pipeline.GenerateAsync(new PipelineContext
        {
            Input = new ProjectInput
            {
                Rules =
                {
                    new BusinessRule { Id = "BR-1", Text = "Her müşterinin tekil bir müşteri numarası vardır." },
                    new BusinessRule { Id = "BR-2", Text = "Bir müşteri birden çok sipariş verebilir." }
                }
            }
        });

        Assert.DoesNotContain(package.Ambiguities, a => a.Target.Contains("DURUMKODU"));
        Assert.DoesNotContain(package.Questions, q => q.Text.Contains("DURUMKODU"));
        Assert.Contains(package.Ambiguities, a => a.Kind == AmbiguityKind.Cardinality);
        Assert.Contains(package.NormalizationNotes, n => n.Contains("standart alan belirsizliği elendi"));
    }

    [Fact]
    public async Task Uretim_StandartAlanlariModeleEkler()
    {
        var provider = new FakeModelProvider { AttributesResponse = FakeModelProvider.NormalizedAttributes };
        var pipeline = new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model");

        var package = await pipeline.GenerateAsync(new PipelineContext
        {
            Input = new ProjectInput
            {
                Rules =
                {
                    new BusinessRule { Id = "BR-1", Text = "Her müşterinin tekil bir müşteri numarası vardır." },
                    new BusinessRule { Id = "BR-2", Text = "Bir müşteri birden çok sipariş verebilir." }
                }
            }
        });

        foreach (var entity in package.Model.Entities)
            Assert.All(Expected, name =>
                Assert.Contains(entity.Attributes, a => a.TechnicalName == name));
    }

    [Fact]
    public void IzlenebilirlikTablosu_STDKaynaginiAciklar()
    {
        var model = ModelWith();
        StandardFields.Apply(model);

        var package = new ModelPackage { Model = model };
        var input = new ProjectInput { Rules = { new BusinessRule { Id = "BR-01", Text = "Müşteri vardır." } } };

        var table = TraceabilityTable.Build(package, input);
        var row = table.Entries.Single(e => e.Attribute == "DURUMKODU");

        Assert.Equal("STD", row.Badges);
        Assert.Contains("Kurumsal standart alan", row.Tooltip);
        Assert.DoesNotContain("bulunamadı", row.Tooltip);
    }

    [Fact]
    public void SkillDosyasi_AlanlariBelgeler()
    {
        var text = new SkillLibrary().Get("default-fields");

        Assert.All(Expected, name => Assert.Contains(name, text));
        Assert.Contains("soru üretilmez", text);
    }
}
