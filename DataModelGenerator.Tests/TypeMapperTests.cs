using DataModelGenerator.Core.Rules;
using Xunit;

namespace DataModelGenerator.Tests;

public class TypeMapperTests
{
    [Theory]
    [InlineData("Doğum Tarihi", "DATE")]
    [InlineData("Sipariş Tutarı", "DECIMAL(18,2)")]
    [InlineData("İşlem Zamanı", "TIMESTAMP")]
    [InlineData("Ürün Adedi", "INT")]
    [InlineData("E-Posta", "VARCHAR(254)")]
    [InlineData("TCKN", "CHAR(11)")]
    [InlineData("IBAN", "CHAR(26)")]
    [InlineData("Müşteri No", "VARCHAR(50)")]
    [InlineData("Aktif", "BOOLEAN")]
    public void Map_AlanAdindanTipTuretir(string attributeName, string expected) =>
        Assert.Equal(expected, TypeMapper.Map(attributeName));

    [Fact]
    public void Map_HamTipVarsaOnaOncelikVerir() =>
        Assert.Equal("DECIMAL(9,4)", TypeMapper.Map("Tutar", "DECIMAL(9,4)"));

    [Theory]
    [InlineData("string", "VARCHAR(200)")]
    [InlineData("integer", "INT")]
    [InlineData("datetime", "TIMESTAMP")]
    [InlineData("bool", "BOOLEAN")]
    [InlineData("uuid", "UUID")]
    public void NormalizeRawType_YaygınTakmaAdlariKanonikTipeCevirir(string raw, string expected) =>
        Assert.Equal(expected, TypeMapper.NormalizeRawType(raw));

    [Fact]
    public void NormalizeRawType_TaninmayanTipIcinNullDoner() =>
        Assert.Null(TypeMapper.NormalizeRawType("kırmızı"));

    [Fact]
    public void Map_TipYokVeAdEslesmiyorsaBirincilAnahtariBigintYapar() =>
        Assert.Equal("BIGINT", TypeMapper.Map("Anahtar", null, isPrimaryKey: true));

    [Fact]
    public void GetLength_ParametreliTiptenUzunlukOkur() =>
        Assert.Equal(200, TypeMapper.GetLength("VARCHAR(200)"));

    [Fact]
    public void GetScale_OndalikBasamagiOkur() =>
        Assert.Equal(2, TypeMapper.GetScale("DECIMAL(18,2)"));

    [Fact]
    public void GetLength_ParametresizTiptenNullDoner() =>
        Assert.Null(TypeMapper.GetLength("DATE"));
}
