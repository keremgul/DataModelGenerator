using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataModelGenerator.App.Dialogs;
using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Services;
using DataModelGenerator.Core.Validation;

namespace DataModelGenerator.App.ViewModels;

public partial class GenerationViewModel : ObservableObject, IRefreshable
{
    private readonly SessionState _session;
    private readonly MainViewModel _main;
    private CancellationTokenSource? _cancellation;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private string _mermaidCode = string.Empty;
    [ObservableProperty] private string _roundInfo = string.Empty;
    [ObservableProperty] private bool _hasModel;
    [ObservableProperty] private bool _hasSuggestions;
    [ObservableProperty] private string _traceFilterText = string.Empty;
    [ObservableProperty] private string _pendingCountText = string.Empty;

    /// <summary>Doğrulama sekmesi kaldırıldı; ciddi sorunlar bu şeritte duyurulur.</summary>
    [ObservableProperty] private string _validationWarning = string.Empty;
    [ObservableProperty] private string _validationDetails = string.Empty;

    public ObservableCollection<SuggestionItem> Suggestions { get; } = new();
    public ObservableCollection<string> NormalizationNotes { get; } = new();
    public ObservableCollection<Ambiguity> Ambiguities { get; } = new();
    public ObservableCollection<ValidationIssue> ValidationIssues { get; } = new();
    /// <summary>Kaynaksız, belirsiz veya normalizasyonu geçemediği için modele alınmayan öğeler.</summary>
    public ObservableCollection<PendingItem> PendingItems { get; } = new();

    /// <summary>İzlenebilirlik tablosunda gösterilen satırlar (kaynak seçiliyse filtrelenmiş).</summary>
    public ObservableCollection<TraceEntry> TraceEntries { get; } = new();

    /// <summary>Kurallar, cevaplar ve sohbet direktifleri — ters yön gezinmenin başlangıcı.</summary>
    public ObservableCollection<TraceSource> TraceSources { get; } = new();

    private TraceabilityTable _trace = new();

    /// <summary>Seçilen kaynak tabloyu filtreler: o kaynaktan türeyen alanlar listelenir.</summary>
    [ObservableProperty] private TraceSource? _selectedTraceSource;

    /// <summary>Seçilen satırın kaynakları tam metniyle detay panelinde açılır.</summary>
    [ObservableProperty] private TraceEntry? _selectedTraceEntry;

    partial void OnSelectedTraceSourceChanged(TraceSource? value) => ApplyTraceFilter();

    /// <summary>
    /// Bu adımın altında çalışan pipeline adımları. Ayrıntısı docs/pipeline.md
    /// içindeki akış diyagramlarında; buradaki liste oradaki sırayla aynıdır.
    /// </summary>
    public string PipelineStages { get; } = string.Join("  ›  ",
        Enum.GetValues<PipelineStage>()
            .Select(stage => $"{(int)stage + 1}. {PipelineStageMap.DisplayName(stage)}"));

    public GenerationViewModel(SessionState session, MainViewModel main)
    {
        _session = session;
        _main = main;
    }

    public void Refresh()
    {
        if (_session.Package is not null) Display(_session.Package);
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (!_session.HasConnection)
        {
            ProgressText = "Aktif bağlantı profili yok.";
            return;
        }

        if (_session.Input.Rules.Count == 0)
        {
            ProgressText = "Business rule listesi boş.";
            return;
        }

        IsBusy = true;
        _cancellation = new CancellationTokenSource();
        var progress = new Progress<string>(message => ProgressText = message);

        try
        {
            var pipeline = _session.CreatePipeline();
            var context = new PipelineContext { Input = _session.Input };

            var package = await pipeline.GenerateAsync(context, progress, _cancellation.Token);

            _session.Context = context;
            _session.Package = package;

            Display(package);
            _main.CompleteStep(2);
            ProgressText = $"Üretim tamamlandı — {package.Model.Entities.Count} varlık, " +
                           $"{package.Model.Relationships.Count} ilişki, {package.Questions.Count} soru.";
            _main.SetStatus(ProgressText);
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Üretim iptal edildi.";
        }
        catch (PipelineException ex)
        {
            ProgressText = ex.Message;
            ApiLogDialog.Show($"Pipeline hatası — {PipelineStageMap.DisplayName(ex.Stage)}",
                string.IsNullOrWhiteSpace(ex.RawOutput)
                    ? ex.Message
                    : $"{ex.Message}\n\n=== Ham Yanıt ===\n{ex.RawOutput}",
                ApiLogger.LastEntry);
        }
        catch (Exception ex)
        {
            ProgressText = $"Beklenmedik hata: {ex.Message}";
            ApiLogDialog.Show("Üretim Başarısız", ex, ApiLogger.LastEntry);
        }
        finally
        {
            IsBusy = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    [RelayCommand]
    private async Task ApplySuggestionsAsync()
    {
        if (_session.Context is null)
        {
            ProgressText = "Önce bir model üretmelisiniz.";
            return;
        }

        var decisions = Suggestions.Where(s => !s.IsApplied).Select(s => s.ToDecision()).ToList();
        if (decisions.All(d => d.Decision != SuggestionDecision.Accepted))
        {
            ProgressText = "Uygulanacak öneri yok — en az bir öneriyi 'Kabul' olarak işaretleyin.";
            return;
        }

        IsBusy = true;
        var progress = new Progress<string>(message => ProgressText = message);

        try
        {
            var pipeline = _session.CreatePipeline();
            var package = await pipeline.ApplySuggestionsAsync(_session.Context, decisions, progress);

            _session.Package = package;
            Display(package);
            _main.CompleteStep(2);

            var applied = decisions.Count(d => d.Decision == SuggestionDecision.Accepted);
            var rejected = decisions.Count(d => d.Decision == SuggestionDecision.Rejected);
            ProgressText = $"{applied} öneri modele uygulandı" +
                           (rejected > 0 ? $", {rejected} öneri reddedildi" : string.Empty) +
                           $" (tur {package.RoundNumber}).";
            _main.SetStatus(ProgressText);
        }
        catch (PipelineException ex)
        {
            ProgressText = ex.Message;
            ApiLogDialog.Show($"Öneri uygulama hatası — {PipelineStageMap.DisplayName(ex.Stage)}",
                string.IsNullOrWhiteSpace(ex.RawOutput) ? ex.Message : $"{ex.Message}\n\n{ex.RawOutput}",
                ApiLogger.LastEntry);
        }
        catch (Exception ex)
        {
            ProgressText = $"Beklenmedik hata: {ex.Message}";
            ApiLogDialog.Show("Öneriler Uygulanamadı", ex, ApiLogger.LastEntry);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RequestSuggestionsAsync()
    {
        if (_session.Context is null)
        {
            ProgressText = "Önce bir model üretmelisiniz.";
            return;
        }

        IsBusy = true;
        var progress = new Progress<string>(message => ProgressText = message);

        try
        {
            var pipeline = _session.CreatePipeline();
            var package = await pipeline.RequestSuggestionsAsync(_session.Context, progress);

            _session.Package = package;
            Display(package);

            ProgressText = package.NewSuggestionCount > 0
                ? $"{package.NewSuggestionCount} yeni öneri üretildi."
                : "Yeni öneri üretilemedi — cevaplardan doğan başka bir öneri kalmadı.";
            _main.SetStatus(ProgressText);
        }
        catch (PipelineException ex)
        {
            ProgressText = ex.Message;
            ApiLogDialog.Show($"Öneri üretimi hatası — {PipelineStageMap.DisplayName(ex.Stage)}",
                string.IsNullOrWhiteSpace(ex.RawOutput) ? ex.Message : $"{ex.Message}\n\n{ex.RawOutput}",
                ApiLogger.LastEntry);
        }
        catch (Exception ex)
        {
            ProgressText = $"Beklenmedik hata: {ex.Message}";
            ApiLogDialog.Show("Öneri Üretimi Başarısız", ex, ApiLogger.LastEntry);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cancellation?.Cancel();

    [RelayCommand]
    private void Back() => _main.NavigateBack();

    [RelayCommand]
    private void Next() => _main.NavigateNext();

    private void Display(ModelPackage package)
    {
        MermaidCode = package.MermaidCode;
        HasModel = package.Model.Entities.Count > 0;
        RoundInfo = $"Tur {package.RoundNumber} · {package.GeneratedAt:dd.MM.yyyy HH:mm}";

        Suggestions.Clear();
        foreach (var suggestion in package.Suggestions)
            Suggestions.Add(new SuggestionItem(suggestion));

        HasSuggestions = Suggestions.Any(s => !s.IsApplied);

        NormalizationNotes.Clear();
        foreach (var note in package.NormalizationNotes) NormalizationNotes.Add(note);

        Ambiguities.Clear();
        foreach (var ambiguity in package.Ambiguities.OrderByDescending(a => a.Impact))
            Ambiguities.Add(ambiguity);

        ValidationIssues.Clear();
        foreach (var issue in package.ValidationIssues.OrderByDescending(i => i.Severity))
            ValidationIssues.Add(issue);

        ShowValidationWarning(package);

        PendingItems.Clear();
        foreach (var item in package.PendingItems) PendingItems.Add(item);
        PendingCountText = PendingItems.Count == 0
            ? "Beklemede öğe yok — modele giren her şeyin kaynağı var."
            : $"{PendingItems.Count} öğe modele alınmadı. Her biri için soru üretildi; " +
              "cevaplandıktan sonra modele girebilir.";

        // Serbest metin özet yerine yapılandırılmış izlenebilirlik tablosu.
        _trace = TraceabilityTable.Build(package, _session.Input);

        var previousFilter = SelectedTraceSource?.RuleId;
        TraceSources.Clear();
        foreach (var source in _trace.Sources) TraceSources.Add(source);

        SelectedTraceEntry = null;
        SelectedTraceSource = previousFilter is null
            ? null
            : TraceSources.FirstOrDefault(s => s.RuleId == previousFilter);

        ApplyTraceFilter();
    }

    /// <summary>
    /// Hata ve uyarı seviyesindeki doğrulama bulguları, sekme olmadan da
    /// görünür kalsın diye üstte tek satırda özetlenir; ayrıntı tooltip'te.
    /// </summary>
    private void ShowValidationWarning(ModelPackage package)
    {
        var serious = package.ValidationIssues
            .Where(i => i.Severity >= ValidationSeverity.Warning)
            .ToList();

        if (serious.Count == 0)
        {
            ValidationWarning = string.Empty;
            ValidationDetails = string.Empty;
            return;
        }

        var errors = serious.Count(i => i.Severity == ValidationSeverity.Error);
        var warnings = serious.Count - errors;

        var parts = new List<string>();
        if (errors > 0) parts.Add($"{errors} hata");
        if (warnings > 0) parts.Add($"{warnings} uyarı");

        ValidationWarning = $"Doğrulama: {string.Join(", ", parts)} — ayrıntı için üzerine gelin.";
        ValidationDetails = string.Join("\n", serious.Select(i => $"[{i.Severity}] {i.Target}: {i.Message}"));
    }

    /// <summary>Kaynak seçilmişse yalnızca o kaynaktan türeyen alanlar gösterilir.</summary>
    private void ApplyTraceFilter()
    {
        TraceEntries.Clear();

        var source = SelectedTraceSource;
        var entries = source is null ? _trace.Entries : _trace.EntriesOf(source.RuleId);
        foreach (var entry in entries) TraceEntries.Add(entry);

        TraceFilterText = source is null
            ? $"{TraceEntries.Count} alan · filtre yok"
            : $"{source.RuleId} kaynaklı {TraceEntries.Count} alan — filtreyi kaldırmak için 'Tümünü Göster'.";
    }

    [RelayCommand]
    private void ClearTraceFilter() => SelectedTraceSource = null;
}
