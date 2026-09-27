using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Providers;

public class OpenAIProvider : ModelProviderBase
{
    public override string ProviderKey => "openai";
    public override string DisplayName => "OpenAI";
    public override bool RequiresApiKey => true;

    public override IReadOnlyList<ModelInfo> KnownModels =>
    [
        new("gpt-4o", "GPT-4o"),
        new("gpt-4o-mini", "GPT-4o mini"),
        new("gpt-4.1", "GPT-4.1"),
        new("gpt-4.1-mini", "GPT-4.1 mini")
    ];

    public override async Task<List<ModelInfo>> ListModelsAsync(ProviderConnectionSettings settings, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        var data = json?["data"]?.AsArray();
        if (data is null) return [];

        return data
            .Select(m => new ModelInfo(m!["id"]!.GetValue<string>(), m["id"]!.GetValue<string>()))
            .Where(m => m.Id.StartsWith("gpt") || m.Id.StartsWith("o1") || m.Id.StartsWith("o3"))
            .OrderBy(m => m.Id)
            .ToList();
    }

    public override async Task<string> GenerateAsync(ProviderConnectionSettings settings, string modelId,
        string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        var body = new
        {
            model = modelId,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.2
        };
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        return json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
    }
}
