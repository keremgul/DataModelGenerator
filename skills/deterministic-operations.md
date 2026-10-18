# Kod Tarafında Çalışan İşlemler (LLM'siz)

Aşağıdaki işlemler için **model çağrısı yapılmaz**. Talep yapılandırılmış bir
direktife çevrildikten sonra `DirectiveExecutor` doğrudan model üzerinde
uygular; pipeline baştan çalışmaz, yalnızca etkilenen tablolar yeniden
normalize edilir ve model bütün olarak doğrulanır.

Bu dosya referanstır: içeriği prompt olarak gönderilmez, kodun uyguladığı
kuralları belgeler.

## rename-field — alan adını değiştir
- Alan bulunamazsa işlem uygulanmaz, gerekçe kullanıcıya yazılır.
- Birincil anahtar adı değiştirilemez; model kuralı gereği `ID` olmak zorundadır.
- Hedef ad aynı tabloda zaten varsa işlem uygulanmaz.
- Yeni ad teknik adlandırma kurallarından geçirilir (Türkçe karakter sadeleştirme, PascalCase).
- Bu alana işaret eden yabancı anahtarların `referencesAttribute` değeri de güncellenir.
- Alanın kaynak listesine `CHAT-xx` eklenir.

## remove-field — alanı kaldır
- Birincil anahtar kaldırılamaz; her tabloda `ID` bulunmak zorundadır.
- Alan bulunamazsa işlem uygulanmaz.

## rename-entity — tablo adını değiştir
- Hedef ad başka bir tabloda kullanılıyorsa işlem uygulanmaz.
- İlişkilerdeki `fromEntity` / `toEntity` değerleri güncellenir.
- Bu tabloya işaret eden yabancı anahtarların `referencesEntity` değeri güncellenir;
  adı varsayılan kalıpta (`<EskiTablo>ID`) ise yeni kalıba (`<YeniTablo>ID`) çevrilir.
- Tablonun kaynak listesine `CHAT-xx` eklenir.

## remove-entity — tabloyu kaldır
- Tablo, ona bağlı ilişkiler ve ona işaret eden yabancı anahtarlar birlikte kaldırılır.
- Komşu tablolar "etkilenen" sayılır ve yeniden normalize edilir.

## remove-relation — ilişkiyi kaldır
- İlişki iki yönde de aranır; bulunamazsa işlem uygulanmaz.
- İlişkiyle birlikte yabancı anahtar da temizlenir.

## set-description — alan veya tablo açıklamasını yaz
- `field` boşsa açıklama tablonun kendisine yazılır.
- Kalıp eşleşmesiyle de çalışır: "Sipariş tablosundaki Tutar alanının açıklaması 'Siparişin toplam tutarı' olsun".
- Yazılan açıklama sonraki turlarda üzerine yazılmaz; otomatik açıklama yalnızca boş alanları doldurur.

## update-field — zorunluluk / tekillik
- `required` ve/veya `unique` değeri verilir; verilmeyen özellik değişmez.

## change-cardinality — kardinaliteyi değiştir
- Yeni kardinalite `1-1`, `1-N`, `N-1` veya `N-M` olmalıdır.
- `N-M`'ye dönen ilişki için ara tablo kod tarafında otomatik oluşturulur.
- İlişkinin kaynak listesine `CHAT-xx` eklenir.

## Doğrulama
Her işlemden sonra sonuç, modelin **önceki ve sonraki hâli arasındaki gerçek
diff**'e bakılarak doğrulanır. Diff boşsa işlem "uygulanmadı" olarak raporlanır;
bazı direktifler başarısız olduysa "kısmen uygulandı" denir. Modelin veya
kodun "yaptım" beyanı ölçüt değildir.
