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

        // İlişki kuralları tek kaynaktan gelir: skills/relationship-naming.md.
        _prompts.UseRelationRules(() => _skills.RelationRules);
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
    /// Artımlı geliştirme: mevcut modelin üzerine yeni business rule'lar eklenir.
    /// Model sıfırdan üretilmez — mevcut hali LLM'e bağlam olarak gider ve yalnızca
    /// yeni kuralların gerektirdiği delta istenir. Önceki tablo, alan ve ilişkiler
    /// yeni kural açıkça gerektirmedikçe korunur.
    /// </summary>
    public async Task<ModelPackage> ExtendAsync(PipelineContext context,
        IReadOnlyList<string> newRuleTexts,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (context.Model.Entities.Count == 0)
            throw new InvalidOperationException("Önce bir model üretilmelidir.");

        var texts = newRuleTexts
            .Select(text => text.Trim())
            .Where(text => text.Length > 0)
            .ToList();

        if (texts.Count == 0)
            throw new InvalidOperationException("Eklenecek kural yok.");

        FieldDescriptions.ApplyDefaults(context.Model);
        var before = ModelJson.Deserialize(ModelJson.Serialize(context.Model))!;
        var existingRules = FormatRules(context.Input);

        // Yeni kurallar listeye eklenir; kaynak izleri BR-xx olarak devam eder.
        var added = new List<BusinessRule>();
        foreach (var text in texts)
        {
            var rule = new BusinessRule
            {
                Id = context.Input.NextRuleId(RuleOrigin.User),
                Origin = RuleOrigin.User,
                Text = text
            };

            context.Input.Rules.Add(rule);
            added.Add(rule);
        }

        // Yeni kurallar mevcutlarla çelişiyor mu: çelişki modele girmeden soruya döner.
        progress?.Report("Yeni kurallar mevcut kurallarla karşılaştırılıyor…");
        await AnalyzeRuleRelationsAsync(context, ct);
        AddRuleConflictQuestions(context);

        progress?.Report("Model yeni kurallara göre genişletiliyor…");
        var prompt = _prompts.Render("extend", new Dictionary<string, string>
        {
            ["MODEL"] = ModelJson.Serialize(context.Model),
            ["EXISTING_RULES"] = existingRules,
            ["NEW_RULES"] = string.Join("\n", added.Select(r => $"- {r.Id}: {r.Text}")),
            ["RULE_RELATIONS"] = FormatRuleRelations(context)
        });

        var response = await _llm.SendAsync(PipelineStage.Extraction, prompt, ct);
        MergeModelResponse(context, response, added[0].Id, RemovalGuard.FromRequest(string.Join("\n", texts)));

        progress?.Report("Model doğrulanıyor…");
        ApplyDeterministicLayer(context);

        var package = await RunAsync(context, PipelineStage.NormalizationCheck, progress, ct);
        package.LastChanges = ModelChangeSet.Compare(before, context.Model);
        package.AddedRuleIds = added.Select(r => r.Id).ToList();
        return package;
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

        // Otomatik açıklamalar karşılaştırmadan önce yazılır; aksi halde
        // açıklamasız eski bir modelde hiçbir şey yapmayan tur "uygulandı" görünürdü.
        FieldDescriptions.ApplyDefaults(context.Model);
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

        // Yeniden denemede yalnızca bu turun notları geri verilsin.
        context.RepromptNotes.Clear();

        progress?.Report("Direktif çözümleniyor…");
        turn.Directives = await RouteAsync(context, message, turn, ct);

        // Yalnızca soru soruluyorsa model değişmez; yanıt modele bakılarak yazılır.
        var changeDirectives = turn.Directives.Where(d => d.Operation != DirectiveOperation.Question).ToList();
        turn.IsQuestionOnly = changeDirectives.Count == 0 &&
                              turn.Directives.Count > 0 &&
                              turn.Unsupported.Count == 0;

        if (turn.IsQuestionOnly)
        {
            // Soru bir kural değildir; kural listesinde ve izlenebilirlikte iz bırakmaz.
            context.Input.Rules.RemoveAll(r => r.Id == ruleId);
            turn.RuleId = string.Empty;
            turn.Status = ChatTurnStatus.Answered;

            progress?.Report("Yanıt hazırlanıyor…");
            await WriteChatReplyAsync(context, turn, ct);

            var answered = await SerializeAsync(context, progress, ct);
            answered.LastChatTurn = turn;
            return (answered, turn);
        }

        var affected = new List<string>();
        var needsModelCall = false;

        foreach (var directive in changeDirectives)
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
                    affected.Add(directive.NewName);
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

        // Skill'i olmayan, hiç direktife çevrilemeyen veya bir kısmı router'ın
        // işlem listesine girmeyen talepler genel yola düşer: "anlaşılmadı" deyip
        // bırakmak yerine mesajın tamamı modele iletilir.
        if (needsModelCall || changeDirectives.Count == 0 || turn.Unsupported.Count > 0)
        {
            progress?.Report("Talep modele iletiliyor…");
            var scope = turn.Unsupported.Count > 0
                ? new List<string>()
                : changeDirectives
                    .Where(d => !string.IsNullOrWhiteSpace(d.Entity))
                    .Select(d => d.Entity)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            try
            {
                await ApplyRepromptResponseAsync(context, scope, message, ruleId, ct);
                affected.AddRange(context.Scope);
            }
            catch (PipelineException ex)
            {
                turn.NotApplied.Add($"Talep modele uygulanamadı: {ex.Message}");
            }
        }

        progress?.Report("Model doğrulanıyor…");
        ApplyChatDeterministicLayer(context, affected);
        turn.Changes = ModelChangeSet.Compare(before, context.Model);

        // Model talebi açıklamasında doğru tarif edip JSON'da uygulamadıysa
        // "anlaşılmadı" denmez: önceki denemenin notlarıyla bir kez daha,
        // bu kez modelin tamamı üzerinden ve uygulamanın şart olduğu
        // vurgulanarak istenir.
        if (turn.Changes.IsEmpty)
        {
            progress?.Report("Değişiklik oluşmadı, talep yeniden uygulanıyor…");
            try
            {
                await ApplyRepromptResponseAsync(context, [], BuildRetryRequest(context, turn, message), ruleId, ct,
                    removalRequest: message);
                affected.AddRange(context.Scope);

                ApplyChatDeterministicLayer(context, affected);
                turn.Changes = ModelChangeSet.Compare(before, context.Model);

                if (!turn.Changes.IsEmpty)
                {
                    turn.Retried = true;
                    turn.NotApplied.Clear();
                    turn.Unsupported.Clear();
                }
            }
            catch (PipelineException ex)
            {
                turn.NotApplied.Add($"Yeniden deneme başarısız: {ex.Message}");
            }
        }
        else if (turn.Unsupported.Count > 0)
        {
            // Router'ın tanımadığı kısım genel yoldan modele iletildi ve model değişti:
            // bu kısım artık "anlaşılmadı" sayılmaz.
            turn.Unsupported.Clear();
        }

        turn.Status = DetermineStatus(turn);

        progress?.Report("Yanıt hazırlanıyor…");
        await WriteChatReplyAsync(context, turn, ct);

        var package = await SerializeAsync(context, progress, ct);
        package.LastChatTurn = turn;
        return (package, turn);
    }

    /// <summary>Yalnızca etkilenen tablolar yeniden normalize edilir; model bütün olarak doğrulanır.</summary>
    private void ApplyChatDeterministicLayer(PipelineContext context, IEnumerable<string> affected)
    {
        var scopeNames = affected
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        ApplyDeterministicLayer(context, scopeNames.Count > 0 ? scopeNames : null);
    }

    /// <summary>
    /// İlk deneme modeli değiştirmediğinde gönderilen ikinci talep. Modelin
    /// kendi notları ve router açıklamaları geri verilir: çoğu zaman model
    /// ne yapılması gerektiğini doğru yazıp JSON'a koymayı atlıyor.
    /// </summary>
    private static string BuildRetryRequest(PipelineContext context, ChatTurn turn, string message)
    {
        var sb = new StringBuilder();
        sb.AppendLine(message.Trim());
        sb.AppendLine();
        sb.AppendLine("ÖNEMLİ: Bu talep için yapılan ilk denemede modelde HİÇBİR değişiklik oluşmadı.");
        sb.AppendLine("Not veya açıklama yazmak değişiklik sayılmaz. Talebin gerektirdiği değişikliği " +
                      "\"entities\", \"relationships\" veya kaldırma listelerinde GERÇEKTEN döndür.");
        sb.AppendLine("Tablo ve alan adlarını MODEL'de yazdığı gibi kullan. Talep bir alanın özelliğini " +
                      "(zorunlu, tekil, açıklama) değiştiriyorsa alanı yeni değeriyle \"attributes\" altında döndür.");

        var hints = turn.Directives
            .Select(d => d.Explanation)
            .Concat(turn.Unsupported)
            .Concat(turn.NotApplied)
            .Concat(context.RepromptNotes)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct()
            .ToList();

        if (hints.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("İlk denemede yazılan açıklamalar (bunları uygula):");
            foreach (var hint in hints) sb.AppendLine($"- {hint}");
        }

        return sb.ToString().TrimEnd();
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
                turn.NotApplied.Add("İki denemede de model üzerinde değişiklik oluşmadı. Tablo ve alan adını " +
                                    "modelde göründüğü gibi yazarak tekrar deneyin.");
            return ChatTurnStatus.NotApplied;
        }

        return turn.NotApplied.Count > 0 || turn.Unsupported.Count > 0
            ? ChatTurnStatus.PartiallyApplied
            : ChatTurnStatus.Applied;
    }

    /// <summary>
    /// Tur uygulandıktan sonra kullanıcıya yazılan yanıt ve öneriler. Yanıt
    /// modelin beyanına değil gerçek diff'e dayandırılır; öneriler teklif olarak
    /// kalır, kullanıcı istemeden modele uygulanmaz. Bu adım başarısız olsa bile
    /// turun kendisi geçerlidir.
    /// </summary>
    private async Task WriteChatReplyAsync(PipelineContext context, ChatTurn turn, CancellationToken ct)
    {
        var fallback = turn.IsQuestionOnly
            ? "Soru yanıtlanamadı; model değiştirilmedi."
            : turn.Changes.Describe();

        if (!_skills.Exists("chat"))
        {
            turn.Reply = fallback;
            return;
        }

        var prompt = _skills.Render("chat", new Dictionary<string, string>
        {
            ["MESSAGE"] = turn.UserMessage,
            ["DIFF"] = turn.Changes.IsEmpty ? "(modelde hiçbir değişiklik olmadı)" : turn.Changes.Describe(),
            ["NOT_APPLIED"] = turn.NotApplied.Count == 0
                ? "(uygulanamayan bir şey yok)"
                : string.Join("\n", turn.NotApplied.Select(text => $"- {text}")),
            ["MODEL"] = FormatModelOutline(context.Model),
            ["PENDING"] = FormatPendingItems(context.PendingItems),
            ["QUESTION"] = turn.IsQuestionOnly
                ? "evet — kullanıcı soru soruyor, model değiştirilmedi; soruyu MODEL'e bakarak yanıtla"
                : "hayır — bir değişiklik talebi"
        });

        JsonNode response;
        try
        {
            response = await _llm.SendAsync(PipelineStage.Serialization, prompt, ct);
        }
        catch (PipelineException)
        {
            // Yanıt üretilemezse turun sonucu yine de gösterilir.
            turn.Reply = fallback;
            return;
        }

        var reply = response.Str("reply").Trim();
        turn.Reply = string.IsNullOrWhiteSpace(reply) ? fallback : reply;

        foreach (var node in response.Array("suggestions"))
        {
            var text = node.Str("text").Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            // Var olmayan tabloya atıf yapan öneri gösterilmez.
            var related = node.StrList("relatedEntities")
                .Where(name => context.Model.FindEntity(name) is not null)
                .ToList();

            turn.Suggestions.Add(new ChatSuggestion
            {
                Text = text,
                Reason = node.Str("reason").Trim(),
                RelatedEntities = related
            });
        }
    }

    private static string FormatPendingItems(IReadOnlyList<PendingItem> pending) =>
        pending.Count == 0
            ? "(beklemede öğe yok)"
            : string.Join("\n", pending.Select(item =>
                $"- [{item.ReasonText}] {item.Target}: {item.Explanation}"));

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
            var newName = node.Str("newName");

            // Yeni tablo eklenirken ad çoğu zaman "entity" alanına yazılıyor;
            // henüz var olmayan tablo burada beklenen durumdur.
            if (operation == DirectiveOperation.AddEntity &&
                !string.IsNullOrWhiteSpace(entity) && context.Model.FindEntity(entity) is null)
            {
                if (string.IsNullOrWhiteSpace(newName)) newName = entity;
                entity = string.Empty;
            }

            // Var olmayan tabloya atıf yapan direktif uygulanmaz; ama talep
            // kaybolmasın diye genel yola devredilir.
            if (!string.IsNullOrWhiteSpace(entity) && context.Model.FindEntity(entity) is null)
            {
                turn.Unsupported.Add($"'{entity}' adında bir tablo bulunamadı: {node.Str("explanation")}".TrimEnd(' ', ':'));
                continue;
            }

            directives.Add(new ChatDirective
            {
                Operation = operation,
                Entity = context.Model.FindEntity(entity)?.TechnicalName ?? entity,
                Field = node.Str("field"),
                NewName = newName,
                TargetEntity = node.Str("targetEntity"),
                Cardinality = ParseCardinality(node.Str("cardinality")),
                Fields = node.StrList("fields"),
                Explanation = node.Str("explanation"),
                Description = node.Str("description"),
                Required = OptionalBool(node, "required"),
                Unique = OptionalBool(node, "unique")
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
        "set-description" => DirectiveOperation.SetDescription,
        "update-field" => DirectiveOperation.UpdateField,
        "question" => DirectiveOperation.Question,
        _ => DirectiveOperation.General
    };

    /// <summary>Alan yoksa null — "değişmesin" anlamına gelir, false değil.</summary>
    private static bool? OptionalBool(JsonNode? node, string property) =>
        node?[property] is null ? null : node.Bool(property);

    /// <summary>
    /// Modelin güncel hali. Kullanıcı bir alana teknik adıyla da görünen adıyla da
    /// atıfta bulunabildiği için ikisi birlikte verilir; anahtar bilgisi de
    /// eklenir ki model kaldırılamayacak alanları önceden bilsin.
    /// </summary>
    private static string FormatModelOutline(DataModel model)
    {
        var sb = new StringBuilder();

        foreach (var entity in model.Entities)
        {
            sb.AppendLine($"- Tablo: {entity.Name} (teknik ad: {entity.TechnicalName})" +
                          (string.IsNullOrWhiteSpace(entity.Description) ? "" : $" — {entity.Description}"));

            foreach (var attribute in entity.Attributes)
            {
                var marks = attribute.IsPrimaryKey ? " [PK]"
                    : attribute.IsForeignKey ? $" [FK → {attribute.ReferencesEntity}]"
                    : string.Empty;

                var display = attribute.Name.Equals(attribute.TechnicalName, StringComparison.Ordinal)
                    ? attribute.TechnicalName
                    : $"{attribute.Name} (teknik ad: {attribute.TechnicalName})";

                var flags = attribute.IsPrimaryKey ? string.Empty
                    : (attribute.IsRequired ? " zorunlu" : " opsiyonel") + (attribute.IsUnique ? ", tekil" : string.Empty);
                var description = string.IsNullOrWhiteSpace(attribute.Description)
                    ? string.Empty
                    : $" — {attribute.Description}";

                sb.AppendLine($"    - {display}{marks}{flags}{description}");
            }
        }

        foreach (var relationship in model.Relationships)
            sb.AppendLine($"- İlişki: {relationship.FromEntity} → {relationship.ToEntity} ({relationship.Cardinality})");

        return sb.Length == 0 ? "(model boş)" : sb.ToString().TrimEnd();
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
    /// <param name="removalRequest">
    /// Silme kararının dayandırılacağı metin; verilmezse <paramref name="request"/>.
    /// Yeniden denemede talebe eklenen yönlendirme metni silme gerekçesi sayılmasın diye ayrıdır.
    /// </param>
    private async Task ApplyRepromptResponseAsync(PipelineContext context,
        IReadOnlyCollection<string> scopeEntities, string request, string ruleId, CancellationToken ct,
        string? removalRequest = null)
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
        MergeModelResponse(context, response, ruleId, RemovalGuard.FromRequest(removalRequest ?? request));
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

        // Ayrıştırmada taşınan alanlar kaynak tablodan kalkar; başka silme ancak
        // mesaj açıkça istiyorsa uygulanır.
        var guard = RemovalGuard.FromRequest(message,
            directive.Operation == DirectiveOperation.SplitEntity ? directive.Fields : null);
        MergeModelResponse(context, response, ruleId, guard);

        if (directive.Operation == DirectiveOperation.SplitEntity)
            MoveSplitFields(context.Model, directive);

        return true;
    }

    /// <summary>
    /// Birleştirme toplayıcı olduğu için ayrıştırmada taşınan alanlar kaynak
    /// tabloda da kalırdı. Taşıma kod tarafında tamamlanır: alan yeni tabloya
    /// geçmişse kaynaktan kaldırılır, geçmemişse taşınır. Birincil anahtar taşınmaz.
    /// </summary>
    private static void MoveSplitFields(DataModel model, ChatDirective directive)
    {
        var source = model.FindEntity(directive.Entity);
        var target = model.FindEntity(directive.NewName);
        if (source is null || target is null || ReferenceEquals(source, target)) return;

        foreach (var field in directive.Fields)
        {
            var technical = NamingRules.ToTechnicalName(field);
            var attribute = source.Attributes.FirstOrDefault(a =>
                a.TechnicalName.Equals(technical, StringComparison.OrdinalIgnoreCase) ||
                a.Name.Equals(field, StringComparison.OrdinalIgnoreCase));
            if (attribute is null || attribute.IsPrimaryKey) continue;

            if (!target.Attributes.Any(a => a.TechnicalName.Equals(attribute.TechnicalName, StringComparison.OrdinalIgnoreCase)))
                target.Attributes.Add(attribute.Clone());

            source.Attributes.Remove(attribute);
        }
    }

    private static bool SameEntityName(DataModel model, string left, string right)
    {
        var a = model.FindEntity(left)?.TechnicalName ?? NamingRules.ToTechnicalName(left);
        var b = model.FindEntity(right)?.TechnicalName ?? NamingRules.ToTechnicalName(right);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
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
    /// bildirdiği listeler üzerinden yapılır; dönmeyen öğeler korunur. Bildirilen
    /// silme de <paramref name="guard"/> izin vermedikçe — yani kullanıcının talebi
    /// o öğeyi açıkça kaldırmayı istemedikçe — uygulanmaz.
    /// </summary>
    private void MergeModelResponse(PipelineContext context, JsonNode response, string ruleId, RemovalGuard guard)
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

        var blocked = new List<string>();

        // Aynı yanıtta hem eklenip hem silinen ilişki çelişkidir; eklenen korunur.
        var returnedPairs = response.Array("relationships")
            .Select(node => (From: FirstStr(node, "from", "fromEntity"), To: FirstStr(node, "to", "toEntity")))
            .ToList();

        var relationshipTargets = new List<(string From, string To)>();
        foreach (var node in response.Array("removedRelationships"))
        {
            var (from, to) = (FirstStr(node, "from", "fromEntity"), FirstStr(node, "to", "toEntity"));
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) continue;

            var readded = returnedPairs.Any(pair =>
                SameEntityName(context.Model, pair.From, from) && SameEntityName(context.Model, pair.To, to));

            if (!readded && guard.AllowsRelationship(from, to)) relationshipTargets.Add((from, to));
            else if (!readded) blocked.Add($"ilişki {from} → {to}");
        }

        var removedRelationships = ModelMerger.RemoveRelationships(context.Model, relationshipTargets);

        // Alanlar da yalnızca açıkça bildirildiğinde ve talep istediğinde silinir.
        var fieldTargets = new List<(string Entity, string Field)>();
        foreach (var node in response.Array("removedFields"))
        {
            var (entity, field) = (node.Str("entity"), node.Str("field"));
            if (string.IsNullOrWhiteSpace(entity) || string.IsNullOrWhiteSpace(field)) continue;

            if (guard.AllowsField(entity, field)) fieldTargets.Add((entity, field));
            else blocked.Add($"alan {entity}.{field}");
        }

        var removedFields = ModelMerger.RemoveAttributes(context.Model, fieldTargets);

        var entityTargets = new List<string>();
        foreach (var name in response.StrList("removedEntities"))
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (guard.AllowsEntity(name)) entityTargets.Add(name);
            else blocked.Add($"tablo {name}");
        }

        var removed = ModelMerger.RemoveEntities(context.Model, entityTargets);

        context.RepromptNotes.Clear();
        if (blocked.Count > 0)
            context.RepromptNotes.Add(
                $"Talepte istenmediği için silinmeyip korunan öğeler: {string.Join(", ", blocked)}");
        if (removed.Count > 0)
            context.RepromptNotes.Add($"Modelden kaldırılan varlıklar: {string.Join(", ", removed)}");
        if (removedFields.Count > 0)
            context.RepromptNotes.Add($"Kaldırılan alanlar: {string.Join(", ", removedFields)}");
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
            ["EXISTING_STRUCTURE"] = context.Input.ExistingStructure.ToPromptContext(),
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

            // Mevcut tablonun alanları adaya taşınmaz: tablo birleştirilirken
            // alanlarına dokunulmaz, alan adımı bunları ayrıca ele alır.
            candidates.Add(entity);
        }

        // Kaynaksız veya belirsiz varlıklar modele girmez; soru listesine düşer.
        context.PendingItems.RemoveAll(p => p.Kind == PendingItemKind.Entity);
        var entities = SourceGuard.FilterEntities(candidates, context.Input, context.PendingItems);

        // İlk üretimde model bu adımda kurulur.
        if (context.Model.Entities.Count == 0)
        {
            if (entities.Count == 0)
                throw new PipelineException(PipelineStage.Extraction,
                    "Kurallardan hiç varlık çıkarılamadı. Kural listesini genişletip tekrar deneyin.");

            context.Model.Entities = entities;
            return;
        }

        // Model zaten varsa (soru cevabı, yeniden çalıştırma) yeniden çıkarım
        // modeli baştan yazmaz: yanıtta dönmeyen tablo silinmez, yalnızca yeni
        // tablolar eklenir ve mevcutların açıklama/kaynak bilgisi güncellenir.
        foreach (var entity in entities)
            ModelMerger.UpsertEntity(context.Model, entity);
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
        var relationships =
            SourceGuard.FilterRelationships(candidates, context.Input, context.Model, context.PendingItems);

        if (context.Model.Relationships.Count == 0)
        {
            context.Model.Relationships = relationships;
            return;
        }

        // Mevcut ilişkiler korunur; yanıt yalnızca ekler veya kardinaliteyi günceller.
        foreach (var relationship in relationships)
            ModelMerger.UpsertRelationship(context.Model, relationship);
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
            ["EXISTING_STRUCTURE"] = context.Input.ExistingStructure.ToPromptContext(),
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
            if (filtered.Count == 0) continue;

            // Alanı olmayan tablo (ilk üretim) doğrudan doldurulur; alanları olan
            // tabloya yanıt eklenerek birleştirilir — dönmeyen alan silinmez.
            if (entity.Attributes.Count == 0)
                entity.Attributes = filtered[0].Attributes;
            else
                ModelMerger.MergeAttributes(entity, filtered[0].Attributes);
        }

        // Alan silme yalnızca bu turda verilen cevap açıkça istiyorsa yapılır.
        var guard = RemovalGuard.FromRequest(string.Join("\n", context.Questions
            .Where(q => q.IsAnswered && q.AppliedInRound == context.RoundNumber)
            .Select(q => $"{q.Text} {q.Answer}")));

        var removals = response.Array("removedFields")
            .Select(node => (Entity: node.Str("entity"), Field: node.Str("field")))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Entity) && !string.IsNullOrWhiteSpace(pair.Field))
            .Where(pair => guard.AllowsField(pair.Entity, pair.Field))
            .ToList();

        var removedFields = ModelMerger.RemoveAttributes(context.Model, removals);
        if (removedFields.Count > 0)
            context.NormalizationNotes.Add($"Cevaba göre kaldırılan alanlar: {string.Join(", ", removedFields)}");

        ApplyDeterministicLayer(context);
    }

    /// <summary>
    /// Adı yüklenen mevcut yapıda geçen tablolar "mevcut" olarak işaretlenir.
    /// Modelin beyanına bırakılmaz: eşleşme kod tarafında yapılır.
    /// </summary>
    private static void MarkExistingEntities(PipelineContext context)
    {
        var existing = context.Input.ExistingStructureEntities;
        if (existing.Count == 0) return;

        foreach (var entity in context.Model.Entities)
        {
            if (existing.Contains(entity.TechnicalName, StringComparer.OrdinalIgnoreCase))
                entity.Origin = EntityOrigin.Existing;
        }
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

        // İsimlendirme standardı prompt'a yazılır ama ona güvenilmez: model
        // kurala uymayan bir ad döndürürse burada düzeltilir.
        foreach (var correction in NamingEnforcer.Enforce(context.Model))
            context.NormalizationNotes.Add(correction);

        // <TABLOADI>ID kalıbı ilişkinin kendisidir; eksik ilişki kurulur,
        // mükerrer tanım elenir, hedefi bulunamayan referans uyarıya döner.
        // Yüklenen mevcut yapıdaki tablolar da geçerli referans hedefi sayılır.
        foreach (var note in RelationshipRules.Apply(context.Model, context.PendingItems,
                     context.Input.ExistingStructureEntities))
            context.NormalizationNotes.Add(note);

        MarkExistingEntities(context);

        // Kurumsal standart alanlar modelin üretmesine bırakılmaz, kod ekler.
        foreach (var note in StandardFields.Apply(context.Model))
            context.NormalizationNotes.Add(note);

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

        // Anahtar ve standart alanların açıklaması model kuralından bellidir.
        FieldDescriptions.ApplyDefaults(context.Model);

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

        var droppedTypeQuestions = 0;
        var droppedStandardFieldQuestions = 0;

        foreach (var node in response.Array("ambiguities"))
        {
            var description = node.Str("description");
            if (string.IsNullOrWhiteSpace(description)) continue;

            var kind = ParseKind(node.Str("kind"));

            // Model tip bilgisi tutmuyor: tip belirsizliği prompt'ta yasaklandı
            // ama yine gelirse burada elenir — soruların odağı mantıksal yapı.
            if (kind == AmbiguityKind.DataType || IsTypeQuestion(description))
            {
                droppedTypeQuestions++;
                continue;
            }

            // Standart alanlar kurumsal standartla belirlidir; sorulacak bir şey yok.
            if (MentionsStandardField(node.Str("target")) || MentionsStandardField(description))
            {
                droppedStandardFieldQuestions++;
                continue;
            }

            AddAmbiguity(context, new Ambiguity
            {
                Target = node.Str("target"),
                Kind = kind,
                Impact = node.Str("impact").Equals("High", StringComparison.OrdinalIgnoreCase)
                    ? AmbiguityImpact.High
                    : AmbiguityImpact.Low,
                Description = description,
                Confidence = node.Num("confidence")
            });
        }

        if (droppedTypeQuestions > 0)
            context.NormalizationNotes.Add(
                $"{droppedTypeQuestions} alan tipi belirsizliği elendi; model tip bilgisi tutmuyor.");

        if (droppedStandardFieldQuestions > 0)
            context.NormalizationNotes.Add(
                $"{droppedStandardFieldQuestions} standart alan belirsizliği elendi; " +
                "bu alanlar kurumsal standartla belirlidir.");
    }

    /// <summary>
    /// Metin standart alanlardan birine mi işaret ediyor? Bu alanlar için soru
    /// üretilmez — anlamları ve zorunlulukları standartla tanımlıdır.
    /// </summary>
    private static bool MentionsStandardField(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var folded = NamingRules.FoldTurkish(text).ToUpperInvariant();
        return StandardFields.All.Any(f => folded.Contains(f.Name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Tip/uzunluk sorusu mu? İş mantığını etkileyen kısıt soruları (negatif
    /// olabilir mi, ileri tarihli olabilir mi) tip sorusu sayılmaz.
    /// </summary>
    private static bool IsTypeQuestion(string description)
    {
        var folded = NamingRules.FoldTurkish(description).ToLowerInvariant();

        string[] typeHints =
        [
            "varchar", "nvarchar", "char(", "decimal", "numeric", "bigint", "smallint",
            "veri tipi", "alan tipi", "veri turu", "alan turu", "kac karakter",
            "karakter uzunlugu", "alan uzunlugu", "kac hane", "ondalik basamak"
        ];

        return typeHints.Any(hint => folded.Contains(hint));
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

            // Standart alanlar için soru üretilmez.
            if (MentionsStandardField(text) || MentionsStandardField(ambiguity.Target)) continue;

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
    /// <summary>
    /// Birebir karşılaştırma yetmez: model aynı soruyu farklı kelimelerle
    /// sorabilir. Anlamlı kelime örtüşmesine bakılır.
    /// </summary>
    private static bool IsSameQuestion(string first, string second) =>
        QuestionSimilarity.AreSame(first, second);

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
        await DescribeFieldsAsync(context, progress, ct);

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

    /// <summary>
    /// Açıklaması boş kalan her alan için yalın Türkçe bir açıklama istenir.
    /// Yalnızca eksikler gönderilir; dolu açıklamaya — özellikle kullanıcının
    /// sohbetten yazdığına — dokunulmaz. Başarısızlık akışı durdurmaz.
    /// </summary>
    private async Task DescribeFieldsAsync(PipelineContext context, IProgress<string>? progress, CancellationToken ct)
    {
        if (FieldDescriptions.ApplyDefaults(context.Model) == 0 || !_skills.Exists("describe-fields")) return;

        var missing = new StringBuilder();
        foreach (var entity in context.Model.Entities)
        {
            var fields = entity.Attributes.Where(a => string.IsNullOrWhiteSpace(a.Description)).ToList();
            if (fields.Count == 0) continue;

            missing.AppendLine($"- Tablo: {entity.TechnicalName}" +
                               (string.IsNullOrWhiteSpace(entity.Description) ? "" : $" — {entity.Description}"));
            foreach (var field in fields)
                missing.AppendLine($"    - {field.TechnicalName}" +
                                   (field.Name.Equals(field.TechnicalName, StringComparison.Ordinal) ? "" : $" ({field.Name})"));
        }

        progress?.Report("Alan açıklamaları yazılıyor…");
        var prompt = _skills.Render("describe-fields", new Dictionary<string, string>
        {
            ["RULES"] = FormatRules(context.Input),
            ["FIELDS"] = missing.ToString().TrimEnd()
        });

        JsonNode response;
        try
        {
            response = await _llm.SendAsync(PipelineStage.Serialization, prompt, ct);
        }
        catch (PipelineException ex)
        {
            context.NormalizationNotes.Add($"Alan açıklamaları yazılamadı: {ex.Message}");
            return;
        }

        foreach (var node in response.Array("descriptions"))
        {
            var entity = context.Model.FindEntity(node.Str("entity"));
            var fieldName = NamingRules.ToTechnicalName(node.Str("field"));
            var text = node.Str("description").Trim();
            if (entity is null || fieldName.Length == 0 || text.Length == 0) continue;

            var attribute = entity.Attributes.FirstOrDefault(a =>
                a.TechnicalName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

            // Yalnızca boş açıklama doldurulur.
            if (attribute is not null && string.IsNullOrWhiteSpace(attribute.Description))
                attribute.Description = text;
        }
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
        // Audit alanları artık kurumsal standart olarak her tabloya ekleniyor;
        // ayrıca önerilmesine gerek kalmadı.
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
            Description = FirstStr(node, "description", "definition"),
            SourceRuleIds = ruleIds,
            Origin = node.Str("origin").Trim().Equals("mevcut", StringComparison.OrdinalIgnoreCase)
                ? EntityOrigin.Existing
                : EntityOrigin.New,
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

            // Skill şemaları "isPrimaryKey"/"referencesEntity" adlarını kullanıyor,
            // prompt şemaları "primaryKey"/"references"; ikisi de okunur.
            var references = FirstStr(node, "references", "referencesEntity");
            var isForeignKey = node.Bool("foreignKey") || node.Bool("isForeignKey") ||
                               !string.IsNullOrWhiteSpace(references);

            attributes.Add(new EntityAttribute
            {
                Name = name.Trim(),
                TechnicalName = NamingRules.ToTechnicalName(name),
                IsPrimaryKey = node.Bool("primaryKey") || node.Bool("isPrimaryKey"),
                IsForeignKey = isForeignKey,
                ReferencesEntity = string.IsNullOrWhiteSpace(references) ? null : references.Trim(),
                ReferencesAttribute = node.Str("referencesAttribute") is { Length: > 0 } refAttr ? refAttr : null,
                IsRequired = node.Bool("required", true),
                IsUnique = node.Bool("unique"),
                Description = node.Str("description").Trim(),
                SourceRuleIds = ruleIds,
                Certainty = ReadCertainty(node),
                ConfidenceReason = node.Str("confidenceReason")
            });
        }

        return attributes;
    }

    private static string FirstStr(JsonNode? node, params string[] properties) =>
        properties.Select(p => node.Str(p)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static Relationship? ReadRelationship(JsonNode? node, string? extraRuleId = null)
    {
        var from = FirstStr(node, "from", "fromEntity");
        var to = FirstStr(node, "to", "toEntity");
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
