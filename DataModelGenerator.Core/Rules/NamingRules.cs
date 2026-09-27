using System.Globalization;
using System.Text;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Rules;

/// <summary>
/// Deterministik adlandırma katmanı: Türkçe doğal adları teknik adlara çevirir.
/// LLM'e bırakılmaz — aynı girdi her zaman aynı teknik adı üretir.
/// </summary>
public static class NamingRules
{
    private static readonly Dictionary<char, char> TurkishFolding = new()
    {
        ['ç'] = 'c', ['Ç'] = 'C',
        ['ğ'] = 'g', ['Ğ'] = 'G',
        ['ı'] = 'i', ['I'] = 'I',
        ['İ'] = 'I',
        ['ö'] = 'o', ['Ö'] = 'O',
        ['ş'] = 's', ['Ş'] = 'S',
        ['ü'] = 'u', ['Ü'] = 'U'
    };

    public static string FoldTurkish(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            sb.Append(TurkishFolding.TryGetValue(ch, out var folded) ? folded : ch);
        return sb.ToString();
    }

    /// <summary>PascalCase teknik ad: "Müşteri Sipariş Detayı" → "MusteriSiparisDetayi".</summary>
    public static string ToTechnicalName(string naturalName, IEnumerable<GlossaryTerm>? glossary = null)
    {
        if (string.IsNullOrWhiteSpace(naturalName)) return string.Empty;

        var mapped = glossary?.FirstOrDefault(g =>
            g.Term.Equals(naturalName.Trim(), StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(g.TechnicalName));
        if (mapped is not null)
            naturalName = mapped.TechnicalName;

        var folded = FoldTurkish(naturalName);
        var words = SplitWords(folded);
        if (words.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var word in words)
        {
            sb.Append(char.ToUpperInvariant(word[0]));
            if (word.Length > 1) sb.Append(word[1..].ToLowerInvariant());
        }

        var result = sb.ToString();
        return char.IsDigit(result[0]) ? "N" + result : result;
    }

    /// <summary>UPPER_SNAKE_CASE — DDL çıktısı için.</summary>
    public static string ToSnakeUpper(string naturalName)
    {
        var words = SplitWords(FoldTurkish(naturalName));
        return words.Count == 0
            ? string.Empty
            : string.Join("_", words.Select(w => w.ToUpperInvariant()));
    }

    /// <summary>Bir varlık adından varsayılan birincil anahtar alan adını türetir.</summary>
    public static string DefaultPrimaryKeyName(string entityTechnicalName) =>
        $"{entityTechnicalName}Id";

    /// <summary>Bir ilişkiden varsayılan yabancı anahtar alan adını türetir.</summary>
    public static string DefaultForeignKeyName(string referencedEntityTechnicalName) =>
        $"{referencedEntityTechnicalName}Id";

    /// <summary>İki varlık arasındaki N-N ilişkisi için ara tablo adı.</summary>
    public static string JunctionTableName(string left, string right) =>
        $"{left}{right}";

    /// <summary>Aynı kapsamda tekrar eden adı benzersizleştirir: Musteri, Musteri2, Musteri3…</summary>
    public static string MakeUnique(string candidate, ICollection<string> existing)
    {
        if (!existing.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            return candidate;

        var index = 2;
        while (existing.Contains($"{candidate}{index}", StringComparer.OrdinalIgnoreCase))
            index++;
        return $"{candidate}{index}";
    }

    private static List<string> SplitWords(string value)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsLetterOrDigit(ch))
            {
                // camelCase sınırında böl: "musteriNo" → "musteri", "No"
                if (current.Length > 0 && char.IsUpper(ch) && !char.IsUpper(value[i - 1]))
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0) words.Add(current.ToString());

        return words
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w => w.Normalize(NormalizationForm.FormC))
            .ToList();
    }
}
