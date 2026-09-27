using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Rules;
using Xunit;

namespace DataModelGenerator.Tests;

public class NamingRulesTests
{
    [Theory]
    [InlineData("Müşteri", "Musteri")]
    [InlineData("Sipariş Detayı", "SiparisDetayi")]
    [InlineData("çalışan görev ataması", "CalisanGorevAtamasi")]
    [InlineData("müşteri_no", "MusteriNo")]
    [InlineData("musteriNo", "MusteriNo")]
    public void ToTechnicalName_TurkceAdlariPascalCaseYapar(string input, string expected) =>
        Assert.Equal(expected, NamingRules.ToTechnicalName(input));

    [Fact]
    public void ToTechnicalName_SozlukKarsiligiVarsaOnuKullanir()
    {
        var glossary = new List<GlossaryTerm>
        {
            new() { Term = "Müşteri", TechnicalName = "Customer" }
        };

        Assert.Equal("Customer", NamingRules.ToTechnicalName("Müşteri", glossary));
    }

    [Fact]
    public void ToTechnicalName_RakamlaBaslayanAdiHarflePrefikslerAlir() =>
        Assert.Equal("N3AylikRapor", NamingRules.ToTechnicalName("3 aylık rapor"));

    [Fact]
    public void ToSnakeUpper_DdlIcinBuyukHarfliSnakeUretir() =>
        Assert.Equal("SIPARIS_DETAYI", NamingRules.ToSnakeUpper("Sipariş Detayı"));

    [Fact]
    public void MakeUnique_CakismayiNumaralandirir()
    {
        var existing = new List<string> { "Musteri", "Musteri2" };
        Assert.Equal("Musteri3", NamingRules.MakeUnique("Musteri", existing));
    }

    [Fact]
    public void MakeUnique_CakismaYoksaAyniAdiDondurur() =>
        Assert.Equal("Siparis", NamingRules.MakeUnique("Siparis", new List<string> { "Musteri" }));
}
