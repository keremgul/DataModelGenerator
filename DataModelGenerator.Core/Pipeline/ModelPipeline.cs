using System.Text;
using System.Text.Json.Nodes;
using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Providers;
using DataModelGenerator.Core.Rules;
using DataModelGenerator.Core.Serialization;
using DataModelGenerator.Core.Validation;

namespace DataModelGenerator.Core.Pipeline;

/// <summary>
/// Çok adımlı üretim zinciri. Her adımın çıktısı bir sonrakine yapılandırılmış
/// veri olarak aktarılır; adımlar tek büyük prompt yerine ayrı ayrı çalıştırılır.
/// </summary>
public class ModelPipeline
{
    private readonly LlmJsonClient _llm;
    private readonly PromptLibrary _prompts;
    private readonly RuleEngine _ruleEngine = new();
    private readonly ModelValidator _validator = new();

    public ModelPipeline(IModelProvider provider, ProviderConnectionSettings settings, string modelId,
        PromptLibrary? prompts = null)
    {
        _llm = new LlmJsonClient(provider, settings, modelId);
        _prompts = prompts ?? new PromptLibrary();
    }

    /// <summary>İlk üretim: kurallardan model + belirsizlik listesi.</summary>
    public async Task<ModelPackage> GenerateAsync(PipelineContext context,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        context.Model = new DataModel { Topic = context.Input.Topic };
        context.Scope.Clear();
        return await RunAsync(context, PipelineStage.Extraction, progress, ct, generateQuestions: true);
    }

    /// <summary>
    /// Kullanıcının tetiklediği ek soru turu: belirsizlikler yeniden taranır ve
    /// daha önce sorulmamış sorular üretilir. Otomatik çalışmaz — modeli daha ileri
    /// götürmek isteyip istemediğine kullanıcı karar verir.
    /// </summary>
    public async Task<ModelPackage> RequestQuestionsAsync(PipelineContext context,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (context.Model.Entities.Count == 0)
            throw new InvalidOperationException("Önce bir model üretilmelidir.");

        var before = context.Questions.Count;

        progress?.Report("Belirsizlikler yeniden taranıyor…");
        await DetectGapsAsync(context, ct);

        progress?.Report("Yeni sorular üretiliyor…");
        await GenerateQuestionsAsync(context, ct);

        var package = await SerializeAsync(context, progress, ct);
        package.NewQuestionCount = context.Questions.Count - before;
        return package;
    }

    /// <summary>
    /// Yönlendirme turu: cevaplar kural listesine enjekte edilir ve pipeline baştan değil,
    /// yalnızca etkilenen adımdan itibaren çalıştırılır.
    /// </summary>
    public async Task<ModelPackage> ApplyAnswersAsync(PipelineContext context,
        IReadOnlyList<ClarifyingQuestion> answered,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var answeredQuestions = answered.Where(q => q.IsAnswered).ToList();
        if (answeredQuestions.Count == 0)
            throw new InvalidOperationException("Uygulanacak cevap yok.");

        var affectedStage = PipelineStage.Serialization;

        foreach (var question in answeredQuestions)
        {
            var ruleId = context.Input.NextRuleId(RuleOrigin.Clarification);
            context.Input.Rules.Add(new BusinessRule
            {
                Id = ruleId,
                Origin = RuleOrigin.Clarification,
                Text = $"{question.Text} → {question.Answer.Trim()}"
            });

            var ambiguity = context.Ambiguities.FirstOrDefault(a => a.Id == question.AmbiguityId);
            if (ambiguity is not null)
            {
                ambiguity.IsResolved = true;
                ambiguity.AppliedDefault = question.Answer.Trim();
            }

            var tracked = context.Questions.FirstOrDefault(q => q.Id == question.Id);
            if (tracked is not null)
            {
                tracked.Answer = question.Answer;
                tracked.IsApplied = true;
                tracked.AppliedInRound = context.RoundNumber + 1;
            }

            var stage = PipelineStageMap.AffectedStage(question.Kind);
            if (stage < affectedStage) affectedStage = stage;

            foreach (var entityName in EntityNamesFromTarget(context.Model, question.Target))
                context.Scope.Add(entityName);
        }

        // Kapsam yalnızca alan/tip kararlarını etkileyen adımlarda daraltılabilir;
        // varlık veya ilişki yeniden çıkarılıyorsa model bütün olarak değerlendirilmelidir.
        if (affectedStage <= PipelineStage.RelationshipInference)
            context.Scope.Clear();

        return await RunAsync(context, affectedStage, progress, ct);
    }

    /// <summary>
    /// Serbest reprompt: yalnızca ilgili varlık/ilişki context'i ve değişiklik talebi
    /// gönderilir; modelin tamamı LLM'e aktarılmaz.
    /// </summary>
    public async Task<ModelPackage> RepromptAsync(PipelineContext context,
        IReadOnlyList<string> scopeEntities, string request,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request))
            throw new InvalidOperationException("Değişiklik talebi boş olamaz.");
        if (scopeEntities.Count == 0)
            throw new InvalidOperationException("En az bir varlık seçilmelidir.");

        context.Scope.Clear();
        foreach (var name in scopeEntities) context.Scope.Add(name);

        progress?.Report("Seçili varlıklar için değişiklik isteniyor…");

        var scopedModel = BuildScopedModel(context.Model, context.Scope);
        var prompt = _prompts.Render("reprompt", new Dictionary<string, string>
        {
            ["TOPIC"] = context.Input.Topic,
            ["RULES"] = FormatRules(context.Input),
            ["SCOPE"] = string.Join(", ", context.Scope),
            ["MODEL"] = ModelJson.Serialize(scopedModel),
            ["REQUEST"] = request
        });

        var response = await _llm.SendAsync(PipelineStage.AttributeDerivation, prompt, ct);

        var ruleId = context.Input.NextRuleId(RuleOrigin.Clarification);
        context.Input.Rules.Add(new BusinessRule
        {
            Id = ruleId,
            Origin = RuleOrigin.Clarification,
            Text = $"[Reprompt: {string.Join(", ", context.Scope)}] {request.Trim()}"
        });

        // Kapsamdaki hangi varlıkların geri döndüğü, hangilerinin silindiğini belirler.
        var scopedBefore = scopedModel.Entities.Select(e => e.TechnicalName).ToList();
        var returned = new List<string>();

        foreach (var node in response.Array("entities"))
        {
            var entity = ReadEntity(node, ruleId);
            if (entity is null) continue;

            entity.Attributes = ReadAttributes(node, ruleId);
            ModelMerger.UpsertEntity(context.Model, entity);
            context.Scope.Add(entity.Name);

            var merged = context.Model.FindEntity(entity.Name);
            if (merged is not null) returned.Add(merged.TechnicalName);
        }

        var removed = ModelMerger.RemoveEntities(context.Model,
            scopedBefore.Where(name => !returned.Contains(name, StringComparer.OrdinalIgnoreCase)));

        ModelMerger.ReplaceScopedRelationships(context.Model, scopedBefore,
            response.Array("relationships")
                .Select(node => ReadRelationship(node, ruleId))
                .Where(r => r is not null)
                .Select(r => r!)
                .ToList());

        context.RepromptNotes.Clear();
        if (removed.Count > 0)
            context.RepromptNotes.Add($"Modelden kaldırılan varlıklar: {string.Join(", ", removed)}");

        foreach (var note in response.StrList("notes"))
            context.RepromptNotes.Add(note);

        foreach (var note in context.RepromptNotes)
            context.NormalizationNotes.Add($"Reprompt notu: {note}");

        ApplyDeterministicLayer(context);
        return await RunAsync(context, PipelineStage.NormalizationCheck, progress, ct);
    }

    /// <param name="generateQuestions">
    /// Yalnızca ilk üretimde true. Sonraki turlarda soru üretimi kullanıcının
    /// <see cref="RequestQuestionsAsync"/> çağrısına bırakılır.
    /// </param>
    private async Task<ModelPackage> RunAsync(PipelineContext context, PipelineStage fromStage,
        IProgress<string>? progress, CancellationToken ct, bool generateQuestions = false)
    {
        context.RoundNumber++;

        for (var stage = fromStage; stage <= PipelineStage.Serialization; stage++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"{(int)stage + 1}/7 — {PipelineStageMap.DisplayName(stage)}…");

            switch (stage)
            {
                case PipelineStage.Extraction:
                    await ExtractEntitiesAsync(context, ct);
                    break;
                case PipelineStage.RelationshipInference:
                    await InferRelationshipsAsync(context, ct);
                    break;
                case PipelineStage.AttributeDerivation:
                    await DeriveAttributesAsync(context, ct);
                    break;
                case PipelineStage.NormalizationCheck:
                    await CheckNormalizationAsync(context, ct);
                    break;
                case PipelineStage.GapDetection:
                    await DetectGapsAsync(context, ct);
                    break;
                case PipelineStage.QuestionGeneration:
                    if (generateQuestions) await GenerateQuestionsAsync(context, ct);
                    break;
                case PipelineStage.Serialization:
                    break;
            }
        }

        var package = await SerializeAsync(context, progress, ct);
        context.Scope.Clear();
        return package;
    }

    // ---------------------------------------------------------------- adım 1
    private async Task ExtractEntitiesAsync(PipelineContext context, CancellationToken ct)
    {
        var prompt = _prompts.Render("extraction", new Dictionary<string, string>
        {
            ["TOPIC"] = context.Input.Topic,
            ["CONTEXT"] = context.Input.Context,
            ["GLOSSARY"] = FormatGlossary(context.Input),
            ["RULES"] = FormatRules(context.Input)
        });

        var response = await _llm.SendAsync(PipelineStage.Extraction, prompt, ct);

        var entities = new List<ModelEntity>();
        foreach (var node in response.Array("entities"))
        {
            var entity = ReadEntity(node);
            if (entity is null) continue;

            // Yeniden çıkarımda mevcut alanlar korunur; alan adımı bunları tazeleyecek.
            var existing = context.Model.FindEntity(entity.Name);
            if (existing is not null) entity.Attributes = existing.Attributes;

            entities.Add(entity);
        }

        if (entities.Count == 0)
            throw new PipelineException(PipelineStage.Extraction,
                "Kurallardan hiç varlık çıkarılamadı. Kural listesini genişletip tekrar deneyin.");

        context.Model.Topic = context.Input.Topic;
        context.Model.Entities = entities;
        context.Model.Relationships.RemoveAll(r =>
            context.Model.FindEntity(r.FromEntity) is null || context.Model.FindEntity(r.ToEntity) is null);
    }

    // ---------------------------------------------------------------- adım 2
    private async Task InferRelationshipsAsync(PipelineContext context, CancellationToken ct)
    {
        if (context.Model.Entities.Count < 2)
        {
            context.Model.Relationships.Clear();
            return;
        }

        var prompt = _prompts.Render("relationships", new Dictionary<string, string>
        {
            ["TOPIC"] = context.Input.Topic,
            ["RULES"] = FormatRules(context.Input),
            ["ENTITIES"] = FormatEntities(context.Model)
        });

        var response = await _llm.SendAsync(PipelineStage.RelationshipInference, prompt, ct);

        var relationships = new List<Relationship>();
        foreach (var node in response.Array("relationships"))
        {
            var relationship = ReadRelationship(node);
            if (relationship is null) continue;
            if (context.Model.FindEntity(relationship.FromEntity) is null) continue;
            if (context.Model.FindEntity(relationship.ToEntity) is null) continue;

            relationships.Add(relationship);
        }

        context.Model.Relationships = relationships;
    }

    // ---------------------------------------------------------------- adım 3
    private async Task DeriveAttributesAsync(PipelineContext context, CancellationToken ct)
    {
        var targets = context.Model.Entities
            .Where(e => context.IsInScope(e.Name) || context.IsInScope(e.TechnicalName))
            .ToList();

        if (targets.Count == 0) targets = context.Model.Entities;

        var prompt = _prompts.Render("attributes", new Dictionary<string, string>
        {
            ["TOPIC"] = context.Input.Topic,
            ["GLOSSARY"] = FormatGlossary(context.Input),
            ["RULES"] = FormatRules(context.Input),
            ["ENTITIES"] = FormatEntities(context.Model),
            ["RELATIONSHIPS"] = FormatRelationships(context.Model),
            ["SCOPE"] = context.Scope.Count == 0
                ? "TÜMÜ"
                : string.Join(", ", targets.Select(t => t.Name))
        });

        var response = await _llm.SendAsync(PipelineStage.AttributeDerivation, prompt, ct);

        foreach (var node in response.Array("entities"))
        {
            var name = node.Str("name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var entity = context.Model.FindEntity(name);
            if (entity is null) continue;

            var attributes = ReadAttributes(node);
            if (attributes.Count > 0) entity.Attributes = attributes;
        }

        ApplyDeterministicLayer(context);
    }

    /// <summary>Adlandırma, tip eşlemesi, anahtar tamamlama ve doğrulama — hepsi kod tarafında.</summary>
    private void ApplyDeterministicLayer(PipelineContext context)
    {
        context.Ambiguities.RemoveAll(a => a.FromRuleEngine && !a.IsResolved);

        var notes = _ruleEngine.Normalize(context.Model, context.Input);
        ModelMerger.SyncForeignKeys(context.Model);
        notes.AddRange(_ruleEngine.RelinkReferences(context.Model));

        foreach (var note in notes)
        {
            note.FromRuleEngine = true;
            AddAmbiguity(context, note);
        }

        context.ValidationIssues.Clear();
        context.ValidationIssues.AddRange(_validator.Validate(context.Model));
    }

    // ---------------------------------------------------------------- adım 4
    private async Task CheckNormalizationAsync(PipelineContext context, CancellationToken ct)
    {
        context.NormalizationNotes.RemoveAll(n => !n.StartsWith("Reprompt notu:", StringComparison.Ordinal));

        var prompt = _prompts.Render("normalization", new Dictionary<string, string>
        {
            ["RULES"] = FormatRules(context.Input),
            ["MODEL"] = ModelJson.Serialize(context.Model)
        });

        JsonNode response;
        try
        {
            response = await _llm.SendAsync(PipelineStage.NormalizationCheck, prompt, ct);
        }
        catch (PipelineException ex)
        {
            // Normalizasyon denetimi modelin kendisini üretmez; başarısızlığı akışı durdurmamalı.
            context.NormalizationNotes.Add($"Normalizasyon denetimi çalıştırılamadı: {ex.Message}");
            return;
        }

        foreach (var node in response.Array("violations"))
        {
            var entity = node.Str("entity");
            var form = node.Str("normalForm", "NF");
            var description = node.Str("description");
            var fix = node.Str("suggestedFix");
            if (string.IsNullOrWhiteSpace(description)) continue;

            context.NormalizationNotes.Add(
                $"[{form}] {entity}: {description}" + (string.IsNullOrWhiteSpace(fix) ? "" : $" → Öneri: {fix}"));
        }

        foreach (var note in response.StrList("notes"))
            context.NormalizationNotes.Add(note);

        foreach (var relationship in context.Model.Relationships.Where(r => r.Cardinality == Cardinality.ManyToMany))
        {
            var from = context.Model.FindEntity(relationship.FromEntity)?.TechnicalName ?? relationship.FromEntity;
            var to = context.Model.FindEntity(relationship.ToEntity)?.TechnicalName ?? relationship.ToEntity;
            context.NormalizationNotes.Add(
                $"[N-N] {from} ↔ {to}: mantıksal modelde ara tablo gerekir; " +
                $"kurallarda açıkça geçmediği için otomatik eklenmedi (öneri: {NamingRules.JunctionTableName(from, to)}).");
        }
    }

    // ---------------------------------------------------------------- adım 5
    private async Task DetectGapsAsync(PipelineContext context, CancellationToken ct)
    {
        AddConfidenceAmbiguities(context);

        var prompt = _prompts.Render("gaps", new Dictionary<string, string>
        {
            ["RULES"] = FormatRules(context.Input),
            ["MODEL"] = ModelJson.Serialize(context.Model),
            ["AMBIGUITIES"] = FormatAmbiguities(context.Ambiguities)
        });

        JsonNode response;
        try
        {
            response = await _llm.SendAsync(PipelineStage.GapDetection, prompt, ct);
        }
        catch (PipelineException ex)
        {
            context.NormalizationNotes.Add($"Belirsizlik taraması çalıştırılamadı: {ex.Message}");
            return;
        }

        foreach (var node in response.Array("ambiguities"))
        {
            var description = node.Str("description");
            if (string.IsNullOrWhiteSpace(description)) continue;

            AddAmbiguity(context, new Ambiguity
            {
                Target = node.Str("target"),
                Kind = ParseKind(node.Str("kind")),
                Impact = node.Str("impact").Equals("High", StringComparison.OrdinalIgnoreCase)
                    ? AmbiguityImpact.High
                    : AmbiguityImpact.Low,
                Description = description,
                Confidence = node.Num("confidence")
            });
        }
    }

    /// <summary>Düşük confidence'lı varlık/ilişkiler de belirsizlik listesine girer.</summary>
    private static void AddConfidenceAmbiguities(PipelineContext context)
    {
        foreach (var entity in context.Model.Entities.Where(e => e.Confidence < 0.5))
        {
            AddAmbiguity(context, new Ambiguity
            {
                Target = entity.Name,
                Kind = AmbiguityKind.EntityExistence,
                Impact = AmbiguityImpact.High,
                Description = $"'{entity.Name}' varlığının ayrı bir varlık olup olmadığı kurallardan net değil. " +
                              entity.ConfidenceReason,
                Confidence = entity.Confidence
            });
        }

        foreach (var relationship in context.Model.Relationships.Where(r => r.Confidence < 0.5))
        {
            AddAmbiguity(context, new Ambiguity
            {
                Target = $"{relationship.FromEntity} → {relationship.ToEntity}",
                Kind = AmbiguityKind.Cardinality,
                Impact = AmbiguityImpact.High,
                Description = $"İlişkinin kardinalitesi kurallardan net çıkarılamadı. {relationship.ConfidenceReason}",
                Confidence = relationship.Confidence
            });
        }
    }

    // ---------------------------------------------------------------- adım 6
    private async Task GenerateQuestionsAsync(PipelineContext context, CancellationToken ct)
    {
        var open = context.Ambiguities
            .Where(a => a.Impact == AmbiguityImpact.High && !a.IsResolved)
            .Where(a => context.Questions.All(q => q.AmbiguityId != a.Id))
            .ToList();

        if (open.Count == 0)
        {
            context.NormalizationNotes.Add(
                "Sorulacak yeni bir yüksek etkili belirsizlik bulunamadı; model bu haliyle kararlı görünüyor.");
            return;
        }

        var prompt = _prompts.Render("questions", new Dictionary<string, string>
        {
            ["TOPIC"] = context.Input.Topic,
            ["MODEL"] = ModelJson.Serialize(context.Model),
            ["AMBIGUITIES"] = FormatAmbiguities(open),
            ["ASKED"] = FormatAskedQuestions(context.Questions)
        });

        JsonNode response;
        try
        {
            response = await _llm.SendAsync(PipelineStage.QuestionGeneration, prompt, ct);
        }
        catch (PipelineException ex)
        {
            context.NormalizationNotes.Add($"Soru üretimi çalıştırılamadı: {ex.Message}");
            return;
        }

        var index = 1;
        var added = 0;

        foreach (var node in response.Array("questions"))
        {
            var text = node.Str("text");
            if (string.IsNullOrWhiteSpace(text)) continue;

            var ambiguityId = node.Str("ambiguityId");
            var ambiguity = open.FirstOrDefault(a => a.Id == ambiguityId) ?? open.FirstOrDefault();
            if (ambiguity is null) continue;

            // Aynı belirsizlik veya aynı soru ikinci kez sorulmaz.
            if (context.Questions.Any(q => q.AmbiguityId == ambiguity.Id)) continue;
            if (context.Questions.Any(q => IsSameQuestion(q.Text, text))) continue;

            context.Questions.Add(new ClarifyingQuestion
            {
                Id = $"Q{context.RoundNumber}-{index++}",
                AmbiguityId = ambiguity.Id,
                Kind = ambiguity.Kind,
                Target = ambiguity.Target,
                Text = text,
                Reason = node.Str("reason"),
                Options = node.StrList("options")
            });
            added++;
        }

        if (added > 0)
            context.QuestionRounds++;
        else
            context.NormalizationNotes.Add(
                "Üretilen sorular daha önce sorulanlarla aynıydı; yeni soru eklenmedi.");
    }

    /// <summary>Noktalama ve büyük/küçük harf farkları tekrarı gizlemesin.</summary>
    private static bool IsSameQuestion(string first, string second) =>
        string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        new(NamingRules.FoldTurkish(value).Where(char.IsLetterOrDigit).ToArray());

    private static string FormatAskedQuestions(IEnumerable<ClarifyingQuestion> questions)
    {
        var list = questions.ToList();
        return list.Count == 0
            ? "(henüz soru sorulmadı)"
            : string.Join("\n", list.Select(q =>
                $"- {q.Text}" + (q.IsApplied ? $" → CEVAP: {q.Answer}" : " (cevap bekliyor)")));
    }

    // ---------------------------------------------------------------- adım 7
    private async Task<ModelPackage> SerializeAsync(PipelineContext context,
        IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Özet ve öneriler hazırlanıyor…");

        var prompt = _prompts.Render("summary", new Dictionary<string, string>
        {
            ["TOPIC"] = context.Input.Topic,
            ["RULES"] = FormatRules(context.Input),
            ["MODEL"] = ModelJson.Serialize(context.Model),
            ["AMBIGUITIES"] = FormatAmbiguities(context.Ambiguities),
            ["NOTES"] = string.Join("\n", context.NormalizationNotes)
        });

        context.Suggestions.Clear();

        try
        {
            var response = await _llm.SendAsync(PipelineStage.Serialization, prompt, ct);
            context.Summary = response.Str("summary");
            context.Suggestions.AddRange(response.StrList("suggestions"));
        }
        catch (PipelineException ex)
        {
            context.Summary = $"Özet üretilemedi: {ex.Message}";
        }

        AddDeterministicSuggestions(context);

        var package = context.ToPackage();
        package.MermaidCode = MermaidSerializer.Serialize(context.Model);
        package.ModelJson = ModelJson.Serialize(context.Model);
        return package;
    }

    private static void AddDeterministicSuggestions(PipelineContext context)
    {
        if (context.Model.Entities.All(e =>
                e.Attributes.Any(a => a.Name.Contains("Olusturma", StringComparison.OrdinalIgnoreCase) ||
                                      a.Name.Contains("Oluşturma", StringComparison.OrdinalIgnoreCase))))
            return;

        context.Suggestions.Add(
            "Audit alanları (OlusturmaTarihi, GuncellemeTarihi, OlusturanKullanici) kurallarda geçmediği için " +
            "modele eklenmedi; standart pratik gereği eklenmesi önerilir.");

        foreach (var relationship in context.Model.Relationships.Where(r => r.Cardinality == Cardinality.ManyToMany))
        {
            var from = context.Model.FindEntity(relationship.FromEntity)?.TechnicalName ?? relationship.FromEntity;
            var to = context.Model.FindEntity(relationship.ToEntity)?.TechnicalName ?? relationship.ToEntity;
            context.Suggestions.Add(
                $"{from} ↔ {to} arasındaki N-N ilişki için " +
                $"{NamingRules.JunctionTableName(from, to)} ara tablosu gerekebilir; kurallarda açıkça geçmiyor.");
        }
    }

    // ------------------------------------------------------------- yardımcı
    private static void AddAmbiguity(PipelineContext context, Ambiguity ambiguity)
    {
        var duplicate = context.Ambiguities.FirstOrDefault(a =>
            a.Kind == ambiguity.Kind &&
            string.Equals(a.Target, ambiguity.Target, StringComparison.OrdinalIgnoreCase));

        if (duplicate is not null) return;

        ambiguity.Id = $"A{context.Ambiguities.Count + 1}";
        context.Ambiguities.Add(ambiguity);
    }

    private static DataModel BuildScopedModel(DataModel model, ICollection<string> scope)
    {
        var entities = model.Entities
            .Where(e => scope.Contains(e.Name) || scope.Contains(e.TechnicalName))
            .ToList();

        var names = entities.Select(e => e.TechnicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new DataModel
        {
            Topic = model.Topic,
            Entities = entities,
            Relationships = model.Relationships
                .Where(r => names.Contains(r.FromEntity) || names.Contains(r.ToEntity))
                .ToList()
        };
    }

    private static IEnumerable<string> EntityNamesFromTarget(DataModel model, string target)
    {
        if (string.IsNullOrWhiteSpace(target)) yield break;

        var separators = new[] { "→", "->", "↔", ".", " " };
        foreach (var token in target.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var entity = model.FindEntity(token.Trim());
            if (entity is not null) yield return entity.Name;
        }
    }

    private static ModelEntity? ReadEntity(JsonNode? node, string? extraRuleId = null)
    {
        var name = node.Str("name");
        if (string.IsNullOrWhiteSpace(name)) return null;

        var ruleIds = node.StrList("sourceRuleIds");
        if (extraRuleId is not null && !ruleIds.Contains(extraRuleId)) ruleIds.Add(extraRuleId);

        return new ModelEntity
        {
            Name = name.Trim(),
            TechnicalName = NamingRules.ToTechnicalName(name),
            Description = node.Str("description"),
            SourceRuleIds = ruleIds,
            Confidence = node.Num("confidence"),
            ConfidenceReason = node.Str("confidenceReason")
        };
    }

    private static List<EntityAttribute> ReadAttributes(JsonNode? entityNode, string? extraRuleId = null)
    {
        var attributes = new List<EntityAttribute>();

        foreach (var node in entityNode.Array("attributes"))
        {
            var name = node.Str("name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var ruleIds = node.StrList("sourceRuleIds");
            if (extraRuleId is not null && !ruleIds.Contains(extraRuleId)) ruleIds.Add(extraRuleId);

            var references = node.Str("references");
            var isForeignKey = node.Bool("foreignKey") || !string.IsNullOrWhiteSpace(references);

            attributes.Add(new EntityAttribute
            {
                Name = name.Trim(),
                TechnicalName = NamingRules.ToTechnicalName(name),
                IsPrimaryKey = node.Bool("primaryKey"),
                IsForeignKey = isForeignKey,
                ReferencesEntity = string.IsNullOrWhiteSpace(references) ? null : references.Trim(),
                ReferencesAttribute = node.Str("referencesAttribute") is { Length: > 0 } refAttr ? refAttr : null,
                IsRequired = node.Bool("required", true),
                IsUnique = node.Bool("unique"),
                SourceRuleIds = ruleIds,
                Confidence = node.Num("confidence"),
                ConfidenceReason = node.Str("confidenceReason")
            });
        }

        return attributes;
    }

    private static Relationship? ReadRelationship(JsonNode? node, string? extraRuleId = null)
    {
        var from = node.Str("from");
        var to = node.Str("to");
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) return null;

        var ruleIds = node.StrList("sourceRuleIds");
        if (extraRuleId is not null && !ruleIds.Contains(extraRuleId)) ruleIds.Add(extraRuleId);

        return new Relationship
        {
            Name = node.Str("name", "ilişkili"),
            FromEntity = from.Trim(),
            ToEntity = to.Trim(),
            Cardinality = ParseCardinality(node.Str("cardinality")),
            IsRequired = node.Bool("required", true),
            Description = node.Str("description"),
            SourceRuleIds = ruleIds,
            Confidence = node.Num("confidence"),
            ConfidenceReason = node.Str("confidenceReason")
        };
    }

    private static Cardinality ParseCardinality(string value) => value.Trim().ToUpperInvariant() switch
    {
        "1-1" or "1:1" or "ONETOONE" => Cardinality.OneToOne,
        "1-N" or "1:N" or "1-M" or "ONETOMANY" => Cardinality.OneToMany,
        "N-1" or "N:1" or "M-1" or "MANYTOONE" => Cardinality.ManyToOne,
        "N-N" or "N:M" or "N-M" or "M-N" or "MANYTOMANY" => Cardinality.ManyToMany,
        _ => Cardinality.Unknown
    };

    private static AmbiguityKind ParseKind(string value) =>
        Enum.TryParse<AmbiguityKind>(value.Trim(), true, out var kind) ? kind : AmbiguityKind.Other;

    private static string FormatRules(ProjectInput input) =>
        input.Rules.Count == 0
            ? "(kural girilmedi)"
            : string.Join("\n", input.Rules.Select(r => $"{r.Id}: {r.Text}"));

    private static string FormatGlossary(ProjectInput input) =>
        input.Glossary.Count == 0
            ? "(terim sözlüğü girilmedi)"
            : string.Join("\n", input.Glossary.Select(g => $"{g.Term} → {g.TechnicalName}"));

    private static string FormatEntities(DataModel model)
    {
        var sb = new StringBuilder();
        foreach (var entity in model.Entities)
        {
            sb.Append($"- {entity.Name}");
            if (!string.IsNullOrWhiteSpace(entity.Description)) sb.Append($": {entity.Description}");
            if (entity.SourceRuleIds.Count > 0) sb.Append($" [kaynak: {string.Join(", ", entity.SourceRuleIds)}]");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatRelationships(DataModel model) =>
        model.Relationships.Count == 0
            ? "(ilişki belirlenmedi)"
            : string.Join("\n", model.Relationships.Select(r =>
                $"- {r.FromEntity} → {r.ToEntity} ({CardinalityText(r.Cardinality)}) : {r.Name}"));

    private static string CardinalityText(Cardinality cardinality) => cardinality switch
    {
        Cardinality.OneToOne => "1-1",
        Cardinality.OneToMany => "1-N",
        Cardinality.ManyToOne => "N-1",
        Cardinality.ManyToMany => "N-N",
        _ => "?"
    };

    private static string FormatAmbiguities(IEnumerable<Ambiguity> ambiguities)
    {
        var list = ambiguities.ToList();
        return list.Count == 0
            ? "(belirsizlik yok)"
            : string.Join("\n", list.Select(a =>
                $"{a.Id} [{a.Impact}/{a.Kind}] {a.Target}: {a.Description}" +
                (a.AppliedDefault is null ? "" : $" (uygulanan varsayılan: {a.AppliedDefault})")));
    }
}
