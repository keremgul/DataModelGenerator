using System.Text.RegularExpressions;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Storage;

/// <summary>
/// Yapıştırılan serbest metni ID'li kural listesine ayrıştırır. Girdi sisteme
/// verilmeden önce daima kural maddelerine dönüştürülür.
/// </summary>
public static partial class RuleTextParser
{
    [GeneratedRegex(@"^\s*(?<id>[A-Za-z]{1,4}[-_]?\d+)\s*[).:\-–]\s+(?<text>.+)$")]
    private static partial Regex ExplicitIdPattern();

    [GeneratedRegex(@"^\s*\d+\s*[).:\-–]\s*(?<text>.+)$")]
    private static partial Regex NumberedPattern();

    [GeneratedRegex(@"^\s*[-*•·]\s*(?<text>.+)$")]
    private static partial Regex BulletPattern();

    public static List<BusinessRule> Parse(string text)
    {
        var rules = new List<BusinessRule>();
        if (string.IsNullOrWhiteSpace(text)) return rules;

        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var autoIndex = 1;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            string? id = null;
            var body = line;

            var explicitMatch = ExplicitIdPattern().Match(line);
            if (explicitMatch.Success)
            {
                id = explicitMatch.Groups["id"].Value.ToUpperInvariant().Replace("_", "-");
                body = explicitMatch.Groups["text"].Value.Trim();
            }
            else
            {
                var numbered = NumberedPattern().Match(line);
                if (numbered.Success)
                {
                    body = numbered.Groups["text"].Value.Trim();
                }
                else
                {
                    var bullet = BulletPattern().Match(line);
                    if (bullet.Success)
                        body = bullet.Groups["text"].Value.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(body)) continue;

            if (id is null || !usedIds.Add(id))
            {
                while (!usedIds.Add($"BR-{autoIndex}")) autoIndex++;
                id = $"BR-{autoIndex}";
            }

            rules.Add(new BusinessRule { Id = id, Text = body });
        }

        return rules;
    }
}
