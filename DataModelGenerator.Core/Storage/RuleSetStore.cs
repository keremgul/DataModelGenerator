using System.Text.Json;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Storage;

/// <summary>Konu + kural seti + terim sözlüğünü flat-file olarak saklar.</summary>
public class RuleSetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _directory;

    public RuleSetStore(string? directory = null)
    {
        _directory = directory ?? AppPaths.RuleSetsDirectory;
        Directory.CreateDirectory(_directory);
    }

    public string DirectoryPath => _directory;

    public List<string> ListNames() =>
        System.IO.Directory.GetFiles(_directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n)
            .ToList();

    public void Save(string name, ProjectInput input)
    {
        var path = Path.Combine(_directory, Sanitize(name) + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(input, JsonOptions));
    }

    public ProjectInput? Load(string name)
    {
        var path = Path.Combine(_directory, Sanitize(name) + ".json");
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<ProjectInput>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public void Delete(string name)
    {
        var path = Path.Combine(_directory, Sanitize(name) + ".json");
        if (File.Exists(path)) File.Delete(path);
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "kural-seti" : cleaned;
    }
}
