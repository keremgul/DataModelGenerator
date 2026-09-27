using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataModelGenerator.App.Dialogs;
using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Services;
using DataModelGenerator.Core.Validation;

namespace DataModelGenerator.App.ViewModels;

public class TraceRow
{
    public string Entity { get; init; } = string.Empty;
    public string Attribute { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string Required { get; init; } = string.Empty;
    public string SourceRules { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public partial class GenerationViewModel : ObservableObject, IRefreshable
{
    private readonly SessionState _session;
    private readonly MainViewModel _main;
    private CancellationTokenSource? _cancellation;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private string _mermaidCode = string.Empty;
    [ObservableProperty] private string _modelJson = string.Empty;
    [ObservableProperty] private string _ddl = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _roundInfo = string.Empty;
    [ObservableProperty] private bool _hasModel;

    public ObservableCollection<string> Suggestions { get; } = new();
    public ObservableCollection<string> NormalizationNotes { get; } = new();
    public ObservableCollection<Ambiguity> Ambiguities { get; } = new();
    public ObservableCollection<ValidationIssue> ValidationIssues { get; } = new();
    public ObservableCollection<TraceRow> TraceRows { get; } = new();
    public ObservableCollection<Relationship> Relationships { get; } = new();

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
    private void Cancel() => _cancellation?.Cancel();

    [RelayCommand]
    private void Back() => _main.NavigateBack();

    [RelayCommand]
    private void Next() => _main.NavigateNext();

    private void Display(ModelPackage package)
    {
        MermaidCode = package.MermaidCode;
        ModelJson = package.ModelJson;
        Ddl = package.Ddl;
        Summary = package.Summary;
        HasModel = package.Model.Entities.Count > 0;
        RoundInfo = $"Tur {package.RoundNumber} · {package.GeneratedAt:dd.MM.yyyy HH:mm}";

        Suggestions.Clear();
        foreach (var suggestion in package.Suggestions) Suggestions.Add(suggestion);

        NormalizationNotes.Clear();
        foreach (var note in package.NormalizationNotes) NormalizationNotes.Add(note);

        Ambiguities.Clear();
        foreach (var ambiguity in package.Ambiguities.OrderByDescending(a => a.Impact))
            Ambiguities.Add(ambiguity);

        ValidationIssues.Clear();
        foreach (var issue in package.ValidationIssues.OrderByDescending(i => i.Severity))
            ValidationIssues.Add(issue);

        Relationships.Clear();
        foreach (var relationship in package.Model.Relationships) Relationships.Add(relationship);

        TraceRows.Clear();
        foreach (var entity in package.Model.Entities)
        {
            foreach (var attribute in entity.Attributes)
            {
                TraceRows.Add(new TraceRow
                {
                    Entity = entity.Name,
                    Attribute = attribute.Name,
                    DataType = attribute.DataType,
                    Key = attribute.IsPrimaryKey ? "PK"
                        : attribute.IsForeignKey ? $"FK → {attribute.ReferencesEntity}"
                        : string.Empty,
                    Required = attribute.IsRequired ? "Evet" : "Hayır",
                    SourceRules = string.Join(", ", attribute.SourceRuleIds.Count > 0
                        ? attribute.SourceRuleIds
                        : entity.SourceRuleIds),
                    Confidence = Math.Round(attribute.Confidence, 2),
                    Reason = attribute.ConfidenceReason
                });
            }
        }
    }
}
