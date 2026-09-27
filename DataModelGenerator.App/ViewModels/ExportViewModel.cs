using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataModelGenerator.Core.Export;
using DataModelGenerator.Core.Simulation;
using Microsoft.Win32;

namespace DataModelGenerator.App.ViewModels;

public partial class ExportViewModel : ObservableObject, IRefreshable
{
    private readonly SessionState _session;
    private readonly MainViewModel _main;

    [ObservableProperty] private int _rowCount = 20;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _hasModel;
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<string> EntitySummaries { get; } = new();

    public ExportViewModel(SessionState session, MainViewModel main)
    {
        _session = session;
        _main = main;
        RowCount = App.SettingsStore.Settings.SampleRowCount;
    }

    public void Refresh()
    {
        EntitySummaries.Clear();
        HasModel = _session.Package is not null;

        if (_session.Package is null)
        {
            StatusMessage = "Önce bir model üretmelisiniz.";
            return;
        }

        foreach (var entity in _session.Package.Model.Entities)
            EntitySummaries.Add($"{entity.TechnicalName} — {entity.Attributes.Count} alan");

        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void ExportMermaid() =>
        SaveText(_session.Package?.MermaidCode, "Mermaid kodu",
            "Mermaid dosyası (*.mmd)|*.mmd|Metin dosyası (*.txt)|*.txt", ".mmd");

    [RelayCommand]
    private void ExportJson() =>
        SaveText(_session.Package?.ModelJson, "Model JSON",
            "JSON dosyası (*.json)|*.json", ".json");

    [RelayCommand]
    private void ExportDdl() =>
        SaveText(_session.Package?.Ddl, "DDL betiği",
            "SQL dosyası (*.sql)|*.sql|Metin dosyası (*.txt)|*.txt", ".sql");

    [RelayCommand]
    private async Task ExportSampleDataAsync()
    {
        var package = _session.Package;
        if (package is null)
        {
            SetStatus("Önce bir model üretmelisiniz.", true);
            return;
        }

        if (RowCount < 1)
        {
            SetStatus("Satır sayısı en az 1 olmalıdır.", true);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Örnek veri simülasyonunu kaydet",
            Filter = "Excel çalışma kitabı (*.xlsx)|*.xlsx",
            FileName = SuggestFileName(".xlsx"),
            InitialDirectory = App.SettingsStore.Settings.LastExportDirectory
        };

        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        SetStatus($"{RowCount} satırlık örnek veri üretiliyor…", false);

        try
        {
            var rowCount = RowCount;
            var model = package.Model;
            var path = dialog.FileName;

            await Task.Run(() =>
            {
                var dataSet = new SampleDataGenerator().Generate(model, rowCount);
                App.ExcelExport.Export(dataSet, path);
            });

            RememberDirectory(path);
            App.SettingsStore.Settings.SampleRowCount = rowCount;
            App.SettingsStore.Save();

            SetStatus($"✓ Örnek veri kaydedildi: {path} ({model.Entities.Count} sekme × {rowCount} satır)", false);
        }
        catch (Exception ex)
        {
            SetStatus($"Excel kaydedilemedi: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Back() => _main.NavigateBack();

    private void SaveText(string? content, string label, string filter, string extension)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            SetStatus("Önce bir model üretmelisiniz.", true);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = $"{label} kaydet",
            Filter = filter,
            FileName = SuggestFileName(extension),
            InitialDirectory = App.SettingsStore.Settings.LastExportDirectory
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            TextExportService.Save(content, dialog.FileName);
            RememberDirectory(dialog.FileName);
            SetStatus($"✓ {label} kaydedildi: {dialog.FileName}", false);
        }
        catch (Exception ex)
        {
            SetStatus($"{label} kaydedilemedi: {ex.Message}", true);
        }
    }

    private void RememberDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) return;

        App.SettingsStore.Settings.LastExportDirectory = directory;
        App.SettingsStore.Save();
    }

    private string SuggestFileName(string extension)
    {
        var topic = _session.Input.Topic;
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((string.IsNullOrWhiteSpace(topic) ? "veri-modeli" : topic)
            .Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();

        return $"{cleaned}-{DateTime.Now:yyyyMMdd-HHmm}{extension}";
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
        _main.SetStatus(message);
    }
}
