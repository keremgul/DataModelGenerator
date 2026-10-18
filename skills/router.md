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
| `set-description` | entity, field (tablo açıklamasıysa boş), description | Alanın veya tablonun açıklamasını yaz/değiştir |
| `update-field` | entity, field, required ve/veya unique (true/false) | Alanı zorunlu/opsiyonel veya tekil yap |
| `question` | explanation | Model değişikliği değil, soru ("bu tablo ne işe yarar?", "neden böyle?") |
| `general` | explanation | Yukarıdakilere girmeyen her türlü değişiklik talebi |

## Kurallar
- `entity` ve `field` değerleri MODEL içinde gerçekten var olan adlar olmalı; teknik adı (ör. `MUSTERI`) kullan. Kullanıcı tabloyu doğal adıyla ("müşteri tablosu") yazsa da MODEL'deki karşılığını bul.
- Yeni tablo eklerken (`add-entity`) tablonun adını `newName` alanına yaz; `entity` boş kalır.
- Kullanıcı tek mesajda birden çok şey istiyorsa her birini ayrı direktif olarak yaz.
- **Talebi reddetme.** Kullanıcı modelde bir değişiklik istiyorsa en uygun işlemi seç; hiçbirine tam uymuyorsa `general` kullan ve `explanation` alanına ne yapılacağını açıkça yaz. `general` direktifi modelin tamamı üzerinden uygulanır — "anlaşılmadı" demek yerine onu kullan.
- `unsupported` yalnızca model ile ilgisi olmayan (ör. dosya kaydetme, başka bir sistemi çalıştırma) istekler içindir.
- Kullanıcı yalnızca soru soruyorsa (`?` ile biten veya "nedir, neden, nasıl, hangi" gibi) ve bir değişiklik istemiyorsa `question` kullan; modeli değiştirme.
- Açıklama isteyen talepler ("şu alanın açıklamasını … yap", "açıklamasını sadeleştir") `set-description`'dır; açıklamayı yalın Türkçe yaz.
- Alan/tablo adını değiştirme dışındaki hiçbir şeyi kendiliğinden ekleme; kullanıcı istemediyse alan ekleme.
- `cardinality` değeri yalnızca "1-1", "1-N", "N-1" veya "N-M" olabilir.

## Girdi verisi

### MODEL
{{MODEL}}

### MESSAGE
{{MESSAGE}}
