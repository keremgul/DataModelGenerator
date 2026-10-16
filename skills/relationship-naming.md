# Skill: İlişki Kurma ve Yabancı Anahtar Adlandırma

İlişki kurma ve yabancı anahtar adlandırma kurallarının **tek kaynağı** bu
dosyadır. Model üretimi, soru üretimi ve sohbet yanıtları bu dosyayı bağlam
olarak kullanır. Kural değişecekse burası değişir.

{{NAMING_RULES}}

## Temel kural

1. **Her tablonun birincil anahtarı `ID` adlı alandır.** Doğal anahtarlar
   (müşteri no, kod, vergi no…) birincil anahtar değildir; tekil (unique) alan
   olarak tutulur.
2. **Başka bir tabloya referans veren alan, referans verilen tablonun adı ile
   `ID` birleştirilerek adlandırılır: `<TABLOADI>ID`.**
   Örnek: `SIPARIS.MUSTERIID` → `MUSTERI.ID`
3. **Bu adlandırma ilişkinin kendisidir.** `SIPARIS.MUSTERIID` alanı varsa
   `MUSTERI` ile `SIPARIS` arasındaki ilişki zaten tanımlıdır. Bu ilişki için
   ayrı bir ilişki tanımı, ara tablo ya da ek alan **üretme**.

## Aynı tabloya birden fazla referans

Bir tablo aynı tabloya birden çok kez referans veriyorsa ayrım **rol önekiyle**
yapılır: `<ROL><TABLOADI>ID`.

Örnek — `TRANSFER` tablosunda hem gönderen hem alıcı müşteri varsa:

```
TRANSFER
  ID                   PK
  GONDERENMUSTERIID    FK → MUSTERI.ID
  ALICIMUSTERIID       FK → MUSTERI.ID
  TUTAR
```

Rol öneki alanın iş anlamını taşımalıdır (GONDEREN, ALICI, ONAYLAYAN, ÜST…).
Rol öneki olmadan aynı tabloya iki alan açma; `MUSTERIID` ve `MUSTERIID2` gibi
numaralandırma yapma.

## Çoka-çok ilişkiler

Ara (bağlantı) tablo **yalnızca gerçek N-N ilişkilerde** kurulur ve aynı
standarda uyar: kendi `ID` birincil anahtarı ve her iki tarafa birer
`<TABLOADI>ID` alanı.

```
SIPARISURUN
  ID          PK
  SIPARISID   FK → SIPARIS.ID
  URUNID      FK → URUN.ID
  ADET
```

1-N ilişki için ara tablo kurma; yabancı anahtar N tarafındaki tabloya konur.

## Kod tarafında yapılan doğrulama

Bu kurallar prompt'a yazılır ama onlara güvenilmez. `RelationshipRules` her
turda şunları yapar:

- `<TABLOADI>ID` kalıbına uyan alan, hedef tablo modelde varsa **otomatik olarak
  yabancı anahtar sayılır** ve ilişki kurulur — model ayrıca ilişki tanımı
  döndürmese bile.
- Rol önekli alanlar (`GONDERENMUSTERIID`) çözümlenir; rol, ilişkinin adı olur.
- **Mükerrer ilişki tanımları elenir.** Tekilleştirme `(kaynak, hedef, yabancı
  anahtar alanı)` üçlüsüne göre yapılır — salt tablo çiftine göre değil; çünkü
  aynı tablo çifti arasında farklı rollerle birden çok ilişki meşrudur
  (`TRANSFER` → `MUSTERI` iki kez).
- Referans verilen tablo ne modelde ne de yüklenen mevcut yapı dosyasında yoksa
  **kullanıcıya uyarı verilir**; alan sessizce silinmez.

## Model çıktısında ne yazmalısın

İlişkiyi kurmak için yabancı anahtar alanını tanımlaman yeterlidir:

```json
{
  "name": "MUSTERIID",
  "isForeignKey": true,
  "referencesEntity": "MUSTERI",
  "referencesAttribute": "ID",
  "sources": ["BR-02"],
  "confidence": "kesin"
}
```

Aynı ilişki için ayrıca `relationships` altında bir kayıt yazmana gerek yoktur;
yazarsan da kod tarafı mükerrer kaydı eler. Kardinaliteyi kurallardan kesin
çıkaramıyorsan `confidence` değerini `belirsiz` ver — belirsiz ilişki modele
girmez, kullanıcıya soru olarak döner.
