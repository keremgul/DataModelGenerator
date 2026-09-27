using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Providers;

public interface IModelProvider
{
    string ProviderKey { get; }
    string DisplayName { get; }

    /// <summary>Offline/yerel sağlayıcılarda false — API anahtarı opsiyoneldir.</summary>
    bool RequiresApiKey { get; }

    /// <summary>Endpoint URL'inin kullanıcı tarafından girilmesi zorunlu mu?</summary>
    bool RequiresBaseUrl { get; }

    /// <summary>
    /// Sağlayıcının bilinen modelleri. Canlı listeleme API anahtarı gerektirdiği için
    /// kullanıcı, anahtarı girmeden önce buradan model seçebilir.
    /// </summary>
    IReadOnlyList<ModelInfo> KnownModels { get; }

    Task<List<ModelInfo>> ListModelsAsync(ProviderConnectionSettings settings, CancellationToken ct = default);

    Task<string> GenerateAsync(ProviderConnectionSettings settings, string modelId,
        string systemPrompt, string userPrompt, CancellationToken ct = default);

    /// <summary>
    /// Endpoint'in ayakta olması yeterli değildir: modelin gerçekten yanıt ürettiği,
    /// ufak bir completion çağrısıyla doğrulanır.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(ProviderConnectionSettings settings, string modelId,
        CancellationToken ct = default);
}
