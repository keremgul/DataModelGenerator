# Skill: Direktif Yönlendirici (Router)

Kullanıcının sohbete yazdığı mesajı, model üzerinde yapılacak **yapılandırılmış
bir değişiklik listesine** çevirir. Amaç, tüm pipeline'ı baştan çalıştırmadan
yalnızca etkilenen parçayı güncellemektir.

## Girdi
- `MESSAGE`: kullanıcının mesajı.
- `MODEL`: mevcut modelin tablo ve alan adları.

## Çıktı şeması
Yalnızca geçerli bir JSON nesnesi döndür; açıklama veya markdown çiti yazma.

```json
{
  "directives": [
    {
      "operation": "rename-field",
      "entity": "Müşteri",
      "field": "AdSoyad",
      "newName": "MusteriAdi",
      "explanation": "Kullanıcı alan adının değişmesini istedi"
    }
  ],
  "unsupported": []
}
```

## Desteklenen işlemler (`operation`)
| operation | gerekli alanlar | anlamı |
|---|---|---|
| `rename-field` | entity, field, newName | Alan adını değiştir |
| `add-field` | entity, newName | Tabloya alan ekle |
| `remove-field` | entity, field | Alanı kaldır |
| `rename-entity` | entity, newName | Tablo adını değiştir |
| `add-entity` | newName | Yeni tablo ekle |
| `remove-entity` | entity | Tabloyu kaldır |
| `add-relation` | entity, targetEntity, cardinality | İlişki ekle |
| `remove-relation` | entity, targetEntity | İlişkiyi kaldır |
| `change-cardinality` | entity, targetEntity, cardinality | Kardinaliteyi değiştir |
| `split-entity` | entity, fields[], newName | Alanları yeni tabloya taşı |
| `general` | explanation | Yukarıdakilere girmeyen, modelin tamamını ilgilendiren talep |

## Kurallar
- `entity` ve `field` değerleri MODEL içinde gerçekten var olan adlar olmalı. Var olmayan bir ada atıfta bulunma.
- Kullanıcı tek mesajda birden çok şey istiyorsa her birini ayrı direktif olarak yaz.
- Mesajın bir kısmını anlamadıysan veya desteklenen işlemlere girmiyorsa `unsupported` dizisine o kısmı yaz; uydurma.
- Alan/tablo adını değiştirme dışındaki hiçbir şeyi kendiliğinden ekleme; kullanıcı istemediyse alan ekleme.
- `cardinality` değeri yalnızca "1-1", "1-N", "N-1" veya "N-M" olabilir.

## Girdi verisi

### MODEL
{{MODEL}}

### MESSAGE
{{MESSAGE}}
