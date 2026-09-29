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
    [ObservableProperty] private bool _canApplyAnswers;
    [ObservableProperty] private string _roundInfo = string.Empty;

    public ObservableCollection<ClarifyingQuestion> Questions { get; } = new();
    public ObservableCollection<ClarifyingQuestion> AppliedQuestions { get; } = new();
    public ObservableCollection<Ambiguity> AutoResolved { get; } = new();

    public QuestionsViewModel(SessionState session, MainViewModel main)
    {
        _session = session;
        _main = main;
    }

    public void Refresh()
    {
        Questions.Clear();
        AppliedQuestions.Clear();
        AutoResolved.Clear();

        var package = _session.Package;
        if (package is null)
        {
            HasQuestions = false;
            ProgressText = "Önce bir model üretmelisiniz.";
            RoundInfo = string.Empty;
            return;
        }

        foreach (var question in package.OpenQuestions)
            Questions.Add(question);

        foreach (var question in package.AppliedQuestions.OrderBy(q => q.AppliedInRound))
            AppliedQuestions.Add(question);

        foreach (var ambiguity in package.Ambiguities.Where(a =>
                     a.Impact == AmbiguityImpact.Low && a.AppliedDefault is not null))
            AutoResolved.Add(ambiguity);

        HasQuestions = Questions.Count > 0;
        CanApplyAnswers = Questions.Count > 0 || AppliedQuestions.Count > 0;
        RoundInfo = $"Soru turu {package.QuestionRoundsUsed}" +
                    (AppliedQuestions.Count > 0 ? $" · {AppliedQuestions.Count} soru uygulandı" : string.Empty);

        if (string.IsNullOrWhiteSpace(ProgressText) || HasQuestions)
        {
            ProgressText = HasQuestions
                ? $"{Questions.Count} açık soru var. Cevapladıklarınız kural listesine eklenir ve " +
                  "yalnızca etkilenen adımlar yeniden çalıştırılır."
                : "Açık soru kalmadı. Modeli daha ileri götürmek isterseniz yeni sorular üretebilirsiniz.";
        }
    }

    [RelayCommand]
    private async Task RequestQuestionsAsync()
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
            var package = await pipeline.RequestQuestionsAsync(_session.Context, progress);

            _session.Package = package;
            Refresh();

            ProgressText = package.NewQuestionCount > 0
                ? $"{package.NewQuestionCount} yeni soru üretildi."
                : "Yeni soru üretilemedi — sorulacak yeni bir yüksek etkili belirsizlik kalmadı.";
            _main.SetStatus(ProgressText);
        }
        catch (PipelineException ex)
        {
            ProgressText = ex.Message;
            ApiLogDialog.Show($"Soru üretimi hatası — {PipelineStageMap.DisplayName(ex.Stage)}",
                string.IsNullOrWhiteSpace(ex.RawOutput) ? ex.Message : $"{ex.Message}\n\n{ex.RawOutput}",
                ApiLogger.LastEntry);
        }
        catch (Exception ex)
        {
            ProgressText = $"Beklenmedik hata: {ex.Message}";
            ApiLogDialog.Show("Soru Üretimi Başarısız", ex, ApiLogger.LastEntry);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyAnswersAsync()
    {
        if (_session.Context is null)
        {
            ProgressText = "Önce bir model üretmelisiniz.";
            return;
        }

        // Düzenlenen eski cevaplar da gönderilir; model her zaman son cevaplara göre üretilir.
        var answered = Questions.Concat(AppliedQuestions).Where(q => q.IsAnswered).ToList();
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

            ProgressText = $"{answered.Count} cevap uygulandı — model son cevaplara göre güncellendi " +
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
