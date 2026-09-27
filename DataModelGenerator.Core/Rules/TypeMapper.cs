namespace DataModelGenerator.Core.Rules;

/// <summary>
/// Deterministik veri tipi eşlemesi. Önce LLM'in önerdiği ham tip normalize edilir,
/// tip yoksa alan adındaki Türkçe/teknik anahtar kelimelerden çıkarım yapılır.
/// </summary>
public static class TypeMapper
{
    public const string DefaultType = "VARCHAR(100)";

    private static readonly (string Keyword, string Type)[] NameHints =
    [
        ("tckn", "CHAR(11)"),
        ("tcno", "CHAR(11)"),
        ("kimlik no", "CHAR(11)"),
        ("vkn", "CHAR(10)"),
        ("iban", "CHAR(26)"),
        ("eposta", "VARCHAR(254)"),
        ("e-posta", "VARCHAR(254)"),
        ("email", "VARCHAR(254)"),
        ("mail", "VARCHAR(254)"),
        ("telefon", "VARCHAR(20)"),
        ("gsm", "VARCHAR(20)"),
        ("adres", "VARCHAR(500)"),
        ("aciklama", "VARCHAR(500)"),
        ("açıklama", "VARCHAR(500)"),
        ("not", "VARCHAR(500)"),
        ("tarihi", "DATE"),
        ("tarih", "DATE"),
        ("date", "DATE"),
        ("zamani", "TIMESTAMP"),
        ("zaman", "TIMESTAMP"),
        ("saat", "TIMESTAMP"),
        ("tutar", "DECIMAL(18,2)"),
        ("fiyat", "DECIMAL(18,2)"),
        ("bakiye", "DECIMAL(18,2)"),
        ("ucret", "DECIMAL(18,2)"),
        ("ücret", "DECIMAL(18,2)"),
        ("maliyet", "DECIMAL(18,2)"),
        ("limit", "DECIMAL(18,2)"),
        ("faiz", "DECIMAL(9,4)"),
        ("oran", "DECIMAL(9,4)"),
        ("yuzde", "DECIMAL(5,2)"),
        ("yüzde", "DECIMAL(5,2)"),
        ("adedi", "INT"),
        ("adet", "INT"),
        ("sayi", "INT"),
        ("miktar", "DECIMAL(18,3)"),
        ("sayisi", "INT"),
        ("sayısı", "INT"),
        ("para birimi", "CHAR(3)"),
        ("doviz", "CHAR(3)"),
        ("döviz", "CHAR(3)"),
        ("durum", "VARCHAR(50)"),
        ("tipi", "VARCHAR(50)"),
        ("turu", "VARCHAR(50)"),
        ("türü", "VARCHAR(50)"),
        ("kodu", "VARCHAR(50)"),
        ("kod", "VARCHAR(50)"),
        ("numarasi", "VARCHAR(50)"),
        ("numarası", "VARCHAR(50)"),
        ("no", "VARCHAR(50)"),
        ("unvan", "VARCHAR(200)"),
        ("soyad", "VARCHAR(100)"),
        ("adi", "VARCHAR(200)"),
        ("adı", "VARCHAR(200)"),
        ("isim", "VARCHAR(200)"),
        ("ad", "VARCHAR(200)"),
        ("id", "BIGINT")
    ];

    private static readonly (string Raw, string Type)[] RawTypeAliases =
    [
        ("bigint", "BIGINT"),
        ("long", "BIGINT"),
        ("int", "INT"),
        ("integer", "INT"),
        ("number", "DECIMAL(18,2)"),
        ("numeric", "DECIMAL(18,2)"),
        ("decimal", "DECIMAL(18,2)"),
        ("money", "DECIMAL(18,2)"),
        ("currency", "DECIMAL(18,2)"),
        ("float", "DECIMAL(18,4)"),
        ("double", "DECIMAL(18,4)"),
        ("datetime", "TIMESTAMP"),
        ("timestamp", "TIMESTAMP"),
        ("date", "DATE"),
        ("time", "TIME"),
        ("bool", "BOOLEAN"),
        ("boolean", "BOOLEAN"),
        ("bit", "BOOLEAN"),
        ("guid", "UUID"),
        ("uuid", "UUID"),
        ("text", "TEXT"),
        ("clob", "TEXT"),
        ("string", "VARCHAR(200)"),
        ("varchar", "VARCHAR(200)"),
        ("nvarchar", "VARCHAR(200)"),
        ("char", "CHAR(1)")
    ];

    /// <summary>
    /// Alan için nihai veri tipini belirler.
    /// </summary>
    /// <param name="attributeName">Alanın doğal adı (Türkçe olabilir).</param>
    /// <param name="rawType">LLM'in önerdiği ham tip (boş olabilir).</param>
    /// <param name="isPrimaryKey">Birincil anahtarlar için varsayılan BIGINT.</param>
    public static string Map(string attributeName, string? rawType = null, bool isPrimaryKey = false)
    {
        var normalized = NormalizeRawType(rawType);
        if (normalized is not null)
            return normalized;

        var fromName = MapFromName(attributeName);
        if (fromName is not null)
            return fromName;

        return isPrimaryKey ? "BIGINT" : DefaultType;
    }

    /// <summary>LLM'in verdiği ham tipi kanonik tipe çevirir; tanınmazsa null döner.</summary>
    public static string? NormalizeRawType(string? rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType)) return null;

        var value = rawType.Trim();

        // Zaten "VARCHAR(50)" / "DECIMAL(18,2)" gibi parametreli bir tip verilmişse koru.
        if (value.Contains('(') && value.Contains(')'))
            return value.ToUpperInvariant();

        var lower = NamingRules.FoldTurkish(value).ToLowerInvariant();
        foreach (var (raw, type) in RawTypeAliases)
        {
            if (lower == raw) return type;
        }
        foreach (var (raw, type) in RawTypeAliases)
        {
            if (lower.StartsWith(raw)) return type;
        }

        return null;
    }

    /// <summary>Alan adındaki anahtar kelimeden tip çıkarır; eşleşme yoksa null döner.</summary>
    public static string? MapFromName(string attributeName)
    {
        if (string.IsNullOrWhiteSpace(attributeName)) return null;

        var lower = NamingRules.FoldTurkish(attributeName).ToLowerInvariant();

        if (lower.StartsWith("is ") || lower.StartsWith("has ") ||
            lower.EndsWith(" mi") || lower.EndsWith(" mı") ||
            lower.EndsWith("aktif") || lower.EndsWith("flag"))
            return "BOOLEAN";

        // Kelime sınırında eşleşme aranır; "ürün adedi" içindeki "ad" gibi parça eşleşmeler kabul edilmez.
        var words = lower.Split([' ', '_', '-', '.', '/'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var (keyword, type) in NameHints)
        {
            if (lower.EndsWith(keyword) || words.Any(word => MatchesWord(word, keyword)))
                return type;
        }

        // "musteriId" gibi bitişik yazımlar
        if (lower.EndsWith("id")) return "BIGINT";

        return null;
    }

    /// <summary>Türkçe iyelik/çoğul ekleri anahtar kelime eşleşmesini bozmasın diye tolere edilir.</summary>
    private static readonly string[] Suffixes =
        ["", "i", "u", "si", "su", "ni", "nu", "lar", "ler", "lari", "leri"];

    private static bool MatchesWord(string word, string keyword)
    {
        if (!word.StartsWith(keyword, StringComparison.Ordinal)) return false;
        return Suffixes.Contains(word[keyword.Length..]);
    }

    public static bool IsNumeric(string dataType) =>
        dataType.StartsWith("INT") || dataType.StartsWith("BIGINT") ||
        dataType.StartsWith("DECIMAL") || dataType.StartsWith("SMALLINT");

    public static bool IsDecimal(string dataType) => dataType.StartsWith("DECIMAL");
    public static bool IsDate(string dataType) => dataType is "DATE";
    public static bool IsTimestamp(string dataType) => dataType is "TIMESTAMP" or "TIME";
    public static bool IsBoolean(string dataType) => dataType is "BOOLEAN";
    public static bool IsUuid(string dataType) => dataType is "UUID";

    /// <summary>VARCHAR(200) → 200. Uzunluk yoksa null.</summary>
    public static int? GetLength(string dataType)
    {
        var open = dataType.IndexOf('(');
        var close = dataType.IndexOf(')');
        if (open < 0 || close <= open) return null;

        var inner = dataType[(open + 1)..close];
        var first = inner.Split(',')[0].Trim();
        return int.TryParse(first, out var len) ? len : null;
    }

    /// <summary>DECIMAL(18,2) → 2. Ondalık basamak yoksa 0.</summary>
    public static int GetScale(string dataType)
    {
        var open = dataType.IndexOf('(');
        var close = dataType.IndexOf(')');
        if (open < 0 || close <= open) return 0;

        var parts = dataType[(open + 1)..close].Split(',');
        return parts.Length > 1 && int.TryParse(parts[1].Trim(), out var scale) ? scale : 0;
    }
}
