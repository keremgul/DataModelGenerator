# Skill: İlişki Ekle

İki mevcut tablo arasına ilişki kurar ve gereken yabancı anahtarı tanımlar.

{{SOURCE_RULES}}

{{MODEL_RULES}}

## Çıktı şeması
Yalnızca geçerli bir JSON nesnesi döndür; açıklama veya markdown çiti yazma.

```json
{
  "entities": [],
  "relationships": [
    {
      "fromEntity": "Musteri",
      "toEntity": "Siparis",
      "cardinality": "1-N",
      "description": "Bir müşterinin birden çok siparişi olur",
      "sources": ["{{RULE_ID}}"],
      "confidence": "kesin"
    }
  ],
  "notes": []
}
```

## Kurallar
- `cardinality` yalnızca `1-1`, `1-N`, `N-1` veya `N-M` olabilir.
- Kardinalite talepten net anlaşılmıyorsa uydurma: `confidence` değerini
  `belirsiz` ver ve `notes` içine neyin sorulması gerektiğini yaz. Belirsiz
  ilişki modele girmez, soruya dönüşür.
- `1-N` ilişkide yabancı anahtar **N** tarafındaki tabloda yer alır ve adı hedef
  tablo adı + `ID` olur (`MusteriID`). Bu alanı ilgili tablonun `attributes`
  listesine `isForeignKey: true`, `referencesEntity`, `referencesAttribute: "ID"`
  ile ekle.
- `N-M` ilişkide yabancı anahtar eklemeye çalışma; ara tablo kod tarafında
  otomatik oluşturulur.
- Tabloları yeniden tanımlamak zorunda değilsin; yalnızca yabancı anahtar
  eklenen tabloyu tüm alanlarıyla döndür.

## Girdi verisi

### RULES
{{RULES}}

### MODEL
{{MODEL}}

### DIRECTIVE
{{DIRECTIVE}}

### MESSAGE
{{MESSAGE}}
