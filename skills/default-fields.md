# Skill: Standart (Default) Alanlar

Her tabloya kurumsal standart gereği eklenen alanların **tek kaynağı** bu
dosyadır. Bu alanlar business rule'lardan türemez; kurumsal standarttan gelir.

Dosya hem prompt'lara bağlam olarak gider hem de kod tarafında işletilir:
alanlar modele deterministik olarak eklenir, modelin eklemesi beklenmez.

## Alanlar

| Alan | Anlamı |
|---|---|
| `GIRISTARIHI` | Kaydın oluşturulma tarihi |
| `GIRISKULLANICIKODU` | Kaydı oluşturan kullanıcı |
| `ISLEMTARIHI` | Kaydın son değiştirilme tarihi |
| `ISLEMKULLANICIKODU` | Kaydı son değiştiren kullanıcı |
| `DURUMKODU` | Kaydın durumu (aktif, pasif, silinmiş vb.) |

## Kurallar

- Bu alanlar **her tabloda** bulunur; ara (bağlantı) tablolar dahil.
- Kaynak zorunluluğundan muaftırlar: bir business rule'a bağlı olmaları
  beklenmez, kaynakları kurumsal standarttır (`STD`).
- **Bu alanlar için soru üretilmez.** Varlıkları, zorunlulukları ve anlamları
  standartla belirlidir; kullanıcıya sorulacak bir şey yoktur.
- Normalizasyon kurallarından muaftırlar: `GIRISTARIHI` tarih alanıdır,
  `DURUMKODU` kod alanıdır; bunlar bileşik veya türetilmiş sayılmaz.
- Birincil veya yabancı anahtar değildirler.
- Model bu alanları kendiliğinden üretirse tekrar eklenmez; adı eşleşen alan
  standart alan olarak işaretlenir.

## Modelin yapması gerekenler

Bu alanları **çıktında yazma.** Uygulama bunları her tabloya kendisi ekler.
Senin görevin yalnızca iş kurallarından türeyen alanları üretmek.

Kullanıcı bu alanlardan birini açıkça soruyorsa veya kaldırmak istiyorsa bunu
not olarak bildir; kendiliğinden ekleme veya çıkarma yapma.
