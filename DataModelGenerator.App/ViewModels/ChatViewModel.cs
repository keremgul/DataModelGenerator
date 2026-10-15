using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataModelGenerator.App.Dialogs;
using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Services;

namespace DataModelGenerator.App.ViewModels;

/// <summary>Sohbet geçmişindeki tek bir tur: ne istendi, ne yapıldı, modelde ne değişti.</summary>
public partial class ChatTurnItem : ObservableObject
{
    [ObservableProperty] private string _userMessage = string.Empty;
    [ObservableProperty] private string _ruleId = string.Empty;
    [ObservableProperty] private string _timeText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _statusColor = "#2E7D32";
    [ObservableProperty] private string _changeSummary = string.Empty;

    /// <summary>Talebin çevrildiği yapılandırılmış direktifler.</summary>
    public ObservableCollection<string> Directives { get; } = new();

    /// <summary>Modelin önceki/sonraki hâli arasındaki gerçek fark.</summary>
    public ObservableCollection<string> Changes { get; } = new();

    /// <summary>Uygulanamayan direktifler ve gerekçeleri.</summary>
    public ObservableCollection<string> Problems { get; } = new();

    public bool HasProblems => Problems.Count > 0;
    public bool HasChanges => Changes.Count > 0;
}

/// <summary>
/// Serbest reprompt yerine gelen sohbet adımı. Her mesaj yapılandırılmış
/// direktiflere çevrilir, basit işlemler LLM'siz uygulanır ve sonuç modelin
/// gerçek diff'iyle doğrulanır — modelin "yaptım" demesi yeterli sayılmaz.
/// </summary>
public partial class ChatViewModel : ObservableObject, IRefreshable
{
    private readonly SessionState _session;
    private readonly MainViewModel _main;

    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private string _mermaidCode = string.Empty;

    public ObservableCollection<ChatTurnItem> Turns { get; } = new();

    public ChatViewModel(SessionState session, MainViewModel main)
    {
        _session = session;
        _main = main;
    }

    public void Refresh()
    {
        var package = _session.Package;
        if (package is null)
        {
            ProgressText = "Önce bir model üretmelisiniz.";
            return;
        }

        MermaidCode = package.MermaidCode;
        SyncTurns();

        if (string.IsNullOrWhiteSpace(ProgressText))
            ProgressText = "Modelde ne değişsin? Örn: \"Müşteri tablosundan FaksNo alanını kaldır\" veya " +
                           "\"adres bilgisini ayrı tabloya taşı\". Basit değişiklikler modele hiç gitmeden uygulanır.";
    }

    /// <summary>Oturum durumundaki tur geçmişini listeye yansıtır (geri/ileri gezinmede korunur).</summary>
    private void SyncTurns()
    {
        var history = _session.Context?.ChatTurns;
        if (history is null || history.Count == Turns.Count) return;

        Turns.Clear();
        foreach (var turn in history) Turns.Add(ToItem(turn));
    }

    private static ChatTurnItem ToItem(ChatTurn turn)
    {
        var item = new ChatTurnItem
        {
            UserMessage = turn.UserMessage,
            RuleId = turn.RuleId,
            TimeText = turn.At.ToString("HH:mm"),
            StatusText = turn.StatusText,
            StatusColor = turn.Status switch
            {
                ChatTurnStatus.Applied => "#2E7D32",
                ChatTurnStatus.PartiallyApplied => "#B36B00",
                _ => "#B3261E"
            },
            ChangeSummary = $"{turn.Changes.TotalChanges} değişiklik"
        };

        foreach (var directive in turn.Directives)
            item.Directives.Add($"→ {directive}");

        foreach (var line in turn.Changes.Describe().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            if (!turn.Changes.IsEmpty) item.Changes.Add(line.Trim());

        foreach (var problem in turn.NotApplied) item.Problems.Add(problem);
        foreach (var unsupported in turn.Unsupported) item.Problems.Add($"Anlaşılmadı: {unsupported}");

        return item;
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (_session.Context is null)
        {
            ProgressText = "Önce bir model üretmelisiniz.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Message))
        {
            ProgressText = "Mesajınızı yazın.";
            return;
        }

        var text = Message;
        IsBusy = true;
        var progress = new Progress<string>(message => ProgressText = message);

        try
        {
            var pipeline = _session.CreatePipeline();
            var (package, turn) = await pipeline.SendChatAsync(_session.Context, text, progress);

            _session.Package = package;
            MermaidCode = package.MermaidCode;
            Message = string.Empty;

            Turns.Add(ToItem(turn));

            ProgressText = turn.Status == ChatTurnStatus.NotApplied
                ? $"{turn.StatusText} — model üzerinde fark oluşmadı. Talebi daha açık yazmayı deneyin."
                : $"{turn.StatusText} — {turn.Changes.TotalChanges} değişiklik ({turn.RuleId}, tur {package.RoundNumber}).";
            _main.SetStatus(ProgressText);
        }
        catch (PipelineException ex)
        {
            ProgressText = ex.Message;
            ApiLogDialog.Show($"Sohbet hatası — {PipelineStageMap.DisplayName(ex.Stage)}",
                string.IsNullOrWhiteSpace(ex.RawOutput) ? ex.Message : $"{ex.Message}\n\n{ex.RawOutput}",
                ApiLogger.LastEntry);
        }
        catch (Exception ex)
        {
            ProgressText = $"Beklenmedik hata: {ex.Message}";
            ApiLogDialog.Show("Sohbet Başarısız", ex, ApiLogger.LastEntry);
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
