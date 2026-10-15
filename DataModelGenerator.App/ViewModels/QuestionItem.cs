using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.App.ViewModels;

/// <summary>
/// Önerilen bir cevap. Metni seçilebilir biçimde gösterilir; kopyalanabilir ve
/// tek tıkla cevap alanına yazılabilir — yazıldıktan sonra düzenlenebilir.
/// </summary>
public partial class AnswerOptionItem : ObservableObject
{
    private readonly Action<string> _use;

    public AnswerOptionItem(string text, Action<string> use)
    {
        Text = text;
        _use = use;
    }

    public string Text { get; }

    [RelayCommand]
    private void Use() => _use(Text);

    [RelayCommand]
    private void Copy() => ClipboardHelper.Copy(Text);
}

/// <summary>
/// Soruyu ekrana bağlarken saran kabuk. Çekirdek model bildirim yapmadığı için
/// cevap alanı buradan yazılır; "bu cevabı kullan" tıklandığında kutu anında
/// güncellenir.
/// </summary>
public partial class QuestionItem : ObservableObject
{
    public QuestionItem(ClarifyingQuestion question)
    {
        Question = question;

        foreach (var option in question.Options)
            Options.Add(new AnswerOptionItem(option, UseAnswer));
    }

    public ClarifyingQuestion Question { get; }

    public ObservableCollection<AnswerOptionItem> Options { get; } = new();

    public string Id => Question.Id;
    public AmbiguityKind Kind => Question.Kind;
    public string Target => Question.Target;
    public string Text => Question.Text;
    public string Reason => Question.Reason;
    public int AppliedInRound => Question.AppliedInRound;

    public string Answer
    {
        get => Question.Answer;
        set
        {
            if (Question.Answer == value) return;
            Question.Answer = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Önerilen cevapların tamamını satır satır panoya kopyalar.</summary>
    [RelayCommand]
    private void CopyAllOptions() =>
        ClipboardHelper.Copy(string.Join(Environment.NewLine, Options.Select(o => o.Text)));

    private void UseAnswer(string text) => Answer = text;
}

/// <summary>Pano erişimi başka bir uygulama tarafından kilitlenmiş olabilir; hata kullanıcıyı durdurmamalı.</summary>
internal static class ClipboardHelper
{
    public static void Copy(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            Clipboard.SetDataObject(text, true);
        }
        catch (Exception)
        {
            // Pano geçici olarak kullanılamıyor — sessizce geç.
        }
    }
}
