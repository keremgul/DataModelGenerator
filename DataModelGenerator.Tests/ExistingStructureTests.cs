using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Rules;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// Yüklenen mevcut yapı dosyası: oradaki tablolar referans alınır, yeniden
/// üretilmez; yeni yapılar bunlarla ilişkilendirilir.
/// </summary>
public class ExistingStructureTests
{
    private const string Markdown = """
        # Mevcut Yapı

        ## MUSTERI
        | Alan | Açıklama |
        |---|---|
        | ID | Birincil anahtar |
        | UNVAN | Müşteri unvanı |

        ## Tablo: ADRES
        Müşterinin adres bilgileri.

        ```sql
        CREATE TABLE ILCE (ID INT PRIMARY KEY);
        ```
        """;

    [Fact]
    public void BasliklardanTabloAdlariCikarilir()
    {
        var structure = ExistingStructure.Parse("mevcut.md", Markdown);

        Assert.Contains("MUSTERI", structure.EntityNames);
        Assert.Contains("ADRES", structure.EntityNames);
        Assert.Contains("ILCE", structure.EntityNames);
    }

    [Fact]
    public void BolumBasliklariTabloSanilmaz()
    {
        var structure = ExistingStructure.Parse("mevcut.md", Markdown);

        Assert.DoesNotContain("MEVCUTYAPI", structure.EntityNames);
        Assert.DoesNotContain("ALAN", structure.EntityNames);
        Assert.DoesNotContain("ACIKLAMA", structure.EntityNames);
    }

    [Fact]
    public void TabloAdlari_IsimlendirmeStandardindanGecer()
    {
        var structure = ExistingStructure.Parse("mevcut.md", "## Müşteri Adresi");

        Assert.Contains("MUSTERIADRESI", structure.EntityNames);
    }

    [Fact]
    public void BosDosya_IcerikUretmez()
    {
        var structure = ExistingStructure.Parse("bos.md", "   ");

        Assert.True(structure.IsEmpty);
        Assert.Empty(structure.EntityNames);
        Assert.Contains("yüklenmedi", structure.ToPromptContext());
    }

    [Fact]
    public void PromptBaglami_DosyaAdiTablolarVeIcerigiTasir()
    {
        var context = ExistingStructure.Parse("mevcut.md", Markdown).ToPromptContext();

        Assert.Contains("mevcut.md", context);
        Assert.Contains("MUSTERI", context);
        Assert.Contains("Müşterinin adres bilgileri.", context);
    }

    // -------------------------------------------------------- pipeline

    private static PipelineContext ContextWith(ExistingStructure structure) => new()
    {
        Input = new ProjectInput
        {
            ExistingStructure = structure,
            Rules =
            {
                new BusinessRule { Id = "BR-1", Text = "Her müşterinin tekil bir müşteri numarası vardır." },
                new BusinessRule { Id = "BR-2", Text = "Bir müşteri birden çok sipariş verebilir." }
            }
        }
    };

    [Fact]
    public async Task MevcutYapi_PromptaBaglamOlarakGider()
    {
        var provider = new FakeModelProvider { AttributesResponse = FakeModelProvider.NormalizedAttributes };
        var pipeline = new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model");

        await pipeline.GenerateAsync(ContextWith(ExistingStructure.Parse("mevcut.md", Markdown)));

        var extraction = provider.Prompts.Single(p => p.Contains("VARLIK (entity) adaylarını"));
        Assert.Contains("mevcut.md", extraction);
        Assert.Contains("Müşterinin adres bilgileri.", extraction);
    }

    [Fact]
    public async Task MevcutYapidakiTablo_ModeldeMevcutOlarakIsaretlenir()
    {
        var provider = new FakeModelProvider { AttributesResponse = FakeModelProvider.NormalizedAttributes };
        var pipeline = new ModelPipeline(provider, new ProviderConnectionSettings(), "fake-model");

        var package = await pipeline.GenerateAsync(
            ContextWith(ExistingStructure.Parse("mevcut.md", "## MUSTERI")));

        Assert.Equal(EntityOrigin.Existing, package.Model.FindEntity("MUSTERI")!.Origin);
        Assert.Equal(EntityOrigin.New, package.Model.FindEntity("SIPARIS")!.Origin);
    }

    [Fact]
    public void MevcutYapidakiTabloyaReferans_UyariUretmez()
    {
        var siparis = new ModelEntity { Name = "SIPARIS", TechnicalName = "SIPARIS" };
        siparis.Attributes.Add(new EntityAttribute
        {
            Name = "ID", TechnicalName = "ID", IsPrimaryKey = true
        });
        siparis.Attributes.Add(new EntityAttribute
        {
            Name = "ILCEID", TechnicalName = "ILCEID", SourceRuleIds = { "BR-01" }
        });

        var model = new DataModel { Entities = { siparis } };
        var pending = new List<PendingItem>();

        // ILCE yalnızca mevcut yapı dosyasında var; referans geçerli sayılmalı.
        var notes = RelationshipRules.Apply(model, pending, ["ILCE"]);

        Assert.DoesNotContain(pending, p => p.ViolatedRule == "REL-1");
        Assert.DoesNotContain(notes, n => n.Contains("böyle bir tablo"));
    }

    [Fact]
    public void IzlenebilirlikTablosu_KokenKolonunuTasir()
    {
        var musteri = new ModelEntity
        {
            Name = "MUSTERI", TechnicalName = "MUSTERI",
            Origin = EntityOrigin.Existing, SourceRuleIds = { "BR-01" },
            Attributes = { new EntityAttribute { Name = "ID", TechnicalName = "ID", IsPrimaryKey = true } }
        };

        var package = new ModelPackage { Model = new DataModel { Entities = { musteri } } };
        var input = new ProjectInput { Rules = { new BusinessRule { Id = "BR-01", Text = "Müşteri vardır." } } };

        var table = TraceabilityTable.Build(package, input);

        Assert.Equal("mevcut", table.Entries.Single().OriginText);
    }
}
