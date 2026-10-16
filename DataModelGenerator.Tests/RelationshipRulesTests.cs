using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Rules;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// skills/relationship-naming.md kurallarının kod tarafındaki karşılığı:
/// &lt;TABLOADI&gt;ID ilişkinin kendisidir, mükerrer tanım elenir, hedefi
/// bulunamayan referans uyarıya döner.
/// </summary>
public class RelationshipRulesTests
{
    private static ModelEntity Entity(string technicalName, params EntityAttribute[] attributes)
    {
        var entity = new ModelEntity { Name = technicalName, TechnicalName = technicalName };
        entity.Attributes.Add(new EntityAttribute
        {
            Name = "ID", TechnicalName = "ID", IsPrimaryKey = true, IsRequired = true, IsUnique = true
        });
        entity.Attributes.AddRange(attributes);
        return entity;
    }

    private static EntityAttribute Field(string technicalName) => new()
    {
        Name = technicalName, TechnicalName = technicalName, SourceRuleIds = { "BR-01" }
    };

    // ------------------------------------------------------- ad çözümleme

    [Fact]
    public void TabloAdiArtiID_YabanciAnahtarOlarakCozulur()
    {
        var model = new DataModel { Entities = { Entity("MUSTERI"), Entity("SIPARIS") } };

        var match = RelationshipRules.Resolve("MUSTERIID", model);

        Assert.NotNull(match);
        Assert.Equal("MUSTERI", match!.TargetEntity);
        Assert.False(match.HasRole);
    }

    [Fact]
    public void RolOnekliAd_RolVeHedefOlarakAyrilir()
    {
        var model = new DataModel { Entities = { Entity("MUSTERI"), Entity("TRANSFER") } };

        var gonderen = RelationshipRules.Resolve("GONDERENMUSTERIID", model);
        var alici = RelationshipRules.Resolve("ALICIMUSTERIID", model);

        Assert.Equal("MUSTERI", gonderen!.TargetEntity);
        Assert.Equal("GONDEREN", gonderen.Role);
        Assert.Equal("MUSTERI", alici!.TargetEntity);
        Assert.Equal("ALICI", alici.Role);
    }

    [Fact]
    public void BirincilAnahtar_YabanciAnahtarSayilmaz() =>
        Assert.Null(RelationshipRules.Resolve("ID", new DataModel { Entities = { Entity("MUSTERI") } }));

    [Fact]
    public void HedefiModeldeOlmayanAd_Cozulmez() =>
        Assert.Null(RelationshipRules.Resolve("DEPOID", new DataModel { Entities = { Entity("MUSTERI") } }));

    [Fact]
    public void MevcutYapidakiTablo_HedefOlarakTaninir()
    {
        var model = new DataModel { Entities = { Entity("SIPARIS") } };

        var match = RelationshipRules.Resolve("MUSTERIID", model, externalEntities: ["MUSTERI"]);

        Assert.Equal("MUSTERI", match!.TargetEntity);
    }

    // ----------------------------------------------------- otomatik ilişki

    [Fact]
    public void KalibaUyanAlan_OtomatikFKSayilirVeIliskiKurulur()
    {
        var siparis = Entity("SIPARIS", Field("MUSTERIID"));
        var model = new DataModel { Entities = { Entity("MUSTERI"), siparis } };

        RelationshipRules.Apply(model, new List<PendingItem>());

        var fk = siparis.Attributes.Single(a => a.TechnicalName == "MUSTERIID");
        Assert.True(fk.IsForeignKey);
        Assert.Equal("MUSTERI", fk.ReferencesEntity);
        Assert.Equal("ID", fk.ReferencesAttribute);

        var relationship = Assert.Single(model.Relationships);
        Assert.Equal("MUSTERI", relationship.FromEntity);
        Assert.Equal("SIPARIS", relationship.ToEntity);
    }

    [Fact]
    public void AyniTabloyaIkiRolluReferans_IkiAyriIliskiUretir()
    {
        var transfer = Entity("TRANSFER", Field("GONDERENMUSTERIID"), Field("ALICIMUSTERIID"));
        var model = new DataModel { Entities = { Entity("MUSTERI"), transfer } };

        RelationshipRules.Apply(model, new List<PendingItem>());

        Assert.Equal(2, model.Relationships.Count);
        Assert.Contains(model.Relationships, r => r.Role == "GONDEREN");
        Assert.Contains(model.Relationships, r => r.Role == "ALICI");
    }

    // ---------------------------------------------------------- mükerrer

    [Fact]
    public void AyniCiftIcinMukerrerTanim_Elenir()
    {
        var model = new DataModel { Entities = { Entity("MUSTERI"), Entity("SIPARIS") } };
        model.Relationships.Add(new Relationship { FromEntity = "MUSTERI", ToEntity = "SIPARIS" });
        model.Relationships.Add(new Relationship { FromEntity = "MUSTERI", ToEntity = "SIPARIS" });

        var notes = RelationshipRules.Apply(model, new List<PendingItem>());

        Assert.Single(model.Relationships);
        Assert.Contains(notes, n => n.Contains("Mükerrer ilişki"));
    }

    [Fact]
    public void FarkliRolluIliskiler_MukerrerSayilmaz()
    {
        var model = new DataModel { Entities = { Entity("MUSTERI"), Entity("TRANSFER") } };
        model.Relationships.Add(new Relationship { Role = "GONDEREN", FromEntity = "MUSTERI", ToEntity = "TRANSFER" });
        model.Relationships.Add(new Relationship { Role = "ALICI", FromEntity = "MUSTERI", ToEntity = "TRANSFER" });

        RelationshipRules.Apply(model, new List<PendingItem>());

        Assert.Equal(2, model.Relationships.Count);
    }

    [Fact]
    public void ZatenTanimliIliski_IkinciKezEklenmez()
    {
        var siparis = Entity("SIPARIS", Field("MUSTERIID"));
        var model = new DataModel { Entities = { Entity("MUSTERI"), siparis } };
        model.Relationships.Add(new Relationship { FromEntity = "MUSTERI", ToEntity = "SIPARIS" });

        RelationshipRules.Apply(model, new List<PendingItem>());

        Assert.Single(model.Relationships);
    }

    // ------------------------------------------------------------- uyarı

    [Fact]
    public void HedefiBulunamayanReferans_KullaniciyaUyariVerir()
    {
        var siparis = Entity("SIPARIS");
        siparis.Attributes.Add(new EntityAttribute
        {
            Name = "DEPOID", TechnicalName = "DEPOID",
            IsForeignKey = true, ReferencesEntity = "DEPO", SourceRuleIds = { "BR-02" }
        });

        var model = new DataModel { Entities = { siparis } };
        var pending = new List<PendingItem>();

        var notes = RelationshipRules.Apply(model, pending);

        Assert.Contains(notes, n => n.Contains("DEPO") && n.Contains("böyle bir tablo"));

        var item = Assert.Single(pending, p => p.ViolatedRule == "REL-1");
        Assert.Equal("SIPARIS.DEPOID", item.Target);
        Assert.NotEmpty(item.Question);
        Assert.NotEmpty(item.SuggestedAnswers);
    }

    [Fact]
    public void HedefiBulunamayanReferans_AlaniSessizceSilmez()
    {
        var siparis = Entity("SIPARIS");
        siparis.Attributes.Add(new EntityAttribute
        {
            Name = "DEPOID", TechnicalName = "DEPOID", IsForeignKey = true, ReferencesEntity = "DEPO"
        });

        var model = new DataModel { Entities = { siparis } };
        RelationshipRules.Apply(model, new List<PendingItem>());

        Assert.Contains(siparis.Attributes, a => a.TechnicalName == "DEPOID");
    }

    // ------------------------------------------------- skill tek kaynaktır

    [Fact]
    public void IliskiKurallari_PromptlaraBaglamOlarakGider()
    {
        var skills = new SkillLibrary();
        var text = skills.RelationRules;

        Assert.Contains("<TABLOADI>ID", text);
        Assert.Contains("GONDERENMUSTERIID", text);
        Assert.Contains("SIPARISURUN", text);
    }
}
