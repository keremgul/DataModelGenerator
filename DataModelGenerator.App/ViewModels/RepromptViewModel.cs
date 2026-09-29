using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataModelGenerator.App.Dialogs;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Services;

namespace DataModelGenerator.App.ViewModels;

public partial class SelectableEntity : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
}

public partial class RepromptViewModel : ObservableObject, IRefreshable
{
    private readonly SessionState _session;
    private readonly MainViewModel _main;

    [ObservableProperty] private string _request = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private string _mermaidCode = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;

    public ObservableCollection<SelectableEntity> Entities { get; } = new();

    /// <summary>Modelin son turda bildirdiği notlar — kapsam dışı kalan işler burada görünür.</summary>
    public ObservableCollection<string> Notes { get; } = new();

    public RepromptViewModel(SessionState session, MainViewModel main)
    {
        _session = session;
        _main = main;
    }

    public void Refresh()
    {
        var selected = Entities.Where(e => e.IsSelected).Select(e => e.Name).ToHashSet();
        Entities.Clear();

        var package = _session.Package;
        if (package is null)
        {
            ProgressText = "Önce bir model üretmelisiniz.";
            return;
        }

        foreach (var entity in package.Model.Entities)
        {
            Entities.Add(new SelectableEntity
            {
                Name = entity.Name,
                Description = $"{entity.TechnicalName} · {entity.Attributes.Count} alan",
                IsSelected = selected.Contains(entity.Name)
            });
        }

        MermaidCode = package.MermaidCode;
        Summary = package.Summary;

        Notes.Clear();
        foreach (var note in package.RepromptNotes) Notes.Add(note);

        if (string.IsNullOrWhiteSpace(ProgressText))
            ProgressText = "Varlık seçimi opsiyonel: seçim yaparsanız talimat yalnızca o varlıklara, " +
                           "yapmazsanız modelin tamamına uygulanır (ör. \"tüm tablolara oluşturma tarihi ekle\").";
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var entity in Entities) entity.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var entity in Entities) entity.IsSelected = false;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (_session.Context is null)
        {
            ProgressText = "Önce bir model üretmelisiniz.";
            return;
        }

        // Kapsam seçimi opsiyonel: seçim yoksa talimat modelin tamamına uygulanır.
        var scope = Entities.Where(e => e.IsSelected).Select(e => e.Name).ToList();

        if (string.IsNullOrWhiteSpace(Request))
        {
            ProgressText = "Değişiklik talebini yazın.";
            return;
        }

        IsBusy = true;
        var progress = new Progress<string>(message => ProgressText = message);

        try
        {
            var pipeline = _session.CreatePipeline();
            var package = await pipeline.RepromptAsync(_session.Context, scope, Request, progress);

            _session.Package = package;
            MermaidCode = package.MermaidCode;
            Summary = package.Summary;

            ProgressText = scope.Count > 0
                ? $"Değişiklik uygulandı — {string.Join(", ", scope)} güncellendi (tur {package.RoundNumber})."
                : $"Değişiklik modelin tamamına uygulandı (tur {package.RoundNumber}).";
            _main.SetStatus(ProgressText);
            Request = string.Empty;

            Refresh();
        }
        catch (PipelineException ex)
        {
            ProgressText = ex.Message;
            ApiLogDialog.Show($"Reprompt hatası — {PipelineStageMap.DisplayName(ex.Stage)}",
                string.IsNullOrWhiteSpace(ex.RawOutput) ? ex.Message : $"{ex.Message}\n\n{ex.RawOutput}",
                ApiLogger.LastEntry);
        }
        catch (Exception ex)
        {
            ProgressText = $"Beklenmedik hata: {ex.Message}";
            ApiLogDialog.Show("Reprompt Başarısız", ex, ApiLogger.LastEntry);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Back() => _main.NavigateBack();

    [RelayCommand]
    private void Next() => _main.NavigateNext();
}
