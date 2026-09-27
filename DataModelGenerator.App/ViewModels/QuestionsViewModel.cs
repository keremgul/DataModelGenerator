using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataModelGenerator.App.Dialogs;
using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Services;

namespace DataModelGenerator.App.ViewModels;

public partial class QuestionsViewModel : ObservableObject, IRefreshable
{
    private readonly SessionState _session;
    private readonly MainViewModel _main;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private bool _hasQuestions;

    public ObservableCollection<ClarifyingQuestion> Questions { get; } = new();
    public ObservableCollection<Ambiguity> AutoResolved { get; } = new();

    public QuestionsViewModel(SessionState session, MainViewModel main)
    {
        _session = session;
        _main = main;
    }

    public void Refresh()
    {
        Questions.Clear();
        AutoResolved.Clear();

        var package = _session.Package;
        if (package is null)
        {
            HasQuestions = false;
            ProgressText = "Önce bir model üretmelisiniz.";
            return;
        }

        foreach (var question in package.Questions)
            Questions.Add(question);

        foreach (var ambiguity in package.Ambiguities.Where(a =>
                     a.Impact == AmbiguityImpact.Low && a.AppliedDefault is not null))
            AutoResolved.Add(ambiguity);

        HasQuestions = Questions.Count > 0;
        ProgressText = HasQuestions
            ? $"{Questions.Count} yüksek etkili belirsizlik için soru üretildi. " +
              "Cevapladıklarınız kural listesine eklenir ve yalnızca etkilenen adımlar yeniden çalıştırılır."
            : "Yüksek etkili belirsizlik kalmadı.";
    }

    [RelayCommand]
    private async Task ApplyAnswersAsync()
    {
        if (_session.Context is null)
        {
            ProgressText = "Önce bir model üretmelisiniz.";
            return;
        }

        var answered = Questions.Where(q => q.IsAnswered).ToList();
        if (answered.Count == 0)
        {
            ProgressText = "En az bir soruyu cevaplayın.";
            return;
        }

        IsBusy = true;
        var progress = new Progress<string>(message => ProgressText = message);

        try
        {
            var pipeline = _session.CreatePipeline();
            var package = await pipeline.ApplyAnswersAsync(_session.Context, answered, progress);

            _session.Package = package;

            ProgressText = $"{answered.Count} cevap uygulandı — model kısmen güncellendi " +
                           $"(tur {package.RoundNumber}).";
            _main.SetStatus(ProgressText);

            Refresh();
        }
        catch (PipelineException ex)
        {
            ProgressText = ex.Message;
            ApiLogDialog.Show($"Güncelleme hatası — {PipelineStageMap.DisplayName(ex.Stage)}",
                string.IsNullOrWhiteSpace(ex.RawOutput) ? ex.Message : $"{ex.Message}\n\n{ex.RawOutput}",
                ApiLogger.LastEntry);
        }
        catch (Exception ex)
        {
            ProgressText = $"Beklenmedik hata: {ex.Message}";
            ApiLogDialog.Show("Güncelleme Başarısız", ex, ApiLogger.LastEntry);
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
