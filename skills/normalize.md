# Skill: Normalizasyon

`NORMALIZATION_RULES.md` içindeki kuralları işleten adım. Bu skill **kod
tarafında** çalışır (`NormalizationEngine`); LLM'e sorulmaz. Normalizasyon
kararı bir dil modelinin kanaatine bırakılamaz: bir alanın modele girip
girmeyeceği buna bağlıdır.

Bu dosya referanstır — içeriği prompt olarak gönderilmez, kuralların ne zaman ve
nasıl işletildiğini belgeler.

## Ne zaman çalışır
- Model üretiminin her turunda, alan türetiminden sonra (deterministik katman).
- Her hedefli değişiklikten sonra, **yalnızca etkilenen tablolar için**
  (sohbet direktifi, öneri uygulama, cevap uygulama).

## Nasıl çalışır
Kurallar her tablo ve alan için **sırayla** işletilir. Bir kuralı geçemeyen alan:

1. modelden çıkarılır (hiç eklenmez),
2. ihlal edilen kural ve nedeniyle birlikte **beklemede** listesine alınır,
3. kullanıcıya sorulacak soruya çevrilir (`skills/README.md` → beklemede akışı).

Otomatik düzeltme yalnızca kural tek bir doğru çözüm tanımlıyorsa yapılır
(ör. PK-2: birincil anahtar zorunlu ve tekil işaretlenir). Çözüm kullanıcı
bilgisi gerektiriyorsa düzeltme yapılmaz, soru sorulur.

## Kuralların nerede işletildiği

| Kural | Nerede | Sonuç |
|---|---|---|
| PK-1 | `CheckPrimaryKey` | Anahtar yoksa tablo beklemede |
| PK-2 | `CheckPrimaryKey` | Otomatik düzeltme: zorunlu + tekil |
| 1NF-4 | `CheckPrimaryKey` | Anahtarla birlikte değerlendirilir |
| AD-1 | `CheckAttribute` | Kaynağı olmayan alan beklemede |
| 1NF-1 | `CheckAttribute` | Bileşik alan beklemede |
| 1NF-2 | `CheckRepeatingGroups` | Telefon1/Telefon2 grubu beklemede |
| 1NF-3 | `CheckAttribute` | Çok değerli alan beklemede |
| 2NF-1, 2NF-2 | `CheckCompositeKey` | Bileşik anahtarlı tabloda sorulur |
| 3NF-1, 3NF-2 | `CheckTransitiveDependencies` | Geçişli bağımlılık beklemede |
| 3NF-3 | `CheckAttribute` | Türetilebilir alan beklemede |
| BCNF-1, BCNF-2 | `CheckPrimaryKey`, `CheckCompositeKey` | Tek sütunlu anahtarda sağlanır, bileşikte sorulur |
| REL-1 | `CheckForeignKeys` | Hedefi olmayan yabancı anahtar beklemede |
| REL-2 | `CheckRelationships` | Kardinalite desteklenmiyorsa beklemede |
| REL-3 | `CheckRelationships` | Ara tablo açılamayan N-M beklemede |
| REL-4 | `CheckAttribute` | Aynı bilgiyi tutan ikinci alan beklemede |

## Çıktı
Her alan için bir satır: tablo, alan, uygulanan kurallar ve sonuç
`GEÇTİ | AYRIŞTIRILDI | ENGELLENDİ`. Engellenen satırda ihlal edilen kural,
neden ve kullanıcıya sorulan soru da bulunur. Bu satırlar izlenebilirlik
tablosundaki **NF durumu** kolonunda görünür (Madde 2).
