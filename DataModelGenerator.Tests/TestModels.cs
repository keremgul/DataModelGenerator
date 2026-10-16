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
            TechnicalName = "MUSTERI",
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
                    Name = "Müşteri No", TechnicalName = "MUSTERINO",
                    IsRequired = true, IsUnique = true, SourceRuleIds = { "BR-1" }
                },
                new EntityAttribute
                {
                    Name = "Ad Soyad", TechnicalName = "ADSOYAD",
                    IsRequired = true, SourceRuleIds = { "BR-1" }
                }
            }
        };

        var order = new ModelEntity
        {
            Name = "Sipariş",
            TechnicalName = "SIPARIS",
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
                    Name = "Tutar", TechnicalName = "TUTAR",
                    IsRequired = true, SourceRuleIds = { "BR-2" }
                },
                new EntityAttribute
                {
                    Name = "MusteriID", TechnicalName = "MUSTERIID",
                    IsForeignKey = true, ReferencesEntity = "MUSTERI", ReferencesAttribute = "ID",
                    IsRequired = true, SourceRuleIds = { "BR-2" }
                }
            }
        };

        return new DataModel
        {
            Entities = { customer, order },
            Relationships =
            {
                new Relationship
                {
                    Name = "verir", FromEntity = "MUSTERI", ToEntity = "SIPARIS",
                    Cardinality = Cardinality.OneToMany, IsRequired = true, SourceRuleIds = { "BR-2" }
                }
            }
        };
    }
}
