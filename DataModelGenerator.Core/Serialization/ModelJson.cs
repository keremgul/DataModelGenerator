using System.Text.Json;
using System.Text.Json.Serialization;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Serialization;

/// <summary>Modelin dahili JSON temsili — pipeline adımları arasında ve dışa aktarımda kullanılır.</summary>
public static class ModelJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(DataModel model) => JsonSerializer.Serialize(model, Options);

    public static string Serialize(ModelPackage package) => JsonSerializer.Serialize(package, Options);

    public static DataModel? Deserialize(string json) =>
        JsonSerializer.Deserialize<DataModel>(json, Options);

    /// <summary>
    /// LLM yanıtındaki JSON bloğunu ayıklar — markdown çitleri veya açıklama metni olabilir.
    /// </summary>
    public static string ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');

        if (start >= 0 && end > start)
            return raw[start..(end + 1)];

        return raw.Trim();
    }
}
