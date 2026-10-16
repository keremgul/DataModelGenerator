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
    private Func<string>? _relationRules;

    public PromptLibrary(string? overrideDirectory = null)
    {
        _overrideDirectory = overrideDirectory
            ?? Path.Combine(AppContext.BaseDirectory, "Resources", "Prompts");
    }

    /// <summary>
    /// İlişki kurallarının kaynağı skill kütüphanesidir; prompt şablonları da
    /// aynı metni kullansın diye buradan beslenir.
    /// </summary>
    public void UseRelationRules(Func<string> provider) => _relationRules = provider;

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

        // Yapısal kurallar ve kaynak/kesinlik sözleşmesi her prompt'ta aynı metinle verilir.
        sb.Replace("{{MODEL_RULES}}", Rules.ModelRules.PromptText);
        sb.Replace("{{SOURCE_RULES}}", Rules.SourcePolicy.PromptText);

        // İlişki kuralları dosyası kendi içinde {{NAMING_RULES}} kullanıyor;
        // bu yüzden önce o gömülür, sonra adlandırma kuralları genişletilir.
        sb.Replace("{{RELATION_RULES}}", _relationRules?.Invoke() ?? string.Empty);
        sb.Replace("{{NAMING_RULES}}", Rules.NamingRules.Policy.Describe());
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
