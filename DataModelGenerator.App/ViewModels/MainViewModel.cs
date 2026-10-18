using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DataModelGenerator.App.ViewModels;

public enum StepState
{
    /// <summary>Henüz çalıştırılmadı.</summary>
    Pending,

    /// <summary>Çalıştırıldı ve çıktısı güncel.</summary>
    Completed,

    /// <summary>Çalıştırıldı ama öncesinde bir şey değişti — çıktısı artık güncel değil.</summary>
    Stale
}

public partial class StepItem : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private StepState _state = StepState.Pending;

    public int Index { get; init; }

    /// <summary>Adım başlığının yanındaki durum işareti.</summary>
    public string BadgeText => State switch
    {
        StepState.Completed => "✓",
        StepState.Stale => "!",
        _ => "○"
    };

    public string StateText => State switch
    {
        StepState.Completed => "Tamamlandı — çıktısı güncel.",
        StepState.Stale => "Güncel değil: önceki bir adımda değişiklik yapıldı. " +
                           "Çıktı silinmedi, ama yeniden çalıştırılması gerekiyor.",
        _ => "Henüz çalıştırılmadı."
    };
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
        _pages = [];
        BuildPages();

        var titles = new[]
        {
            "1. Bağlantı", "2. Girdi", "3. Üretim",
            "4. Sorular", "5. Sohbet", "6. Dışa Aktar"
        };

        for (var i = 0; i < titles.Length; i++)
            Steps.Add(new StepItem { Title = titles[i], Index = i });

        NavigateToStep(0);
    }

    /// <summary>
    /// Sayfa görünüm modelleri yeniden kurulur. Temizlemede her ekranın kendi
    /// durumunu ayrı ayrı sıfırlamak yerine sayfalar baştan oluşturulur.
    /// </summary>
    private void BuildPages()
    {
        _pages.Clear();
        _pages.AddRange(
        [
            new ConnectionViewModel(_session, this),
            new InputViewModel(_session, this),
            new GenerationViewModel(_session, this),
            new QuestionsViewModel(_session, this),
            new ChatViewModel(_session, this),
            new ExportViewModel(_session, this)
        ]);
    }

    /// <summary>
    /// Modeli, kuralları, soruları ve sohbet geçmişini siler; tasarıma baştan
    /// başlanır. Geri alınamadığı için onay istenir. Bağlantı profili korunur.
    /// </summary>
    [RelayCommand]
    private void ResetModel()
    {
        var answer = MessageBox.Show(
            "Model, kural listesi, sorular ve sohbet geçmişi silinecek. " +
            "Bağlantı profiliniz korunur.\n\nDevam edilsin mi?",
            "Her Şeyi Temizle", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes) return;

        _session.Reset();
        BuildPages();

        foreach (var step in Steps) step.State = StepState.Pending;
        if (_session.HasConnection) Steps[0].State = StepState.Completed;

        NavigateToStep(1);
        SetStatus("Her şey temizlendi — tasarıma baştan başlayabilirsiniz.");
    }

    /// <summary>
    /// Bir adım başarıyla çalıştığında işaretlenir. Sonraki adımların çıktısı
    /// silinmez; yalnızca "güncel değil" olarak işaretlenir.
    /// </summary>
    public void CompleteStep(int index)
    {
        if (index < 0 || index >= Steps.Count) return;

        Steps[index].State = StepState.Completed;

        foreach (var step in Steps.Where(s => s.Index > index && s.State == StepState.Completed))
            step.State = StepState.Stale;
    }

    /// <summary>Adım geri alındığında veya girdisi bozulduğunda çağrılır.</summary>
    public void InvalidateStepsAfter(int index)
    {
        foreach (var step in Steps.Where(s => s.Index > index && s.State == StepState.Completed))
            step.State = StepState.Stale;
    }

    public bool HasStaleSteps => Steps.Any(s => s.State == StepState.Stale);

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
    private void GoToStep(int index) => NavigateToStep(index);

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
