using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Storage;

namespace DataModelGenerator.App.ViewModels;

public partial class InputViewModel : ObservableObject, IRefreshable
{
    private readonly SessionState _session;
    private readonly MainViewModel _main;

    [ObservableProperty] private string _topic = string.Empty;
    [ObservableProperty] private string _context = string.Empty;
    [ObservableProperty] private string _pastedText = string.Empty;
    [ObservableProperty] private BusinessRule? _selectedRule;
    [ObservableProperty] private GlossaryTerm? _selectedTerm;
    [ObservableProperty] private ObservableCollection<string> _savedRuleSets = new();
    [ObservableProperty] private string _ruleSetName = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ObservableCollection<BusinessRule> Rules { get; } = new();
    public ObservableCollection<GlossaryTerm> Glossary { get; } = new();

    public InputViewModel(SessionState session, MainViewModel main)
    {
        _session = session;
        _main = main;

        Topic = session.Input.Topic;
        Context = session.Input.Context;
        foreach (var rule in session.Input.Rules) Rules.Add(rule);
        foreach (var term in session.Input.Glossary) Glossary.Add(term);

        ReloadRuleSets();
    }

    public void Refresh() => ReloadRuleSets();

    private void ReloadRuleSets()
    {
        SavedRuleSets.Clear();
        foreach (var name in App.RuleSetStore.ListNames())
            SavedRuleSets.Add(name);
    }

    [RelayCommand]
    private void ParsePasted()
    {
        if (string.IsNullOrWhiteSpace(PastedText))
        {
            StatusMessage = "Ayrıştırılacak metin yok.";
            return;
        }

        var parsed = RuleTextParser.Parse(PastedText);
        if (parsed.Count == 0)
        {
            StatusMessage = "Metinden kural çıkarılamadı.";
            return;
        }

        var existingIds = Rules.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var rule in parsed)
        {
            if (existingIds.Contains(rule.Id))
                rule.Id = NextRuleId();

            Rules.Add(rule);
            existingIds.Add(rule.Id);
            added++;
        }

        PastedText = string.Empty;
        StatusMessage = $"{added} kural eklendi.";
    }

    [RelayCommand]
    private void AddRule()
    {
        Rules.Add(new BusinessRule { Id = NextRuleId(), Text = string.Empty });
        StatusMessage = "Yeni kural satırı eklendi.";
    }

    [RelayCommand]
    private void RemoveRule()
    {
        if (SelectedRule is null) return;
        Rules.Remove(SelectedRule);
        StatusMessage = "Kural silindi.";
    }

    [RelayCommand]
    private void AddTerm()
    {
        Glossary.Add(new GlossaryTerm());
        StatusMessage = "Yeni terim satırı eklendi.";
    }

    [RelayCommand]
    private void RemoveTerm()
    {
        if (SelectedTerm is null) return;
        Glossary.Remove(SelectedTerm);
    }

    [RelayCommand]
    private void SaveRuleSet()
    {
        var name = string.IsNullOrWhiteSpace(RuleSetName) ? Topic : RuleSetName;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Kayıt için bir ad girin.";
            return;
        }

        App.RuleSetStore.Save(name, BuildInput());
        ReloadRuleSets();
        RuleSetName = name;
        StatusMessage = $"'{name}' kural seti kaydedildi ({App.RuleSetStore.DirectoryPath}).";
    }

    [RelayCommand]
    private void LoadRuleSet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var input = App.RuleSetStore.Load(name);
        if (input is null)
        {
            StatusMessage = $"'{name}' kural seti okunamadı.";
            return;
        }

        Topic = input.Topic;
        Context = input.Context;
        RuleSetName = name;

        Rules.Clear();
        foreach (var rule in input.Rules) Rules.Add(rule);

        Glossary.Clear();
        foreach (var term in input.Glossary) Glossary.Add(term);

        StatusMessage = $"'{name}' kural seti yüklendi ({input.Rules.Count} kural).";
    }

    [RelayCommand]
    private void DeleteRuleSet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var confirm = MessageBox.Show($"'{name}' kural seti silinsin mi?", "Kural Setini Sil",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        App.RuleSetStore.Delete(name);
        ReloadRuleSets();
        StatusMessage = $"'{name}' silindi.";
    }

    [RelayCommand]
    private void Back() => _main.NavigateBack();

    [RelayCommand]
    private void Next()
    {
        if (string.IsNullOrWhiteSpace(Topic))
        {
            StatusMessage = "Konu adı zorunludur.";
            return;
        }

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

        _session.Input = BuildInput();
        _main.SetStatus($"{validRules.Count} kural hazır — üretim adımına geçildi.");
        _main.NavigateNext();
    }

    private ProjectInput BuildInput() => new()
    {
        Topic = Topic.Trim(),
        Context = Context.Trim(),
        Rules = Rules.Where(r => !string.IsNullOrWhiteSpace(r.Text)).ToList(),
        Glossary = Glossary
            .Where(g => !string.IsNullOrWhiteSpace(g.Term) && !string.IsNullOrWhiteSpace(g.TechnicalName))
            .ToList()
    };

    private string NextRuleId()
    {
        var max = Rules
            .Where(r => r.Id.StartsWith("BR-", StringComparison.OrdinalIgnoreCase))
            .Select(r => int.TryParse(r.Id[3..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"BR-{max + 1}";
    }
}
