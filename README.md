# Veri Modeli Generator

Bir konu ve business rule listesi girildiğinde yapay zeka destekli **kavramsal/mantıksal veri modeli** üreten bağımsız Windows masaüstü uygulaması (.NET 8 / WPF).

Uygulama hiçbir veritabanına bağlanmaz; tüm çıktılar dosya bazlıdır.

## Çalıştırma

### Hazır paketi indir (derleme gerekmez)

1. [Releases](https://github.com/keremgul/DataModelGenerator/releases) sayfasından `DataModelGenerator-vX.Y.Z-win-x64.zip` dosyasını indirin.
2. Bir klasöre çıkarın — kurulum yoktur.
3. `DataModelGenerator.exe` dosyasını çalıştırın.

**Gereksinim:** Windows 10/11 x64. .NET kurulumu gerekmez (self-contained). ER diyagramı için WebView2 çalışma zamanı kullanılır; Windows 11'de hazır gelir. Yoksa yalnızca diyagram sekmesi uyarı gösterir, Mermaid kodu ve tüm dışa aktarımlar çalışmaya devam eder.

### Kaynak koddan çalıştır

[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) gerekir.

```powershell
git clone https://github.com/keremgul/DataModelGenerator.git
cd DataModelGenerator
dotnet run --project DataModelGenerator.App
```

Testler:

```powershell
dotnet test
```

### Portable paketi kendin üret

`publish/` klasörü derleme çıktısıdır ve depoda tutulmaz. Paketi üretmek için:

```powershell
.\publish.ps1                  # veya: .\publish.ps1 -Version 1.1.0
```

Komut `publish/DataModelGenerator-vX.Y.Z-win-x64/` klasörünü ve aynı adlı zip dosyasını oluşturur.

## Kullanım akışı

1. **Bağlantı** — Sağlayıcı seçin (OpenAI, Anthropic, Google Gemini, Azure OpenAI, Ollama, özel endpoint), hazır listeden model seçin veya adını yazın, API anahtarını girin ve "Bağlantıyı Test Et" ile doğrulayın. Test başarılı olunca profil kaydedilebilir.
2. **Girdi** — Konu, opsiyonel kapsam, business rule listesi (madde madde veya serbest metin yapıştırıp ayrıştırarak) ve opsiyonel terim sözlüğü.
3. **Üretim** — Yedi adımlı zincir çalışır; model, özet, öneriler, izlenebilirlik tablosu, belirsizlikler ve doğrulama sonuçları sekmelerde görüntülenir.
4. **Sorular** — Yüksek etkili belirsizlikler için üretilen soruları cevaplayın; model yalnızca etkilenen adımdan itibaren kısmen güncellenir. Döngü en fazla 3 turla sınırlıdır; uygulanan sorular ✓ ile geçmişte kalır.
5. **Reprompt** — Seçtiğiniz varlıklar için serbest değişiklik talebi gönderin; yalnızca o varlıkların context'i modele iletilir. Bir varlığı silmek için onu kapsama ekleyip kaldırılmasını isteyin.
6. **Dışa aktarma** — Mermaid kodu (.mmd), model JSON'u ve örnek veri simülasyonu (her varlık ayrı sekme olacak şekilde .xlsx).

## Mimari

- **DataModelGenerator.Core** — Pipeline (varlık çıkarımı → ilişki/kardinalite → alan/tip türetimi → normalizasyon denetimi → belirsizlik tespiti → soru üretimi → çıktı üretimi), deterministik kural motoru (adlandırma, tip eşlemesi, anahtar tamamlama), doğrulama katmanı, sağlayıcı soyutlaması, örnek veri üretimi ve dışa aktarma.
- **DataModelGenerator.App** — WPF / MVVM arayüz.
- **DataModelGenerator.Tests** — xUnit testleri.

LLM çıktısı "öneri", kod doğrulaması "gerçeklik kontrolü" olarak konumlanır: isim çakışması, karşılığı olmayan FK/PK, döngüsel referans ve eksik zorunlu alanlar mekanik olarak yakalanır. Her varlık ve alan, türediği business rule ID'sine geri izlenebilir.

Model **kavramsaldır**: varlık, ilişki, alan ve anahtar bilgisi tutar; veri tipi ve uzunluk tutmaz — bunlar fiziksel tasarımda, somut ihtiyaca göre belirlenir.

## Dosya konumları

| Ne | Nerede |
|---|---|
| Bağlantı profilleri, kural setleri, ayarlar | `%AppData%\DataModelGenerator\` |
| API anahtarları (Windows DPAPI ile şifreli) | `%AppData%\DataModelGenerator\keys.dat` |
| API ve hata logları | exe'nin yanında `logs\` |
| Prompt şablonları | exe'nin yanında `Resources\Prompts\*.txt` |

Prompt dosyalarını düzenlerseniz exe'ye gömülü sürümün yerine geçerler; yeniden derleme gerekmez. API anahtarları kullanıcıya ve makineye özel şifrelendiği için paketle taşınmaz, her makinede bir kez girilir.
