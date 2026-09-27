using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Providers;

public class AzureOpenAIProvider : ModelProviderBase
{
    private const string ApiVersion = "2024-02-01";

    public override string ProviderKey => "azure-openai";
    public override string DisplayName => "Azure OpenAI";
    public override bool RequiresApiKey => true;
    public override bool RequiresBaseUrl => true;

    /// <summary>Azure standart bir model listeleme ucu sunmaz; deployment adı elle girilir.</summary>
    public override Task<List<ModelInfo>> ListModelsAsync(ProviderConnectionSettings settings, CancellationToken ct = default)
    {
        var models = string.IsNullOrWhiteSpace(settings.DeploymentName)
            ? new List<ModelInfo>()
            : [new ModelInfo(settings.DeploymentName, settings.DeploymentName)];

        return Task.FromResult(models);
    }

    public override async Task<string> GenerateAsync(ProviderConnectionSettings settings, string modelId,
        string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var deployment = string.IsNullOrWhiteSpace(modelId) ? settings.DeploymentName : modelId;
        var url = $"{settings.BaseUrl.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={ApiVersion}";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("api-key", settings.ApiKey);

        var body = new
        {
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
