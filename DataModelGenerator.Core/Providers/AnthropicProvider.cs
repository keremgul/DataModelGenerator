using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Providers;

public class AnthropicProvider : ModelProviderBase
{
    private const string ApiVersion = "2023-06-01";

    public override string ProviderKey => "anthropic";
    public override string DisplayName => "Anthropic (Claude)";
    public override bool RequiresApiKey => true;

    public override IReadOnlyList<ModelInfo> KnownModels =>
    [
        new("claude-opus-5", "Claude Opus 5"),
        new("claude-sonnet-5", "Claude Sonnet 5"),
        new("claude-haiku-4-5", "Claude Haiku 4.5"),
        new("claude-opus-4-8", "Claude Opus 4.8"),
        new("claude-fable-5-1", "Claude Fable 5.1")
    ];

    public override async Task<List<ModelInfo>> ListModelsAsync(ProviderConnectionSettings settings, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
        req.Headers.Add("x-api-key", settings.ApiKey);
        req.Headers.Add("anthropic-version", ApiVersion);

        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        var data = json?["data"]?.AsArray();
        if (data is null) return [];

        return data
            .Select(m => new ModelInfo(
                m!["id"]!.GetValue<string>(),
                m["display_name"]?.GetValue<string>() ?? m["id"]!.GetValue<string>()))
            .OrderByDescending(m => m.Id)
            .ToList();
    }

    public override async Task<string> GenerateAsync(ProviderConnectionSettings settings, string modelId,
        string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key", settings.ApiKey);
        req.Headers.Add("anthropic-version", ApiVersion);

        var body = new
        {
            model = modelId,
            max_tokens = 16000,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } }
        };
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        return ExtractText(await resp.Content.ReadAsStringAsync(ct));
    }

    /// <summary>
    /// Yanıttaki metin bloklarını birleştirir. Düşünme (thinking) açık modellerde ilk blok
    /// metin olmayabilir; körlemesine content[0] okumak boş yanıt gibi görünür.
    /// </summary>
    public static string ExtractText(string responseJson)
    {
        var json = JsonNode.Parse(responseJson);
        var blocks = json?["content"]?.AsArray();

        var text = blocks is null
            ? string.Empty
            : string.Concat(blocks
                .Where(b => b?["type"]?.GetValue<string>() == "text")
                .Select(b => b!["text"]?.GetValue<string>() ?? string.Empty));

        if (!string.IsNullOrWhiteSpace(text)) return text;

        var stopReason = json?["stop_reason"]?.GetValue<string>();
        throw stopReason switch
        {
            "max_tokens" => new InvalidOperationException(
                "Model yanıtı token sınırına takıldı ve metin üretemedi. Kural listesini kısaltmayı veya " +
                "düşünme (thinking) kullanmayan bir model seçmeyi deneyin."),
            "refusal" => new InvalidOperationException("Model isteği güvenlik gerekçesiyle reddetti."),
            _ => new InvalidOperationException(
                $"Yanıtta metin bloğu yok (stop_reason: {stopReason ?? "bilinmiyor"}).")
        };
    }
}
