# Skill: Alan Açıklamaları

Modeldeki alanlar için **yalın Türkçe açıklama** yazar. Açıklamalar ER
diyagramında alanın yanında gösterilir; okuyan kişi veri modelini bilmeyen bir
iş birimi çalışanı olabilir.

Bu skill modelin yapısını değiştirmez: alan eklemez, silmez, adını değiştirmez.
Yalnızca `FIELDS` listesindeki alanlar için açıklama döndürür.

## Açıklama kuralları
- Tek kısa cümle, en fazla ~12 kelime.
- Alanın **ne tuttuğunu** anlat: "Müşterinin vergi dairesine kayıtlı numarası".
- Teknik terim kullanma: PK, FK, ID, varchar, nullable, tablo, kayıt anahtarı gibi
  ifadeler yazma.
- Alan adını tekrar etme ("VERGINO alanı vergi numarasıdır" gibi).
- Kurallarda (`RULES`) alanla ilgili bir kısıt geçiyorsa (ör. "boş olamaz",
  "her müşteride farklı") bunu açıklamaya yansıtabilirsin.
- Anlamını kestiremediğin alan için en makul açıklamayı yaz; boş bırakma.

## Çıktı şeması
Yalnızca geçerli bir JSON nesnesi döndür; açıklama veya markdown çiti yazma.

```json
{
  "descriptions": [
    { "entity": "MUSTERI", "field": "VERGINO", "description": "Müşterinin vergi dairesine kayıtlı numarası" }
  ]
}
```

`entity` ve `field` değerlerini `FIELDS` listesinde yazdığı gibi (teknik adla) kullan.

## Girdi verisi

### RULES
{{RULES}}

### FIELDS
{{FIELDS}}
