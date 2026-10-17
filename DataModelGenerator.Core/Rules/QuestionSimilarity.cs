namespace DataModelGenerator.Core.Rules;

/// <summary>
/// Soru tekrarını kod tarafında eler. Prompt'a "bunları sorma" listesi verilir
/// ama ona güvenilmez: model aynı soruyu farklı kelimelerle sorduğunda birebir
/// karşılaştırma yakalamaz, bu yüzden anlamlı kelime örtüşmesine bakılır.
/// </summary>
public static class QuestionSimilarity
{
    /// <summary>Bu oranın üstünde örtüşen sorular aynı sayılır.</summary>
    public const double Threshold = 0.6;

    /// <summary>
    /// Soru kalıbı her soruda tekrar ettiği için ayırt edici değildir; ağırlık
    /// içerik kelimelerinde olmalı.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "bir", "bu", "su", "o", "ve", "veya", "ile", "icin", "mi", "mu", "mi̇", "mu̇",
        "midir", "mudur", "mi̇dir", "olabilir", "olmali", "olmalidir", "var", "yok",
        "hangi", "hangisi", "nasil", "nedir", "ne", "kac", "her", "birden", "cok",
        "tek", "yalnizca", "sadece", "gerekli", "gerekir", "tutulacak", "tutulmali",
        "alani", "alan", "tablosu", "tablo", "kaydi", "kayit", "degeri", "deger"
    };

    /// <summary>
    /// Türkçe ekli bir dil: "sipariş" ile "siparişin" aynı kelimedir. Tam kelime
    /// karşılaştırması bunu kaçırdığı için kelimeler köklerine indirgenir.
    /// </summary>
    private const int StemLength = 5;

    public static bool AreSame(string first, string second)
    {
        var left = Tokenize(first);
        var right = Tokenize(second);

        if (left.Count == 0 || right.Count == 0)
            return Collapse(first) == Collapse(second);

        // Örtüşme katsayısı: ortak kök / kısa sorunun kök sayısı. Jaccard yerine
        // bu kullanılır, çünkü sorular farklı uzunlukta olabiliyor.
        var shared = left.Intersect(right, StringComparer.OrdinalIgnoreCase).Count();
        var smaller = Math.Min(left.Count, right.Count);

        return smaller > 0 && (double)shared / smaller >= Threshold;
    }

    /// <summary>Daha önce sorulmuş sorulardan biriyle aynı mı?</summary>
    public static bool IsAlreadyAsked(string candidate, IEnumerable<string> asked) =>
        asked.Any(previous => AreSame(candidate, previous));

    private static List<string> Tokenize(string value)
    {
        var folded = NamingRules.FoldTurkish(value).ToLowerInvariant();
        var words = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var ch in folded)
        {
            if (char.IsLetterOrDigit(ch)) current.Append(ch);
            else if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
        }

        if (current.Length > 0) words.Add(current.ToString());

        return words
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
            .Select(w => w.Length <= StemLength ? w : w[..StemLength])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Collapse(string value) =>
        new(NamingRules.FoldTurkish(value).Where(char.IsLetterOrDigit).ToArray());
}
