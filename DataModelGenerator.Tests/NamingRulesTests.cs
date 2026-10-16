using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Rules;
using Xunit;

namespace DataModelGenerator.Tests;

public class NamingRulesTests
{
    [Theory]
    [InlineData("Müşteri", "MUSTERI")]
    [InlineData("Sipariş Detayı", "SIPARISDETAYI")]
    [InlineData("çalışan görev ataması", "CALISANGOREVATAMASI")]
    [InlineData("müşteri_no", "MUSTERINO")]
    [InlineData("musteriNo", "MUSTERINO")]
    [InlineData("MUSTERINO", "MUSTERINO")]
    public void ToTechnicalName_BuyukHarfliVeTurkceKarakdersizUretir(string input, string expected) =>
        Assert.Equal(expected, NamingRules.ToTechnicalName(input));

    [Fact]
    public void ToTechnicalName_SozlukKarsiligiVarsaOnuKullanir()
    {
        var glossary = new List<GlossaryTerm>
        {
            new() { Term = "Müşteri", TechnicalName = "Customer" }
        };

        Assert.Equal("CUSTOMER", NamingRules.ToTechnicalName("Müşteri", glossary));
    }

    [Fact]
    public void ToTechnicalName_RakamlaBaslayanAdiHarflePrefikslerAlir() =>
        Assert.Equal("N3AYLIKRAPOR", NamingRules.ToTechnicalName("3 aylık rapor"));

    /// <summary>Düzeltme ikinci geçişte adı tekrar değiştirmemeli.</summary>
    [Fact]
    public void ToTechnicalName_Idempotenttir()
    {
        var once = NamingRules.ToTechnicalName("Sipariş Detayı");
        Assert.Equal(once, NamingRules.ToTechnicalName(once));
        Assert.True(NamingRules.IsCompliant(once));
    }

    [Fact]
    public void MakeUnique_CakismayiNumaralandirir()
    {
        var existing = new List<string> { "MUSTERI", "MUSTERI2" };
        Assert.Equal("MUSTERI3", NamingRules.MakeUnique("MUSTERI", existing));
    }

    [Fact]
    public void MakeUnique_CakismaYoksaAyniAdiDondurur() =>
        Assert.Equal("SIPARIS", NamingRules.MakeUnique("SIPARIS", new List<string> { "MUSTERI" }));

    // ------------------------------------------------------- yabancı anahtar

    [Fact]
    public void YabanciAnahtar_TabloAdiArtiID() =>
        Assert.Equal("MUSTERIID", NamingRules.DefaultForeignKeyName("MUSTERI"));

    [Fact]
    public void YabanciAnahtar_RolVerildigindeRolOnekiAlir() =>
        Assert.Equal("GONDERENMUSTERIID", NamingRules.ForeignKeyName("MUSTERI", "Gönderen"));

    // ------------------------------------------------- konfigüre edilebilirlik

    [Fact]
    public void Politika_AltCizgiliFormataGecirilebilir()
    {
        var policy = new NamingPolicy { Case = NameCase.Upper, Separator = NameSeparator.Underscore };

        Assert.Equal("SIPARIS_DETAYI", NamingRules.ToTechnicalName("Sipariş Detayı", policy));
    }

    [Fact]
    public void Politika_PascalCaseGeriAlinabilir()
    {
        var policy = new NamingPolicy { Case = NameCase.Pascal, Separator = NameSeparator.None };

        Assert.Equal("SiparisDetayi", NamingRules.ToTechnicalName("Sipariş Detayı", policy));
    }

    [Fact]
    public void Politika_MaksimumUzunlukUygulanir()
    {
        var policy = new NamingPolicy { MaxLength = 8 };

        Assert.Equal("SIPARISD", NamingRules.ToTechnicalName("Sipariş Detayı", policy));
    }

    [Fact]
    public void Politika_TurkceKatlamaKapatilabilir()
    {
        var policy = new NamingPolicy { FoldTurkish = false };

        Assert.Equal("MÜŞTERI", NamingRules.ToTechnicalName("Müşteri", policy));
    }

    [Fact]
    public void PolitikaMetni_PromptIcinKurallariAnlatir()
    {
        var text = NamingPolicy.Default.Describe();

        Assert.Contains("BÜYÜK HARF", text);
        Assert.Contains("Ç→C", text);
        Assert.Contains("ayraç kullanılmaz", text);
    }
}
