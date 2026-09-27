using DataModelGenerator.Core.Providers;
using Xunit;

namespace DataModelGenerator.Tests;

public class ProviderResponseTests
{
    [Fact]
    public void Anthropic_DusunmeBlogundanSonrakiMetniOkur()
    {
        const string response = """
            {
              "content": [
                { "type": "thinking", "thinking": "Kuralları inceliyorum…" },
                { "type": "text", "text": "{\"entities\":[]}" }
              ],
              "stop_reason": "end_turn"
            }
            """;

        Assert.Equal("{\"entities\":[]}", AnthropicProvider.ExtractText(response));
    }

    [Fact]
    public void Anthropic_BirdenCokMetinBlogunuBirlestirir()
    {
        const string response = """
            {
              "content": [
                { "type": "text", "text": "ilk " },
                { "type": "text", "text": "ikinci" }
              ],
              "stop_reason": "end_turn"
            }
            """;

        Assert.Equal("ilk ikinci", AnthropicProvider.ExtractText(response));
    }

    [Fact]
    public void Anthropic_TokenSinirindaAnlamliHataVerir()
    {
        const string response = """
            {
              "content": [ { "type": "thinking", "thinking": "uzun düşünme…" } ],
              "stop_reason": "max_tokens"
            }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => AnthropicProvider.ExtractText(response));
        Assert.Contains("token sınırına", ex.Message);
    }

    [Fact]
    public void Anthropic_ReddedilenIstekIcinAnlamliHataVerir()
    {
        const string response = """
            { "content": [], "stop_reason": "refusal" }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => AnthropicProvider.ExtractText(response));
        Assert.Contains("reddetti", ex.Message);
    }

    [Fact]
    public void Gemini_DusunmeParcasiniAtlar()
    {
        const string response = """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      { "text": "düşünme", "thought": true },
                      { "text": "{\"entities\":[]}" }
                    ]
                  }
                }
              ]
            }
            """;

        Assert.Equal("{\"entities\":[]}", GoogleGeminiProvider.ExtractText(response));
    }

    [Fact]
    public void Gemini_AdayYoksaBosDoner() =>
        Assert.Equal(string.Empty, GoogleGeminiProvider.ExtractText("""{"candidates":[]}"""));
}
