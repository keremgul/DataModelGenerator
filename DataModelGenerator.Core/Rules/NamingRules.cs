using System.Globalization;
using System.Text;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Rules;

/// <summary>
/// Deterministik adlandırma katmanı: Türkçe doğal adları teknik adlara çevirir.
/// LLM'e bırakılmaz — aynı girdi her zaman aynı teknik adı üretir.
/// Kurallar <see cref="NamingPolicy"/> üzerinden konfigüre edilir.
/// </summary>
public static class NamingRules
{
    /// <summary>
    /// Uygulama açılışında ayar dosyasından yüklenir. Statik olması, adlandırmanın
    /// çağrıldığı onlarca noktaya politika parametresi taşımamak içindir.
    /// </summary>
    public static NamingPolicy Policy { get; private set; } = NamingPolicy.Default;

    public static void UsePolicy(NamingPolicy policy) => Policy = policy ?? NamingPolicy.Default;

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

    /// <summary>
    /// Doğal adı yürürlükteki politikaya göre teknik ada çevirir.
    /// Varsayılan politikada: "Müşteri Sipariş Detayı" → "MUSTERISIPARISDETAYI".
    /// </summary>
    public static string ToTechnicalName(string naturalName, IEnumerable<GlossaryTerm>? glossary = null) =>
        ToTechnicalName(naturalName, Policy, glossary);

    public static string ToTechnicalName(string naturalName, NamingPolicy policy,
        IEnumerable<GlossaryTerm>? glossary = null)
    {
        if (string.IsNullOrWhiteSpace(naturalName)) return string.Empty;

        var mapped = glossary?.FirstOrDefault(g =>
            g.Term.Equals(naturalName.Trim(), StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(g.TechnicalName));
        if (mapped is not null)
            naturalName = mapped.TechnicalName;

        var source = policy.FoldTurkish ? FoldTurkish(naturalName) : naturalName;
        var words = SplitWords(source);
        if (words.Count == 0) return string.Empty;

        var joined = policy.Case switch
        {
            NameCase.Upper => Join(words.Select(w => w.ToUpperInvariant()), policy.Separator),
            NameCase.Lower => Join(words.Select(w => w.ToLowerInvariant()), policy.Separator),
            _ => Join(words.Select(Capitalize), policy.Separator)
        };

        // Ad rakamla başlayamaz.
        if (joined.Length > 0 && char.IsDigit(joined[0]))
            joined = "N" + joined;

        return policy.MaxLength > 0 && joined.Length > policy.MaxLength
            ? joined[..policy.MaxLength]
            : joined;
    }

    private static string Join(IEnumerable<string> words, NameSeparator separator) =>
        string.Join(separator == NameSeparator.Underscore ? "_" : string.Empty, words);

    private static string Capitalize(string word) =>
        word.Length switch
        {
            0 => word,
            1 => word.ToUpperInvariant(),
            _ => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()
        };

    /// <summary>Her tablonun birincil anahtarı bu adı taşır.</summary>
    public static string PrimaryKeyName => Policy.PrimaryKeyName;

    /// <summary>Yabancı anahtar, referans verdiği tablonun adı + "ID" şeklinde adlandırılır.</summary>
    public static string DefaultForeignKeyName(string referencedEntityTechnicalName) =>
        ForeignKeyName(referencedEntityTechnicalName, role: null);

    /// <summary>
    /// Rol verilirse &lt;ROL&gt;&lt;TABLOADI&gt;ID üretilir. Aynı tabloya birden çok
    /// referans gerektiğinde ayrım bu önekle yapılır (GONDERENMUSTERIID).
    /// </summary>
    public static string ForeignKeyName(string referencedEntityTechnicalName, string? role)
    {
        var suffix = Policy.ForeignKeySuffix;
        var separator = Policy.Separator == NameSeparator.Underscore ? "_" : string.Empty;

        var name = string.IsNullOrWhiteSpace(role)
            ? $"{referencedEntityTechnicalName}{separator}{suffix}"
            : $"{ToTechnicalName(role)}{separator}{referencedEntityTechnicalName}{separator}{suffix}";

        return Policy.MaxLength > 0 && name.Length > Policy.MaxLength
            ? name[..Policy.MaxLength]
            : name;
    }

    /// <summary>İki varlık arasındaki N-N ilişkisi için ara tablo adı.</summary>
    public static string JunctionTableName(string left, string right) =>
        Policy.Separator == NameSeparator.Underscore ? $"{left}_{right}" : $"{left}{right}";

    /// <summary>Ad yürürlükteki politikaya uyuyor mu? Uymuyorsa düzeltilir.</summary>
    public static bool IsCompliant(string technicalName) =>
        !string.IsNullOrWhiteSpace(technicalName) &&
        technicalName.Equals(ToTechnicalName(technicalName), StringComparison.Ordinal);

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
