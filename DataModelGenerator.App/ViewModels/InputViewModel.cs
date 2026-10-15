using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Storage;

namespace DataModelGenerator.App.ViewModels;

public partial class InputViewModel : ObservableObject, IRefreshable
{
    private readonly SessionState _session;
    private readonly MainViewModel _main;

    [ObservableProperty] private string _newRuleText = string.Empty;
    [ObservableProperty] private BusinessRule? _selectedRule;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ObservableCollection<BusinessRule> Rules { get; } = new();

    public InputViewModel(SessionState session, MainViewModel main)
    {
        _session = session;
        _main = main;

        foreach (var rule in session.Input.Rules) Rules.Add(rule);
    }

    public void Refresh() => StatusMessage = string.Empty;

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
        _main.SetStatus($"{validRules.Count} kural hazır — üretim adımına geçildi.");
        _main.NavigateNext();
    }
}
