using System.Text;

namespace DataModelGenerator.Core.Services;

/// <summary>
/// Her HTTP isteğini/yanıtını kaydeder; hassas başlıklar maskelenir.
/// Bağlantı testi başarısız olduğunda kullanıcıya gösterilecek ham detay buradan gelir.
/// </summary>
public class LoggingHttpHandler : DelegatingHandler
{
    private static readonly string[] SensitiveHeaders =
        ["Authorization", "x-api-key", "api-key", "Api-Key"];

    public LoggingHttpHandler() : base(new HttpClientHandler()) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var entry = new ApiLogEntry
        {
            Method = request.Method.Method,
            Url = MaskQueryKey(request.RequestUri?.ToString() ?? string.Empty)
        };

        var headerSb = new StringBuilder();
        foreach (var header in request.Headers)
        {
            var value = SensitiveHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase)
                ? "***"
                : string.Join(", ", header.Value);
            headerSb.AppendLine($"{header.Key}: {value}");
        }
        entry.RequestHeaders = headerSb.ToString().TrimEnd();

        if (request.Content != null)
        {
            entry.RequestBody = await request.Content.ReadAsStringAsync(ct);
            var mediaType = request.Content.Headers.ContentType?.MediaType ?? "application/json";
            request.Content = new StringContent(entry.RequestBody, Encoding.UTF8, mediaType);
        }

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            entry.NetworkError = ex.Message;
            ApiLogger.Record(entry);
            throw;
        }

        entry.StatusCode = (int)response.StatusCode;
        entry.ResponseBody = await response.Content.ReadAsStringAsync(ct);
        var responseMediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
        response.Content = new StringContent(entry.ResponseBody, Encoding.UTF8, responseMediaType);

        ApiLogger.Record(entry);
        return response;
    }

    /// <summary>Gemini gibi anahtarı query string'de taşıyan sağlayıcılar için.</summary>
    private static string MaskQueryKey(string url)
    {
        var index = url.IndexOf("key=", StringComparison.OrdinalIgnoreCase);
        if (index < 0) return url;

        var end = url.IndexOf('&', index);
        return end < 0
            ? url[..(index + 4)] + "***"
            : url[..(index + 4)] + "***" + url[end..];
    }
}
