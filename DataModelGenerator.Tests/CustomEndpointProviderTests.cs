using System.Net;
using System.Text;
using System.Text.Json;
using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Providers;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// Bir kurumsal nginx/WAF, User-Agent'sız isteği uygulamaya hiç ulaştırmadan
/// 403 ile reddedebiliyor — .NET'in HttpClient'ı tarayıcıların aksine
/// varsayılan olarak bu başlığı eklemiyor. Test Plan Generator'ın aynı
/// mimarideki sağlayıcısında gerçek intranet endpoint'inde (nginx, çıplak
/// 403 sayfası) gözlemlendi; bu dosya aynı düzeltmeyi burada doğrular.
/// </summary>
public sealed class CustomEndpointProviderTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _baseUrl;
    private string? _receivedUserAgent;
    private string? _receivedAccept;

    public CustomEndpointProviderTests()
    {
        var port = 58936;
        _baseUrl = $"http://localhost:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(_baseUrl);
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public void Dispose()
    {
        try { _listener.Stop(); } catch { /* yoksay */ }
        _listener.Close();
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }

            _receivedUserAgent = ctx.Request.Headers["User-Agent"];
            _receivedAccept = ctx.Request.Headers["Accept"];

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { message = "{}", data = Array.Empty<object>() }));
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
    }

    [Fact]
    public async Task GenerateAsync_UserAgentVeAcceptBasliklariniGonderir()
    {
        var provider = new CustomEndpointProvider();
        var settings = new ProviderConnectionSettings { BaseUrl = _baseUrl.TrimEnd('/'), ApiKey = "k" };

        await provider.GenerateAsync(settings, "model", "sistem", "kullanıcı", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(_receivedUserAgent));
        Assert.Contains("application/json", _receivedAccept);
    }

    [Fact]
    public async Task ListModelsAsync_UserAgentBasliginiGonderir()
    {
        var provider = new CustomEndpointProvider();
        var settings = new ProviderConnectionSettings { BaseUrl = _baseUrl.TrimEnd('/'), ApiKey = "k" };

        await provider.ListModelsAsync(settings, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(_receivedUserAgent));
    }
}
