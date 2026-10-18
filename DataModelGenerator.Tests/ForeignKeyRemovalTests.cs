using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Rules;
using Xunit;

namespace DataModelGenerator.Tests;

/// <summary>
/// Yabancı anahtar alanı kaldırıldığında onu doğuran ilişki de kalkmalı.
/// Aksi halde deterministik katman alanı geri yazıyor: kullanıcıya "kaldırıldı"
/// deniyor ama modelde hiçbir şey değişmiyor.
/// </summary>
public class ForeignKeyRemovalTests
{
    /// <summary>
    /// PROJE ile CALISAN arasındaki ilişki PROJECALISAN ara tablosu üzerinden
    /// kurulmuş; ayrıca PROJE'ye yönetici için doğrudan CALISANID eklenmiş.
    /// </summary>
    private static DataModel ProjectModel()
    {
        static ModelEntity Table(string name, params EntityAttribute[] extra)
        {
            var entity = new ModelEntity { Name = name, TechnicalName = name, SourceRuleIds = { "BR-1" } };
            entity.Attributes.Add(new EntityAttribute
            {
                Name = "ID", TechnicalName = "ID", IsPrimaryKey = true, IsRequired = true, IsUnique = true
            });
            entity.Attributes.AddRange(extra);
            return entity;
        }

        var proje = Table("PROJE", new EntityAttribute
        {
            Name = "CALISANID", TechnicalName = "CALISANID",
            IsForeignKey = true, ReferencesEntity = "CALISAN", ReferencesAttribute = "ID",
            SourceRuleIds = { "BR-1" }
        });

        var calisan = Table("CALISAN");

        var junction = Table("PROJECALISAN",
            new EntityAttribute
            {
                Name = "PROJEID", TechnicalName = "PROJEID",
                IsForeignKey = true, ReferencesEntity = "PROJE", ReferencesAttribute = "ID",
                SourceRuleIds = { "BR-1" }
            },
            new EntityAttribute
            {
                Name = "CALISANID", TechnicalName = "CALISANID",
                IsForeignKey = true, ReferencesEntity = "CALISAN", ReferencesAttribute = "ID",
                SourceRuleIds = { "BR-1" }
            });

        return new DataModel
        {
            Entities = { proje, calisan, junction },
            Relationships =
            {
                new Relationship
                {
                    FromEntity = "CALISAN", ToEntity = "PROJE",
                    Cardinality = Cardinality.OneToMany, SourceRuleIds = { "BR-1" }
                },
                new Relationship
                {
                    FromEntity = "PROJE", ToEntity = "PROJECALISAN",
                    Cardinality = Cardinality.OneToMany, SourceRuleIds = { "BR-1" }
                },
                new Relationship
                {
                    FromEntity = "CALISAN", ToEntity = "PROJECALISAN",
                    Cardinality = Cardinality.OneToMany, SourceRuleIds = { "BR-1" }
                }
            }
        };
    }

    [Fact]
    public void YabanciAnahtarKaldirildiginda_OnuDoguranIliskiDeKalkar()
    {
        var model = ProjectModel();

        var result = DirectiveExecutor.Execute(model, new ChatDirective
        {
            Operation = DirectiveOperation.RemoveField,
            Entity = "PROJE",
            Field = "CALISANID"
        }, "CHAT-1");

        Assert.True(result.Applied);

        // İlişki kalırsa deterministik katman alanı geri yazar.
        Assert.DoesNotContain(model.Relationships,
            r => r.FromEntity == "CALISAN" && r.ToEntity == "PROJE");
    }

    /// <summary>Hatanın kendisi: alan kaldırılıyor ama modele geri geliyor.</summary>
    [Fact]
    public void KaldirilanYabanciAnahtar_DeterministikKatmandaGeriGelmez()
    {
        var model = ProjectModel();

        DirectiveExecutor.Execute(model, new ChatDirective
        {
            Operation = DirectiveOperation.RemoveField,
            Entity = "PROJE",
            Field = "CALISANID"
        }, "CHAT-1");

        // Üretimden sonra her turda çalışan adımlar.
        ModelMerger.SyncForeignKeys(model);
        RelationshipRules.Apply(model, new List<PendingItem>());

        var proje = model.FindEntity("PROJE")!;
        Assert.DoesNotContain(proje.Attributes, a => a.TechnicalName == "CALISANID");
    }

    [Fact]
    public void AraTabloUzerindenKurulanIliski_Korunur()
    {
        var model = ProjectModel();

        DirectiveExecutor.Execute(model, new ChatDirective
        {
            Operation = DirectiveOperation.RemoveField,
            Entity = "PROJE",
            Field = "CALISANID"
        }, "CHAT-1");

        ModelMerger.SyncForeignKeys(model);
        RelationshipRules.Apply(model, new List<PendingItem>());

        // Ara tablo ve onun yabancı anahtarları yerinde kalmalı.
        var junction = model.FindEntity("PROJECALISAN");
        Assert.NotNull(junction);
        Assert.Contains(junction!.Attributes, a => a.TechnicalName == "PROJEID");
        Assert.Contains(junction.Attributes, a => a.TechnicalName == "CALISANID");

        Assert.Contains(model.Relationships, r => r.ToEntity == "PROJECALISAN" && r.FromEntity == "PROJE");
        Assert.Contains(model.Relationships, r => r.ToEntity == "PROJECALISAN" && r.FromEntity == "CALISAN");
    }

    /// <summary>
    /// Aynı tabloya rolü farklı iki referans varsa, birini kaldırmak ilişkiyi
    /// götürmemeli — diğer alan hâlâ o ilişkiyi taşıyor.
    /// </summary>
    [Fact]
    public void AyniTabloyaIkinciReferansVarsa_IliskiKorunur()
    {
        var model = ProjectModel();
        var proje = model.FindEntity("PROJE")!;
        proje.Attributes.Add(new EntityAttribute
        {
            Name = "YONETICICALISANID", TechnicalName = "YONETICICALISANID",
            IsForeignKey = true, ReferencesEntity = "CALISAN", ReferencesAttribute = "ID",
            SourceRuleIds = { "BR-1" }
        });

        DirectiveExecutor.Execute(model, new ChatDirective
        {
            Operation = DirectiveOperation.RemoveField,
            Entity = "PROJE",
            Field = "CALISANID"
        }, "CHAT-1");

        Assert.Contains(model.Relationships, r => r.FromEntity == "CALISAN" && r.ToEntity == "PROJE");
        Assert.Contains(proje.Attributes, a => a.TechnicalName == "YONETICICALISANID");
    }

    /// <summary>Yabancı anahtar olmayan alanın kaldırılması ilişkilere dokunmamalı.</summary>
    [Fact]
    public void NormalAlanKaldirildiginda_IliskilereDokunulmaz()
    {
        var model = ProjectModel();
        var proje = model.FindEntity("PROJE")!;
        proje.Attributes.Add(new EntityAttribute
        {
            Name = "BUTCE", TechnicalName = "BUTCE", SourceRuleIds = { "BR-1" }
        });

        var before = model.Relationships.Count;

        DirectiveExecutor.Execute(model, new ChatDirective
        {
            Operation = DirectiveOperation.RemoveField,
            Entity = "PROJE",
            Field = "BUTCE"
        }, "CHAT-1");

        Assert.Equal(before, model.Relationships.Count);
        Assert.DoesNotContain(proje.Attributes, a => a.TechnicalName == "BUTCE");
    }
}
