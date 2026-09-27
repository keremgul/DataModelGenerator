using System.Reflection;
using System.Text;

namespace DataModelGenerator.Core.Pipeline;

/// <summary>
/// Pipeline adımlarının prompt şablonlarını yükler. Exe yanındaki
/// Resources\Prompts klasöründe aynı adlı dosya varsa gömülü sürümün yerine o kullanılır.
/// </summary>
public class PromptLibrary
{
    private const string ResourcePrefix = "DataModelGenerator.Core.Resources.Prompts.";

    private readonly string _overrideDirectory;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public PromptLibrary(string? overrideDirectory = null)
    {
        _overrideDirectory = overrideDirectory
            ?? Path.Combine(AppContext.BaseDirectory, "Resources", "Prompts");
    }

    public string Get(string name)
    {
        if (_cache.TryGetValue(name, out var cached)) return cached;

        var template = LoadFromDisk(name) ?? LoadEmbedded(name)
            ?? throw new InvalidOperationException($"'{name}' prompt şablonu bulunamadı.");

        _cache[name] = template;
        return template;
    }

    public string Render(string name, IDictionary<string, string> values)
    {
        var sb = new StringBuilder(Get(name));
        foreach (var (key, value) in values)
            sb.Replace("{{" + key + "}}", string.IsNullOrWhiteSpace(value) ? "(belirtilmedi)" : value);
        return sb.ToString();
    }

    private string? LoadFromDisk(string name)
    {
        var path = Path.Combine(_overrideDirectory, name + ".txt");
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? LoadEmbedded(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + name + ".txt");
        if (stream is null) return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
