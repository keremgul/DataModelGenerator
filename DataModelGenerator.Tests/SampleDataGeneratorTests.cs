using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Simulation;
using Xunit;

namespace DataModelGenerator.Tests;

public class SampleDataGeneratorTests
{
    private readonly SampleDataGenerator _generator = new(seed: 42);

    [Fact]
    public void Generate_HerVarlikIcinIstenenSatirSayisiniUretir()
    {
        var dataSet = _generator.Generate(TestModels.CustomerOrder(), 15);

        Assert.Equal(2, dataSet.Tables.Count);
        Assert.All(dataSet.Tables, table => Assert.Equal(15, table.Rows.Count));
    }

    [Fact]
    public void Generate_UstSinirDayatmaz()
    {
        var dataSet = _generator.Generate(TestModels.CustomerOrder(), 5000);
        Assert.All(dataSet.Tables, table => Assert.Equal(5000, table.Rows.Count));
    }

    [Fact]
    public void Generate_YabanciAnahtarDegerleriGercektenVarOlanBirincilAnahtarlariReferansVerir()
    {
        var model = TestModels.CustomerOrder();
        var dataSet = _generator.Generate(model, 30);

        var customers = dataSet.Tables.Single(t => t.TechnicalName == "Musteri");
        var orders = dataSet.Tables.Single(t => t.TechnicalName == "Siparis");

        var customerKeyIndex = customers.Columns.FindIndex(c => c.IsPrimaryKey);
        var orderForeignKeyIndex = orders.Columns.FindIndex(c => c.IsForeignKey);

        var validKeys = customers.Rows.Select(r => r[customerKeyIndex]?.ToString()).ToHashSet();
        var usedKeys = orders.Rows.Select(r => r[orderForeignKeyIndex]?.ToString()).ToList();

        Assert.All(usedKeys, key => Assert.Contains(key, validKeys));
    }

    [Fact]
    public void Generate_BirincilAnahtarlariTekilUretir()
    {
        var dataSet = _generator.Generate(TestModels.CustomerOrder(), 50);
        var customers = dataSet.Tables.Single(t => t.TechnicalName == "Musteri");
        var keyIndex = customers.Columns.FindIndex(c => c.IsPrimaryKey);

        var keys = customers.Rows.Select(r => r[keyIndex]?.ToString()).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Generate_ZorunluAlanlariBosBirakmaz()
    {
        var dataSet = _generator.Generate(TestModels.CustomerOrder(), 20);

        foreach (var table in dataSet.Tables)
        {
            for (var c = 0; c < table.Columns.Count; c++)
            {
                if (!table.Columns[c].IsRequired) continue;
                Assert.All(table.Rows, row => Assert.NotNull(row[c]));
            }
        }
    }

    [Fact]
    public void Generate_TipeUygunDegerUretir()
    {
        var model = new DataModel
        {
            Entities =
            {
                new ModelEntity
                {
                    Name = "İşlem", TechnicalName = "Islem",
                    Attributes =
                    {
                        new EntityAttribute { TechnicalName = "IslemId", Name = "İşlem Id", DataType = "BIGINT", IsPrimaryKey = true, IsRequired = true },
                        new EntityAttribute { TechnicalName = "Tutar", Name = "Tutar", DataType = "DECIMAL(18,2)", IsRequired = true },
                        new EntityAttribute { TechnicalName = "IslemTarihi", Name = "İşlem Tarihi", DataType = "DATE", IsRequired = true },
                        new EntityAttribute { TechnicalName = "Aktif", Name = "Aktif", DataType = "BOOLEAN", IsRequired = true }
                    }
                }
            }
        };

        var table = _generator.Generate(model, 10).Tables.Single();

        Assert.All(table.Rows, row =>
        {
            Assert.IsType<int>(row[0]);
            Assert.IsType<double>(row[1]);
            Assert.IsType<DateTime>(row[2]);
            Assert.IsType<bool>(row[3]);
        });
    }

    [Fact]
    public void Generate_DongusellIliskideDeCalisir()
    {
        var model = TestModels.CustomerOrder();
        model.Entities[0].Attributes.Add(new EntityAttribute
        {
            Name = "Son Sipariş", TechnicalName = "SonSiparisNo", DataType = "BIGINT",
            IsForeignKey = true, ReferencesEntity = "Siparis", ReferencesAttribute = "SiparisNo",
            IsRequired = false
        });

        var dataSet = _generator.Generate(model, 10);

        var orders = dataSet.Tables.Single(t => t.TechnicalName == "Siparis");
        var orderKeyIndex = orders.Columns.FindIndex(c => c.IsPrimaryKey);
        var validOrderKeys = orders.Rows.Select(r => r[orderKeyIndex]?.ToString()).ToHashSet();

        var customers = dataSet.Tables.Single(t => t.TechnicalName == "Musteri");
        var lastOrderIndex = customers.Columns.FindIndex(c => c.TechnicalName == "SonSiparisNo");

        Assert.All(customers.Rows, row =>
        {
            var value = row[lastOrderIndex]?.ToString();
            if (value is not null) Assert.Contains(value, validOrderKeys);
        });
    }

    [Fact]
    public void Generate_SifirSatirIstensinDahiEnAzBirSatirUretir() =>
        Assert.Single(_generator.Generate(TestModels.CustomerOrder(), 0).Tables[0].Rows);
}
