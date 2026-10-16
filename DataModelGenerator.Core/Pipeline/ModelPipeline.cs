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
    private readonly SkillLibrary _skills;
    private readonly RuleEngine _ruleEngine = new();
    private readonly NormalizationEngine _normalization = new();
    private readonly ModelValidator _validator = new();

    public ModelPipeline(IModelProvider provider, ProviderConnectionSettings settings, string modelId,
        PromptLibrary? prompts = null, SkillLibrary? skills = null)
    {
        _llm = new LlmJsonClient(provider, settings, modelId);
        _prompts = prompts ?? new PromptLibrary();
        _skills = skills ?? new SkillLibrary();
    }

    /// <summary>İlk üretim: kurallardan model + belirsizlik listesi.</summary>
    public async Task<ModelPackage> GenerateAsync(PipelineContext context,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        context.Model = new DataModel();
        context.Scope.Clear();
        return await RunAsync(context, PipelineStage.RuleRelationAnalysis, progress, ct, generateQuestions: true);
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
            var tracked = context.Questions.FirstOrDefault(q => q.Id == question.Id);
            var ruleText = $"{question.Text} → {question.Answer.Trim()}";

            // Cevap sonradan düzenlendiyse yeni kural eklenmez, mevcut kural güncellenir;
            // böylece model her zaman son cevaba göre üretilir.
            var existingRule = tracked?.InjectedRuleId is null
                ? null
                : context.Input.Rules.FirstOrDefault(r => r.Id == tracked.InjectedRuleId);

            string ruleId;
            if (existingRule is not null)
            {
                existingRule.Text = ruleText;
                ruleId = existingRule.Id;
            }
            else
            {
                ruleId = context.Input.NextRuleId(RuleOrigin.Clarification);
                context.Input.Rules.Add(new BusinessRule
                {
                    Id = ruleId,
                    Origin = RuleOrigin.Clarification,
                    Text = ruleText
                });
            }

            var ambiguity = context.Ambiguities.FirstOrDefault(a => a.Id == question.AmbiguityId);
            if (ambiguity is not null)
            {
                ambiguity.IsResolved = true;
                ambiguity.AppliedDefault = question.Answer.Trim();
            }

            if (tracked is not null)
            {
                tracked.Answer = question.Answer;
                tracked.IsApplied = true;
                tracked.AppliedInRound = context.RoundNumber + 1;
                tracked.InjectedRuleId = ruleId;
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
    /// Sohbetten gelen direktifi uygular. Önce yapılandırılmış bir değişiklik
    /// listesine çevrilir; basit işlemler LLM'siz doğrudan kodla uygulanır.
    /// Sonuç, LLM'in beyanına değil modelin önceki/sonraki hali arasındaki
    /// GERÇEK diff'e bakılarak doğrulanır.
    /// </summary>
    public async Task<(ModelPackage Package, ChatTurn Turn)> SendChatAsync(PipelineContext context,
        string message, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new InvalidOperationException("Mesaj boş olamaz.");
        if (context.Model.Entities.Count == 0)
            throw new InvalidOperationException("Önce bir model üretilmelidir.");

        var before = ModelJson.Deserialize(ModelJson.Serialize(context.Model))!;

        var ruleId = context.Input.NextRuleId(RuleOrigin.Chat);
        context.Input.Rules.Add(new BusinessRule
        {
            Id = ruleId,
            Origin = RuleOrigin.Chat,
            Text = message.Trim()
        });

        var turn = new ChatTurn { UserMessage = message.Trim(), RuleId = ruleId };
        context.ChatTurns.Add(turn);

        progress?.Report("Direktif çözümleniyor…");
        turn.Directives = await RouteAsync(context, message, turn, ct);

        var affected = new List<string>();
        var needsModelCall = false;

        foreach (var directive in turn.Directives)
        {
            if (directive.IsDeterministic)
            {
                // LLM çağrısı yok: yeniden adlandırma, kaldırma, kardinalite doğrudan kodla.
                progress?.Report($"Uygulanıyor: {directive}");
                var result = DirectiveExecutor.Execute(context.Model, directive, ruleId);

                if (result.Applied)
                    affected.AddRange(result.AffectedEntities);
                else
                    turn.NotApplied.Add($"{directive}: {result.Message}");

                continue;
            }

            // Hedefli ama üretim gerektiren işlemler kendi skill dosyasıyla çalışır.
            progress?.Report($"Modele iletiliyor: {directive}");
            try
            {
                if (await ApplyDirectiveSkillAsync(context, directive, message, ruleId, ct))
                {
                    affected.Add(directive.Entity);
                    affected.Add(directive.TargetEntity);
                    continue;
                }
            }
            catch (PipelineException ex)
            {
                turn.NotApplied.Add($"{directive}: {ex.Message}");
                continue;
            }

            needsModelCall = true;
        }

        // Skill'i olmayan veya hiç direktife çevrilemeyen talepler genel yola düşer.
        if (needsModelCall || turn.Directives.Count == 0)
        {
            progress?.Report("Talep modele iletiliyor…");
            var scope = turn.Directives
                .Where(d => !string.IsNullOrWhiteSpace(d.Entity))
                .Select(d => d.Entity)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            await ApplyRepromptResponseAsync(context, scope, message, ruleId, ct);
            affected.AddRange(scope);
        }

        progress?.Report("Model doğrulanıyor…");
        var scopeNames = affected
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Yalnızca etkilenen tablolar yeniden normalize edilir; model bütün olarak doğrulanır.
        ApplyDeterministicLayer(context, scopeNames.Count > 0 ? scopeNames : null);

        turn.Changes = ModelChangeSet.Compare(before, context.Model);
        turn.Status = DetermineStatus(turn);

        var package = await SerializeAsync(context, progress, ct);
        package.LastChatTurn = turn;
        return (package, turn);
    }

    /// <summary>
    /// Diff boşsa "uygulanmadı", bazı direktifler başarısızsa "kısmen uygulandı".
    /// LLM'in "yaptım" demesi ölçüt değildir.
    /// </summary>
    private static ChatTurnStatus DetermineStatus(ChatTurn turn)
    {
        if (turn.Changes.IsEmpty)
        {
            if (turn.NotApplied.Count == 0)
                turn.NotApplied.Add("Model üzerinde hiçbir değişiklik oluşmadı; talep anlaşılmamış olabilir.");
            return ChatTurnStatus.NotApplied;
        }

        return turn.NotApplied.Count > 0 || turn.Unsupported.Count > 0
            ? ChatTurnStatus.PartiallyApplied
            : ChatTurnStatus.Applied;
    }

    private async Task<List<ChatDirective>> RouteAsync(PipelineContext context, string message,
        ChatTurn turn, CancellationToken ct)
    {
        // Önce LLM'siz kalıp eşleştirme: "X alanının adını Y yap" gibi net talepler.
        var matched = DirectivePatterns.TryMatch(message, context.Model);
        if (matched is not null) return [matched];

        var prompt = _skills.Render("router", new Dictionary<string, string>
        {
            ["MESSAGE"] = message,
            ["MODEL"] = FormatModelOutline(context.Model)
        });

        JsonNode response;
        try
        {
            response = await _llm.SendAsync(PipelineStage.AttributeDerivation, prompt, ct);
        }
        catch (PipelineException ex)
        {
            turn.Unsupported.Add($"Direktif çözümlenemedi: {ex.Message}");
            return [];
        }

        turn.Unsupported.AddRange(response.StrList("unsupported"));

        var directives = new List<ChatDirective>();
        foreach (var node in response.Array("directives"))
        {
            var operation = ParseOperation(node.Str("operation"));
            var entity = node.Str("entity");

            // Var olmayan tabloya atıf yapan direktif uygulanmaz.
            if (!string.IsNullOrWhiteSpace(entity) && context.Model.FindEntity(entity) is null)
            {
                turn.NotApplied.Add($"'{entity}' adında bir tablo yok.");
                continue;
            }

            directives.Add(new ChatDirective
            {
                Operation = operation,
                Entity = entity,
                Field = node.Str("field"),
                NewName = node.Str("newName"),
                TargetEntity = node.Str("targetEntity"),
                Cardinality = ParseCardinality(node.Str("cardinality")),
                Fields = node.StrList("fields"),
                Explanation = node.Str("explanation")
            });
        }

        return directives;
    }

    private static DirectiveOperation ParseOperation(string value) => value.Trim().ToLowerInvariant() switch
    {
        "rename-field" => DirectiveOperation.RenameField,
        "add-field" => DirectiveOperation.AddField,
        "remove-field" => DirectiveOperation.RemoveField,
        "rename-entity" => DirectiveOperation.RenameEntity,
        "add-entity" => DirectiveOperation.AddEntity,
        "remove-entity" => DirectiveOperation.RemoveEntity,
        "add-relation" => DirectiveOperation.AddRelation,
        "remove-relation" => DirectiveOperation.RemoveRelation,
        "change-cardinality" => DirectiveOperation.ChangeCardinality,
        "split-entity" => DirectiveOperation.SplitEntity,
        _ => DirectiveOperation.General
    };

    private static string FormatModelOutline(DataModel model) =>
        string.Join("\n", model.Entities.Select(e =>
            $"- {e.Name} ({e.TechnicalName}): {string.Join(", ", e.Attributes.Select(a => a.TechnicalName))}"));

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
        if (context.Model.Entities.Count == 0)
            throw new InvalidOperationException("Önce bir model üretilmelidir.");

        context.Scope.Clear();
        foreach (var name in scopeEntities) context.Scope.Add(name);

        // Kapsam seçimi opsiyoneldir: seçim yoksa talimat modelin tamamına uygulanır.
        var isScoped = context.Scope.Count > 0;
        progress?.Report(isScoped
            ? "Seçili varlıklar için değişiklik isteniyor…"
            : "Değişiklik modelin tamamı için isteniyor…");

        var ruleId = context.Input.NextRuleId(RuleOrigin.Clarification);
        context.Input.Rules.Add(new BusinessRule
        {
            Id = ruleId,
            Origin = RuleOrigin.Clarification,
            Text = $"[Reprompt: {string.Join(", ", context.Scope)}] {request.Trim()}"
        });

        await ApplyRepromptResponseAsync(context, scopeEntities, request, ruleId, ct);

        ApplyDeterministicLayer(context);
        return await RunAsync(context, PipelineStage.NormalizationCheck, progress, ct);
    }

    /// <summary>
    /// Modelden değişiklik ister ve yanıtı modele birleştirir. Hem reprompt hem de
    /// sohbetteki hedefli olmayan talepler bu yolu kullanır.
    /// </summary>
    private async Task ApplyRepromptResponseAsync(PipelineContext context,
        IReadOnlyCollection<string> scopeEntities, string request, string ruleId, CancellationToken ct)
    {
        context.Scope.Clear();
        foreach (var name in scopeEntities) context.Scope.Add(name);

        var isScoped = context.Scope.Count > 0;
        var scopedModel = isScoped ? BuildScopedModel(context.Model, context.Scope) : context.Model;

        var prompt = _prompts.Render("reprompt", new Dictionary<string, string>
        {
            ["RULES"] = FormatRules(context.Input),
            ["SCOPE"] = isScoped
                ? string.Join(", ", context.Scope)
                : "TÜM MODEL (varlık seçilmedi — talimatı modelin tamamına uygula)",
            ["MODEL"] = ModelJson.Serialize(scopedModel),
            ["REQUEST"] = request
        });

        var response = await _llm.SendAsync(PipelineStage.AttributeDerivation, prompt, ct);
        MergeModelResponse(context, response, ruleId);
    }

    /// <summary>
    /// Hedefli bir direktifi, işleme ait skill dosyasıyla modele uygular. Skill
    /// bulunamazsa çağıran taraf genel reprompt yoluna düşer.
    /// </summary>
    private async Task<bool> ApplyDirectiveSkillAsync(PipelineContext context, ChatDirective directive,
        string message, string ruleId, CancellationToken ct)
    {
        var skillName = SkillNameFor(directive.Operation);
        if (skillName is null || !_skills.Exists(skillName)) return false;

        // Yalnızca ilgili varlıklar gönderilir; modelin tamamı LLM'e aktarılmaz.
        var scope = new[] { directive.Entity, directive.TargetEntity }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scopedModel = scope.Count > 0 ? BuildScopedModel(context.Model, scope) : context.Model;

        var prompt = _skills.Render(skillName, new Dictionary<string, string>
        {
            ["RULES"] = FormatRules(context.Input),
            ["RULE_ID"] = ruleId,
            ["MODEL"] = ModelJson.Serialize(scopedModel),
            ["DIRECTIVE"] = directive.ToString(),
            ["FIELDS"] = string.Join(", ", directive.Fields),
            ["MESSAGE"] = message
        });

        var response = await _llm.SendAsync(PipelineStage.AttributeDerivation, prompt, ct);
        MergeModelResponse(context, response, ruleId);
        return true;
    }

    private static string? SkillNameFor(DirectiveOperation operation) => operation switch
    {
        DirectiveOperation.AddField => "add-field",
        DirectiveOperation.AddEntity => "add-entity",
        DirectiveOperation.AddRelation => "add-relation",
        DirectiveOperation.SplitEntity => "split-entity",
        _ => null
    };

    /// <summary>
    /// Model yanıtını mevcut modele birleştirir. Silme yalnızca modelin açıkça
    /// bildirdiği listeler üzerinden yapılır; dönmeyen öğeler korunur.
    /// </summary>
    private void MergeModelResponse(PipelineContext context, JsonNode response, string ruleId)
    {
        foreach (var node in response.Array("entities"))
        {
            var entity = ReadEntity(node, ruleId);
            if (entity is null) continue;

            entity.Attributes = ReadAttributes(node, ruleId);
            ModelMerger.UpsertEntity(context.Model, entity);
            context.Scope.Add(entity.Name);
        }

        // İlişkiler yalnızca modelin açıkça bildirdiği durumda silinir; dönmeyen
        // ilişkiler korunur — aksi halde her reprompt ilişkileri eritirdi.
        foreach (var node in response.Array("relationships"))
        {
            var relationship = ReadRelationship(node, ruleId);
            if (relationship is not null)
                ModelMerger.UpsertRelationship(context.Model, relationship);
        }

        var removedRelationships = ModelMerger.RemoveRelationships(context.Model,
            response.Array("removedRelationships")
                .Select(node => (From: node.Str("from"), To: node.Str("to")))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.From) && !string.IsNullOrWhiteSpace(pair.To))
                .ToList());

        var removed = ModelMerger.RemoveEntities(context.Model, response.StrList("removedEntities"));

        context.RepromptNotes.Clear();
        if (removed.Count > 0)
            context.RepromptNotes.Add($"Modelden kaldırılan varlıklar: {string.Join(", ", removed)}");
        if (removedRelationships.Count > 0)
            context.RepromptNotes.Add($"Kaldırılan ilişkiler: {string.Join(", ", removedRelationships)}");

        foreach (var note in response.StrList("notes"))
            context.RepromptNotes.Add(note);

        foreach (var note in context.RepromptNotes)
            context.NormalizationNotes.Add($"Model notu: {note}");
    }

    /// <summary>
    /// Kullanıcının tetiklediği öneri turu: mevcut model ve soru cevapları üzerinden
    /// daha önce sunulmamış yeni öneriler üretilir.
    /// </summary>
    public async Task<ModelPackage> RequestSuggestionsAsync(PipelineContext context,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (context.Model.Entities.Count == 0)
            throw new InvalidOperationException("Önce bir model üretilmelidir.");

        // Bekleyen öneriler her turda tazelendiği için sayı farkı değil, metin karşılaştırması yapılır.
        var known = context.Suggestions.Select(s => Normalize(s.Text)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        progress?.Report("Cevaplara göre yeni öneriler üretiliyor…");
        var package = await SerializeAsync(context, progress, ct);

        package.NewSuggestionCount = context.Suggestions.Count(s => !known.Contains(Normalize(s.Text)));
        return package;
    }

    /// <summary>
    /// Kullanıcının kabul ettiği öneriler modele uygulanır. Reddedilenler modele
    /// yansıtılmaz; kullanıcının eklediği açıklamalar talimatın parçası olur.
    /// </summary>
    public async Task<ModelPackage> ApplySuggestionsAsync(PipelineContext context,
        IReadOnlyList<Suggestion> decisions,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        foreach (var decision in decisions)
        {
            var tracked = context.Suggestions.FirstOrDefault(s => s.Id == decision.Id);
            if (tracked is null) continue;

            tracked.Decision = decision.Decision;
            tracked.Note = decision.Note;
        }

        var accepted = context.Suggestions
            .Where(s => s.Decision == SuggestionDecision.Accepted && !s.IsApplied)
            .ToList();

        if (accepted.Count == 0)
            throw new InvalidOperationException("Uygulanacak kabul edilmiş öneri yok.");

        var request = new StringBuilder("Aşağıdaki önerileri modele uygula:");
        foreach (var suggestion in accepted)
        {
            request.AppendLine();
            request.Append($"- {suggestion.Text}");
            if (!string.IsNullOrWhiteSpace(suggestion.Note))
                request.Append($" (kullanıcı açıklaması: {suggestion.Note.Trim()})");
        }

        // Uygulandı işareti tur başlamadan konur; aksi halde özet adımı öneriyi listeden düşürür.
        foreach (var suggestion in accepted) suggestion.IsApplied = true;

        var package = await RepromptAsync(context, [], request.ToString(), progress, ct);

        foreach (var suggestion in accepted) suggestion.AppliedInRound = context.RoundNumber;

        package.Suggestions = new List<Suggestion>(context.Suggestions);
        return package;
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
            progress?.Report($"{(int)stage + 1}/{PipelineStageMap.StageCount} — {PipelineStageMap.DisplayName(stage)}…");

            switch (stage)
            {
                case PipelineStage.RuleRelationAnalysis:
                    await AnalyzeRuleRelationsAsync(context, ct);
                    break;
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
    /// <summary>
    /// Kurallar satır satır girilir ama anlamları bağımsız değildir. Bu adım her
    /// kural çiftinin semantik ilişkisini çıkarır; çelişki ve tekrarlar modele
    /// yansımadan önce kullanıcıya soru olarak döner.
    /// </summary>
    private async Task AnalyzeRuleRelationsAsync(PipelineContext context, CancellationToken ct)
    {
        context.RuleRelations.Clear();
        context.PendingItems.RemoveAll(p => p.Kind == PendingItemKind.RuleConflict);

        if (context.Input.Rules.Count < 2) return;

        var prompt = _skills.Render("analyze-br-relations", new Dictionary<string, string>
        {
            ["RULES"] = FormatRules(context.Input)
        });

        JsonNode response;
        try
        {
            response = await _llm.SendAsync(PipelineStage.RuleRelationAnalysis, prompt, ct);
        }
        catch (PipelineException ex)
        {
            context.NormalizationNotes.Add($"Kural ilişki analizi çalıştırılamadı: {ex.Message}");
            return;
        }

        var known = context.Input.Rules.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var node in response.Array("relations"))
        {
            var first = node.Str("first").Trim();
            var second = node.Str("second").Trim();
            if (!known.Contains(first) || !known.Contains(second)) continue;

            var relation = new RuleRelation
            {
                FirstRuleId = first,
                SecondRuleId = second,
                Kind = ParseRelationKind(node.Str("kind")),
                Explanation = node.Str("explanation")
            };

            if (relation.Kind == RuleRelationKind.Unrelated) continue;
            context.RuleRelations.Add(relation);
        }

        AddRuleConflictQuestions(context);
    }

    /// <summary>Çelişen ve tekrar eden kurallar çözülmeden modele yansımaz.</summary>
    private static void AddRuleConflictQuestions(PipelineContext context)
    {
        foreach (var relation in context.RuleRelations.Where(r => r.NeedsResolution))
        {
            var first = context.Input.Rules.FirstOrDefault(r => r.Id == relation.FirstRuleId);
            var second = context.Input.Rules.FirstOrDefault(r => r.Id == relation.SecondRuleId);
            if (first is null || second is null) continue;

            var isConflict = relation.Kind == RuleRelationKind.Contradicts;

            AddAmbiguity(context, new Ambiguity
            {
                Kind = AmbiguityKind.Other,
                Impact = AmbiguityImpact.High,
                Target = $"{relation.FirstRuleId} ↔ {relation.SecondRuleId}",
                Description = isConflict
                    ? $"Kurallar çelişiyor: {relation.Explanation}"
                    : $"Kurallar aynı bilgiyi tekrarlıyor: {relation.Explanation}",
                Confidence = 0.2
            });

            context.PendingItems.Add(new PendingItem
            {
                Kind = PendingItemKind.RuleConflict,
                Reason = PendingReason.Uncertain,
                Target = $"{relation.FirstRuleId} ↔ {relation.SecondRuleId}",
                Explanation = relation.Explanation,
                SourceRuleIds = [relation.FirstRuleId, relation.SecondRuleId],
                Question = isConflict
                    ? $"Bu iki kural çelişiyor. Hangisi geçerli?\n{first.Id}: {first.Text}\n{second.Id}: {second.Text}"
                    : $"Bu iki kural aynı şeyi söylüyor. Hangisi kalsın?\n{first.Id}: {first.Text}\n{second.Id}: {second.Text}",
                SuggestedAnswers =
                [
                    $"{first.Id} geçerli",
                    $"{second.Id} geçerli",
                    isConflict ? "İkisi farklı durumları anlatıyor, ikisi de geçerli" : "İkisi de kalsın"
                ]
            });
        }
    }

    private static RuleRelationKind ParseRelationKind(string value) => value.Trim().ToLowerInvariant() switch
    {
        "ayni-entity" or "aynı-entity" or "sameentity" => RuleRelationKind.SameEntity,
        "detaylandirir" or "detaylandırır" or "elaborates" => RuleRelationKind.Elaborates,
        "bagimli" or "bağımlı" or "dependson" => RuleRelationKind.DependsOn,
        "celisir" or "çelişir" or "contradicts" => RuleRelationKind.Contradicts,
        "tekrar" or "duplicate" => RuleRelationKind.Duplicate,
        _ => RuleRelationKind.Unrelated
    };

    // ---------------------------------------------------------------- adım 2
    private async Task ExtractEntitiesAsync(PipelineContext context, CancellationToken ct)
    {
        var prompt = _prompts.Render("extraction", new Dictionary<string, string>
        {
            ["GLOSSARY"] = FormatGlossary(context.Input),
            ["RULES"] = FormatRules(context.Input),
            ["RULE_RELATIONS"] = FormatRuleRelations(context)
        });

        var response = await _llm.SendAsync(PipelineStage.Extraction, prompt, ct);

        var candidates = new List<ModelEntity>();
        foreach (var node in response.Array("entities"))
        {
            var entity = ReadEntity(node);
            if (entity is null) continue;

            // Yeniden çıkarımda mevcut alanlar korunur; alan adımı bunları tazeleyecek.
            var existing = context.Model.FindEntity(entity.Name);
            if (existing is not null) entity.Attributes = existing.Attributes;

            candidates.Add(entity);
        }

        // Kaynaksız veya belirsiz varlıklar modele girmez; soru listesine düşer.
        context.PendingItems.RemoveAll(p => p.Kind == PendingItemKind.Entity);
        var entities = SourceGuard.FilterEntities(candidates, context.Input, context.PendingItems);

        if (entities.Count == 0)
            throw new PipelineException(PipelineStage.Extraction,
                "Kurallardan hiç varlık çıkarılamadı. Kural listesini genişletip tekrar deneyin.");

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
            ["RULES"] = FormatRules(context.Input),
            ["ENTITIES"] = FormatEntities(context.Model)
        });

        var response = await _llm.SendAsync(PipelineStage.RelationshipInference, prompt, ct);

        var candidates = new List<Relationship>();
        foreach (var node in response.Array("relationships"))
        {
            var relationship = ReadRelationship(node);
            if (relationship is not null) candidates.Add(relationship);
        }

        context.PendingItems.RemoveAll(p => p.Kind == PendingItemKind.Relationship);
        context.Model.Relationships =
            SourceGuard.FilterRelationships(candidates, context.Input, context.Model, context.PendingItems);
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
            ["MUST_CONSTRAINTS"] = FormatMustConstraints(context),
            ["GLOSSARY"] = FormatGlossary(context.Input),
            ["RULES"] = FormatRules(context.Input),
            ["ENTITIES"] = FormatEntities(context.Model),
            ["RELATIONSHIPS"] = FormatRelationships(context.Model),
            ["SCOPE"] = context.Scope.Count == 0
                ? "TÜMÜ"
                : string.Join(", ", targets.Select(t => t.Name))
        });

        var response = await _llm.SendAsync(PipelineStage.AttributeDerivation, prompt, ct);

        context.PendingItems.RemoveAll(p => p.Kind == PendingItemKind.Attribute);

        foreach (var node in response.Array("entities"))
        {
            var name = node.Str("name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var entity = context.Model.FindEntity(name);
            if (entity is null) continue;

            var attributes = ReadAttributes(node);
            if (attributes.Count == 0) continue;

            // Kaynaksız / belirsiz alanlar modele girmez.
            var candidate = new ModelEntity
            {
                Name = entity.Name,
                TechnicalName = entity.TechnicalName,
                SourceRuleIds = entity.SourceRuleIds,
                Attributes = attributes
            };

            var filtered = SourceGuard.FilterEntities([candidate], context.Input, context.PendingItems);
            if (filtered.Count > 0) entity.Attributes = filtered[0].Attributes;
        }

        ApplyDeterministicLayer(context);
    }

    /// <summary>Adlandırma, tip eşlemesi, anahtar tamamlama ve doğrulama — hepsi kod tarafında.</summary>
    /// <param name="scope">Yalnızca bu varlıklar normalize edilir; null ise tüm model.</param>
    private void ApplyDeterministicLayer(PipelineContext context, IEnumerable<string>? scope = null)
    {
        context.Ambiguities.RemoveAll(a => a.FromRuleEngine && !a.IsResolved);

        var notes = _ruleEngine.Normalize(context.Model, context.Input);

        // Model kuralı: çoka-çok ilişkiler ara tabloya çevrilir, sonra FK'ler kurulur.
        var junctions = ModelMerger.CreateJunctionTables(context.Model);
        if (junctions.Count > 0)
        {
            notes.AddRange(_ruleEngine.Normalize(context.Model, context.Input));
            context.NormalizationNotes.Add(
                $"Çoka-çok ilişkiler için ara tablo eklendi: {string.Join(", ", junctions)}.");
        }

        ModelMerger.SyncForeignKeys(context.Model);
        notes.AddRange(_ruleEngine.RelinkReferences(context.Model));

        // Normalizasyon kuralları sırayla işletilir; geçemeyen alan modelde kalmaz.
        context.PendingItems.RemoveAll(p => p.Reason == PendingReason.NormalizationBlocked);
        context.NormalizationResults.Clear();
        context.NormalizationResults.AddRange(
            _normalization.Apply(context.Model, context.PendingItems, scope));

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
        context.NormalizationNotes.RemoveAll(n => !n.StartsWith("Model notu:", StringComparison.Ordinal));

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
        // Beklemede listesi LLM'e sorulmadan doğrudan soruya çevrilir: öğe
        // zaten modele alınmadı, eksik olan yalnızca kullanıcının kararı.
        var fromPending = PendingQuestions.Sync(context.PendingItems, context.Questions, context.RoundNumber);
        if (fromPending > 0)
            context.NormalizationNotes.Add(
                $"Beklemede listesindeki {fromPending} öğe için soru üretildi; bu öğeler modele eklenmedi.");

        var open = context.Ambiguities
            .Where(a => a.Impact == AmbiguityImpact.High && !a.IsResolved)
            .Where(a => context.Questions.All(q => q.AmbiguityId != a.Id))
            .ToList();

        if (open.Count == 0)
        {
            if (fromPending > 0)
                context.QuestionRounds++;
            else
                context.NormalizationNotes.Add(
                    "Sorulacak yeni bir yüksek etkili belirsizlik bulunamadı; model bu haliyle kararlı görünüyor.");
            return;
        }

        var prompt = _prompts.Render("questions", new Dictionary<string, string>
        {
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

        if (added + fromPending > 0)
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

    /// <summary>
    /// Cevaplanmış sorular, serbest metin içine gömülmeden bağlayıcı kısıt bloğu
    /// olarak verilir — model bunları yorumlayamaz, genişletemez, yok sayamaz.
    /// </summary>
    private static string FormatMustConstraints(PipelineContext context)
    {
        var applied = context.Questions.Where(q => q.IsAnswered).ToList();
        if (applied.Count == 0) return "(henüz bağlayıcı cevap yok)";

        return string.Join("\n", applied.Select(q =>
            $"- [{q.InjectedRuleId ?? "Q"}] MUST: \"{q.Text}\" sorusunun cevabı → \"{q.Answer.Trim()}\". " +
            "Bu cevap modelde birebir karşılık bulmalıdır."));
    }

    private static string FormatRuleRelations(PipelineContext context) =>
        context.RuleRelations.Count == 0
            ? "(ilişki analizi yapılmadı)"
            : string.Join("\n", context.RuleRelations.Select(r =>
                $"- {r.FirstRuleId} ↔ {r.SecondRuleId}: {r.Kind} — {r.Explanation}"));

    /// <summary>Öneri üretimi, kullanıcının cevaplarını görerek yapılır.</summary>
    private static string FormatAnswers(IEnumerable<ClarifyingQuestion> questions)
    {
        var answered = questions.Where(q => q.IsAnswered).ToList();
        return answered.Count == 0
            ? "(henüz cevaplanmış soru yok)"
            : string.Join("\n", answered.Select(q => $"- SORU: {q.Text}\n  CEVAP: {q.Answer.Trim()}"));
    }

    private static string FormatSuggestions(IEnumerable<Suggestion> suggestions)
    {
        var list = suggestions.ToList();
        return list.Count == 0
            ? "(henüz öneri sunulmadı)"
            : string.Join("\n", list.Select(s =>
                $"- {s.Text}" + s.Decision switch
                {
                    SuggestionDecision.Accepted => " (kullanıcı kabul etti)",
                    SuggestionDecision.Rejected => " (kullanıcı REDDETTİ — tekrar önerme)",
                    _ => " (karar bekliyor)"
                }));
    }

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
            ["RULES"] = FormatRules(context.Input),
            ["MODEL"] = ModelJson.Serialize(context.Model),
            ["ANSWERS"] = FormatAnswers(context.Questions),
            ["EXISTING_SUGGESTIONS"] = FormatSuggestions(context.Suggestions),
            ["AMBIGUITIES"] = FormatAmbiguities(context.Ambiguities),
            ["NOTES"] = string.Join("\n", context.NormalizationNotes)
        });

        // Kullanıcının karar verdiği öneriler korunur; yalnızca bekleyenler tazelenir.
        context.Suggestions.RemoveAll(s => !s.IsApplied && s.Decision == SuggestionDecision.Pending);

        try
        {
            var response = await _llm.SendAsync(PipelineStage.Serialization, prompt, ct);
            context.Summary = response.Str("summary");
            foreach (var text in response.StrList("suggestions"))
                AddSuggestion(context, text);
        }
        catch (PipelineException ex)
        {
            context.Summary = $"Özet üretilemedi: {ex.Message}";
        }

        AddDeterministicSuggestions(context);

        // Her cevap bağlayıcı kısıttır: modelde karşılık bulup bulmadığı denetlenir.
        context.AnswerCompliance.Clear();
        context.AnswerCompliance.AddRange(AnswerComplianceChecker.Check(context.Model, context.Questions));

        var package = context.ToPackage();
        package.MermaidCode = MermaidSerializer.Serialize(context.Model);
        package.ModelJson = ModelJson.Serialize(context.Model);
        return package;
    }

    /// <summary>Aynı öneri tekrar eklenmez; kullanıcının kararı korunur.</summary>
    private static void AddSuggestion(PipelineContext context, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (context.Suggestions.Any(s => IsSameQuestion(s.Text, text))) return;

        context.Suggestions.Add(new Suggestion
        {
            Id = $"S{context.Suggestions.Count + 1}",
            Text = text.Trim()
        });
    }

    private static void AddDeterministicSuggestions(PipelineContext context)
    {
        var hasAuditFields = context.Model.Entities.All(e =>
            e.Attributes.Any(a => NamingRules.FoldTurkish(a.Name)
                .Contains("Olusturma", StringComparison.OrdinalIgnoreCase)));

        if (!hasAuditFields)
        {
            AddSuggestion(context,
                "Audit alanları (OlusturmaTarihi, GuncellemeTarihi, OlusturanKullanici) kurallarda geçmediği için " +
                "modele eklenmedi; standart pratik gereği eklenmesi önerilir.");
        }

        foreach (var orphan in ModelRules.FindUnrelatedEntities(context.Model))
        {
            AddSuggestion(context,
                $"'{orphan.Name}' hiçbir tabloyla ilişkili değil. Modelde ilişkisiz tablo bulunmamalı — " +
                "ilgili olduğu tabloyu belirtin veya varlığı kaldırın.");
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

    /// <summary>
    /// "confidence" artık kesin/belirsiz. Eski sayısal biçim de okunur: 0.7'nin
    /// altı belirsiz sayılır.
    /// </summary>
    private static Certainty ReadCertainty(JsonNode? node)
    {
        var raw = node.Str("confidence").Trim();

        if (raw.StartsWith("belirsiz", StringComparison.OrdinalIgnoreCase)) return Certainty.Belirsiz;
        if (raw.StartsWith("kesin", StringComparison.OrdinalIgnoreCase)) return Certainty.Kesin;

        return node.Num("confidence", 1.0) < 0.7 ? Certainty.Belirsiz : Certainty.Kesin;
    }

    /// <summary>Şema "sources" kullanır; eski "sourceRuleIds" adı da kabul edilir.</summary>
    private static List<string> ReadSources(JsonNode? node)
    {
        var sources = node.StrList("sources");
        return sources.Count > 0 ? sources : node.StrList("sourceRuleIds");
    }

    private static ModelEntity? ReadEntity(JsonNode? node, string? extraRuleId = null)
    {
        var name = node.Str("name");
        if (string.IsNullOrWhiteSpace(name)) return null;

        var ruleIds = ReadSources(node);
        if (extraRuleId is not null && !ruleIds.Contains(extraRuleId)) ruleIds.Add(extraRuleId);

        return new ModelEntity
        {
            Name = name.Trim(),
            TechnicalName = NamingRules.ToTechnicalName(name),
            Description = node.Str("description"),
            SourceRuleIds = ruleIds,
            Certainty = ReadCertainty(node),
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

            var ruleIds = ReadSources(node);
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
                Certainty = ReadCertainty(node),
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

        var ruleIds = ReadSources(node);
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
            Certainty = ReadCertainty(node),
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
