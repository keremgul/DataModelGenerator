namespace DataModelGenerator.Core.Providers;

public class ProviderRegistry
{
    private readonly List<IModelProvider> _providers =
    [
        new OpenAIProvider(),
        new AnthropicProvider(),
        new GoogleGeminiProvider(),
        new AzureOpenAIProvider(),
        new OllamaProvider(),
        new CustomEndpointProvider()
    ];

    public IReadOnlyList<IModelProvider> All => _providers;

    public IModelProvider? GetByKey(string key) =>
        _providers.FirstOrDefault(p => p.ProviderKey.Equals(key, StringComparison.OrdinalIgnoreCase));
}
