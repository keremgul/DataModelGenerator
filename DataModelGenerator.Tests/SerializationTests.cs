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
    public void Mermaid_VeriTipiYazmaz()
    {
        var mermaid = MermaidSerializer.Serialize(TestModels.CustomerOrder());

        Assert.Contains("alan Tutar", mermaid);
        Assert.DoesNotContain("DECIMAL", mermaid);
        Assert.DoesNotContain("VARCHAR", mermaid);
        Assert.DoesNotContain("BIGINT", mermaid);
    }

    [Fact]
    public void Mermaid_AnahtarIsaretleriniYazar()
    {
        var mermaid = MermaidSerializer.Serialize(TestModels.CustomerOrder());

        Assert.Contains("alan MusteriNo PK", mermaid);
        Assert.Contains("alan MusteriNo FK", mermaid);
    }

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
