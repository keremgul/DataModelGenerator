namespace DataModelGenerator.Core.Rules;

public enum NameCase
{
    /// <summary>MUSTERINO</summary>
    Upper,

    /// <summary>musterino</summary>
    Lower,

    /// <summary>MusteriNo</summary>
    Pascal
}

public enum NameSeparator
{
    /// <summary>MUSTERINO</summary>
    None,

    /// <summary>MUSTERI_NO</summary>
    Underscore
}

/// <summary>
/// Teknik adlandırma kuralları. Ayar dosyasında tutulur; yeni kural gerektiğinde
/// burası genişletilir, çağıran taraflar değişmez.
/// </summary>
public class NamingPolicy
{
    public NameCase Case { get; set; } = NameCase.Upper;
    public NameSeparator Separator { get; set; } = NameSeparator.None;

    /// <summary>Türkçe karakterler ASCII karşılığına çevrilsin mi (Ç→C, Ğ→G, İ→I, Ö→O, Ş→S, Ü→U).</summary>
    public bool FoldTurkish { get; set; } = true;

    /// <summary>Üst sınır; 0 ise sınırsız. Veritabanı ad uzunluğu kısıtları için.</summary>
    public int MaxLength { get; set; }

    /// <summary>Her tablonun birincil anahtarının adı.</summary>
    public string PrimaryKeyName { get; set; } = "ID";

    /// <summary>Yabancı anahtar eki: &lt;TABLOADI&gt; + bu ek.</summary>
    public string ForeignKeySuffix { get; set; } = "ID";

    public static NamingPolicy Default => new();

    public NamingPolicy Clone() => new()
    {
        Case = Case,
        Separator = Separator,
        FoldTurkish = FoldTurkish,
        MaxLength = MaxLength,
        PrimaryKeyName = PrimaryKeyName,
        ForeignKeySuffix = ForeignKeySuffix
    };

    /// <summary>Prompt'a yazılan, insan tarafından okunabilir kural metni.</summary>
    public string Describe()
    {
        var caseText = Case switch
        {
            NameCase.Upper => "TAMAMI BÜYÜK HARF",
            NameCase.Lower => "tamamı küçük harf",
            _ => "PascalCase"
        };

        var separatorText = Separator == NameSeparator.Underscore
            ? "kelimeler alt çizgiyle ayrılır (MUSTERI_NO)"
            : "kelimeler bitişik yazılır, ayraç kullanılmaz (MUSTERINO)";

        var lines = new List<string>
        {
            $"- Tüm tablo ve alan adları {caseText} olmalı; {separatorText}.",
            "- Türkçe karakter kullanılmaz: Ç→C, Ğ→G, İ→I, Ö→O, Ş→S, Ü→U.",
            "- Ad yalnızca harf ve rakam içerir; boşluk, tire ve noktalama kullanılmaz.",
            $"- Her tablonun birincil anahtarı '{PrimaryKeyName}' adlı alandır."
        };

        if (MaxLength > 0)
            lines.Add($"- Ad en fazla {MaxLength} karakter olabilir.");

        return string.Join("\n", lines);
    }
}
