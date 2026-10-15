namespace DataModelGenerator.Core.Rules;

/// <summary>
/// Her prompt'a gömülen kaynak/kesinlik sözleşmesi. Kod tarafındaki
/// <see cref="SourceGuard"/> ile aynı kuralı anlatır; ikisi birlikte çalışır.
/// </summary>
public static class SourcePolicy
{
    public const string PromptText = """
        KAYNAK VE KESİNLİK ZORUNLULUĞU (halüsinasyon yasağı):
        - Ürettiğin HER varlık, alan ve ilişki en az bir kaynağa bağlı olmalıdır: bir business rule (BR-xx), bir soru cevabı (Q-xx) veya bir sohbet direktifi (CHAT-xx).
        - Kaynağı olmayan hiçbir şey üretme. Emin olmadığın bir şeyi "olabilir" diye ekleme.
        - Her öğede "sources" alanı zorunludur ve yalnızca yukarıdaki listede GERÇEKTEN var olan kimlikleri içerebilir. Var olmayan bir kimlik uydurma.
        - Her öğede "confidence" alanı zorunludur: "kesin" veya "belirsiz".
        - "belirsiz" işaretlediğin öğe modele EKLENMEZ; bunun yerine kullanıcıya soru sorulur. Emin değilsen "belirsiz" yaz — uydurma.
        - "confidenceReason" alanında neden kesin/belirsiz olduğunu kısaca yaz.
        """;
}
