using System.Text.Json;

namespace DataModelGenerator.Core.Storage;

public class AppSettings
{
    public string ActiveProfileId { get; set; } = string.Empty;
    public int SampleRowCount { get; set; } = 20;
    public string LastExportDirectory { get; set; } = string.Empty;
}

public class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public AppSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.SettingsFile;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        Settings = Load();
    }

    public AppSettings Settings { get; private set; }

    public void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Settings, JsonOptions));
        }
        catch { /* ayar yazılamazsa varsayılanlarla devam edilir */ }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath)) ?? new AppSettings();
        }
        catch { }

        return new AppSettings();
    }
}
