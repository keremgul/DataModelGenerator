using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Providers;

/// <summary>
/// Kurum içi / özel barındırılan model endpoint'i.
/// İstek formatı Test Plan Generator'daki offline model çağrısıyla birebir aynıdır:
/// POST {BaseUrl}/call_llm, "Api-Key" başlığı, gövde { system_prompt, message, model },
/// yanıtın "message" alanı okunur.
/// </summary>
public class CustomEndpointProvider : ModelProviderBase
{
    public override string ProviderKey => "custom";
    public override string DisplayName => "Özel Endpoint (Offline)";
    public override bool RequiresApiKey => false;
    public override bool RequiresBaseUrl => true;

    public override async Task<List<ModelInfo>> ListModelsAsync(ProviderConnectionSettings settings, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{settings.BaseUrl.TrimEnd('/')}/v1/models");
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            req.Headers.Add("Api-Key", settings.ApiKey);

        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        var data = json?["data"]?.AsArray();
        if (data is null) return [];

        return data
            .Select(m => new ModelInfo(m!["id"]!.GetValue<string>(), m["id"]!.GetValue<string>()))
            .OrderBy(m => m.Id)
            .ToList();
    }

    public override async Task<string> GenerateAsync(ProviderConnectionSettings settings, string modelId,
        string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/call_llm");
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            req.Headers.Add("Api-Key", settings.ApiKey);

        var body = new
        {
            system_prompt = systemPrompt,
            message = userPrompt,
            model = modelId
        };
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        return json?["message"]?.GetValue<string>() ?? string.Empty;
    }
}
