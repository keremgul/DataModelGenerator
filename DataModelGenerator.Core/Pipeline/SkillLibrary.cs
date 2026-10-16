using System.Reflection;
using System.Text;

namespace DataModelGenerator.Core.Pipeline;

/// <summary>
/// skills/ klasöründeki düz metin skill dosyalarını yükler. Dosyalar portable
/// paketle birlikte gelir ve uygulama yeniden derlenmeden düzenlenebilir;
/// dosya bulunamazsa assembly'ye gömülü kopya kullanılır.
/// </summary>
public class SkillLibrary
{
    private const string ResourcePrefix = "DataModelGenerator.Core.Skills.";

    private readonly string _directory;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public SkillLibrary(string? directory = null)
    {
        _directory = directory ?? Path.Combine(AppContext.BaseDirectory, "skills");
    }

    public string Directory => _directory;

    public bool Exists(string name) =>
        File.Exists(Path.Combine(_directory, name + ".md")) || Embedded(name) is not null;

    public string Get(string name)
    {
        if (_cache.TryGetValue(name, out var cached)) return cached;

        var content = FromDisk(name) ?? Embedded(name)
            ?? throw new InvalidOperationException($"'{name}' skill dosyası bulunamadı ({_directory}).");

        _cache[name] = content;
        return content;
    }

    public string Render(string name, IDictionary<string, string> values)
    {
        var sb = new StringBuilder(Get(name));
        foreach (var (key, value) in values)
            sb.Replace("{{" + key + "}}", string.IsNullOrWhiteSpace(value) ? "(belirtilmedi)" : value);

        sb.Replace("{{MODEL_RULES}}", Rules.ModelRules.PromptText);
        sb.Replace("{{SOURCE_RULES}}", Rules.SourcePolicy.PromptText);
        sb.Replace("{{NAMING_RULES}}", Rules.NamingRules.Policy.Describe());
        return sb.ToString();
    }

    private string? FromDisk(string name)
    {
        try
        {
            var path = Path.Combine(_directory, name + ".md");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? Embedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ResourcePrefix + name + ".md");
        if (stream is null) return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
