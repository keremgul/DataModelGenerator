using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Rules;
using Xunit;

namespace DataModelGenerator.Tests;

public class RuleEngineTests
{
    private readonly RuleEngine _engine = new();

    [Fact]
    public void Normalize_TeknikAdlariTuretir()
    {
        var model = new DataModel
        {
            Entities =
            {
                new ModelEntity
                {
                    Name = "Sipariş Detayı",
                    Attributes = { new EntityAttribute { Name = "Ürün Adı", RawType = "string" } }
                }
            }
        };

        _engine.Normalize(model, new ProjectInput());

        Assert.Equal("SiparisDetayi", model.Entities[0].TechnicalName);
        Assert.Equal("UrunAdi", model.Entities[0].Attributes.Single(a => a.Name == "Ürün Adı").TechnicalName);
    }

    [Fact]
    public void Normalize_BirincilAnahtarYoksaVekilAnahtarEkler()
    {
        var model = new DataModel
        {
            Entities =
            {
                new ModelEntity { Name = "Müşteri", Attributes = { new EntityAttribute { Name = "Ad" } } }
            }
        };

        var notes = _engine.Normalize(model, new ProjectInput());
        var entity = model.Entities[0];

        Assert.NotNull(entity.PrimaryKey);
        Assert.Equal("MusteriId", entity.PrimaryKey!.TechnicalName);
        Assert.Contains(notes, n => n.Kind == AmbiguityKind.PrimaryKey && n.Impact == AmbiguityImpact.High);
    }

    [Fact]
    public void Normalize_TipiBelirtilmemisAlaniDusukEtkiliBelirsizlikOlarakKaydeder()
    {
        var model = new DataModel
        {
            Entities =
            {
                new ModelEntity
                {
                    Name = "Müşteri",
                    Attributes =
                    {
                        new EntityAttribute { Name = "Müşteri No", IsPrimaryKey = true },
                        new EntityAttribute { Name = "Doğum Tarihi" }
                    }
                }
            }
        };

        var notes = _engine.Normalize(model, new ProjectInput());

        Assert.Equal("DATE", model.Entities[0].Attributes.Single(a => a.Name == "Doğum Tarihi").DataType);
        Assert.Contains(notes, n => n.Kind == AmbiguityKind.DataType &&
                                    n.Impact == AmbiguityImpact.Low && n.IsResolved);
    }

    [Fact]
    public void Normalize_CakisanTeknikAdiBenzersizlestirir()
    {
        var model = new DataModel
        {
            Entities =
            {
                new ModelEntity { Name = "Müşteri", Attributes = { new EntityAttribute { Name = "Id", IsPrimaryKey = true } } },
                new ModelEntity { Name = "Musteri", Attributes = { new EntityAttribute { Name = "Id", IsPrimaryKey = true } } }
            }
        };

        _engine.Normalize(model, new ProjectInput());

        Assert.Equal("Musteri", model.Entities[0].TechnicalName);
        Assert.Equal("Musteri2", model.Entities[1].TechnicalName);
    }

    [Fact]
    public void Normalize_YabanciAnahtarTipiniHedefAnahtarlaHizalar()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[1].Attributes[2].DataType = "VARCHAR(10)";
        model.Entities[1].Attributes[2].RawType = "VARCHAR(10)";

        _engine.Normalize(model, new ProjectInput());

        Assert.Equal("BIGINT", model.Entities[1].Attributes[2].DataType);
    }

    [Fact]
    public void Normalize_BelirsizKardinaliteyiVarsayilanaCekerVeYuksekEtkiliIsaretler()
    {
        var model = TestModels.CustomerOrder();
        model.Relationships[0].Cardinality = Cardinality.Unknown;

        var notes = _engine.Normalize(model, new ProjectInput());

        Assert.Equal(Cardinality.OneToMany, model.Relationships[0].Cardinality);
        Assert.Contains(notes, n => n.Kind == AmbiguityKind.Cardinality && n.Impact == AmbiguityImpact.High);
    }

    [Fact]
    public void SyncForeignKeys_IliskiIcinEksikYabanciAnahtariEkler()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[1].Attributes.RemoveAt(2);

        ModelMerger.SyncForeignKeys(model);

        var foreignKey = model.Entities[1].Attributes.SingleOrDefault(a => a.IsForeignKey);
        Assert.NotNull(foreignKey);
        Assert.Equal("Musteri", foreignKey!.ReferencesEntity);
        Assert.Equal("MusteriNo", foreignKey.ReferencesAttribute);
        Assert.Equal("BIGINT", foreignKey.DataType);
    }

    [Fact]
    public void SyncForeignKeys_KarsiligiOlmayanYabanciAnahtarIcinIliskiUretir()
    {
        var model = TestModels.CustomerOrder();
        model.Relationships.Clear();

        ModelMerger.SyncForeignKeys(model);

        var relationship = Assert.Single(model.Relationships);
        Assert.Equal("Musteri", relationship.FromEntity);
        Assert.Equal("Siparis", relationship.ToEntity);
        Assert.Equal(Cardinality.OneToMany, relationship.Cardinality);
    }

    [Fact]
    public void SyncForeignKeys_TekrarCagrildigindaAlanCogaltmaz()
    {
        var model = TestModels.CustomerOrder();

        ModelMerger.SyncForeignKeys(model);
        ModelMerger.SyncForeignKeys(model);

        Assert.Single(model.Entities[1].Attributes.Where(a => a.IsForeignKey));
        Assert.Single(model.Relationships);
    }

    [Fact]
    public void SyncForeignKeys_NNIliskisiIcinOtomatikAraTabloOlusturmaz()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[1].Attributes.RemoveAt(2);
        model.Relationships[0].Cardinality = Cardinality.ManyToMany;

        ModelMerger.SyncForeignKeys(model);

        Assert.Equal(2, model.Entities.Count);
        Assert.DoesNotContain(model.Entities[1].Attributes, a => a.IsForeignKey);
    }
}
