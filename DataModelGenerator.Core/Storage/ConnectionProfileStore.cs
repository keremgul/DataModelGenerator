using System.Text.Json;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Storage;

/// <summary>
/// Bağlantı profillerini flat-file (JSON) olarak saklar. API anahtarları burada değil,
/// <see cref="Security.ApiKeyStore"/> içinde şifreli tutulur.
/// </summary>
public class ConnectionProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private List<ConnectionProfile> _profiles = new();

    public ConnectionProfileStore(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.ProfilesFile;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        Load();
    }

    public IReadOnlyList<ConnectionProfile> Profiles => _profiles;

    public ConnectionProfile? GetById(string id) =>
        _profiles.FirstOrDefault(p => p.Id == id);

    public void Save(ConnectionProfile profile)
    {
        var index = _profiles.FindIndex(p => p.Id == profile.Id);
        if (index >= 0)
            _profiles[index] = profile;
        else
            _profiles.Add(profile);

        Persist();
    }

    public void Delete(string id)
    {
        if (_profiles.RemoveAll(p => p.Id == id) > 0)
            Persist();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
                _profiles = JsonSerializer.Deserialize<List<ConnectionProfile>>(File.ReadAllText(_filePath)) ?? new();
        }
        catch
        {
            _profiles = new();
        }
    }

    private void Persist()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_profiles, JsonOptions));
        }
        catch { /* diske yazılamazsa oturum içi liste geçerli kalır */ }
    }
}
