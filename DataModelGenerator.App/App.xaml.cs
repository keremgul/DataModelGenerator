using System.IO;
using System.Windows;
using System.Windows.Threading;
using DataModelGenerator.Core.Export;
using DataModelGenerator.Core.Providers;
using DataModelGenerator.Core.Security;
using DataModelGenerator.Core.Storage;

namespace DataModelGenerator.App;

public partial class App : Application
{
    public static ProviderRegistry ProviderRegistry { get; private set; } = null!;
    public static ApiKeyStore ApiKeyStore { get; private set; } = null!;
    public static ConnectionProfileStore ProfileStore { get; private set; } = null!;
    public static AppSettingsStore SettingsStore { get; private set; } = null!;
    public static RuleSetStore RuleSetStore { get; private set; } = null!;
    public static ExcelExportService ExcelExport { get; private set; } = null!;

    private static readonly string LogPath = Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory,
        "logs", "error.txt");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        AppPaths.EnsureDirectories();

        ProviderRegistry = new ProviderRegistry();
        ApiKeyStore = new ApiKeyStore();
        ProfileStore = new ConnectionProfileStore();
        SettingsStore = new AppSettingsStore();
        RuleSetStore = new RuleSetStore();
        ExcelExport = new ExcelExportService();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteLog(e.Exception);
        MessageBox.Show(FormatError(e.Exception), "Beklenmedik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        WriteLog(exception);
        MessageBox.Show(FormatError(exception), "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteLog(e.Exception);
        e.SetObserved();
    }

    private static string FormatError(Exception? ex)
    {
        if (ex is null) return "Bilinmeyen hata.";

        var message = ex.Message;
        if (ex.InnerException is not null)
            message += $"{Environment.NewLine}İç hata: {ex.InnerException.Message}";

        return $"{message}{Environment.NewLine}{Environment.NewLine}Log dosyası: {LogPath}";
    }

    private static void WriteLog(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* log yazma hatası akışı durdurmaz */ }
    }
}
