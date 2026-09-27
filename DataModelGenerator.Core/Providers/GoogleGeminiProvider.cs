using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Providers;

public class GoogleGeminiProvider : ModelProviderBase
{
    private const string ApiBase = "https://generativelanguage.googleapis.com/v1beta";

    public override string ProviderKey => "google";
    public override string DisplayName => "Google Gemini";
    public override bool RequiresApiKey => true;

    public override IReadOnlyList<ModelInfo> KnownModels =>
    [
        new("gemini-2.5-pro", "Gemini 2.5 Pro"),
        new("gemini-2.5-flash", "Gemini 2.5 Flash"),
        new("gemini-2.0-flash", "Gemini 2.0 Flash")
    ];

    public override async Task<List<ModelInfo>> ListModelsAsync(ProviderConnectionSettings settings, CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync($"{ApiBase}/models?key={settings.ApiKey}", ct);
        resp.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        var models = json?["models"]?.AsArray();
        if (models is null) return [];

        return models
            .Where(m => m?["name"]?.GetValue<string>().Contains("gemini") == true)
            .Select(m =>
            {
                var name = m!["name"]!.GetValue<string>();
                return new ModelInfo(name, m["displayName"]?.GetValue<string>() ?? name);
            })
            .OrderByDescending(m => m.Id)
            .ToList();
    }

    public override async Task<string> GenerateAsync(ProviderConnectionSettings settings, string modelId,
        string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var qualifiedId = modelId.StartsWith("models/") ? modelId : $"models/{modelId}";
        var url = $"{ApiBase}/{qualifiedId}:generateContent?key={settings.ApiKey}";

        var body = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
            generationConfig = new { temperature = 0.2 }
        };

        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync(url, content, ct);
        resp.EnsureSuccessStatusCode();

        return ExtractText(await resp.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Düşünme (thought) parçalarını atlayıp metin parçalarını birleştirir.</summary>
    public static string ExtractText(string responseJson)
    {
        var candidate = JsonNode.Parse(responseJson)?["candidates"]?.AsArray().FirstOrDefault();
        var parts = candidate?["content"]?["parts"]?.AsArray();
        if (parts is null) return string.Empty;

        return string.Concat(parts
            .Where(p => p?["thought"]?.GetValue<bool>() != true)
            .Select(p => p?["text"]?.GetValue<string>() ?? string.Empty));
    }
}
