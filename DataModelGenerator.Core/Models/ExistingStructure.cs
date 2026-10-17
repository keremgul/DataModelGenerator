using System.Text.RegularExpressions;

namespace DataModelGenerator.Core.Models;

/// <summary>
/// Kullanıcının yüklediği, konuyla ilgili mevcut yapıyı anlatan markdown dosyası.
/// İçeriği LLM'e bağlam olarak verilir; buradaki tablolar yeniden üretilmez,
/// yeni yapılar bunlarla ilişkilendirilir.
/// </summary>
public partial class ExistingStructure
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    /// <summary>Dosyadan çıkarılan tablo adları — referans olarak kullanılır.</summary>
    public List<string> EntityNames { get; set; } = new();

    public bool IsEmpty => string.IsNullOrWhiteSpace(Content);

    public string Summary => IsEmpty
        ? "Mevcut yapı dosyası yüklenmedi."
        : $"{FileName} · {EntityNames.Count} tablo";

    // Markdown başlıkları: "## MUSTERI" veya "### Tablo: MUSTERI"
    [GeneratedRegex(@"^#{1,6}\s*(?:tablo\s*:?\s*)?(?<name>[^\r\n#|]+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex HeadingPattern();

    // Markdown tablosunun ilk kolonu: "| MUSTERI | ... |"
    [GeneratedRegex(@"^\|\s*(?<name>[A-Za-z_][\wçğıöşüÇĞİÖŞÜ ]*?)\s*\|",
        RegexOptions.Multiline)]
    private static partial Regex TableRowPattern();

    // "CREATE TABLE MUSTERI" veya "- Tablo: MUSTERI"
    [GeneratedRegex(@"create\s+table\s+(?:if\s+not\s+exists\s+)?[`""\[]?(?<name>[\w\.]+)[`""\]]?",
        RegexOptions.IgnoreCase)]
    private static partial Regex CreateTablePattern();

    /// <summary>
    /// Dosyadan tablo adlarını çıkarır. Tanıma başarısız olursa içerik yine de
    /// LLM'e bağlam olarak gider; kod tarafı yalnızca referans doğrulaması için
    /// bu listeyi kullanır.
    /// </summary>
    public static ExistingStructure Parse(string fileName, string content)
    {
        var structure = new ExistingStructure { FileName = fileName, Content = content ?? string.Empty };
        if (structure.IsEmpty) return structure;

        var names = new List<string>();

        foreach (Match match in CreateTablePattern().Matches(structure.Content))
            names.Add(match.Groups["name"].Value.Split('.').Last());

        foreach (Match match in HeadingPattern().Matches(structure.Content))
            names.Add(match.Groups["name"].Value);

        foreach (Match match in TableRowPattern().Matches(structure.Content))
            names.Add(match.Groups["name"].Value);

        structure.EntityNames = names
            .Select(n => Rules.NamingRules.ToTechnicalName(n))
            .Where(n => n.Length > 1 && !Noise.Contains(n, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return structure;
    }

    /// <summary>Markdown tablosu başlıkları ve bölüm adları tablo sanılmamalı.</summary>
    private static readonly string[] Noise =
    [
        "TABLO", "TABLOLAR", "ALAN", "ALANLAR", "KOLON", "KOLONLAR", "ACIKLAMA",
        "TIP", "TUR", "ANAHTAR", "ILISKI", "ILISKILER", "MEVCUT", "MEVCUTYAPI",
        "YAPI", "ICINDEKILER", "OZET", "NOT", "NOTLAR", "TABLE", "COLUMN", "TYPE",
        "NAME", "DESCRIPTION", "KEY", "FIELD", "FIELDS"
    ];

    /// <summary>Prompt'a gömülen bağlam metni.</summary>
    public string ToPromptContext() => IsEmpty
        ? "(mevcut yapı dosyası yüklenmedi)"
        : $"""
           DOSYA: {FileName}
           TANINAN TABLOLAR: {(EntityNames.Count > 0 ? string.Join(", ", EntityNames) : "(ad çıkarılamadı)")}

           {Content}
           """;
}
