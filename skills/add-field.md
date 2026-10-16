# Skill: Alan Ekle

Mevcut bir tabloya alan ekler. Yalnızca istenen alan eklenir; tabloya
kendiliğinden başka alan, başka tablo veya ilişki eklenmez.

{{SOURCE_RULES}}

{{MODEL_RULES}}

## Çıktı şeması
Yalnızca geçerli bir JSON nesnesi döndür; açıklama veya markdown çiti yazma.
Yalnızca **eklenen alanı** döndürmen yeterlidir: alanlar modele eklenerek
birleştirilir, dönmeyen alanlar olduğu gibi korunur.

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
- Bu işlem silme yapmaz: `removedFields`, `removedEntities` veya
  `removedRelationships` kullanma.
- MODEL içinde zaten var olan alanları tekrar yazmana gerek yok; yazmazsan silinmezler.

## Girdi verisi

### RULES
{{RULES}}

### MODEL
{{MODEL}}

### DIRECTIVE
{{DIRECTIVE}}

### MESSAGE
{{MESSAGE}}
