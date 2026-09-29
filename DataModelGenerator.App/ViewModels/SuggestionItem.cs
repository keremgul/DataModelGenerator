using CommunityToolkit.Mvvm.ComponentModel;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.App.ViewModels;

/// <summary>Öneri satırının kabul/ret durumunu ve kullanıcı açıklamasını taşır.</summary>
public partial class SuggestionItem : ObservableObject
{
    private readonly Suggestion _suggestion;

    public SuggestionItem(Suggestion suggestion)
    {
        _suggestion = suggestion;
        _note = suggestion.Note;
        _isAccepted = suggestion.Decision == SuggestionDecision.Accepted;
        _isRejected = suggestion.Decision == SuggestionDecision.Rejected;
    }

    [ObservableProperty] private bool _isAccepted;
    [ObservableProperty] private bool _isRejected;
    [ObservableProperty] private string _note;

    public string Id => _suggestion.Id;
    public string Text => _suggestion.Text;
    public bool IsApplied => _suggestion.IsApplied;
    public string StatusText => _suggestion.IsApplied
        ? $"✓ Tur {_suggestion.AppliedInRound} — modele uygulandı"
        : string.Empty;

    partial void OnIsAcceptedChanged(bool value)
    {
        if (value) IsRejected = false;
    }

    partial void OnIsRejectedChanged(bool value)
    {
        if (value) IsAccepted = false;
    }

    /// <summary>Kullanıcının kararını çekirdek nesneye aktarır.</summary>
    public Suggestion ToDecision()
    {
        _suggestion.Note = Note;
        _suggestion.Decision = IsAccepted ? SuggestionDecision.Accepted
            : IsRejected ? SuggestionDecision.Rejected
            : SuggestionDecision.Pending;

        return _suggestion;
    }
}
