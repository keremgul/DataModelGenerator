# Pipeline

Bu dosya koddaki **gerçek akışı** anlatır. Kaynak:
`DataModelGenerator.Core/Pipeline/ModelPipeline.cs` (`RunAsync`) ve
`PipelineStage.cs`. Uygulamadaki adım göstergesiyle (Madde 4) tutarlıdır:
gösterge kullanıcı adımlarını, buradaki diyagramlar bu adımların altında çalışan
pipeline adımlarını gösterir.

## Kullanıcı adımları ↔ pipeline adımları

Üstteki adım göstergesindeki 6 kullanıcı adımı ile 8 pipeline adımının ilişkisi:

| Adım göstergesi | Tetiklediği pipeline çağrısı | Çalışan pipeline adımları |
|---|---|---|
| 1. Bağlantı | — | — |
| 2. Girdi | — | — |
| 3. Üretim | `GenerateAsync` | 1 → 8 (hepsi) |
| 3. Üretim (öneri uygula) | `ApplySuggestionsAsync` → `RepromptAsync` | 5 → 8 |
| 4. Sorular | `ApplyAnswersAsync` | etkilenen adımdan → 8 |
| 4. Sorular (yeni soru) | `RequestQuestionsAsync` | 6 → 8 |
| 5. Sohbet | `SendChatAsync` | yalnızca etkilenen parça + 8 |
| 6. Dışa Aktar | — (dosya yazımı) | — |

## Eski akış (bu iyileştirmelerden önce)

7 adım, kural ilişki analizi yok, normalizasyon yalnızca LLM'e sorulan bir
denetimdi ve modele giren öğe için kaynak zorunluluğu yoktu.

```mermaid
flowchart TD
    IN["Girdi: konu + kapsam + BR listesi"] --> S1

    S1["1 · Varlık çıkarımı<br/>girdi: BR listesi<br/>çıktı: varlık adayları"]:::llm
    S1 --> S2["2 · İlişki ve kardinalite<br/>girdi: varlıklar + BR<br/>çıktı: ilişkiler"]:::llm
    S2 --> S3["3 · Alan ve tip türetimi<br/>girdi: varlıklar + ilişkiler<br/>çıktı: alanlar + tipler"]:::llm
    S3 --> D1["Deterministik katman<br/>adlandırma · FK · doğrulama"]:::code
    D1 --> S4["4 · Normalizasyon denetimi<br/>girdi: model<br/>çıktı: öneri niteliğinde notlar"]:::llm
    S4 --> S5["5 · Belirsizlik tespiti<br/>girdi: model + BR<br/>çıktı: belirsizlikler"]:::llm
    S5 --> S6["6 · Soru üretimi<br/>girdi: belirsizlikler<br/>çıktı: sorular"]:::llm
    S6 --> S7["7 · Çıktı üretimi<br/>girdi: model<br/>çıktı: özet · öneri · Mermaid · JSON"]:::llm

    S7 --> OUT["ModelPackage"]
    OUT --> RP["Serbest reprompt<br/>sonuç doğrulanmaz"]:::llm
    RP --> D1

    classDef llm fill:#E8EFFB,stroke:#2F5496,color:#1F3864
    classDef code fill:#EBF5E8,stroke:#6C8256,color:#2C4218
```

**Eski akışın zayıf noktaları**
- Kurallar birbirinden bağımsız işleniyordu; çelişki ve tekrar fark edilmiyordu.
- Modelin ürettiği her şey modele giriyordu: kaynak zorunluluğu yoktu.
- Normalizasyon LLM'e soruluyordu; ihlal eden alan yine de modelde kalıyordu.
- Reprompt sonucunun gerçekten uygulanıp uygulanmadığı denetlenmiyordu.

## Yeni akış

8 adım. Başa kural ilişki analizi eklendi; her LLM çıktısı modele girmeden önce
kod tarafında süzülüyor; normalizasyon deterministik olarak işletiliyor.

```mermaid
flowchart TD
    IN["Girdi: numaralı BR listesi<br/>(konu/kapsam kaldırıldı)"] --> S1

    S1["1 · Kural ilişki analizi<br/>girdi: BR listesi<br/>çıktı: BR çiftleri arası ilişki<br/>çelişki ve tekrar → soru"]:::llm
    S1 --> S2["2 · Varlık çıkarımı<br/>girdi: BR + ilişki grafiği<br/>çıktı: varlık adayları + sources + confidence"]:::llm
    S2 --> G1{"SourceGuard<br/>kaynak var mı? kesin mi?"}:::guard
    G1 -- hayır --> BEK[("Beklemede listesi<br/>+ otomatik soru")]:::pending
    G1 -- evet --> S3

    S3["3 · İlişki ve kardinalite<br/>girdi: varlıklar + BR<br/>çıktı: ilişkiler"]:::llm
    S3 --> G2{"SourceGuard<br/>kardinalite kesin mi?"}:::guard
    G2 -- hayır --> BEK
    G2 -- evet --> S4

    S4["4 · Alan türetimi<br/>girdi: varlıklar + ilişkiler + bağlayıcı cevaplar<br/>çıktı: alanlar (tipsiz)"]:::llm
    S4 --> G3{"SourceGuard"}:::guard
    G3 -- hayır --> BEK
    G3 -- evet --> D1

    D1["Deterministik katman<br/>RuleEngine · ara tablo · FK senkronu<br/>NormalizationEngine (NF kuralları sırayla)<br/>ModelValidator"]:::code
    D1 -- "NF'i geçemeyen alan" --> BEK
    D1 --> S5["5 · Normalizasyon denetimi<br/>girdi: model<br/>çıktı: ek notlar (tavsiye)"]:::llm
    S5 --> S6["6 · Belirsizlik tespiti<br/>girdi: model + BR<br/>çıktı: belirsizlikler"]:::llm
    S6 --> S7

    S7["7 · Soru üretimi"]:::llm
    BEK --> P1["Beklemede → soru<br/>LLM yok"]:::code
    P1 --> S7
    S7 --> S8["8 · Çıktı üretimi<br/>özet · öneri · Mermaid · JSON<br/>+ cevap uyum kontrolü"]:::llm

    S8 --> OUT["ModelPackage<br/>model · izlenebilirlik · beklemede · doğrulama"]

    classDef llm fill:#E8EFFB,stroke:#2F5496,color:#1F3864
    classDef code fill:#EBF5E8,stroke:#6C8256,color:#2C4218
    classDef guard fill:#FFF8E1,stroke:#E0C97F,color:#5F4B00
    classDef pending fill:#FDECEA,stroke:#F0B4AE,color:#8C1D18
```

### Adım tablosu

| # | Adım | LLM | Girdi | Çıktı |
|---|---|---|---|---|
| 1 | Kural ilişki analizi | ✔ `skills/analyze-br-relations.md` | BR listesi (2+ kural) | `RuleRelation[]`; `çelişir`/`tekrar` → beklemede + soru |
| 2 | Varlık çıkarımı | ✔ `extraction.txt` | BR + ilişki grafiği | varlık adayları (`sources`, `confidence`) |
| 3 | İlişki ve kardinalite | ✔ `relationships.txt` | varlıklar + BR | ilişkiler + kardinalite |
| 4 | Alan türetimi | ✔ `attributes.txt` | varlıklar + ilişkiler + bağlayıcı cevaplar | alanlar (tipsiz) |
| 5 | Normalizasyon denetimi | ✔ `normalization.txt` | model | tavsiye niteliğinde notlar |
| 6 | Belirsizlik tespiti | ✔ `gaps.txt` | model + BR | `Ambiguity[]` |
| 7 | Soru üretimi | ✔ `questions.txt` (+ beklemede → soru, LLM'siz) | belirsizlikler + beklemede listesi | `ClarifyingQuestion[]` |
| 8 | Çıktı üretimi | ✔ `summary.txt` | model + cevaplar | özet, öneriler, Mermaid, JSON, cevap uyumu |

Adım 7 yalnızca ilk üretimde ve kullanıcı "Yeni Sorular Üret" dediğinde çalışır;
cevap uygulandığında kendiliğinden tetiklenmez (sonsuz soru döngüsünü önler).

### Her adımdan sonra çalışan deterministik katman (LLM yok)

`ApplyDeterministicLayer` sırayla:

1. `RuleEngine.Normalize` — teknik adlandırma, PK/FK tamamlama.
2. `ModelMerger.CreateJunctionTables` — N-M ilişkiler için ara tablo.
3. `ModelMerger.SyncForeignKeys` + `RuleEngine.RelinkReferences`.
4. `NormalizationEngine.Apply` — `skills/NORMALIZATION_RULES.md` kurallarını
   sırayla işletir; geçemeyen alan **modelden çıkarılır** ve beklemede listesine alınır.
5. `ModelValidator.Validate`.

## Sohbet akışı (hedefli değişiklik)

Sohbetten gelen bir talep için tüm pipeline baştan çalışmaz.

```mermaid
flowchart TD
    M["Kullanıcı mesajı"] --> R1{"Kalıp eşleşmesi<br/>DirectivePatterns"}:::code
    R1 -- "eşleşti" --> DIR["Direktif listesi"]
    R1 -- "eşleşmedi" --> R2["Router skill<br/>skills/router.md"]:::llm
    R2 --> DIR

    DIR --> Q{"Direktif deterministik mi?"}:::guard
    Q -- "evet<br/>rename · remove · kardinalite" --> EX["DirectiveExecutor<br/>doğrudan model üzerinde"]:::code
    Q -- "hayır<br/>add-field · add-entity<br/>add-relation · split-entity" --> SK["İşleme ait skill<br/>yalnızca ilgili varlıkların context'i"]:::llm
    Q -- "skill yok / genel talep" --> RP["reprompt.txt"]:::llm

    EX --> ND["Etkilenen tablolar için<br/>normalizasyon + doğrulama"]:::code
    SK --> ND
    RP --> ND

    ND --> DF{"ModelChangeSet.Compare<br/>önceki ↔ sonraki model"}:::guard
    DF -- "fark yok" --> NA["✗ Direktif uygulanmadı<br/>+ nedeni"]:::pending
    DF -- "bazı direktifler başarısız" --> PA["⚠ Kısmen uygulandı<br/>uygulanan/uygulanmayan ayrı listelenir"]:::pending
    DF -- "fark var" --> OK["✓ Uygulandı<br/>gerçek diff gösterilir"]:::code

    NA --> CH
    PA --> CH
    OK --> CH["skills/chat.md<br/>girdi: gerçek diff + uygulanmayanlar<br/>+ güncel model + beklemede<br/>çıktı: yanıt + öneriler"]:::llm
    CH --> SG["Öneriler teklif olarak gösterilir<br/>kullanıcı istemeden uygulanmaz"]:::code

    classDef llm fill:#E8EFFB,stroke:#2F5496,color:#1F3864
    classDef code fill:#EBF5E8,stroke:#6C8256,color:#2C4218
    classDef guard fill:#FFF8E1,stroke:#E0C97F,color:#5F4B00
    classDef pending fill:#FDECEA,stroke:#F0B4AE,color:#8C1D18
```

Mesaj kural listesine `CHAT-xx` olarak eklenir; o turda modele giren her öğe bu
kimliği kaynak olarak taşır ve izlenebilirlik tablosunda görünür.

### Alanlar neden baştan yazılmaz

Model yanıtı çoğu zaman yalnızca **değişen** alanı içerir. Gelen listeyi varlığın
yeni tam hali saymak, daha önce eklenmiş alanların sessizce kaybolmasına yol
açıyordu — sohbetin kendi eklediği alanı bir sonraki turda bulamamasının nedeni
buydu. Artık alanlar `ModelMerger.MergeAttributes` ile toplanarak birleştirilir;
silme yalnızca modelin açıkça bildirdiği `removedFields` listesiyle yapılır ve
birincil anahtar bu listede olsa bile kaldırılmaz. Aynı ilke varlık ve
ilişkilerde zaten geçerliydi.

Router'a giden model özeti her alanı **hem görünen hem teknik adıyla** ve PK/FK
işaretleriyle verir; kullanıcı alana hangi adla atıfta bulunursa bulunsun
eşleşme sağlanır.

## Kısmi yeniden çalıştırma

Bir cevap uygulandığında pipeline baştan değil, belirsizliğin türüne göre
belirlenen adımdan itibaren çalışır (`PipelineStageMap.AffectedStage`):

| Belirsizlik türü | Yeniden başlanan adım |
|---|---|
| `EntityExistence` | 2 · Varlık çıkarımı |
| `Cardinality` | 3 · İlişki ve kardinalite |
| `PrimaryKey`, `ForeignKey`, `DataType`, `Requiredness` | 4 · Alan türetimi |
| `Naming`, diğer | 5 · Normalizasyon denetimi |

Adım 2 veya 3'ten başlanıyorsa kapsam temizlenir ve model bütün olarak
değerlendirilir; daha sonraki adımlardan başlanıyorsa yalnızca etkilenen
varlıklar LLM'e gönderilir.
