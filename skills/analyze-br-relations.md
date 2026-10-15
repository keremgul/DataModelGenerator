# Skill: Business Rule İlişki Analizi

Business rule'lar kullanıcıdan satır satır alınır, ama anlamları birbirinden
bağımsız değildir. Bu skill her kural çiftinin semantik ilişkisini belirler;
model üretimi kuralları tek tek değil, bu ilişki grafiğiyle birlikte işler.

## Girdi
- `RULES`: `BR-01: <metin>` biçiminde numaralı kural listesi.

## Çıktı şeması
Yalnızca geçerli bir JSON nesnesi döndür; açıklama veya markdown çiti yazma.

```json
{
  "relations": [
    {
      "first": "BR-01",
      "second": "BR-03",
      "kind": "ayni-entity",
      "explanation": "İkisi de Müşteri tablosunu tanımlıyor"
    }
  ]
}
```

## İlişki tipleri (`kind`)
- `ayni-entity` — iki kural aynı varlığı tanımlıyor; birlikte değerlendirilmeli, tek varlık üretilmeli.
- `detaylandirir` — biri diğerinin ayrıntısını veriyor (alan, kısıt, istisna).
- `bagimli` — biri diğerinin önkoşulu; sırası önemli.
- `celisir` — iki kural aynı konuda birbirine zıt şey söylüyor.
- `tekrar` — aynı bilgiyi iki kez söylüyorlar.
- `iliskisiz` — anlamlı bir bağ yok.

## Kurallar
- Yalnızca gerçekten ilişkili çiftleri döndür; `iliskisiz` olanları listeleme.
- `celisir` ve `tekrar` tespit ettiğinde `explanation` alanında hangi noktada
  çeliştiklerini veya neyin tekrarlandığını açıkça yaz. Bu iki tip kullanıcıya
  soru olarak döner ve çözülmeden modele yansımaz.
- Emin olmadığın bir ilişkiyi uydurma; şüphedeysen o çifti listeleme.
- Bir kural birden çok kuralla ilişkili olabilir; her çifti ayrı satır olarak yaz.

## Girdi verisi

### RULES
{{RULES}}
