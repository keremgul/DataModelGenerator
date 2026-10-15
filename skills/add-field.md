# Skill: Alan Ekle

Mevcut bir tabloya alan ekler. Yalnızca istenen alan eklenir; tabloya
kendiliğinden başka alan, başka tablo veya ilişki eklenmez.

{{SOURCE_RULES}}

{{MODEL_RULES}}

## Çıktı şeması
Yalnızca geçerli bir JSON nesnesi döndür; açıklama veya markdown çiti yazma.
Değişen tabloyu **tüm alanlarıyla** döndür — eksik bıraktığın alan silinmez,
ama güncellenmez de.

```json
{
  "entities": [
    {
      "name": "Müşteri",
      "technicalName": "Musteri",
      "definition": "Hizmet alan kişi veya kurum",
      "sources": ["BR-01"],
      "confidence": "kesin",
      "attributes": [
        {
          "name": "ID",
          "isPrimaryKey": true,
          "description": "Birincil anahtar",
          "sources": ["BR-01"],
          "confidence": "kesin"
        },
        {
          "name": "VergiNo",
          "description": "Kurumsal müşterinin vergi numarası",
          "sources": ["{{RULE_ID}}"],
          "confidence": "kesin"
        }
      ]
    }
  ],
  "notes": []
}
```

## Kurallar
- Eklenen alanın `sources` değeri `{{RULE_ID}}` olmalıdır; talebin dayanağı başka
  bir kural da varsa onu da ekle.
- Alanın anlamı talepten net anlaşılmıyorsa ekleme; `notes` içine neyin belirsiz
  kaldığını yaz ve `confidence` değerini `belirsiz` ver.
- Alan tipi veya uzunluğu yazma — model tipsizdir.
- Zaten var olan bir alanı tekrar eklemeye çalışma; adı aynıysa açıklamasını güncelle.
- `removedEntities` / `removedRelationships` kullanmana gerek yok; bu işlem silme yapmaz.

## Girdi verisi

### RULES
{{RULES}}

### MODEL
{{MODEL}}

### DIRECTIVE
{{DIRECTIVE}}

### MESSAGE
{{MESSAGE}}
