using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using DataModelGenerator.Core.Services;

namespace DataModelGenerator.App.Dialogs;

public partial class ApiLogDialog : Window
{
    public ApiLogDialog(string title, string errorDetail, ApiLogEntry? logEntry)
    {
        InitializeComponent();

        TitleBlock.Text = title;
        ErrorBox.Text = errorDetail;
        LogPathBlock.Text = $"Log: {ApiLogger.LogPath}";

        if (logEntry is null)
        {
            UrlBlock.Text = "(HTTP çağrısı kaydedilemedi)";
            return;
        }

        UrlBlock.Text = $"{logEntry.Method}  {logEntry.Url}";

        var request = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(logEntry.RequestHeaders))
        {
            request.AppendLine("=== Başlıklar ===");
            request.AppendLine(logEntry.RequestHeaders);
            request.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(logEntry.RequestBody))
        {
            request.AppendLine("=== Gövde ===");
            request.AppendLine(PrettyJson(logEntry.RequestBody));
        }
        RequestBox.Text = request.ToString().TrimEnd();

        if (logEntry.StatusCode.HasValue)
        {
            StatusBlock.Text = $"HTTP {logEntry.StatusCode}";
            StatusBlock.Foreground = new SolidColorBrush(logEntry.StatusCode >= 400
                ? Color.FromRgb(192, 0, 0)
                : Color.FromRgb(55, 86, 35));
        }

        ResponseBox.Text = PrettyJson(logEntry.ResponseBody);
    }

    public static void Show(string title, string errorDetail, ApiLogEntry? logEntry = null)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            new ApiLogDialog(title, errorDetail, logEntry)
            {
                Owner = Application.Current.MainWindow
            }.ShowDialog();
        });
    }

    public static void Show(string title, Exception ex, ApiLogEntry? logEntry = null) =>
        Show(title, BuildExceptionDetail(ex), logEntry);

    private static string BuildExceptionDetail(Exception ex)
    {
        var sb = new StringBuilder();
        var current = (Exception?)ex;
        var level = 0;

        while (current is not null)
        {
            if (level > 0)
            {
                sb.AppendLine();
                sb.AppendLine("════════ İç Hata ════════");
            }

            sb.AppendLine($"Tür   : {current.GetType().FullName}");
            sb.AppendLine($"Mesaj : {current.Message}");
            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                sb.AppendLine("Stack Trace:");
                sb.AppendLine(current.StackTrace);
            }

            current = current.InnerException;
            level++;
        }

        return sb.ToString().TrimEnd();
    }

    private static string PrettyJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        try
        {
            var document = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(document, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch
        {
            return raw;
        }
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== HATA ===");
        sb.AppendLine(ErrorBox.Text);
        sb.AppendLine();
        sb.AppendLine("=== İSTEK ===");
        sb.AppendLine(UrlBlock.Text);
        sb.AppendLine(RequestBox.Text);
        sb.AppendLine();
        sb.AppendLine("=== YANIT ===");
        sb.AppendLine(StatusBlock.Text);
        sb.AppendLine(ResponseBox.Text);

        Clipboard.SetText(sb.ToString());
        CopyAllBtn.Content = "Kopyalandı ✓";
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
