namespace DataModelGenerator.Core.Models;

/// <summary>
/// İzlenebilirlik tablosunda bir alanın dayandığı tek kaynak. Kural metni ve
/// (soru kaynağıysa) soru-cevap çifti tam metniyle burada taşınır; tabloda kısa
/// rozet gösterilir, tam metin tıklandığında/tooltip'te açılır.
/// </summary>
public class TraceSource
{
    public string RuleId { get; init; } = string.Empty;

    /// <summary>BR, Q veya CHAT.</summary>
    public string Kind { get; init; } = "BR";

    /// <summary>Kuralın tam metni.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Kaynak bir soruysa sorunun tam metni.</summary>
    public string QuestionText { get; init; } = string.Empty;

    /// <summary>Kaynak bir soruysa verilen cevap.</summary>
    public string Answer { get; init; } = string.Empty;

    /// <summary>Bu kaynaktan türeyen alan sayısı.</summary>
    public int UsageCount { get; set; }

    public bool IsQuestion => Kind == "Q";

    public string ShortText => Traceability.Shorten(Text);

    public string UsageText => UsageCount switch
    {
        0 => "modele yansımadı",
        1 => "1 alan",
        _ => $"{UsageCount} alan"
    };

    /// <summary>Rozetin üzerine gelindiğinde gösterilen tam açıklama.</summary>
    public string Tooltip => IsQuestion && !string.IsNullOrWhiteSpace(QuestionText)
        ? $"{RuleId} · Soru: {QuestionText}\nCevap: {Answer}"
        : $"{RuleId}: {Text}";

    public override string ToString() => $"{RuleId}: {Text}";
}

/// <summary>İzlenebilirlik tablosunun tek satırı: bir varlığın bir alanı.</summary>
public class TraceEntry
{
    public string Entity { get; init; } = string.Empty;
    public string EntityTechnicalName { get; init; } = string.Empty;
    public string Attribute { get; init; } = string.Empty;

    /// <summary>PK, FK → Hedef veya boş.</summary>
    public string KeyText { get; init; } = string.Empty;

    public List<TraceSource> Sources { get; init; } = new();

    public string NormalizationStatus { get; init; } = string.Empty;
    public string NormalizationExplanation { get; init; } = string.Empty;

    /// <summary>Tabloda gösterilen kısa rozetler: "BR-03 Q-07".</summary>
    public string Badges => Sources.Count > 0
        ? string.Join("  ", Sources.Select(s => s.RuleId))
        : "kaynaksız";

    /// <summary>Kaynak BR metinlerinin kısaltılmış hâli.</summary>
    public string RuleSummary => Traceability.Shorten(string.Join(" | ",
        Sources.Where(s => !s.IsQuestion).Select(s => s.Text)));

    public string QuestionSummary => Traceability.Shorten(string.Join(" | ",
        Sources.Where(s => s.IsQuestion).Select(s => s.QuestionText)));

    public string AnswerSummary => Traceability.Shorten(string.Join(" | ",
        Sources.Where(s => s.IsQuestion).Select(s => s.Answer)));

    /// <summary>Satırın tamamı için tooltip — uzun açıklamalar tabloda değil burada.</summary>
    public string Tooltip
    {
        get
        {
            var lines = Sources.Select(s => s.Tooltip).ToList();
            if (!string.IsNullOrWhiteSpace(NormalizationExplanation))
                lines.Add($"Normalizasyon: {NormalizationStatus} — {NormalizationExplanation}");
            else if (!string.IsNullOrWhiteSpace(NormalizationStatus))
                lines.Add($"Normalizasyon: {NormalizationStatus}");

            return lines.Count > 0 ? string.Join("\n", lines) : "Kaynak bilgisi yok.";
        }
    }

    public bool HasSource(string ruleId) =>
        Sources.Any(s => s.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => $"{Entity}.{Attribute}";
}

/// <summary>
/// Modelin hangi alanının hangi kurala ve hangi soru-cevaba dayandığını gösteren
/// yapılandırılmış tablo. Serbest metin özetin yerini alır; iki yönlü gezinmeyi
/// (alan → kaynak, kaynak → alanlar) mümkün kılar.
/// </summary>
public class TraceabilityTable
{
    public List<TraceEntry> Entries { get; } = new();

    /// <summary>Modelde gerçekten kullanılan ve kullanılmayan tüm kaynaklar.</summary>
    public List<TraceSource> Sources { get; } = new();

    /// <summary>Bir kaynağa tıklandığında o kaynaktan türeyen alanlar.</summary>
    public IEnumerable<TraceEntry> EntriesOf(string ruleId) =>
        Entries.Where(e => e.HasSource(ruleId));

    /// <summary>Hiçbir alana yansımamış kaynaklar — cevabın karşılıksız kalıp kalmadığını gösterir.</summary>
    public IEnumerable<TraceSource> UnusedSources => Sources.Where(s => s.UsageCount == 0);

    public static TraceabilityTable Build(ModelPackage package, ProjectInput input)
    {
        var table = new TraceabilityTable();

        // Kural kimliği → kaynak. Soru kaynakları soru metni ve cevabıyla zenginleşir.
        var byQuestion = package.Questions
            .Where(q => !string.IsNullOrWhiteSpace(q.InjectedRuleId))
            .GroupBy(q => q.InjectedRuleId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var sources = new Dictionary<string, TraceSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in input.Rules)
        {
            byQuestion.TryGetValue(rule.Id, out var question);
            sources[rule.Id] = new TraceSource
            {
                RuleId = rule.Id,
                Kind = ProjectInput.PrefixOf(rule.Origin),
                Text = rule.Text,
                QuestionText = question?.Text ?? string.Empty,
                Answer = question?.Answer ?? string.Empty
            };
        }

        // Normalizasyon sonuçları görünen adla, model ise teknik adla çalışır;
        // eşleştirme iki adın da denenmesiyle yapılır.
        var normalization = package.NormalizationResults
            .GroupBy(r => $"{r.Entity}.{r.Attribute}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (var entity in package.Model.Entities)
        {
            foreach (var attribute in entity.Attributes)
            {
                // Alanın kendi kaynağı yoksa varlığın kaynağı devralınır (PK/FK böyle doğar).
                var ruleIds = attribute.SourceRuleIds.Count > 0
                    ? attribute.SourceRuleIds
                    : entity.SourceRuleIds;

                var entrySources = ruleIds
                    .Select(id => sources.TryGetValue(id, out var source)
                        ? source
                        : new TraceSource { RuleId = id, Kind = KindOf(id), Text = "(kural listesinde bulunamadı)" })
                    .ToList();

                foreach (var source in entrySources) source.UsageCount++;

                var nf = FindNormalization(normalization, entity, attribute);

                table.Entries.Add(new TraceEntry
                {
                    Entity = entity.Name,
                    EntityTechnicalName = entity.TechnicalName,
                    Attribute = attribute.TechnicalName,
                    KeyText = attribute.IsPrimaryKey ? "PK"
                        : attribute.IsForeignKey ? $"FK → {attribute.ReferencesEntity}"
                        : string.Empty,
                    Sources = entrySources,
                    NormalizationStatus = nf?.StatusText ?? string.Empty,
                    NormalizationExplanation = nf?.Explanation ?? string.Empty
                });
            }
        }

        table.Sources.AddRange(sources.Values
            .OrderBy(s => s.Kind switch { "BR" => 0, "Q" => 1, _ => 2 })
            .ThenBy(s => s.RuleId, StringComparer.OrdinalIgnoreCase));

        return table;
    }

    private static Rules.NormalizationResult? FindNormalization(
        IReadOnlyDictionary<string, Rules.NormalizationResult> results, ModelEntity entity, EntityAttribute attribute)
    {
        string[] entityNames = [entity.TechnicalName, entity.Name];
        string[] attributeNames = [attribute.TechnicalName, attribute.Name];

        foreach (var entityName in entityNames)
        {
            foreach (var attributeName in attributeNames)
            {
                if (results.TryGetValue($"{entityName}.{attributeName}", out var found))
                    return found;
            }
        }

        return null;
    }

    private static string KindOf(string ruleId)
    {
        if (ruleId.StartsWith("Q-", StringComparison.OrdinalIgnoreCase)) return "Q";
        if (ruleId.StartsWith("CHAT-", StringComparison.OrdinalIgnoreCase)) return "CHAT";
        return "BR";
    }
}

internal static class Traceability
{
    private const int MaxLength = 70;

    /// <summary>Tabloda uzun metin gösterilmez; tam metin tooltip ve detay panelinde.</summary>
    public static string Shorten(string text)
    {
        var single = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (single.Contains("  ")) single = single.Replace("  ", " ");

        return single.Length <= MaxLength ? single : single[..(MaxLength - 1)].TrimEnd() + "…";
    }
}
