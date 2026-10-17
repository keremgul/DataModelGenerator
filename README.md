# Veri Modeli Generator

Business rule listesi girildiğinde yapay zeka destekli **kavramsal/mantıksal veri modeli** üreten bağımsız Windows masaüstü uygulaması (.NET 8 / WPF).

Uygulama hiçbir veritabanına bağlanmaz; tüm çıktılar dosya bazlıdır.

**Halüsinasyon yasağı:** Modele giren her varlık, alan ve ilişki en az bir kaynağa
(`BR-xx`, `Q-xx` veya `CHAT-xx`) bağlıdır. Kaynağı olmayan, belirsiz işaretlenmiş
veya normalizasyon kurallarını geçemeyen hiçbir öğe modele alınmaz; "beklemede"
listesine düşer ve size soru olarak döner. Bu denetim prompt talimatına değil,
kod tarafındaki süzgece dayanır.

## Çalıştırma

### Hazır paketi indir (derleme gerekmez)

1. [Releases](https://github.com/keremgul/DataModelGenerator/releases) sayfasından `DataModelGenerator-vX.Y.Z-win-x64.zip` dosyasını indirin.
2. Bir klasöre çıkarın — kurulum yoktur.
3. `DataModelGenerator.exe` dosyasını çalıştırın.

**Gereksinim:** Windows 10/11 x64. .NET kurulumu gerekmez (self-contained). ER diyagramı için WebView2 çalışma zamanı kullanılır; Windows 11'de hazır gelir. Yoksa yalnızca diyagram sekmesi uyarı gösterir, modelin kendisi ve tüm dışa aktarımlar çalışmaya devam eder.

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
2. **Girdi** — Numaralı business rule listesi (tek tek ya da serbest metin yapıştırıp ayrıştırarak). Kurallar satır satır değil, aralarındaki anlam ilişkisiyle birlikte değerlendirilir; çelişen veya tekrar eden kurallar size soru olarak döner. İsterseniz konuyla ilgili **mevcut yapıyı anlatan bir `.md` dosyası** yükleyin: oradaki tablolar yeniden üretilmez, yeni yapılar bunlarla ilişkilendirilir.
3. **Üretim** — Sekiz adımlı zincir çalışır (bkz. [docs/pipeline.md](docs/pipeline.md)). Sonuçlar sekmelerde görünür: ER diyagramı, özet (izlenebilirlik tablosu), beklemede, öneriler, belirsizlikler, doğrulama.
4. **Sorular** — Belirsizlikler ve beklemede listesi için üretilen soruları cevaplayın. Sorular **mantıksal yapıya** odaklanır: ilişkiler, kardinalite, zorunluluk, iş kuralı kısıtları, tarihçe ihtiyacı. Önerilen cevaplar kopyalanabilir ve tek tıkla kullanılabilir; model yalnızca etkilenen adımdan itibaren kısmen güncellenir. Cevabınız bağlayıcı kısıttır ve modelde karşılık bulup bulmadığı denetlenir.
5. **Sohbet** — Modelde ne değişsin, yazın. Talep yapılandırılmış direktiflere çevrilir; yeniden adlandırma ve kaldırma gibi işlemler LLM'e hiç gitmeden doğrudan uygulanır. Sonuç modelin "yaptım" demesiyle değil, **önceki ve sonraki hali arasındaki gerçek diff** ile doğrulanır.
6. **Dışa aktarma** — Mermaid kodu (.mmd), model JSON'u ve örnek veri simülasyonu (her varlık ayrı sekme olacak şekilde .xlsx).

Üstteki adım göstergesi hangi adımın tamamlandığını (✓) ve geri dönüp bir şey değiştirdiğinizde hangi adımın **güncel olmadığını** (!) gösterir. Sonraki adımların çıktısı silinmez, yalnızca işaretlenir.

### Artımlı geliştirme

Model üretildikten sonra Girdi adımına dönüp yeni kural ekleyebilir ve **"Kuralları Ekle ve Modeli Güncelle"** diyebilirsiniz. Model sıfırdan üretilmez: mevcut hali bağlam olarak gider ve yalnızca yeni kuralların gerektirdiği değişiklik yapılır. Önceki tablo, alan ve ilişkiler yeni kural açıkça gerektirmedikçe korunur — silinmez, yeniden adlandırılmaz. Sonuçta önceki ve yeni model arasındaki fark (eklenen/kaldırılan tablo ve alanlar) özet olarak gösterilir.

## İsimlendirme standardı

Tüm tablo ve alan adları **büyük harf** ve **Türkçe karaktersizdir**: `MUSTERI`, `MUSTERINO`, `MUSTERIID`. Kural prompt'a yazılır ama ona güvenilmez — her model yanıtından sonra adlar kod tarafında denetlenip düzeltilir, tablo adı değişirse ilişkiler ve yabancı anahtar referansları yeniden bağlanır.

Standart `%AppData%\DataModelGenerator\settings.json` içindeki `Naming` bölümünden değiştirilebilir:

| Ayar | Varsayılan | Diğer değerler |
|---|---|---|
| `Case` | `Upper` | `Lower`, `Pascal` |
| `Separator` | `None` (`MUSTERINO`) | `Underscore` (`MUSTERI_NO`) |
| `FoldTurkish` | `true` | `false` |
| `MaxLength` | `0` (sınırsız) | ör. `30` |
| `PrimaryKeyName` | `ID` | — |
| `ForeignKeySuffix` | `ID` | — |

İlişki ve yabancı anahtar adlandırmasının tek kaynağı [`skills/relationship-naming.md`](skills/relationship-naming.md) dosyasıdır: `<TABLOADI>ID` kalıbı ilişkinin kendisidir, aynı tabloya birden çok referans için `<ROL><TABLOADI>ID` kullanılır (`GONDERENMUSTERIID`). Bu dosya model üretimi, soru üretimi ve sohbet prompt'larına bağlam olarak gider; kod tarafı kalıba uyan alanı otomatik yabancı anahtar sayar, mükerrer ilişkileri eler ve hedefi bulunamayan referans için uyarı üretir.

## Mimari

- **DataModelGenerator.Core** — Pipeline (kural ilişki analizi → varlık çıkarımı → ilişki/kardinalite → alan türetimi → normalizasyon → belirsizlik tespiti → soru üretimi → çıktı üretimi), halüsinasyon süzgeci (`SourceGuard`), normalizasyon motoru, deterministik kural motoru (adlandırma, anahtar tamamlama, ara tablo), doğrulama katmanı, sağlayıcı soyutlaması, örnek veri üretimi ve dışa aktarma.
- **DataModelGenerator.App** — WPF / MVVM arayüz.
- **DataModelGenerator.Tests** — xUnit testleri.

LLM çıktısı "öneri", kod doğrulaması "gerçeklik kontrolü" olarak konumlanır: isim çakışması, karşılığı olmayan FK/PK, döngüsel referans ve eksik zorunlu alanlar mekanik olarak yakalanır. Her varlık ve alan, türediği kural kimliğine geri izlenebilir — özet sekmesindeki izlenebilirlik tablosundan hem alandan kaynağa hem kaynaktan alanlara gezilebilir.

### Skill dosyaları

`skills/` klasöründeki düz metin dosyaları exe'nin yanında gelir ve yeniden derlemeden düzenlenebilir. Sohbetten gelen talep önce kalıp eşleştirmeyle, eşleşme yoksa `router.md` ile yapılandırılmış bir direktife çevrilir; üretim gerektiren işlemler kendi skill dosyasıyla ve yalnızca ilgili varlıkların context'iyle çalışır. `NORMALIZATION_RULES.md` ve `normalize.md` kod tarafında işletilen NF kurallarını belgeler.

Model **kavramsaldır**: varlık, ilişki, alan ve anahtar bilgisi tutar; veri tipi ve uzunluk tutmaz — bunlar fiziksel tasarımda, somut ihtiyaca göre belirlenir.

## Dosya konumları

| Ne | Nerede |
|---|---|
| Bağlantı profilleri, kural setleri, ayarlar | `%AppData%\DataModelGenerator\` |
| API anahtarları (Windows DPAPI ile şifreli) | `%AppData%\DataModelGenerator\keys.dat` |
| API ve hata logları | exe'nin yanında `logs\` |
| Prompt şablonları | exe'nin yanında `Resources\Prompts\*.txt` |
| Skill dosyaları | exe'nin yanında `skills\*.md` |

Prompt ve skill dosyalarını düzenlerseniz exe'ye gömülü sürümün yerine geçerler; yeniden derleme gerekmez. API anahtarları kullanıcıya ve makineye özel şifrelendiği için paketle taşınmaz, her makinede bir kez girilir.
