# skills/

Düz metin skill dosyaları. Uygulama bunları exe'nin yanındaki `skills\`
klasöründen okur; dosya düzenlenirse uygulama yeniden derlenmeden yeni içerik
geçerli olur. Dosya silinirse exe'ye gömülü kopya kullanılır.

`{{BÜYÜK_HARF}}` yer tutucuları çalışma zamanında doldurulur. `{{MODEL_RULES}}`
ve `{{SOURCE_RULES}}` her dosyada otomatik olarak genişler.

| Dosya | Ne zaman çalışır | LLM |
|---|---|---|
| `analyze-br-relations.md` | Üretimin 1. adımı: kurallar arası semantik ilişki | ✔ |
| `router.md` | Sohbet mesajını yapılandırılmış direktiflere çevirir | ✔ (kalıp eşleşirse hiç çağrılmaz) |
| `add-field.md` | `add-field` direktifi | ✔ |
| `add-entity.md` | `add-entity` direktifi | ✔ |
| `add-relation.md` | `add-relation` direktifi | ✔ |
| `split-entity.md` | `split-entity` direktifi | ✔ |
| `normalize.md` | Referans: normalizasyonun ne zaman ve nasıl işletildiği | ✘ |
| `deterministic-operations.md` | Referans: LLM'siz uygulanan işlemler | ✘ |
| `NORMALIZATION_RULES.md` | Referans: sırayla işletilen normalizasyon kuralları | ✘ |

Referans dosyaları prompt olarak gönderilmez; kodun uyguladığı kuralları
belgeler. Davranışı değiştirmek için kod tarafındaki `NormalizationEngine` ve
`DirectiveExecutor` güncellenmelidir.
