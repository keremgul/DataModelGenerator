using System.Text;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Serialization;

/// <summary>
/// Mantıksal modeli okunabilir bir DDL metnine çevirir. Uygulama hiçbir veritabanına
/// bağlanmaz — bu çıktı yalnızca dosya/metin olarak kullanılır.
/// </summary>
public static class DdlSerializer
{
    public static string Serialize(DataModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- Konu: {model.Topic}");
        sb.AppendLine($"-- Üretim: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine("-- Bu betik referans amaçlıdır; uygulama hiçbir veritabanına bağlanmaz.");
        sb.AppendLine();

        foreach (var entity in model.Entities)
        {
            if (entity.SourceRuleIds.Count > 0)
                sb.AppendLine($"-- Kaynak kural(lar): {string.Join(", ", entity.SourceRuleIds)}");

            sb.AppendLine($"CREATE TABLE {entity.TechnicalName} (");

            var lines = new List<string>();
            foreach (var attribute in entity.Attributes)
            {
                var nullability = attribute.IsRequired || attribute.IsPrimaryKey ? " NOT NULL" : " NULL";
                var unique = attribute.IsUnique && !attribute.IsPrimaryKey ? " UNIQUE" : string.Empty;
                var trace = attribute.SourceRuleIds.Count > 0
                    ? $" -- {string.Join(", ", attribute.SourceRuleIds)}"
                    : string.Empty;

                lines.Add($"    {attribute.TechnicalName} {attribute.DataType}{nullability}{unique}{trace}");
            }

            var primaryKeys = entity.PrimaryKeys.Select(a => a.TechnicalName).ToList();
            if (primaryKeys.Count > 0)
                lines.Add($"    CONSTRAINT PK_{entity.TechnicalName} PRIMARY KEY ({string.Join(", ", primaryKeys)})");

            foreach (var fk in entity.Attributes.Where(a => a.IsForeignKey && !string.IsNullOrWhiteSpace(a.ReferencesEntity)))
            {
                lines.Add($"    CONSTRAINT FK_{entity.TechnicalName}_{fk.TechnicalName} " +
                          $"FOREIGN KEY ({fk.TechnicalName}) REFERENCES {fk.ReferencesEntity} ({fk.ReferencesAttribute})");
            }

            sb.AppendLine(JoinDefinitions(lines));
            sb.AppendLine(");");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Satır sonu virgüllerini yorumların önüne koyar.</summary>
    private static string JoinDefinitions(List<string> lines)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var isLast = i == lines.Count - 1;

            var commentIndex = line.IndexOf(" -- ", StringComparison.Ordinal);
            if (!isLast && commentIndex > 0)
                line = line[..commentIndex] + "," + line[commentIndex..];
            else if (!isLast)
                line += ",";

            sb.Append(line);
            if (!isLast) sb.AppendLine();
        }
        return sb.ToString();
    }
}
