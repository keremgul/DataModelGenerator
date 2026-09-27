using System.Text;

namespace DataModelGenerator.Core.Export;

/// <summary>
/// Mermaid kodu, JSON ve DDL çıktılarının düz metin dosyası olarak kaydedilmesi.
/// Uygulama hiçbir veritabanına bağlanmaz; tüm çıktılar dosya bazlıdır.
/// </summary>
public static class TextExportService
{
    public static void Save(string content, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(outputPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
