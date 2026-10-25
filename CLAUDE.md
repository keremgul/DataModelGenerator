# Çalışma Kuralları

Bu dosya bu depoda çalışan yapay zeka ajanları içindir. Mimari ve kullanım
bilgisi `README.md` dosyasındadır; burada yalnızca **koddan okunamayan**
çalışma kuralları var.

## Süreç

- **Önce kodu incele, sonra plan sun.** Değişikliği uygulamadan önce planı
  onaya gönder. Emin değilsen tahmin etme — sor.
- **Her madde ayrı commit.** Commit mesajı madde numarasını içerir:
  `[Madde 3] Soru üretimi mantıksal yapıya odaklanıyor`. Birden fazla maddeyi
  tek commit'e karıştırma; bir maddenin derlenmeyen hâli commit'lenmez.
- **Çalışmaya başlamadan `git pull`.** Bu depo üzerinde birden fazla oturum
  (yerel + bulut + mobil) paralel çalışıyor. Başkasının hâlihazırda çözdüğü
  bir işi yeniden yazma riski gerçek; bir kez yaşandı (PR #1).
- **`dotnet test` yeşil olmadan commit yok.** Kullanıcının bildirdiği her hata
  için önce **başarısız bir test** yaz, sonra düzelt. "Model yapmadı" denen
  hataların çoğu deterministik katman veya merge semantiği hatası çıkıyor.
- Sürüm çıkarken: `publish.ps1 -Version X.Y.Z` → zip'i release'e ekle.
  Etiket adı `vX.Y.Z` (başta fazladan nokta yok, başlıkla sürüm aynı olmalı).

## Bağımlılıklar

Uygulama portable, sıfır bağımlılıkla dağıtılır. **Yeni NuGet veya harici
bağımlılık eklemeden önce kullanıcıya sor.** Mevcut set: CommunityToolkit.Mvvm,
Microsoft.Web.WebView2, ClosedXML, System.Security.Cryptography.ProtectedData
ve test tarafında xUnit.

## Modelin değişmez kuralları

- **Halüsinasyon yasağı.** Modele giren her varlık, alan ve ilişki en az bir
  kaynağa bağlıdır (`BR-xx`, `Q-xx`, `CHAT-xx`; standart alanlar `STD`).
  Denetim **kodda** yapılır — prompt talimatına güvenilmez. Belirsiz olan
  modele girmez, soruya dönüşür.
- **Güncelleme modu.** Soru cevabı, kabul edilen öneri ve sohbet mesajı mevcut
  modelin üzerine eklenir; pipeline baştan çalışmaz. Yanıtta dönmeyen öğe
  silinmez. Silme yalnızca açık `removedFields` / `removedEntities` /
  `removedRelationships` ile olur.
- **Yabancı anahtar ilişkiyi taşır.** Biri kalkarken diğeri kalırsa
  deterministik katman kalkanı geri yazar ve kullanıcı "yapıldı" cevabı aldığı
  hâlde model değişmez. Simetriyi koru (bkz. `skills/deterministic-operations.md`).
- **İlişki kimliği `(from, to, Role)` üçlüsüdür** — serbest metin `Name` değil.
- İsimlendirme: BÜYÜK HARF, Türkçe karaktersiz, ayırıcı yok (`MUSTERINO`).
  Kullanıcıya gösterilen ad da aynı — ayrı "okunabilir ad" gösterilmez.
  Politika `AppSettings.Naming` üzerinden yapılandırılabilir.
- Sonuç, modelin **önceki/sonraki hâli arasındaki gerçek diff**'ine bakılarak
  doğrulanır. Kodun veya modelin "yaptım" beyanı ölçüt değildir.

## Skill dosyaları tek kaynaktır

`skills/*.md` dosyaları bir kuralın tek doğru kaynağıdır; kuralı koda
kopyalamak yerine ilgili skill'e yaz ve yer tutucuyla göm. `{{MODEL_RULES}}`,
`{{SOURCE_RULES}}`, `{{NAMING_RULES}}`, `{{RELATION_RULES}}`,
`{{STANDARD_FIELDS}}` otomatik genişler. `{{RELATION_RULES}}` içinde
`{{NAMING_RULES}}` geçtiği için **ondan önce** genişletilmek zorundadır.

Bir davranış değiştiğinde ilgili skill dosyasını da güncelle; aksi halde
prompt ile kod ayrışır.

## Doğrulama düzeneği

LLM'siz uçtan uca doğrulama için: `node tools/mock-llm.mjs` (port 8765) +
`tools/flow.ps1` (UI Automation) veya `tools/smoke.ps1`. Yeni bir prompt
eklediğinde mock'a da route ekle; yoksa konsolda `BILINMEYEN -> 2 bayt`
görürsün ve özellik "çalışmıyor" sanılır.

Türkçe metin içeren `.ps1` dosyaları **UTF-8 BOM** ile kaydedilmeli; Windows
PowerShell 5.1 aksi halde karakterleri bozar.

## Depo notu

Kök dizindeki `ModelPipeline.cs`, `RuleEngineTests.cs`, `TestModels.cs` ve
`main.py` dosyaları projenin derlemesine girmez — eski taşıma artığıdır.
Bunları kaynak olarak okuma; gerçek kod `DataModelGenerator.Core`,
`DataModelGenerator.App` ve `DataModelGenerator.Tests` altındadır.
