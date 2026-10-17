using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataModelGenerator.App.Dialogs;
using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Services;
using DataModelGenerator.Core.Storage;

namespace DataModelGenerator.App.ViewModels;

public partial class InputViewModel : ObservableObject, IRefreshable
{
    private readonly SessionState _session;
    private readonly MainViewModel _main;

    [ObservableProperty] private string _newRuleText = string.Empty;
    [ObservableProperty] private BusinessRule? _selectedRule;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Model üretilmişse yeni kurallar sıfırdan değil, üzerine eklenebilir.</summary>
    [ObservableProperty] private bool _hasModel;

    [ObservableProperty] private string _changeSummary = string.Empty;

    public ObservableCollection<BusinessRule> Rules { get; } = new();

    /// <summary>Son artımlı güncellemede modelde oluşan fark.</summary>
    public ObservableCollection<string> Changes { get; } = new();

    public InputViewModel(SessionState session, MainViewModel main)
    {
        _session = session;
        _main = main;

        foreach (var rule in session.Input.Rules) Rules.Add(rule);
    }

    public void Refresh()
    {
        StatusMessage = string.Empty;
        HasModel = _session.Package is not null && _session.Package.Model.Entities.Count > 0;

        // Dışarıdan (cevap, sohbet) eklenen kurallar da listede görünmeli.
        foreach (var rule in _session.Input.Rules.Where(r => Rules.All(x => x.Id != r.Id)))
            Rules.Add(rule);
    }

    /// <summary>
    /// Tek satır da olabilir, yapıştırılmış çok satırlı metin de. Metin her
    /// durumda numaralı kural maddelerine ayrıştırılır.
    /// </summary>
    [RelayCommand]
    private void AddRules()
    {
        if (string.IsNullOrWhiteSpace(NewRuleText))
        {
            StatusMessage = "Eklenecek kural metni girin.";
            return;
        }

        var parsed = RuleTextParser.Parse(NewRuleText);
        if (parsed.Count == 0)
        {
            StatusMessage = "Metinden kural çıkarılamadı.";
            return;
        }

        foreach (var rule in parsed)
        {
            rule.Id = NextRuleId();
            Rules.Add(rule);
        }

        NewRuleText = string.Empty;
        StatusMessage = parsed.Count == 1 ? "1 kural eklendi." : $"{parsed.Count} kural eklendi.";
        Renumber();
    }

    [RelayCommand]
    private void RemoveRule()
    {
        if (SelectedRule is null)
        {
            StatusMessage = "Silmek için bir kural seçin.";
            return;
        }

        Rules.Remove(SelectedRule);
        Renumber();
        StatusMessage = "Kural silindi.";
    }

    [RelayCommand]
    private void MoveUp() => Move(-1);

    [RelayCommand]
    private void MoveDown() => Move(1);

    private void Move(int offset)
    {
        if (SelectedRule is null) return;

        var index = Rules.IndexOf(SelectedRule);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Rules.Count) return;

        Rules.Move(index, target);
        Renumber();
    }

    /// <summary>Kurallar BR-01, BR-02 … biçiminde sıralı numaralanır.</summary>
    private void Renumber()
    {
        var index = 1;
        foreach (var rule in Rules.Where(r => r.Origin == RuleOrigin.User))
            rule.Id = $"BR-{index++:00}";
    }

    private string NextRuleId() => $"BR-{Rules.Count(r => r.Origin == RuleOrigin.User) + 1:00}";

    /// <summary>
    /// Mevcut modelin üzerine yeni kurallar ekler. Model sıfırdan üretilmez;
    /// önceki tablo, alan ve ilişkiler yeni kural gerektirmedikçe korunur.
    /// </summary>
    [RelayCommand]
    private async Task ExtendAsync()
    {
        if (_session.Context is null || _session.Package is null)
        {
            StatusMessage = "Önce bir model üretmelisiniz.";
            return;
        }

        // Henüz modele yansımamış kurallar: listede olup kaynak listesinde olmayanlar.
        var applied = _session.Context.Input.Rules.Select(r => r.Text.Trim()).ToHashSet();
        var pending = Rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Text) && !applied.Contains(r.Text.Trim()))
            .Select(r => r.Text)
            .ToList();

        if (!string.IsNullOrWhiteSpace(NewRuleText))
        {
            pending.AddRange(RuleTextParser.Parse(NewRuleText).Select(r => r.Text));
            NewRuleText = string.Empty;
        }

        if (pending.Count == 0)
        {
            StatusMessage = "Eklenecek yeni kural yok. Önce kural yazıp 'Ekle' deyin.";
            return;
        }

        IsBusy = true;
        var progress = new Progress<string>(message => StatusMessage = message);

        try
        {
            var pipeline = _session.CreatePipeline();
            var package = await pipeline.ExtendAsync(_session.Context, pending, progress);

            _session.Package = package;
            _main.CompleteStep(1);

            SyncRulesFromContext();
            ShowChanges(package);

            StatusMessage = $"{package.AddedRuleIds.Count} yeni kural uygulandı — " +
                            $"{package.LastChanges?.TotalChanges ?? 0} değişiklik (tur {package.RoundNumber}).";
            _main.SetStatus(StatusMessage);
        }
        catch (PipelineException ex)
        {
            StatusMessage = ex.Message;
            ApiLogDialog.Show($"Model güncelleme hatası — {PipelineStageMap.DisplayName(ex.Stage)}",
                string.IsNullOrWhiteSpace(ex.RawOutput) ? ex.Message : $"{ex.Message}\n\n{ex.RawOutput}",
                ApiLogger.LastEntry);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Beklenmedik hata: {ex.Message}";
            ApiLogDialog.Show("Model Güncellenemedi", ex, ApiLogger.LastEntry);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Pipeline kural kimliklerini yeniden numaralandırmış olabilir.</summary>
    private void SyncRulesFromContext()
    {
        Rules.Clear();
        foreach (var rule in _session.Context!.Input.Rules) Rules.Add(rule);
    }

    private void ShowChanges(ModelPackage package)
    {
        Changes.Clear();
        var changes = package.LastChanges;

        if (changes is null || changes.IsEmpty)
        {
            ChangeSummary = "Yeni kurallar modelde bir değişiklik gerektirmedi.";
            return;
        }

        ChangeSummary = $"Önceki modele göre {changes.TotalChanges} değişiklik:";
        foreach (var line in changes.Describe().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Changes.Add(line.Trim());
    }

    [RelayCommand]
    private void Back() => _main.NavigateBack();

    [RelayCommand]
    private void Next()
    {
        var validRules = Rules.Where(r => !string.IsNullOrWhiteSpace(r.Text)).ToList();
        if (validRules.Count == 0)
        {
            StatusMessage = "En az bir business rule girilmelidir.";
            return;
        }

        if (!_session.HasConnection)
        {
            StatusMessage = "Önce bir bağlantı profili seçmelisiniz.";
            return;
        }

        Renumber();
        _session.Input = new ProjectInput { Rules = validRules };

        // Kural listesi değiştiğinde sonraki adımların çıktısı silinmez,
        // "güncel değil" olarak işaretlenir.
        _main.CompleteStep(1);
        _main.SetStatus($"{validRules.Count} kural hazır — üretim adımına geçildi.");
        _main.NavigateNext();
    }
}
