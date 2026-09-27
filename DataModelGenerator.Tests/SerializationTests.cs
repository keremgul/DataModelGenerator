using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Serialization;
using Xunit;

namespace DataModelGenerator.Tests;

public class SerializationTests
{
    [Fact]
    public void Mermaid_ErDiyagramiOlarakBaslar() =>
        Assert.StartsWith("erDiagram", MermaidSerializer.Serialize(TestModels.CustomerOrder()));

    [Fact]
    public void Mermaid_KardinaliteyiDogruNotasyonlaYazar() =>
        Assert.Contains("Musteri ||--o{ Siparis", MermaidSerializer.Serialize(TestModels.CustomerOrder()));

    [Fact]
    public void Mermaid_OpsiyonelIliskiyiKesikliCizgiyleGosterir()
    {
        var model = TestModels.CustomerOrder();
        model.Relationships[0].IsRequired = false;

        Assert.Contains("Musteri ||..o{ Siparis", MermaidSerializer.Serialize(model));
    }

    [Fact]
    public void Mermaid_ParantezliTipleriAyristirmayiBozmayacakSekildeTemizler()
    {
        var mermaid = MermaidSerializer.Serialize(TestModels.CustomerOrder());

        Assert.Contains("DECIMAL_18_2 Tutar", mermaid);
        Assert.DoesNotContain("DECIMAL(18,2)", mermaid);
    }

    [Fact]
    public void Mermaid_AnahtarIsaretleriniYazar()
    {
        var mermaid = MermaidSerializer.Serialize(TestModels.CustomerOrder());

        Assert.Contains("BIGINT MusteriNo PK", mermaid);
        Assert.Contains("BIGINT MusteriNo FK", mermaid);
    }

    [Fact]
    public void Ddl_BirincilVeYabanciAnahtarKisitlariniUretir()
    {
        var ddl = DdlSerializer.Serialize(TestModels.CustomerOrder());

        Assert.Contains("CREATE TABLE Musteri (", ddl);
        Assert.Contains("CONSTRAINT PK_Siparis PRIMARY KEY (SiparisNo)", ddl);
        Assert.Contains("FOREIGN KEY (MusteriNo) REFERENCES Musteri (MusteriNo)", ddl);
    }

    [Fact]
    public void Ddl_ZorunluAlanlariNotNullYazar() =>
        Assert.Contains("Tutar DECIMAL(18,2) NOT NULL", DdlSerializer.Serialize(TestModels.CustomerOrder()));

    [Fact]
    public void ModelJson_SerializeVeDeserializeAyniModeliKorur()
    {
        var json = ModelJson.Serialize(TestModels.CustomerOrder());
        var restored = ModelJson.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Entities.Count);
        Assert.Equal(Cardinality.OneToMany, restored.Relationships[0].Cardinality);
    }

    [Theory]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("İşte sonuç: {\"a\":1} umarım yardımcı olur", "{\"a\":1}")]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    public void ExtractJsonObject_MarkdownVeAciklamaMetniniAyiklar(string raw, string expected) =>
        Assert.Equal(expected, ModelJson.ExtractJsonObject(raw));
}
