using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Validation;

public enum ValidationSeverity
{
    Warning,
    Error
}

public class ValidationIssue
{
    public ValidationSeverity Severity { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public override string ToString() => $"[{Severity}] {Target}: {Message}";
}

/// <summary>
/// LLM çıktısının mekanik olarak kontrol edilebilir kısımlarını doğrular.
/// LLM çıktısı "öneri", bu katman "gerçeklik kontrolü"dür.
/// </summary>
public class ModelValidator
{
    public List<ValidationIssue> Validate(DataModel model)
    {
        var issues = new List<ValidationIssue>();

        CheckNameCollisions(model, issues);
        CheckPrimaryKeys(model, issues);
        CheckForeignKeys(model, issues);
        CheckRequiredFields(model, issues);
        CheckRelationships(model, issues);
        CheckCycles(model, issues);

        return issues;
    }

    private static void CheckNameCollisions(DataModel model, List<ValidationIssue> issues)
    {
        var duplicateEntities = model.Entities
            .GroupBy(e => e.TechnicalName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateEntities)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Code = "NAME_COLLISION_ENTITY",
                Target = group.Key,
                Message = $"'{group.Key}' teknik adı {group.Count()} varlık tarafından kullanılıyor."
            });
        }

        foreach (var entity in model.Entities)
        {
            var duplicateAttributes = entity.Attributes
                .GroupBy(a => a.TechnicalName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var group in duplicateAttributes)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Code = "NAME_COLLISION_ATTRIBUTE",
                    Target = $"{entity.TechnicalName}.{group.Key}",
                    Message = $"'{entity.Name}' varlığında '{group.Key}' alanı {group.Count()} kez tanımlanmış."
                });
            }
        }
    }

    private static void CheckPrimaryKeys(DataModel model, List<ValidationIssue> issues)
    {
        foreach (var entity in model.Entities.Where(e => !e.Attributes.Any(a => a.IsPrimaryKey)))
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Code = "MISSING_PK",
                Target = entity.TechnicalName,
                Message = $"'{entity.Name}' varlığının birincil anahtarı yok."
            });
        }
    }

    private static void CheckForeignKeys(DataModel model, List<ValidationIssue> issues)
    {
        foreach (var entity in model.Entities)
        {
            foreach (var attribute in entity.Attributes.Where(a => a.IsForeignKey))
            {
                var target = string.IsNullOrWhiteSpace(attribute.ReferencesEntity)
                    ? null
                    : model.FindEntity(attribute.ReferencesEntity!);

                if (target is null)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Code = "FK_TARGET_MISSING",
                        Target = $"{entity.TechnicalName}.{attribute.TechnicalName}",
                        Message = $"Yabancı anahtar '{attribute.ReferencesEntity}' varlığına işaret ediyor ama böyle bir varlık yok."
                    });
                    continue;
                }

                var referenced = target.Attributes.FirstOrDefault(a =>
                    a.TechnicalName.Equals(attribute.ReferencesAttribute, StringComparison.OrdinalIgnoreCase));

                if (referenced is null)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Code = "FK_COLUMN_MISSING",
                        Target = $"{entity.TechnicalName}.{attribute.TechnicalName}",
                        Message = $"Yabancı anahtarın işaret ettiği '{target.TechnicalName}.{attribute.ReferencesAttribute}' alanı bulunamadı."
                    });
                }
                else if (!referenced.IsPrimaryKey && !referenced.IsUnique)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Code = "FK_TARGET_NOT_KEY",
                        Target = $"{entity.TechnicalName}.{attribute.TechnicalName}",
                        Message = $"'{target.TechnicalName}.{referenced.TechnicalName}' birincil anahtar veya tekil değil; yabancı anahtar hedefi olamaz."
                    });
                }
                else if (!string.Equals(referenced.DataType, attribute.DataType, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Code = "FK_TYPE_MISMATCH",
                        Target = $"{entity.TechnicalName}.{attribute.TechnicalName}",
                        Message = $"Yabancı anahtar tipi ({attribute.DataType}) hedef anahtar tipinden ({referenced.DataType}) farklı."
                    });
                }
            }
        }
    }

    private static void CheckRequiredFields(DataModel model, List<ValidationIssue> issues)
    {
        foreach (var entity in model.Entities)
        {
            if (entity.Attributes.Count == 0)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Code = "NO_ATTRIBUTES",
                    Target = entity.TechnicalName,
                    Message = $"'{entity.Name}' varlığının hiç alanı yok."
                });
            }

            foreach (var attribute in entity.Attributes)
            {
                if (string.IsNullOrWhiteSpace(attribute.TechnicalName))
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Code = "EMPTY_ATTRIBUTE_NAME",
                        Target = entity.TechnicalName,
                        Message = "Adı boş bir alan var."
                    });
                }

                if (string.IsNullOrWhiteSpace(attribute.DataType))
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Code = "MISSING_DATA_TYPE",
                        Target = $"{entity.TechnicalName}.{attribute.TechnicalName}",
                        Message = "Alanın veri tipi belirlenmemiş."
                    });
                }

                if (attribute.IsPrimaryKey && !attribute.IsRequired)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Code = "NULLABLE_PK",
                        Target = $"{entity.TechnicalName}.{attribute.TechnicalName}",
                        Message = "Birincil anahtar zorunlu (NOT NULL) olmalı."
                    });
                }
            }

            if (entity.SourceRuleIds.Count == 0)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Code = "NO_TRACEABILITY",
                    Target = entity.TechnicalName,
                    Message = $"'{entity.Name}' varlığı hiçbir business rule ID'sine bağlanmamış."
                });
            }
        }
    }

    private static void CheckRelationships(DataModel model, List<ValidationIssue> issues)
    {
        foreach (var relationship in model.Relationships)
        {
            if (model.FindEntity(relationship.FromEntity) is null)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Code = "RELATION_ENDPOINT_MISSING",
                    Target = relationship.ToString(),
                    Message = $"İlişkinin kaynak varlığı '{relationship.FromEntity}' modelde yok."
                });
            }

            if (model.FindEntity(relationship.ToEntity) is null)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Code = "RELATION_ENDPOINT_MISSING",
                    Target = relationship.ToString(),
                    Message = $"İlişkinin hedef varlığı '{relationship.ToEntity}' modelde yok."
                });
            }

            if (relationship.Cardinality == Cardinality.ManyToMany)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Code = "MANY_TO_MANY_WITHOUT_JUNCTION",
                    Target = relationship.ToString(),
                    Message = "N-N ilişki mantıksal modelde ara tablo (junction) gerektirir; model içinde karşılığı yok."
                });
            }
        }
    }

    /// <summary>
    /// Zorunlu FK'ler üzerinden döngüsel referans arar. Zorunlu FK'lerden oluşan bir
    /// döngü, hiçbir kaydın oluşturulamaması anlamına gelir.
    /// </summary>
    private static void CheckCycles(DataModel model, List<ValidationIssue> issues)
    {
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in model.Entities)
        {
            graph[entity.TechnicalName] = entity.Attributes
                .Where(a => a.IsForeignKey && a.IsRequired && !string.IsNullOrWhiteSpace(a.ReferencesEntity))
                .Select(a => a.ReferencesEntity!)
                .Where(target => model.FindEntity(target) is not null)
                .Select(target => model.FindEntity(target)!.TechnicalName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Keys)
            Visit(node);

        void Visit(string node)
        {
            if (state.TryGetValue(node, out var s))
            {
                if (s == 1) Report(node);
                return;
            }

            state[node] = 1;
            path.Add(node);

            foreach (var next in graph[node])
                Visit(next);

            path.RemoveAt(path.Count - 1);
            state[node] = 2;
        }

        void Report(string node)
        {
            var start = path.FindIndex(n => n.Equals(node, StringComparison.OrdinalIgnoreCase));
            if (start < 0) return;

            var cycle = path[start..];
            cycle.Add(node);
            var key = string.Join("→", cycle.OrderBy(c => c, StringComparer.OrdinalIgnoreCase));
            if (!reported.Add(key)) return;

            var isSelfReference = cycle.Count == 2;
            issues.Add(new ValidationIssue
            {
                Severity = isSelfReference ? ValidationSeverity.Warning : ValidationSeverity.Error,
                Code = isSelfReference ? "SELF_REFERENCE" : "CIRCULAR_REFERENCE",
                Target = string.Join(" → ", cycle),
                Message = isSelfReference
                    ? $"'{node}' kendine zorunlu olarak referans veriyor; hiyerarşi amaçlanıyorsa alan opsiyonel olmalı."
                    : $"Zorunlu yabancı anahtarlar döngü oluşturuyor: {string.Join(" → ", cycle)}."
            });
        }
    }
}
