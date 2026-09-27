using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Providers;

/// <summary>Yerel/offline model sunucusu. API anahtarı gerektirmez.</summary>
public class OllamaProvider : ModelProviderBase
{
    public override string ProviderKey => "ollama";
    public override string DisplayName => "Ollama (Yerel / Offline)";
    public override bool RequiresApiKey => false;
    public override bool RequiresBaseUrl => true;

    public override IReadOnlyList<ModelInfo> KnownModels =>
    [
        new("llama3.1:8b", "Llama 3.1 8B"),
        new("qwen2.5:14b", "Qwen 2.5 14B"),
        new("mistral:7b", "Mistral 7B"),
        new("gemma2:9b", "Gemma 2 9B")
    ];

    private static string BaseUrl(ProviderConnectionSettings settings) =>
        string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? "http://localhost:11434"
            : settings.BaseUrl.TrimEnd('/');

    public override async Task<List<ModelInfo>> ListModelsAsync(ProviderConnectionSettings settings, CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync($"{BaseUrl(settings)}/api/tags", ct);
        resp.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        var models = json?["models"]?.AsArray();
        if (models is null) return [];

        return models
            .Select(m => new ModelInfo(m!["name"]!.GetValue<string>(), m["name"]!.GetValue<string>()))
            .OrderBy(m => m.Id)
            .ToList();
    }

    public override async Task<string> GenerateAsync(ProviderConnectionSettings settings, string modelId,
        string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var body = new
        {
            model = modelId,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync($"{BaseUrl(settings)}/api/chat", content, ct);
        resp.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        return json?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
    }
}
