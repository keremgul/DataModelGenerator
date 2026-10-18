using System.Text.RegularExpressions;
using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Rules;

/// <summary>
/// Sık kullanılan direktifleri LLM'e gitmeden tanır. "X alanının adını Y yap"
/// gibi net bir talep için model çağrısı yapmak gereksiz maliyet ve risktir.
/// Eşleşme bulunamazsa router skill'i devreye girer.
/// </summary>
public static partial class DirectivePatterns
{
    [GeneratedRegex(@"^\s*(?<entity>[\wçğıöşüÇĞİÖŞÜ\s]+?)\s*[\.\-]?\s*(?<field>[\wçğıöşüÇĞİÖŞÜ]+)\s+alan[ıi]n[ıi]n\s+ad[ıi]n[ıi]\s+(?<new>[\wçğıöşüÇĞİÖŞÜ]+)\s+(yap|olarak de[ğg]i[şs]tir)\s*\.?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex RenameFieldPattern();

    [GeneratedRegex(@"^\s*(?<entity>[\wçğıöşüÇĞİÖŞÜ\s]+?)\s+tablosundan\s+(?<field>[\wçğıöşüÇĞİÖŞÜ]+)\s+alan[ıi]n[ıi]\s+(kald[ıi]r|sil|ç[ıi]kar)\s*\.?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex RemoveFieldPattern();

    [GeneratedRegex(@"^\s*(?<entity>[\wçğıöşüÇĞİÖŞÜ\s]+?)\s+tablosunun\s+ad[ıi]n[ıi]\s+(?<new>[\wçğıöşüÇĞİÖŞÜ]+)\s+(yap|olarak de[ğg]i[şs]tir)\s*\.?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex RenameEntityPattern();

    [GeneratedRegex(@"^\s*(?<entity>[\wçğıöşüÇĞİÖŞÜ\s]+?)\s+tablosunu\s+(kald[ıi]r|sil)\s*\.?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex RemoveEntityPattern();

    [GeneratedRegex(@"^\s*(?<entity>[\wçğıöşüÇĞİÖŞÜ\s]+?)\s+tablosundaki\s+(?<field>[\wçğıöşüÇĞİÖŞÜ]+)\s+alan[ıi]n[ıi]n\s+a[çc][ıi]klamas[ıi](n[ıi])?\s*[:=]?\s*[""'“]?(?<text>.+?)[""'”]?\s*(olsun|yap|olarak\s+(de[ğg]i[şs]tir|g[üu]ncelle|yaz))?\s*\.?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SetDescriptionPattern();

    /// <summary>Eşleşme varsa direktifi döndürür; yoksa null.</summary>
    public static ChatDirective? TryMatch(string message, DataModel model)
    {
        var text = message.Trim();

        var rename = RenameFieldPattern().Match(text);
        if (rename.Success && Resolve(model, rename.Groups["entity"].Value) is { } renameEntity)
        {
            return new ChatDirective
            {
                Operation = DirectiveOperation.RenameField,
                Entity = renameEntity,
                Field = rename.Groups["field"].Value.Trim(),
                NewName = rename.Groups["new"].Value.Trim(),
                Explanation = "Kalıp eşleşmesi: alan adı değiştirme"
            };
        }

        var removeField = RemoveFieldPattern().Match(text);
        if (removeField.Success && Resolve(model, removeField.Groups["entity"].Value) is { } removeFieldEntity)
        {
            return new ChatDirective
            {
                Operation = DirectiveOperation.RemoveField,
                Entity = removeFieldEntity,
                Field = removeField.Groups["field"].Value.Trim(),
                Explanation = "Kalıp eşleşmesi: alan kaldırma"
            };
        }

        var renameEntityMatch = RenameEntityPattern().Match(text);
        if (renameEntityMatch.Success && Resolve(model, renameEntityMatch.Groups["entity"].Value) is { } target)
        {
            return new ChatDirective
            {
                Operation = DirectiveOperation.RenameEntity,
                Entity = target,
                NewName = renameEntityMatch.Groups["new"].Value.Trim(),
                Explanation = "Kalıp eşleşmesi: tablo adı değiştirme"
            };
        }

        var describe = SetDescriptionPattern().Match(text);
        if (describe.Success && Resolve(model, describe.Groups["entity"].Value) is { } describeEntity)
        {
            return new ChatDirective
            {
                Operation = DirectiveOperation.SetDescription,
                Entity = describeEntity,
                Field = describe.Groups["field"].Value.Trim(),
                Description = describe.Groups["text"].Value.Trim(),
                Explanation = "Kalıp eşleşmesi: alan açıklaması"
            };
        }

        var removeEntity = RemoveEntityPattern().Match(text);
        if (removeEntity.Success && Resolve(model, removeEntity.Groups["entity"].Value) is { } removeTarget)
        {
            return new ChatDirective
            {
                Operation = DirectiveOperation.RemoveEntity,
                Entity = removeTarget,
                Explanation = "Kalıp eşleşmesi: tablo kaldırma"
            };
        }

        return null;
    }

    /// <summary>
    /// Kullanıcı tabloyu "Sipariş" diye de "Sipariş tablosunun" diye de yazabilir;
    /// ek atıldıktan sonra modelde gerçekten var olan bir tabloya işaret
    /// ediyorsa kalıp geçerlidir.
    /// </summary>
    private static readonly string[] TableWords =
        ["tablosundaki", "tablosundan", "tablosunun", "tablosunda", "tablosuna", "tablosunu", "tablosu", "tablo"];

    private static string? Resolve(DataModel model, string candidate)
    {
        var trimmed = candidate.Trim();
        if (model.FindEntity(trimmed) is { } direct) return direct.TechnicalName;

        foreach (var word in TableWords)
        {
            if (!trimmed.EndsWith(word, StringComparison.OrdinalIgnoreCase)) continue;

            var stripped = trimmed[..^word.Length].Trim();
            if (stripped.Length > 0 && model.FindEntity(stripped) is { } found)
                return found.TechnicalName;
        }

        return null;
    }
}
