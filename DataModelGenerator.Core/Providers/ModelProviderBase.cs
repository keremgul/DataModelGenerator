using System.Net;
using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Services;

namespace DataModelGenerator.Core.Providers;

public abstract class ModelProviderBase : IModelProvider
{
    protected static readonly HttpClient Http =
        new(new LoggingHttpHandler()) { Timeout = TimeSpan.FromMinutes(10) };

    private const string ProbeSystemPrompt = "Sen bir bağlantı testi asistanısın. Yalnızca istenen kelimeyi yaz.";
    private const string ProbeUserPrompt = "Sadece şu kelimeyi yaz: OK";

    public abstract string ProviderKey { get; }
    public abstract string DisplayName { get; }
    public abstract bool RequiresApiKey { get; }
    public virtual bool RequiresBaseUrl => false;
    public virtual IReadOnlyList<ModelInfo> KnownModels => [];

    public abstract Task<List<ModelInfo>> ListModelsAsync(ProviderConnectionSettings settings, CancellationToken ct = default);

    public abstract Task<string> GenerateAsync(ProviderConnectionSettings settings, string modelId,
        string systemPrompt, string userPrompt, CancellationToken ct = default);

    /// <summary>Zorunlu alanlar eksikse hata mesajı döner; her şey tamamsa null.</summary>
    public string? ValidateSettings(ProviderConnectionSettings settings, string modelId)
    {
        if (RequiresApiKey && string.IsNullOrWhiteSpace(settings.ApiKey))
            return $"{DisplayName} için API anahtarı zorunludur.";

        if (RequiresBaseUrl && string.IsNullOrWhiteSpace(settings.BaseUrl))
            return $"{DisplayName} için endpoint URL zorunludur.";

        if (string.IsNullOrWhiteSpace(modelId))
            return "Model adı girilmedi.";

        return null;
    }

    public virtual async Task<ConnectionTestResult> TestConnectionAsync(
        ProviderConnectionSettings settings, string modelId, CancellationToken ct = default)
    {
        var validationError = ValidateSettings(settings, modelId);
        if (validationError is not null)
            return ConnectionTestResult.Fail(validationError);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));

            var reply = await GenerateAsync(settings, modelId, ProbeSystemPrompt, ProbeUserPrompt, timeout.Token);

            return string.IsNullOrWhiteSpace(reply)
                ? ConnectionTestResult.Fail("Endpoint yanıt verdi ama model boş bir tamamlama döndürdü — geçersiz yanıt formatı veya model adı hatalı olabilir.")
                : ConnectionTestResult.Ok(reply.Trim());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ConnectionTestResult.Fail("Zaman aşımı: endpoint 60 saniye içinde yanıt vermedi.");
        }
        catch (HttpRequestException ex)
        {
            return ConnectionTestResult.Fail(DescribeHttpError(ex));
        }
        catch (Exception ex)
        {
            return ConnectionTestResult.Fail($"Beklenmedik hata: {ex.Message}");
        }
    }

    public override string ToString() => DisplayName;

    public static string DescribeHttpError(HttpRequestException ex) => ex.StatusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            $"Kimlik doğrulama hatası (HTTP {(int)ex.StatusCode}): API anahtarı geçersiz veya yetkisiz.",
        HttpStatusCode.NotFound =>
            "Endpoint veya model bulunamadı (HTTP 404): URL ile model adını kontrol edin.",
        HttpStatusCode.TooManyRequests =>
            "Kota/limit aşıldı (HTTP 429).",
        not null =>
            $"Sunucu hata döndürdü (HTTP {(int)ex.StatusCode}): {ex.Message}",
        _ =>
            $"Bağlantı kurulamadı: {ex.Message}"
    };
}
