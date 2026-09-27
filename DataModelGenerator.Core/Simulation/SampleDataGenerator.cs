using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Rules;

namespace DataModelGenerator.Core.Simulation;

public class SampleTable
{
    public string EntityName { get; set; } = string.Empty;
    public string TechnicalName { get; set; } = string.Empty;
    public List<EntityAttribute> Columns { get; set; } = new();
    public List<List<object?>> Rows { get; set; } = new();
}

public class SampleDataSet
{
    public List<SampleTable> Tables { get; set; } = new();
    public int RowCount { get; set; }
}

/// <summary>
/// Kavramsal modele uygun, ilişki ve kısıtları bozmayan sentetik veri üretir:
/// yabancı anahtar değerleri daima gerçekten var olan birincil anahtarlara referans verir.
/// Satır sayısında sabit bir üst sınır yoktur; kullanıcının verdiği değer kullanılır.
/// </summary>
public class SampleDataGenerator
{
    private static readonly string[] FirstNames =
        ["Ahmet", "Ayşe", "Mehmet", "Zeynep", "Mustafa", "Elif", "Emre", "Fatma", "Burak", "Deniz",
         "Kerem", "Selin", "Onur", "Merve", "Cem", "Ece", "Barış", "Gizem", "Serkan", "Pınar"];

    private static readonly string[] LastNames =
        ["Yılmaz", "Kaya", "Demir", "Çelik", "Şahin", "Öztürk", "Aydın", "Arslan", "Doğan", "Kılıç",
         "Aksoy", "Erdoğan", "Koç", "Polat", "Güneş"];

    private static readonly string[] Cities =
        ["İstanbul", "Ankara", "İzmir", "Bursa", "Antalya", "Adana", "Konya", "Gaziantep", "Kayseri", "Eskişehir"];

    private static readonly string[] Statuses =
        ["Aktif", "Pasif", "Beklemede", "Onaylandı", "İptal", "Tamamlandı"];

    private static readonly string[] Currencies = ["TRY", "USD", "EUR", "GBP"];

    private static readonly string[] Products =
        ["Vadesiz Hesap", "Vadeli Mevduat", "Kredi Kartı", "İhtiyaç Kredisi", "Konut Kredisi",
         "Yatırım Fonu", "Sigorta Poliçesi", "Otomatik Ödeme"];

    private readonly Random _random;

    public SampleDataGenerator(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public SampleDataSet Generate(DataModel model, int rowCount)
    {
        if (rowCount < 1) rowCount = 1;

        var dataSet = new SampleDataSet { RowCount = rowCount };
        var primaryKeyValues = new Dictionary<string, Dictionary<string, List<object?>>>(StringComparer.OrdinalIgnoreCase);

        // 1. geçiş — birincil anahtarlar. Döngüsel FK'lerde de sıralama sorunu yaşanmaması için
        // tüm anahtarlar, yabancı anahtarlar doldurulmadan önce üretilir.
        foreach (var entity in model.Entities)
        {
            var table = new SampleTable
            {
                EntityName = entity.Name,
                TechnicalName = entity.TechnicalName,
                Columns = entity.Attributes.ToList()
            };

            for (var i = 0; i < rowCount; i++)
                table.Rows.Add(Enumerable.Repeat<object?>(null, table.Columns.Count).ToList());

            var keysForEntity = new Dictionary<string, List<object?>>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in entity.PrimaryKeys)
            {
                var columnIndex = table.Columns.IndexOf(key);
                var values = new List<object?>();

                for (var i = 0; i < rowCount; i++)
                {
                    var value = GenerateKeyValue(key, i);
                    table.Rows[i][columnIndex] = value;
                    values.Add(value);
                }

                keysForEntity[key.TechnicalName] = values;
            }

            primaryKeyValues[entity.TechnicalName] = keysForEntity;
            dataSet.Tables.Add(table);
        }

        // 2. geçiş — yabancı anahtarlar ve diğer alanlar.
        foreach (var table in dataSet.Tables)
        {
            for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                if (column.IsPrimaryKey) continue;

                var parentValues = ResolveParentValues(primaryKeyValues, column);

                for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    table.Rows[rowIndex][columnIndex] = parentValues is not null
                        ? PickParentValue(parentValues, column, rowIndex)
                        : GenerateValue(column, rowIndex);
                }
            }
        }

        return dataSet;
    }

    private static List<object?>? ResolveParentValues(
        Dictionary<string, Dictionary<string, List<object?>>> keys, EntityAttribute column)
    {
        if (!column.IsForeignKey || string.IsNullOrWhiteSpace(column.ReferencesEntity)) return null;
        if (!keys.TryGetValue(column.ReferencesEntity!, out var entityKeys)) return null;

        if (!string.IsNullOrWhiteSpace(column.ReferencesAttribute) &&
            entityKeys.TryGetValue(column.ReferencesAttribute!, out var exact))
            return exact;

        return entityKeys.Values.FirstOrDefault();
    }

    private object? PickParentValue(List<object?> parentValues, EntityAttribute column, int rowIndex)
    {
        if (parentValues.Count == 0) return null;

        // Tekil yabancı anahtar (1-1) aynı değeri iki kez kullanamaz.
        if (column.IsUnique)
            return rowIndex < parentValues.Count ? parentValues[rowIndex] : null;

        return parentValues[_random.Next(parentValues.Count)];
    }

    /// <summary>
    /// Kavramsal model veri tipi taşımaz; sentetik değerin biçimi alan adından
    /// yalnızca simülasyon için, geçici olarak çıkarılır.
    /// </summary>
    private static string InferType(EntityAttribute attribute) =>
        TypeMapper.Map(attribute.Name, null, attribute.IsPrimaryKey);

    private object GenerateKeyValue(EntityAttribute key, int index)
    {
        var type = InferType(key);

        if (TypeMapper.IsUuid(type)) return Guid.NewGuid().ToString();
        if (TypeMapper.IsNumeric(type)) return index + 1;

        var length = TypeMapper.GetLength(type) ?? 20;
        var prefix = NamingRules.FoldTurkish(key.TechnicalName)
            .Where(char.IsLetter)
            .Take(3)
            .ToArray();

        var candidate = new string(prefix).ToUpperInvariant() + (index + 1).ToString("D6");
        return candidate.Length > length ? candidate[^length..] : candidate;
    }

    private object? GenerateValue(EntityAttribute column, int rowIndex)
    {
        var type = InferType(column);
        var name = NamingRules.FoldTurkish(column.Name).ToLowerInvariant();

        if (!column.IsRequired && _random.Next(10) == 0) return null;

        if (TypeMapper.IsBoolean(type)) return _random.Next(2) == 1;
        if (TypeMapper.IsUuid(type)) return Guid.NewGuid().ToString();
        if (TypeMapper.IsDate(type)) return DateTime.Today.AddDays(-_random.Next(0, 720));
        if (TypeMapper.IsTimestamp(type)) return DateTime.Now.AddMinutes(-_random.Next(0, 100000));

        if (TypeMapper.IsDecimal(type))
        {
            var scale = TypeMapper.GetScale(type);
            var value = _random.Next(100, 1_000_000) / (scale > 0 ? Math.Pow(10, Math.Min(scale, 2)) : 1);
            return Math.Round(value, scale);
        }

        if (TypeMapper.IsNumeric(type))
            return column.IsUnique ? rowIndex + 1 : _random.Next(1, 1000);

        return GenerateText(column, name, rowIndex);
    }

    private string GenerateText(EntityAttribute column, string foldedName, int rowIndex)
    {
        var length = TypeMapper.GetLength(InferType(column)) ?? 100;

        var value = foldedName switch
        {
            _ when foldedName.Contains("soyad") => Pick(LastNames),
            _ when foldedName.Contains("eposta") || foldedName.Contains("email") || foldedName.Contains("mail") =>
                $"{Pick(FirstNames).ToLowerInvariant()}.{Pick(LastNames).ToLowerInvariant()}{rowIndex + 1}@ornek.com",
            _ when foldedName.Contains("telefon") || foldedName.Contains("gsm") =>
                $"+905{_random.Next(10, 60)}{_random.Next(1000000, 9999999)}",
            _ when foldedName.Contains("tckn") || foldedName.Contains("kimlik") =>
                _random.NextInt64(10000000000, 99999999999).ToString(),
            _ when foldedName.Contains("iban") =>
                "TR" + _random.NextInt64(100000000000000000, 999999999999999999) + _random.Next(100000, 999999),
            _ when foldedName.Contains("sehir") => Pick(Cities),
            _ when foldedName.Contains("adres") =>
                $"{Pick(Cities)}, {Pick(LastNames)} Mah. {_random.Next(1, 200)}. Sk. No:{_random.Next(1, 90)}",
            _ when foldedName.Contains("durum") || foldedName.Contains("statu") => Pick(Statuses),
            _ when foldedName.Contains("doviz") || foldedName.Contains("para birimi") => Pick(Currencies),
            _ when foldedName.Contains("urun") => Pick(Products),
            _ when foldedName.Contains("aciklama") || foldedName.Contains("not") =>
                $"{column.Name} örnek açıklaması {rowIndex + 1}",
            _ when foldedName.Contains("ad") || foldedName.Contains("isim") || foldedName.Contains("unvan") =>
                $"{Pick(FirstNames)} {Pick(LastNames)}",
            _ when foldedName.Contains("kod") || foldedName.Contains("no") || foldedName.Contains("numara") =>
                $"{new string(NamingRules.FoldTurkish(column.TechnicalName).Where(char.IsLetter).Take(3).ToArray()).ToUpperInvariant()}{rowIndex + 1:D5}",
            _ => $"{column.Name} {rowIndex + 1}"
        };

        if (column.IsUnique && !value.EndsWith((rowIndex + 1).ToString()))
            value = $"{value}-{rowIndex + 1}";

        return value.Length > length ? value[..length] : value;
    }

    private string Pick(string[] pool) => pool[_random.Next(pool.Length)];
}
