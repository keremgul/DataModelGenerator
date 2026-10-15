# Normalizasyon Kuralları

Her kural her entity ve alan için sırayla işletilir. Bir kuralı geçemeyen alan
modele EKLENMEZ; ihlal nedeni ve kullanıcıya sorulacak soru ile birlikte
"beklemede" listesine alınır. Otomatik düzeltme yalnızca kural açıkça tek bir
doğru çözüm tanımlıyorsa yapılır; aksi halde soru sorulur.

## Ön koşullar
- PK-1: Her entity'nin tanımlı bir birincil anahtarı olmalı. Belirlenemiyorsa soru sor.
- PK-2: Birincil anahtar null olamaz ve tekil olmalı.
- AD-1: Her alanın açık bir anlamı ve kaynağı (BR/Q/CHAT) olmalı.

## 1NF
- 1NF-1: Her alan atomik olmalı (ör. "Ad Soyad", "Adres" gibi bileşik değerler
  ayrıştırılmalı ya da atomik kabul edildiği kullanıcıdan teyit edilmeli).
- 1NF-2: Tekrarlayan gruplar olmamalı (Telefon1, Telefon2, Telefon3 → ayrı entity).
- 1NF-3: Çok değerli alan olmamalı (virgülle ayrılmış liste, JSON vb. tek alanda tutulamaz).
- 1NF-4: Her satır birincil anahtarla tekil olarak tanımlanabilmeli.

## 2NF
- 2NF-1: 1NF sağlanmış olmalı.
- 2NF-2: Bileşik anahtarlı entity'lerde anahtar olmayan her alan anahtarın
  TAMAMINA bağlı olmalı; anahtarın bir parçasına bağlı alan ayrı entity'ye taşınır.

## 3NF
- 3NF-1: 2NF sağlanmış olmalı.
- 3NF-2: Geçişli bağımlılık olmamalı (anahtar olmayan alan, başka bir anahtar
  olmayan alana bağlıysa ayrı entity'ye taşınır; ör. MusteriId → IlKodu → IlAdi).
- 3NF-3: Türetilebilir/hesaplanabilir alanlar (toplam, yaş vb.) saklanmaz;
  saklanması gerekiyorsa gerekçesi kullanıcıdan alınır ve açıkça işaretlenir.

## BCNF
- BCNF-1: Her fonksiyonel bağımlılıkta belirleyici (determinant) bir aday anahtar olmalı.
- BCNF-2: İhlal varsa ayrıştırma önerilir ancak kayıpsız birleştirme ve bağımlılık
  koruması sağlanamıyorsa kullanıcıya sorulur.

## İlişki ve bütünlük
- REL-1: Her yabancı anahtar var olan bir entity'nin birincil anahtarını göstermeli.
- REL-2: İlişki kardinalitesi (1-1, 1-N, N-M) BR veya cevapla desteklenmeli; desteklenmiyorsa sorulur.
- REL-3: N-M ilişkiler ara (bağlantı) entity ile çözülür.
- REL-4: Aynı bilgiyi tutan iki alan (duplicate) olamaz; tespit edilirse sorulur.

## Çıktı formatı (her alan için)
- alan, entity, uygulanan kurallar, sonuç: GEÇTİ | AYRIŞTIRILDI | ENGELLENDİ
- ENGELLENDİ ise: ihlal edilen kural, neden, kullanıcıya sorulacak soru
