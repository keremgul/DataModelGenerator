using DataModelGenerator.Core.Storage;
using Xunit;

namespace DataModelGenerator.Tests;

public class RuleTextParserTests
{
    [Fact]
    public void Parse_NumaraliListeyiKurallaraCevirir()
    {
        var rules = RuleTextParser.Parse("1. Her müşterinin bir numarası vardır.\n2) Sipariş bir müşteriye aittir.");

        Assert.Equal(2, rules.Count);
        Assert.Equal("BR-1", rules[0].Id);
        Assert.Equal("Her müşterinin bir numarası vardır.", rules[0].Text);
        Assert.Equal("BR-2", rules[1].Id);
    }

    [Fact]
    public void Parse_MadduIsaretleriniTemizler()
    {
        var rules = RuleTextParser.Parse("- Müşteri kaydı zorunludur\n• Sipariş tutarı sıfırdan büyüktür");

        Assert.Equal(2, rules.Count);
        Assert.Equal("Müşteri kaydı zorunludur", rules[0].Text);
        Assert.Equal("Sipariş tutarı sıfırdan büyüktür", rules[1].Text);
    }

    [Fact]
    public void Parse_MevcutIdleriKorur()
    {
        var rules = RuleTextParser.Parse("BR-7: Müşteri silinemez\nKR-2 - Sipariş iptal edilebilir");

        Assert.Equal("BR-7", rules[0].Id);
        Assert.Equal("Müşteri silinemez", rules[0].Text);
        Assert.Equal("KR-2", rules[1].Id);
    }

    [Fact]
    public void Parse_TekrarEdenIdleriBenzersizlestirir()
    {
        var rules = RuleTextParser.Parse("BR-1: birinci\nBR-1: ikinci");

        Assert.Equal("BR-1", rules[0].Id);
        Assert.NotEqual("BR-1", rules[1].Id);
    }

    [Fact]
    public void Parse_BosSatirlariAtlar()
    {
        var rules = RuleTextParser.Parse("Birinci kural\n\n   \nİkinci kural");
        Assert.Equal(2, rules.Count);
    }

    [Fact]
    public void Parse_BosMetindeBosListeDoner() =>
        Assert.Empty(RuleTextParser.Parse("   "));
}
