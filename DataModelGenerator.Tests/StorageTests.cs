using DataModelGenerator.Core.Export;
using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Security;
using DataModelGenerator.Core.Simulation;
using DataModelGenerator.Core.Storage;
using Xunit;

namespace DataModelGenerator.Tests;

public class StorageTests : IDisposable
{
    private readonly string _tempDirectory;

    public StorageTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "dmg-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); }
        catch { /* temizlik hatası testi düşürmemeli */ }
    }

    [Fact]
    public void ApiKeyStore_AnahtariSifreliYazarVeGeriOkur()
    {
        var path = Path.Combine(_tempDirectory, "keys.dat");
        var store = new ApiKeyStore(path);

        store.Save("profil-1", "sk-gizli-anahtar");

        Assert.Equal("sk-gizli-anahtar", new ApiKeyStore(path).Get("profil-1"));
        Assert.DoesNotContain("sk-gizli-anahtar", File.ReadAllText(path));
    }

    [Fact]
    public void ApiKeyStore_SilinenAnahtariDondurmez()
    {
        var path = Path.Combine(_tempDirectory, "keys.dat");
        var store = new ApiKeyStore(path);

        store.Save("profil-1", "anahtar");
        store.Delete("profil-1");

        Assert.Equal(string.Empty, store.Get("profil-1"));
    }

    [Fact]
    public void ConnectionProfileStore_ProfiliKaydederVeGeriYukler()
    {
        var path = Path.Combine(_tempDirectory, "profiles.json");
        var profile = new ConnectionProfile
        {
            Name = "Yerel Model",
            ProviderKey = "ollama",
            BaseUrl = "http://localhost:11434",
            ModelId = "qwen2.5:14b",
            LastTestSucceeded = true,
            LastTestedAt = new DateTime(2026, 1, 1, 10, 0, 0)
        };

        new ConnectionProfileStore(path).Save(profile);

        var reloaded = new ConnectionProfileStore(path).GetById(profile.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Yerel Model", reloaded!.Name);
        Assert.True(reloaded.LastTestSucceeded);
    }

    [Fact]
    public void ConnectionProfileStore_ProfilSilinebilir()
    {
        var path = Path.Combine(_tempDirectory, "profiles.json");
        var store = new ConnectionProfileStore(path);
        var profile = new ConnectionProfile { Name = "Silinecek", ProviderKey = "openai" };

        store.Save(profile);
        store.Delete(profile.Id);

        Assert.Empty(new ConnectionProfileStore(path).Profiles);
    }

    [Fact]
    public void RuleSetStore_KuralSetiniDosyayaYazarVeOkur()
    {
        var store = new RuleSetStore(_tempDirectory);
        var input = new ProjectInput
        {
            Topic = "Kredi Başvurusu",
            Rules = { new BusinessRule { Id = "BR-1", Text = "Her başvurunun bir müşterisi vardır." } },
            Glossary = { new GlossaryTerm { Term = "Başvuru", TechnicalName = "Application" } }
        };

        store.Save("kredi", input);

        var loaded = store.Load("kredi");
        Assert.NotNull(loaded);
        Assert.Equal("Kredi Başvurusu", loaded!.Topic);
        Assert.Single(loaded.Rules);
        Assert.Contains("kredi", store.ListNames());
    }

    [Fact]
    public void ExcelExport_HerVarlikIcinAyriSekmeYazar()
    {
        var model = TestModels.CustomerOrder();
        var dataSet = new SampleDataGenerator(seed: 1).Generate(model, 5);
        var path = Path.Combine(_tempDirectory, "ornek.xlsx");

        new ExcelExportService().Export(dataSet, path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);

        using var workbook = new ClosedXML.Excel.XLWorkbook(path);
        Assert.Equal(2, workbook.Worksheets.Count);
        Assert.Contains(workbook.Worksheets, ws => ws.Name == "Musteri");
        Assert.Contains(workbook.Worksheets, ws => ws.Name == "Siparis");
        Assert.Equal("MusteriNo", workbook.Worksheet("Musteri").Cell(1, 1).GetString());
        Assert.Equal(6, workbook.Worksheet("Musteri").LastRowUsed()!.RowNumber());
    }

    [Fact]
    public void TextExport_IcerigiBomsuzUtf8OlarakYazar()
    {
        var path = Path.Combine(_tempDirectory, "diagram.mmd");
        TextExportService.Save("erDiagram\n    MUSTERI ||--o{ SIPARIS : \"verir\"", path);

        Assert.True(File.Exists(path));
        Assert.StartsWith("erDiagram", File.ReadAllText(path));
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
    }
}
