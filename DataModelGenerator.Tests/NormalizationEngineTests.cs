using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Rules;
using Xunit;

namespace DataModelGenerator.Tests;

public class NormalizationEngineTests
{
    private readonly NormalizationEngine _engine = new();

    private static DataModel ModelWith(params EntityAttribute[] attributes)
    {
        var entity = new ModelEntity { Name = "Müşteri", TechnicalName = "Musteri", SourceRuleIds = { "BR-01" } };
        entity.Attributes.Add(new EntityAttribute
        {
            Name = "ID", TechnicalName = "ID", IsPrimaryKey = true, IsRequired = true, IsUnique = true
        });
        entity.Attributes.AddRange(attributes);

        return new DataModel { Entities = { entity } };
    }

    private static EntityAttribute Field(string name) => new()
    {
        Name = name,
        TechnicalName = name.Replace(" ", string.Empty),
        SourceRuleIds = { "BR-01" }
    };

    [Fact]
    public void BilesikAlan1NF1denGecemezVeModeldenCikar()
    {
        var model = ModelWith(Field("Ad Soyad"));
        var pending = new List<PendingItem>();

        var results = _engine.Apply(model, pending);

        Assert.DoesNotContain(model.Entities[0].Attributes, a => a.Name == "Ad Soyad");
        Assert.Contains(results, r => r.ViolatedRule == "1NF-1" && r.Outcome == NormalizationOutcome.Blocked);

        var item = Assert.Single(pending);
        Assert.Equal(PendingReason.NormalizationBlocked, item.Reason);
        Assert.Equal("1NF-1", item.ViolatedRule);
        Assert.NotEmpty(item.Question);
        Assert.NotEmpty(item.SuggestedAnswers);
    }

    [Fact]
    public void CokDegerliAlan1NF3tenGecemez()
    {
        var model = ModelWith(Field("Telefon Listesi"));
        var pending = new List<PendingItem>();

        _engine.Apply(model, pending);

        Assert.DoesNotContain(model.Entities[0].Attributes, a => a.Name == "Telefon Listesi");
        Assert.Equal("1NF-3", pending[0].ViolatedRule);
    }

    [Fact]
    public void TekrarlayanGrup1NF2denGecemez()
    {
        var model = ModelWith(Field("Telefon1"), Field("Telefon2"));
        var pending = new List<PendingItem>();

        _engine.Apply(model, pending);

        Assert.DoesNotContain(model.Entities[0].Attributes, a => a.Name.StartsWith("Telefon"));
        Assert.Contains(pending, p => p.ViolatedRule == "1NF-2");
    }

    [Fact]
    public void TuretilebilirAlan3NF3tenGecemez()
    {
        var model = ModelWith(Field("Toplam Tutar"));
        var pending = new List<PendingItem>();

        _engine.Apply(model, pending);

        Assert.DoesNotContain(model.Entities[0].Attributes, a => a.Name == "Toplam Tutar");
        Assert.Equal("3NF-3", pending[0].ViolatedRule);
    }

    [Fact]
    public void KaynaksizAlanAD1denGecemez()
    {
        var model = ModelWith(new EntityAttribute { Name = "Kredi Notu", TechnicalName = "KrediNotu" });
        var pending = new List<PendingItem>();

        _engine.Apply(model, pending);

        Assert.DoesNotContain(model.Entities[0].Attributes, a => a.Name == "Kredi Notu");
        Assert.Equal("AD-1", pending[0].ViolatedRule);
    }

    [Fact]
    public void HedefiOlmayanYabanciAnahtarREL1denGecemez()
    {
        var model = ModelWith(new EntityAttribute
        {
            Name = "BirimID", TechnicalName = "BirimID",
            IsForeignKey = true, ReferencesEntity = "Birim", SourceRuleIds = { "BR-01" }
        });

        var pending = new List<PendingItem>();
        _engine.Apply(model, pending);

        Assert.DoesNotContain(model.Entities[0].Attributes, a => a.IsForeignKey);
        Assert.Equal("REL-1", pending[0].ViolatedRule);
    }

    [Fact]
    public void BirincilAnahtarsizTabloPK1denGecemez()
    {
        var model = new DataModel
        {
            Entities = { new ModelEntity { Name = "Kampanya", TechnicalName = "Kampanya" } }
        };

        var pending = new List<PendingItem>();
        _engine.Apply(model, pending);

        Assert.Contains(pending, p => p.ViolatedRule == "PK-1");
    }

    [Fact]
    public void TemizAlanlarGecerVeModeldeKalir()
    {
        var model = ModelWith(Field("Müşteri No"), Field("Doğum Tarihi"));
        var pending = new List<PendingItem>();

        var results = _engine.Apply(model, pending);

        Assert.Equal(3, model.Entities[0].Attributes.Count);
        Assert.Empty(pending);
        Assert.All(results, r => Assert.NotEqual(NormalizationOutcome.Blocked, r.Outcome));
        Assert.Contains(results, r => r.StatusText == "GEÇTİ");
    }

    [Fact]
    public void KapsamVerildigindeYalnizcaOTabloNormalizeEdilir()
    {
        var model = ModelWith(Field("Ad Soyad"));
        model.Entities.Add(new ModelEntity
        {
            Name = "Sipariş", TechnicalName = "Siparis", SourceRuleIds = { "BR-02" },
            Attributes =
            {
                new EntityAttribute { Name = "ID", TechnicalName = "ID", IsPrimaryKey = true },
                Field("Adres")
            }
        });

        var pending = new List<PendingItem>();
        _engine.Apply(model, pending, onlyEntities: ["Siparis"]);

        // Kapsam dışındaki Müşteri'ye dokunulmamalı.
        Assert.Contains(model.Entities[0].Attributes, a => a.Name == "Ad Soyad");
        Assert.DoesNotContain(model.Entities[1].Attributes, a => a.Name == "Adres");
    }
}
