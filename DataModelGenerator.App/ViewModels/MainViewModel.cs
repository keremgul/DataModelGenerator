using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DataModelGenerator.App.ViewModels;

public partial class StepItem : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private bool _isActive;
}

public partial class MainViewModel : ObservableObject
{
    private readonly SessionState _session = new();
    private readonly List<ObservableObject> _pages;

    [ObservableProperty] private ObservableObject? _currentPage;
    [ObservableProperty] private int _currentStepIndex = -1;
    [ObservableProperty] private string _statusMessage = "Hazır.";
    [ObservableProperty] private string _connectionStatus = "Bağlantı profili seçilmedi";
    [ObservableProperty] private bool _connectionIsHealthy;

    public ObservableCollection<StepItem> Steps { get; } = new();

    public MainViewModel()
    {
        _pages =
        [
            new ConnectionViewModel(_session, this),
            new InputViewModel(_session, this),
            new GenerationViewModel(_session, this),
            new QuestionsViewModel(_session, this),
            new RepromptViewModel(_session, this),
            new ExportViewModel(_session, this)
        ];

        foreach (var title in new[]
                 {
                     "1. Bağlantı", "2. Girdi", "3. Üretim",
                     "4. Sorular", "5. Reprompt", "6. Dışa Aktar"
                 })
            Steps.Add(new StepItem { Title = title });

        NavigateToStep(0);
    }

    public void NavigateToStep(int index)
    {
        if (index < 0 || index >= _pages.Count) return;

        CurrentStepIndex = index;
        CurrentPage = _pages[index];

        for (var i = 0; i < Steps.Count; i++)
            Steps[i].IsActive = i == index;

        if (CurrentPage is IRefreshable refreshable)
            refreshable.Refresh();
    }

    [RelayCommand]
    private void GoToStep(string index)
    {
        if (int.TryParse(index, out var parsed)) NavigateToStep(parsed);
    }

    public void NavigateNext() => NavigateToStep(CurrentStepIndex + 1);

    public void NavigateBack() => NavigateToStep(CurrentStepIndex - 1);

    public void SetStatus(string message) => StatusMessage = message;

    public void UpdateConnectionStatus()
    {
        var profile = _session.ActiveProfile;
        if (profile is null)
        {
            ConnectionStatus = "Bağlantı profili seçilmedi";
            ConnectionIsHealthy = false;
            return;
        }

        var tested = profile.LastTestedAt is null
            ? "hiç test edilmedi"
            : $"son test {profile.LastTestedAt:dd.MM.yyyy HH:mm}";

        var state = profile.LastTestSucceeded switch
        {
            true => "✓ başarılı",
            false => "✗ başarısız",
            _ => "test edilmedi"
        };

        ConnectionStatus = $"{profile.Name} · {profile.ModelId} · {state} ({tested})";
        ConnectionIsHealthy = profile.LastTestSucceeded == true;
    }
}

/// <summary>Adıma her dönüldüğünde durumu tazelemesi gereken sayfalar.</summary>
public interface IRefreshable
{
    void Refresh();
}
