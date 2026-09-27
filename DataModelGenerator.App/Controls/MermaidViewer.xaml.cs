using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace DataModelGenerator.App.Controls;

/// <summary>
/// Mermaid ER diyagramını WPF içine gömülü WebView2 üzerinden render eder.
/// mermaid.min.js uygulamayla birlikte dağıtılır; internet bağlantısı gerekmez.
/// </summary>
public partial class MermaidViewer : UserControl
{
    public static readonly DependencyProperty MermaidCodeProperty = DependencyProperty.Register(
        nameof(MermaidCode), typeof(string), typeof(MermaidViewer),
        new PropertyMetadata(string.Empty, OnMermaidCodeChanged));

    private static readonly string OutputPath =
        Path.Combine(Path.GetTempPath(), "DataModelGenerator", "diagram.html");

    private bool _initialized;
    private bool _failed;

    public MermaidViewer()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeAsync();
    }

    public string MermaidCode
    {
        get => (string)GetValue(MermaidCodeProperty);
        set => SetValue(MermaidCodeProperty, value);
    }

    private static void OnMermaidCodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MermaidViewer viewer) viewer.Render();
    }

    private async Task InitializeAsync()
    {
        if (_initialized || _failed) return;

        try
        {
            // Varsayılan kullanıcı verisi klasörü exe'nin yanıdır; portable paket salt-okunur
            // bir konumdan çalıştırıldığında başlatma başarısız olur.
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DataModelGenerator", "WebView2");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await Browser.EnsureCoreWebView2Async(environment);
            _initialized = true;
            Render();
        }
        catch (Exception ex)
        {
            _failed = true;
            Browser.Visibility = Visibility.Collapsed;
            FallbackPanel.Visibility = Visibility.Visible;
            FallbackMessage.Text =
                $"WebView2 çalışma zamanı bulunamadı veya başlatılamadı: {ex.Message}";
        }
    }

    private void Render()
    {
        if (!_initialized || _failed || string.IsNullOrWhiteSpace(MermaidCode)) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
            File.WriteAllText(OutputPath, BuildHtml(MermaidCode));
            Browser.CoreWebView2.Navigate(new Uri(OutputPath).AbsoluteUri);
        }
        catch (Exception ex)
        {
            FallbackPanel.Visibility = Visibility.Visible;
            FallbackMessage.Text = $"Diyagram oluşturulamadı: {ex.Message}";
        }
    }

    /// <summary>
    /// Gömülü mermaid.min.js dosyasını geçici klasöre çıkarır. Tek dosya (single-file)
    /// dağıtımda exe yanında ayrı bir kütüphane dosyası bulunmaz.
    /// </summary>
    private static string? ExtractMermaidScript()
    {
        var scriptPath = Path.Combine(Path.GetDirectoryName(OutputPath)!, "mermaid.min.js");

        try
        {
            if (File.Exists(scriptPath) && new FileInfo(scriptPath).Length > 0)
                return scriptPath;

            var assembly = typeof(MermaidViewer).Assembly;
            using var stream = assembly.GetManifestResourceStream(
                "DataModelGenerator.App.Resources.web.mermaid.min.js");
            if (stream is null) return null;

            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            using var file = File.Create(scriptPath);
            stream.CopyTo(file);
            return scriptPath;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildHtml(string mermaidCode)
    {
        var scriptPath = ExtractMermaidScript();
        var scriptTag = scriptPath is not null
            ? $"<script src=\"{new Uri(scriptPath).AbsoluteUri}\"></script>"
            : "<script src=\"https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js\"></script>";

        return $$"""
                 <!DOCTYPE html>
                 <html lang="tr">
                 <head>
                   <meta charset="utf-8">
                   <style>
                     body { margin: 0; padding: 16px; background: #ffffff;
                            font-family: 'Segoe UI', sans-serif; }
                     #error { display: none; color: #c00000; white-space: pre-wrap;
                              font-family: Consolas, monospace; font-size: 12px; }
                     .mermaid { text-align: center; }
                   </style>
                   {{scriptTag}}
                 </head>
                 <body>
                   <div id="error"></div>
                   <pre class="mermaid">{{WebUtility.HtmlEncode(mermaidCode)}}</pre>
                   <script>
                     try {
                       mermaid.initialize({ startOnLoad: true, theme: 'default', er: { useMaxWidth: false } });
                     } catch (e) {
                       document.getElementById('error').style.display = 'block';
                       document.getElementById('error').textContent =
                         'Diyagram render edilemedi: ' + e.message;
                     }
                   </script>
                 </body>
                 </html>
                 """;
    }
}
