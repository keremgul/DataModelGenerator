using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Core.Rules;

/// <summary>
/// Üretilen her modelde geçerli olması gereken yapısal kurallar. Hem LLM'e
/// prompt olarak verilir hem de üretimden sonra kod tarafında denetlenir.
/// </summary>
public static class ModelRules
{
    /// <summary>Prompt'lara gömülen kural metni.</summary>
    public const string PromptText = """
        VERİ MODELİ KURALLARI (zorunlu):
        1. Her tabloda "ID" adında, benzersiz bir birincil anahtar (PK) alanı bulunur. Doğal anahtarlar (müşteri no, kod vb.) PK değil, tekil (unique) alan olarak tutulur.
        2. Modelde ilişkisiz tablo bulunmaz; her tablo en az bir başka tabloyla ilişkilidir.
        3. İlişkiler yabancı anahtar (FK) alanlarıyla kurulur. FK alanının adı, referans verdiği tablonun adı + "ID" şeklindedir (örn. PROJEID → PROJE.ID).
        4. Çoka-çok ilişkiler ara tabloyla kurulur; ara tablonun da kendi "ID" birincil anahtarı ve iki FK alanı olur.

        Örnek:
        - CALISAN (ID PK, AD, SOYAD)
        - PROJE (ID PK, AD, BASLANGICTARIHI)
        - PROJECALISAN (ID PK, PROJEID FK → PROJE.ID, CALISANID FK → CALISAN.ID)
        """;

    /// <summary>Hiçbir ilişkinin ucunda yer almayan varlıklar.</summary>
    public static List<ModelEntity> FindUnrelatedEntities(DataModel model)
    {
        if (model.Entities.Count < 2) return new List<ModelEntity>();

        return model.Entities
            .Where(entity => !model.Relationships.Any(r =>
                IsSame(model, r.FromEntity, entity.TechnicalName) ||
                IsSame(model, r.ToEntity, entity.TechnicalName)))
            .ToList();
    }

    private static bool IsSame(DataModel model, string left, string right)
    {
        var resolved = model.FindEntity(left)?.TechnicalName ?? left;
        return string.Equals(resolved, right, StringComparison.OrdinalIgnoreCase);
    }
}
