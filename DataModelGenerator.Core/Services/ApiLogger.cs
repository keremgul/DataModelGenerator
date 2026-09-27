using System.Diagnostics;
using System.Text;

namespace DataModelGenerator.Core.Services;

public class ApiLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string RequestHeaders { get; set; } = string.Empty;
    public string RequestBody { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public string? NetworkError { get; set; }
}

public static class ApiLogger
{
    public static readonly string LogPath = Path.Combine(GetExeDir(), "logs", "api.txt");

    private static string GetExeDir()
    {
        try
        {
            var fromModule = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(fromModule))
                return Path.GetDirectoryName(fromModule)!;
        }
        catch { }

        return Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty) ?? AppContext.BaseDirectory;
    }

    public static ApiLogEntry? LastEntry { get; private set; }

    public static void Record(ApiLogEntry entry)
    {
        LastEntry = entry;
        WriteToFile(entry);
    }

    private static void WriteToFile(ApiLogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

            var sb = new StringBuilder();
            sb.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {entry.Method} {entry.Url}");
            if (!string.IsNullOrWhiteSpace(entry.RequestHeaders))
            {
                sb.AppendLine("=== İstek Başlıkları ===");
                sb.AppendLine(entry.RequestHeaders);
            }
            if (!string.IsNullOrWhiteSpace(entry.RequestBody))
            {
                sb.AppendLine("=== İstek Gövdesi ===");
                sb.AppendLine(entry.RequestBody);
            }
            sb.AppendLine($"=== Yanıt (HTTP {entry.StatusCode?.ToString() ?? "??"}) ===");
            sb.AppendLine(entry.ResponseBody);
            if (!string.IsNullOrWhiteSpace(entry.NetworkError))
                sb.AppendLine($"Ağ Hatası: {entry.NetworkError}");
            sb.AppendLine(new string('─', 80));

            File.AppendAllText(LogPath, sb.ToString());
        }
        catch { /* log yazma hatası akışı durdurmaz */ }
    }
}
