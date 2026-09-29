using DataModelGenerator.Core.Models;

namespace DataModelGenerator.Tests;

internal static class TestModels
{
    /// <summary>Müşteri (1) → Sipariş (N) — geçerli, tutarlı bir referans model.</summary>
    public static DataModel CustomerOrder()
    {
        var customer = new ModelEntity
        {
            Name = "Müşteri",
            TechnicalName = "Musteri",
            SourceRuleIds = { "BR-1" },
            Attributes =
            {
                new EntityAttribute
                {
                    Name = "ID", TechnicalName = "ID",
                    IsPrimaryKey = true, IsRequired = true, IsUnique = true, SourceRuleIds = { "BR-1" }
                },
                new EntityAttribute
                {
                    Name = "Müşteri No", TechnicalName = "MusteriNo",
                    IsRequired = true, IsUnique = true, SourceRuleIds = { "BR-1" }
                },
                new EntityAttribute
                {
                    Name = "Ad Soyad", TechnicalName = "AdSoyad",
                    IsRequired = true, SourceRuleIds = { "BR-1" }
                }
            }
        };

        var order = new ModelEntity
        {
            Name = "Sipariş",
            TechnicalName = "Siparis",
            SourceRuleIds = { "BR-2" },
            Attributes =
            {
                new EntityAttribute
                {
                    Name = "ID", TechnicalName = "ID",
                    IsPrimaryKey = true, IsRequired = true, IsUnique = true, SourceRuleIds = { "BR-2" }
                },
                new EntityAttribute
                {
                    Name = "Tutar", TechnicalName = "Tutar",
                    IsRequired = true, SourceRuleIds = { "BR-2" }
                },
                new EntityAttribute
                {
                    Name = "MusteriID", TechnicalName = "MusteriID",
                    IsForeignKey = true, ReferencesEntity = "Musteri", ReferencesAttribute = "ID",
                    IsRequired = true, SourceRuleIds = { "BR-2" }
                }
            }
        };

        return new DataModel
        {
            Topic = "Sipariş Yönetimi",
            Entities = { customer, order },
            Relationships =
            {
                new Relationship
                {
                    Name = "verir", FromEntity = "Musteri", ToEntity = "Siparis",
                    Cardinality = Cardinality.OneToMany, IsRequired = true, SourceRuleIds = { "BR-2" }
                }
            }
        };
    }
}
