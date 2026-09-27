using System.Security.Cryptography;
using System.Text.Json;
using DataModelGenerator.Core.Storage;

namespace DataModelGenerator.Core.Security;

/// <summary>
/// API anahtarlarını profil kimliğine göre Windows DPAPI (CurrentUser kapsamı) ile
/// şifreleyip saklar. Anahtarlar hiçbir zaman düz metin olarak diske yazılmaz.
/// </summary>
public class ApiKeyStore
{
    private readonly string _filePath;

    public ApiKeyStore(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.KeysFile;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
    }

    public void Save(string profileId, string apiKey)
    {
        var keys = Load();
        if (string.IsNullOrWhiteSpace(apiKey))
            keys.Remove(profileId);
        else
            keys[profileId] = apiKey;
        Persist(keys);
    }

    public string Get(string profileId) =>
        Load().TryGetValue(profileId, out var key) ? key : string.Empty;

    public bool Has(string profileId) =>
        !string.IsNullOrEmpty(Get(profileId));

    public void Delete(string profileId)
    {
        var keys = Load();
        if (keys.Remove(profileId))
            Persist(keys);
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_filePath)) return new();

        try
        {
            var encrypted = File.ReadAllBytes(_filePath);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new();
        }
        catch
        {
            // Başka bir kullanıcı/makine tarafından yazılmış veya bozulmuş dosya
            return new();
        }
    }

    private void Persist(Dictionary<string, string> keys)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(keys);
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, encrypted);
    }
}
