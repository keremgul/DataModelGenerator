using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Validation;
using Xunit;

namespace DataModelGenerator.Tests;

public class ModelValidatorTests
{
    private readonly ModelValidator _validator = new();

    [Fact]
    public void Validate_TutarliModeldeHataUretmez()
    {
        var issues = _validator.Validate(TestModels.CustomerOrder());
        Assert.DoesNotContain(issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Validate_AyniTeknikAdaSahipVarliklariYakalar()
    {
        var model = TestModels.CustomerOrder();
        model.Entities.Add(new ModelEntity
        {
            Name = "Musteri Kopya",
            TechnicalName = "Musteri",
            Attributes = { new EntityAttribute { TechnicalName = "Id", IsPrimaryKey = true, IsRequired = true } }
        });

        Assert.Contains(_validator.Validate(model), i => i.Code == "NAME_COLLISION_ENTITY");
    }

    [Fact]
    public void Validate_AyniAdliAlanlariYakalar()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[0].Attributes.Add(new EntityAttribute
        {
            Name = "Ad Soyad", TechnicalName = "AdSoyad"
        });

        Assert.Contains(_validator.Validate(model), i => i.Code == "NAME_COLLISION_ATTRIBUTE");
    }

    [Fact]
    public void Validate_BirincilAnahtarsizVarligiYakalar()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[0].Attributes[0].IsPrimaryKey = false;

        Assert.Contains(_validator.Validate(model), i => i.Code == "MISSING_PK");
    }

    [Fact]
    public void Validate_OlmayanVarligaIsaretEdenYabanciAnahtariYakalar()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[1].Attributes[2].ReferencesEntity = "Tedarikci";

        Assert.Contains(_validator.Validate(model), i => i.Code == "FK_TARGET_MISSING");
    }

    [Fact]
    public void Validate_OlmayanAlanaIsaretEdenYabanciAnahtariYakalar()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[1].Attributes[2].ReferencesAttribute = "OlmayanAlan";

        Assert.Contains(_validator.Validate(model), i => i.Code == "FK_COLUMN_MISSING");
    }

    [Fact]
    public void Validate_AnahtarOlmayanHedefeIsaretEdenYabanciAnahtariYakalar()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[1].Attributes[2].ReferencesAttribute = "AdSoyad";

        Assert.Contains(_validator.Validate(model), i => i.Code == "FK_TARGET_NOT_KEY");
    }

    [Fact]
    public void Validate_ZorunluYabanciAnahtarDongusunuYakalar()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[0].Attributes.Add(new EntityAttribute
        {
            Name = "Son Sipariş", TechnicalName = "SonSiparisNo",
            IsForeignKey = true, ReferencesEntity = "Siparis", ReferencesAttribute = "SiparisNo",
            IsRequired = true
        });

        var issues = _validator.Validate(model);
        Assert.Contains(issues, i => i.Code == "CIRCULAR_REFERENCE" && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Validate_KendineReferansiUyariOlarakRaporlar()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[0].Attributes.Add(new EntityAttribute
        {
            Name = "Üst Müşteri", TechnicalName = "UstMusteriNo",
            IsForeignKey = true, ReferencesEntity = "Musteri", ReferencesAttribute = "MusteriNo",
            IsRequired = true
        });

        var issues = _validator.Validate(model);
        Assert.Contains(issues, i => i.Code == "SELF_REFERENCE" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Validate_OpsiyonelYabanciAnahtarDonguSaymaz()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[0].Attributes.Add(new EntityAttribute
        {
            Name = "Son Sipariş", TechnicalName = "SonSiparisNo",
            IsForeignKey = true, ReferencesEntity = "Siparis", ReferencesAttribute = "SiparisNo",
            IsRequired = false
        });

        Assert.DoesNotContain(_validator.Validate(model), i => i.Code == "CIRCULAR_REFERENCE");
    }

    [Fact]
    public void Validate_KaynakKuraliOlmayanVarligiUyariOlarakIsaretler()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[0].SourceRuleIds.Clear();

        Assert.Contains(_validator.Validate(model), i => i.Code == "NO_TRACEABILITY");
    }

    [Fact]
    public void Validate_AraTablosuzNNIliskisiniUyariOlarakIsaretler()
    {
        var model = TestModels.CustomerOrder();
        model.Relationships[0].Cardinality = Cardinality.ManyToMany;

        Assert.Contains(_validator.Validate(model), i => i.Code == "MANY_TO_MANY_WITHOUT_JUNCTION");
    }
}
