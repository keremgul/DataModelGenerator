# Veri Modeli Generator — Proje Dokümanı

## 1. Genel Bakış

Bir konu (topic) ve buna ait business rule listesi girdi olarak verildiğinde, yapay zeka destekli bir **kavramsal/mantıksal veri modeli** ve **iş modeli** üreten, bağımsız bir masaüstü uygulaması.

Uygulama sadece tek seferlik bir üretim yapmaz; eksik/belirsiz girdiyi tespit eder, kullanıcıyı hedefli sorularla yönlendirir, reprompt ile modeli iteratif olarak günceller ve her turda özet + öneri çıktısı verir.

Bu proje, aynı ekipteki **Test Plan Generator** (.NET 8 / C# / WPF, çok-sağlayıcılı LLM soyutlaması, deterministik rule engine, flat-file storage) ile aynı mimari felsefeyi paylaşır ama ayrı, bağımsız bir uygulamadır.

## 2. Amaç ve Kapsam

**Amaç:** Fonksiyonel/iş analizi aşamasında elle çıkarılan veri modellerini, business rule'lardan otomatik türeterek analistin işini hızlandırmak ve tutarlılığı artırmak.

**Kapsam dahilinde:**
- Kavramsal/mantıksal veri modeli üretimi (varlıklar, ilişkiler, kardinaliteler, alanlar)
- İş modeli çıktısı (süreç/kural özetleri, varsa use-case listesi)
- Belirsizlik tespiti ve kullanıcıya yönlendirici soru üretimi
- Reprompt / iteratif düzeltme döngüsü
- Özet ve öneri çıktıları

**Kapsam dışında (ilk faz):**
- Herhangi bir veritabanına canlı bağlantı kurulması veya modelin bir veritabanına beslenmesi/deploy edilmesi — uygulama **hiçbir veritabanı bağlantısı kurmaz**, tüm çıktılar dosya (metin/Excel) bazlıdır
- Çoklu kullanıcı / eşzamanlı düzenleme (tek kullanıcılı masaüstü aracı)

## 3. Girdi Yapısı

Kullanıcıdan alınacak girdi yarı yapılandırılmış olmalı:

- **Konu adı** (serbest metin)
- **Kapsam / bağlam** (opsiyonel, serbest metin)
- **Business rule listesi** — her biri kendi kimliğine (ID) sahip, madde madde girilir. Bu ID'ler çıktıdaki her varlık/alanın hangi kuraldan türediğini izlemek (traceability) için kullanılır.
- **Terim sözlüğü** (opsiyonel) — Türkçe domain terimlerinin İngilizce/teknik karşılıkları, tutarlı adlandırma için

Girdi UI üzerinden madde madde eklenebilmeli; serbest metin yapıştırma da desteklenmeli ama sisteme verilmeden önce kural listesine ayrıştırılmalı (parse edilmeli).

## 4. Çıktı Yapısı

Her üretim turu üç katmanlı bir paket döndürür:

1. **Model** — asıl çıktı: varlıklar, alanlar, tipler, ilişkiler, kardinaliteler. Hedef formatlar: JSON (dahili temsil), Mermaid ER diyagramı (görselleştirme ve ayrıca ham metin/kod olarak).
2. **Özet** — düz dilde: hangi varlığın hangi kural(lar)dan türediği, yapılan varsayımlar, normalize edilirken alınan kararlar.
3. **Öneri** — modele dahil edilmemiş ama proaktif notlar (örn. "N-N ilişki için ara tablo gerekebilir, kurallarda açıkça geçmiyor", "audit alanları eklenmedi, standart pratik gereği önerilir").

Ayrıca her turda ayrı bir **belirsizlik listesi** (`ambiguities`) üretilir — bkz. Bölüm 6.

### 4.1 Export Fonksiyonları

Uygulama herhangi bir veritabanına bağlanmaz veya beslemez; tüm çıktılar dosya bazlıdır:

- **Mermaid kodu:** Üretilen ER diyagramının Mermaid kodu, düz metin (.mmd veya .txt) dosyası olarak indirilebilir — böylece kullanıcı bunu başka bir dokümana/araca yapıştırabilir.
- **Örnek veri simülasyonu:** Üretilen kavramsal veri modeline dayanarak, her varlık için tutarlı (ilişki ve kısıtlara uygun — örn. FK değerleri gerçekten var olan PK'lara referans verir) **örnek/sentetik veri seti** üretilir. Bu simülasyon her varlık için ayrı bir sekme (sheet) olacak şekilde **Excel (.xlsx)** dosyası olarak indirilebilir.
- Örnek veri satır sayısı kullanıcı tarafından serbestçe tanımlanabilir olmalı — sabit bir üst limit yoktur, sınır kullanıcının belirlediği değere göre tanımsal (dinamik) olacaktır.

## 5. Mimari İlkeler

### 5.1 Deterministik / LLM Ayrımı

Test Plan Generator'daki `RuleEngine` + `IModelProvider` ayrımı burada da uygulanır:

- **Deterministik katman (kod):** Sabit adlandırma kuralları (Türkçe→teknik isim dönüşümü), veri tipi eşlemeleri (örn. "tarih" → `DATE`, "tutar" → `DECIMAL(18,2)`), düşük etkili varsayılanlar.
- **LLM katmanı:** Varlık çıkarımı, ilişki tespiti, kural yorumlama, soru üretimi — "anlama" gerektiren tüm adımlar.

### 5.2 Çok Sağlayıcılı LLM Soyutlaması

`IModelProvider` benzeri bir arayüz ile OpenAI, Anthropic, Gemini, Azure OpenAI, Ollama ve özel endpoint desteği — Test Plan Generator ile aynı pattern, kod tekrarını azaltmak için mümkünse ortak bir kütüphane olarak paylaşılabilir.

### 5.3 Bağlantı Yönetimi (Offline Model / API Endpoint)

Uygulama, bulut tabanlı sağlayıcıların yanı sıra **offline/yerel modelleri** de destekler (örn. Ollama veya özel bir endpoint üzerinden barındırılan model). Bağlantı yönetimi ayrı bir modül olarak ele alınır:

- **Bağlantı profili tanımlama:** Kullanıcı, her sağlayıcı için bir profil oluşturabilir — sağlayıcı tipi (OpenAI, Anthropic, Gemini, Azure OpenAI, Ollama/offline, özel endpoint), **API endpoint URL**, **API Key**, model adı.
- **Offline model çağrısı:** Offline/yerel model için API'ye yapılan çağrı, **Test Plan Generator'daki offline model çağrısıyla birebir aynı** şekilde gerçekleştirilecektir (aynı istek formatı/akışı — kütüphane paylaşılmasa da çağrı mantığı birebir taşınır).
- **Bağlantı testi:** Profil kaydedilmeden önce (ve istenildiğinde sonradan) "Bağlantıyı Test Et" fonksiyonu ile endpoint'e **ufak bir completion çağrısı** gönderilir (örn. kısa bir prompt ile minimal bir tamamlama isteği); yalnızca endpoint'in ayakta olup olmadığını kontrol etmek yeterli değildir — modelin gerçekten yanıt üretebildiği doğrulanmalıdır. Başarı/hata durumu ve varsa hata mesajı (auth hatası, timeout, model bulunamadı, geçersiz yanıt formatı vb.) kullanıcıya gösterilir.
- **Bağlantı kaydetme:** Test başarılı olduktan sonra profil kalıcı olarak saklanabilir. API Key gibi hassas bilgiler düz metin olarak saklanmaz; şifreleme yöntemi **Test Plan Generator'dakine benzer** olacaktır (yerel şifreleme, örn. Windows DPAPI / `ProtectedData` sınıfı).
- **Offline mod ayrımı:** Offline/yerel model seçildiğinde API Key alanı devre dışı bırakılabilir veya opsiyonel hale gelir (bazı yerel sunucular auth gerektirmez); endpoint URL zorunlu kalır.
- **Profil yönetimi:** Kayıtlı profiller arasında geçiş yapılabilir, düzenlenebilir, silinebilir; hangi profilin aktif olduğu pipeline'ın hangi `IModelProvider` implementasyonunu kullanacağını belirler.
- **Bağlantı durumu göstergesi:** UI'da aktif profilin son test sonucu (başarılı/başarısız, son test tarihi) görünür olmalı — kullanıcı üretim başlatmadan önce bağlantının geçerli olduğunu görebilmeli.

### 5.4 Doğrulama Katmanı

LLM çıktısının mekanik olarak kontrol edilebilir kısımları kod ile doğrulanır (LLM'e güvenilmez):
- İsim çakışması yok
- Her FK'nin karşılık geldiği PK var
- Döngüsel referans yok
- Zorunlu alan eksik değil

LLM çıktısı "öneri", kod doğrulaması "gerçeklik kontrolü" olarak konumlanır.

## 6. Pipeline / İşlem Akışı

Tek büyük prompt yerine çok adımlı, yapılandırılmış (JSON) çıktı üreten bir zincir:

1. **Extraction** — kurallardan varlık adaylarını çıkar
2. **Relationship inference** — varlıklar arası ilişki ve kardinaliteleri belirle
3. **Attribute derivation** — her varlığın alan/tip/kısıt bilgilerini türet
4. **Normalization check** — 2NF/3NF ihlali kontrolü (LLM + kural bazlı)
5. **Gap detection** — her varlık/ilişki için confidence skoru ve gerekçe üret; düşük confidence'lı noktaları `ambiguities` listesine yaz
6. **Question generation** — yüksek etkili belirsizlikler (kardinalite, PK/FK gibi) için hedefli sorular üret; düşük etkili olanlar deterministik varsayılanla çözülüp özet'te belirtilir
7. **Serialization** — hedef formatlara (JSON/Mermaid/DDL) dönüştür

Her adımın çıktısı bir sonrakine yapılandırılmış veri olarak aktarılır; serbest metin zincirlemesinden kaçınılır.

## 7. Belirsizlik Yönetimi ve Kullanıcı Yönlendirmesi

- Her varlık/ilişki için **confidence + gerekçe** üretilir (örn. "PK açıkça belirtilmemiş, isimden çıkarım yapıldı").
- **Eşik mantığı:** Yüksek etkili belirsizlikler (kardinalite, PK/FK) mutlaka soru olarak kullanıcıya sorulur; düşük etkili olanlar (adlandırma stili gibi) otomatik varsayılanla çözülüp sadece özette not düşülür.
- Sorular yapılandırılmış formatta üretilir: soru metni + neden soruluyor + varsa örnek seçenekler.
- Kullanıcı cevapları kural listesine ek madde olarak enjekte edilir; pipeline **baştan değil**, sadece etkilenen adımdan itibaren yeniden çalıştırılır (token/maliyet optimizasyonu).

## 8. Reprompt Akışı — Üç Ayrı Durum

Aynı büyük prompt'a sıkıştırmak yerine üç ayrı akış olarak modellenir:

1. **İlk üretim:** kurallardan model + belirsizlik listesi
2. **Yönlendirme turu:** belirsizlik listesinden soru üretimi → kullanıcı cevabı → modelin kısmi güncellemesi (etkilenmeyen varlıklar tekrar LLM'e gönderilmez)
3. **Serbest reprompt:** kullanıcı "şunu değiştir" dediğinde, sadece ilgili varlık/ilişki context'i + değişiklik talebi gönderilir, tüm model değil

## 9. Teknik Yığın ve Dağıtım

Uygulama **.NET Framework** üzerinde, **WPF** ekranları ile geliştirilecektir (Test Plan Generator'ın .NET 8 tabanlı yapısından farklı olarak, bu proje için .NET Framework zorunlu tutulmuştur — MVVM pattern korunur).

**Depolama:** Uygulama ayarları, bağlantı profilleri ve kural setleri **flat-file (text dosya)** üzerinden yönetilecektir — SQLite veya benzeri bir yerel DB kullanılmayacak. Bu, Test Plan Generator'daki yaklaşımla tutarlıdır.

**LLM sağlayıcı kütüphanesi:** `IModelProvider` soyutlaması Test Plan Generator ile **paylaşılmayacak**; bu proje için bağımsız, proje-özel olarak geliştirilecektir.

**Dağıtım:** Test Plan Generator'daki gibi **zero-dependency, portable** bir paket olarak dağıtılacaktır.

**Mermaid render:** Portable çalıştırılabilirliği korumak için **WPF içine gömülü bir web view** (örn. `WebView2`) üzerinden render edilecektir — harici kütüphane bağımlılığı eklenmeyecektir.

## 10. Kabul Kriterleri (MVP)

- [ ] Kullanıcı en az bir konu + business rule listesi girip ilk model üretimini alabiliyor
- [ ] Üretilen model Mermaid ER diyagramı olarak görüntülenebiliyor ve Mermaid kodu metin dosyası olarak indirilebiliyor
- [ ] Kavramsal veri modeline uygun (ilişki/kısıt tutarlı) örnek veri simülasyonu üretilip Excel (.xlsx) olarak indirilebiliyor
- [ ] Her varlık/alan, kaynağı olan business rule ID'sine geri izlenebiliyor (traceability)
- [ ] Belirsizlik listesi üretiliyor ve yüksek etkili belirsizlikler soru olarak kullanıcıya sunuluyor
- [ ] Kullanıcı soruları cevapladığında model, ilgili kısımlar için kısmi güncelleniyor (tam yeniden üretim değil)
- [ ] Serbest reprompt ile belirli bir varlık/ilişki üzerinde değişiklik yapılabiliyor
- [ ] Her turda özet ve öneri metni üretiliyor
- [ ] Doğrulama katmanı isim çakışması, eksik FK/PK, döngüsel referans gibi hataları mekanik olarak yakalıyor
- [ ] En az iki LLM sağlayıcısı (`IModelProvider` üzerinden) desteklenebiliyor
- [ ] Bulut tabanlı ve offline/yerel model bağlantı profilleri (endpoint URL + API Key + model adı) tanımlanabiliyor
- [ ] "Bağlantıyı Test Et" fonksiyonu ile profil kaydedilmeden önce bağlantı doğrulanabiliyor
- [ ] Test başarılı olan profiller kalıcı olarak kaydedilebiliyor; API Key şifreli saklanıyor
- [ ] Uygulama .NET Framework / WPF (MVVM) ekranları üzerinden çalışıyor
- [ ] Uygulama zero-dependency, portable bir paket olarak dağıtılabiliyor

## 11. Faz Planı (Öneri)

**Faz 1 — Çekirdek Pipeline:** Extraction → relationship inference → attribute derivation → serialization (JSON + Mermaid). Tek sağlayıcı, doğrulama katmanının temel kuralları.

**Faz 2 — Belirsizlik Yönetimi:** Gap detection, confidence skorlama, question generation, kısmi güncelleme mekanizması.

**Faz 3 — Reprompt & UX:** Serbest reprompt akışı, özet/öneri üretimi, Mermaid metin export'u, örnek veri simülasyonu + Excel export'u, çok sağlayıcılı LLM desteğinin tamamlanması.

**Faz 4 — Cila:** Terim sözlüğü desteği, gelişmiş normalizasyon kontrolleri, dağıtım (colleagues) için paketleme.

## 12. Açık Sorular (Claude Code Oturumunda Netleştirilecek)

Şu an için netleştirilmemiş bir karar noktası kalmadı. Claude Code oturumu doğrudan Bölüm 1-11'deki kararlara göre başlayabilir.
