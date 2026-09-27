using System.Text.Json;
using System.Text.Json.Nodes;

namespace DataModelGenerator.Core.Pipeline;

/// <summary>LLM çıktısı eksik veya yanlış tipte alan içerebilir; okuma daima toleranslıdır.</summary>
internal static class JsonReadExtensions
{
    public static string Str(this JsonNode? node, string property, string fallback = "")
    {
        var value = node?[property];
        if (value is null) return fallback;

        try
        {
            return value.GetValueKind() switch
            {
                JsonValueKind.String => value.GetValue<string>(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
                _ => fallback
            };
        }
        catch
        {
            return fallback;
        }
    }

    public static bool Bool(this JsonNode? node, string property, bool fallback = false)
    {
        var value = node?[property];
        if (value is null) return fallback;

        try
        {
            return value.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(value.GetValue<string>(), out var parsed) ? parsed : fallback,
                _ => fallback
            };
        }
        catch
        {
            return fallback;
        }
    }

    public static double Num(this JsonNode? node, string property, double fallback = 0.5)
    {
        var value = node?[property];
        if (value is null) return fallback;

        try
        {
            return value.GetValueKind() switch
            {
                JsonValueKind.Number => value.GetValue<double>(),
                JsonValueKind.String => double.TryParse(value.GetValue<string>(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback,
                _ => fallback
            };
        }
        catch
        {
            return fallback;
        }
    }

    public static List<string> StrList(this JsonNode? node, string property)
    {
        var array = node?[property]?.AsArray();
        if (array is null) return new List<string>();

        return array
            .Select(item =>
            {
                try
                {
                    return item?.GetValueKind() == JsonValueKind.String ? item.GetValue<string>() : item?.ToString();
                }
                catch
                {
                    return null;
                }
            })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .ToList();
    }

    public static JsonArray Array(this JsonNode? node, string property)
    {
        try
        {
            return node?[property]?.AsArray() ?? new JsonArray();
        }
        catch
        {
            return new JsonArray();
        }
    }
}
