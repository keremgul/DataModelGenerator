# Skill: Tabloyu Ayrıştır

Bir tablonun belirtilen alanlarını yeni bir tabloya taşır ve aralarındaki
ilişkiyi kurar. Normalizasyon ihlallerinin (1NF/2NF/3NF) tipik çözümü budur.

{{SOURCE_RULES}}

{{MODEL_RULES}}

{{RELATION_RULES}}

## Çıktı şeması
Yalnızca geçerli bir JSON nesnesi döndür; açıklama veya markdown çiti yazma.
Hem kaynak tabloyu (taşınan alanlar çıkarılmış hâliyle) hem yeni tabloyu döndür.

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
        { "name": "ID", "isPrimaryKey": true, "sources": ["BR-01"], "confidence": "kesin" },
        { "name": "MusteriAdi", "sources": ["BR-01"], "confidence": "kesin" }
      ]
    },
    {
      "name": "Müşteri Adresi",
      "technicalName": "MusteriAdresi",
      "definition": "Müşterinin adres bilgileri",
      "sources": ["{{RULE_ID}}"],
      "confidence": "kesin",
      "attributes": [
        { "name": "ID", "isPrimaryKey": true, "sources": ["{{RULE_ID}}"], "confidence": "kesin" },
        {
          "name": "MusteriID",
          "isForeignKey": true,
          "referencesEntity": "Musteri",
          "referencesAttribute": "ID",
          "sources": ["{{RULE_ID}}"],
          "confidence": "kesin"
        },
        { "name": "Il", "sources": ["BR-02"], "confidence": "kesin" }
      ]
    }
  ],
  "relationships": [
    {
      "fromEntity": "Musteri",
      "toEntity": "MusteriAdresi",
      "cardinality": "1-N",
      "sources": ["{{RULE_ID}}"],
      "confidence": "kesin"
    }
  ],
  "notes": []
}
```

## Kurallar
- Taşınan alanların `sources` değerlerini **koru**; alan yeni tabloya geçerken
  kaynağını kaybetmez.
- Yeni tabloda `ID` birincil anahtarı ve kaynak tabloya işaret eden yabancı
  anahtar bulunmak zorundadır.
- Kaynak tablodan yalnızca `FIELDS` listesindeki alanlar çıkarılır; diğerleri
  olduğu gibi kalır.
- Birincil anahtarı taşımaya çalışma.
- Kaynak tabloyu `removedEntities` ile silmeye çalışma; alanları çıkarılmış hâli
  `entities` içinde döner.

## Girdi verisi

### RULES
{{RULES}}

### MODEL
{{MODEL}}

### DIRECTIVE
{{DIRECTIVE}}

### FIELDS
{{FIELDS}}

### MESSAGE
{{MESSAGE}}
