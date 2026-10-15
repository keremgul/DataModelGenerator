# Skill: Tablo Ekle

Modele yeni bir tablo ekler. Yalnızca istenen tablo eklenir; "bunun yanında
şu da gerekir" türü kendiliğinden eklemeler yapılmaz.

{{SOURCE_RULES}}

{{MODEL_RULES}}

## Çıktı şeması
Yalnızca geçerli bir JSON nesnesi döndür; açıklama veya markdown çiti yazma.

```json
{
  "entities": [
    {
      "name": "Fatura",
      "technicalName": "Fatura",
      "definition": "Müşteriye kesilen ödeme belgesi",
      "sources": ["{{RULE_ID}}"],
      "confidence": "kesin",
      "attributes": [
        {
          "name": "ID",
          "isPrimaryKey": true,
          "description": "Birincil anahtar",
          "sources": ["{{RULE_ID}}"],
          "confidence": "kesin"
        }
      ]
    }
  ],
  "relationships": [],
  "notes": []
}
```

## Kurallar
- Her tabloda `ID` adlı birincil anahtar bulunmak zorundadır.
- Yalnızca talepten veya kurallardan dayanağı olan alanları ekle. Dayanağı olmayan
  bir alanı ekleme; `notes` içine "şu alan sorulmalı" diye yaz.
- Alan tipi veya uzunluğu yazma — model tipsizdir.
- İlişki yalnızca kullanıcı açıkça istediyse eklenir. İlişki eklenirse yabancı
  anahtar adı hedef tablo adı + `ID` olur.
- Aynı anlamda bir tablo modelde zaten varsa yeni tablo açma; `notes` içine
  hangi tabloyla çakıştığını yaz.

## Girdi verisi

### RULES
{{RULES}}

### MODEL
{{MODEL}}

### DIRECTIVE
{{DIRECTIVE}}

### MESSAGE
{{MESSAGE}}
