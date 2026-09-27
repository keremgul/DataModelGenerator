using System.Text;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Serialization;

/// <summary>Modeli Mermaid ER diyagramı koduna çevirir (deterministik, LLM kullanılmaz).</summary>
public static class MermaidSerializer
{
    public static string Serialize(DataModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("erDiagram");

        foreach (var relationship in model.Relationships)
        {
            var from = model.FindEntity(relationship.FromEntity)?.TechnicalName ?? Sanitize(relationship.FromEntity);
            var to = model.FindEntity(relationship.ToEntity)?.TechnicalName ?? Sanitize(relationship.ToEntity);
            var label = string.IsNullOrWhiteSpace(relationship.Name) ? "iliskili" : relationship.Name;

            sb.AppendLine($"    {from} {Connector(relationship)} {to} : \"{EscapeLabel(label)}\"");
        }

        if (model.Relationships.Count > 0 && model.Entities.Count > 0)
            sb.AppendLine();

        foreach (var entity in model.Entities)
        {
            sb.AppendLine($"    {entity.TechnicalName} {{");
            foreach (var attribute in entity.Attributes)
            {
                var keys = new List<string>();
                if (attribute.IsPrimaryKey) keys.Add("PK");
                if (attribute.IsForeignKey) keys.Add("FK");
                if (attribute.IsUnique && !attribute.IsPrimaryKey) keys.Add("UK");

                var keyPart = keys.Count > 0 ? " " + string.Join(",", keys) : string.Empty;
                var comment = BuildComment(attribute);

                // Mermaid ER grameri iki jeton ister; tip yerine nötr bir sözcük yazılır.
                sb.AppendLine($"        alan {Sanitize(attribute.TechnicalName)}{keyPart}{comment}");
            }
            sb.AppendLine("    }");
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string Connector(Relationship relationship)
    {
        var line = relationship.IsRequired ? "--" : "..";
        return relationship.Cardinality switch
        {
            Cardinality.OneToOne => $"||{line}||",
            Cardinality.OneToMany => $"||{line}o{{",
            Cardinality.ManyToOne => $"}}o{line}||",
            Cardinality.ManyToMany => $"}}o{line}o{{",
            _ => $"||{line}o{{"
        };
    }

    private static string BuildComment(EntityAttribute attribute)
    {
        var parts = new List<string>();
        if (attribute.SourceRuleIds.Count > 0)
            parts.Add(string.Join(",", attribute.SourceRuleIds));
        if (attribute.IsForeignKey && !string.IsNullOrWhiteSpace(attribute.ReferencesEntity))
            parts.Add($"→{attribute.ReferencesEntity}");

        return parts.Count == 0 ? string.Empty : $" \"{EscapeLabel(string.Join(" | ", parts))}\"";
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Alan";
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        return sb.ToString();
    }

    private static string EscapeLabel(string label) =>
        label.Replace("\"", "'").Replace("\n", " ").Trim();
}
