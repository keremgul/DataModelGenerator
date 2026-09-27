namespace DataModelGenerator.Core.Storage;

/// <summary>
/// Tüm kalıcı veri flat-file olarak tutulur; yerel veritabanı kullanılmaz.
/// </summary>
public static class AppPaths
{
    public static string RootDirectory { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DataModelGenerator");

    public static string RuleSetsDirectory => Path.Combine(RootDirectory, "rulesets");
    public static string ProfilesFile => Path.Combine(RootDirectory, "connection-profiles.json");
    public static string KeysFile => Path.Combine(RootDirectory, "keys.dat");
    public static string SettingsFile => Path.Combine(RootDirectory, "settings.json");

    /// <summary>Testlerin gerçek kullanıcı profiline yazmaması için kök dizini değiştirir.</summary>
    public static void OverrideRoot(string path) => RootDirectory = path;

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(RuleSetsDirectory);
    }
}
