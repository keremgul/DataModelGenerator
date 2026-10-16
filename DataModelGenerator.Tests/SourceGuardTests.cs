using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Rules;
using Xunit;

namespace DataModelGenerator.Tests;

public class SourceGuardTests
{
    private static ProjectInput Input() => new()
    {
        Rules =
        {
            new BusinessRule { Id = "BR-01", Text = "Her müşterinin bir numarası vardır." },
            new BusinessRule { Id = "Q-01", Text = "Sipariş tek müşteriye mi ait? → Evet", Origin = RuleOrigin.Clarification }
        }
    };

    private static ModelEntity Entity(string name, params string[] sources) => new()
    {
        Name = name,
        TechnicalName = name,
        SourceRuleIds = sources.ToList()
    };

    [Fact]
    public void KaynaksizVarlikModeleGirmez()
    {
        var pending = new List<PendingItem>();

        var accepted = SourceGuard.FilterEntities([Entity("Kampanya")], Input(), pending);

        Assert.Empty(accepted);
        var item = Assert.Single(pending);
        Assert.Equal(PendingReason.NoSource, item.Reason);
        Assert.Equal("KAMPANYA", item.Target);
        Assert.NotEmpty(item.Question);
        Assert.NotEmpty(item.SuggestedAnswers);
    }

    [Fact]
    public void UydurmaKaynakKimligiKabulEdilmez()
    {
        var pending = new List<PendingItem>();

        // BR-99 girdide yok; model uydurmuş.
        var accepted = SourceGuard.FilterEntities([Entity("Kampanya", "BR-99")], Input(), pending);

        Assert.Empty(accepted);
        Assert.Equal(PendingReason.NoSource, pending[0].Reason);
    }

    [Fact]
    public void BelirsizVarlikModeleGirmezSoruyaDoner()
    {
        var entity = Entity("Adres", "BR-01");
        entity.Certainty = Certainty.Belirsiz;
        entity.ConfidenceReason = "Ayrı tablo mu, alan mı belirsiz";

        var pending = new List<PendingItem>();
        var accepted = SourceGuard.FilterEntities([entity], Input(), pending);

        Assert.Empty(accepted);
        Assert.Equal(PendingReason.Uncertain, pending[0].Reason);
        Assert.Contains("ADRES", pending[0].Question);
    }

    [Fact]
    public void SoruCevabiGecerliKaynaktir()
    {
        var pending = new List<PendingItem>();

        var accepted = SourceGuard.FilterEntities([Entity("Sipariş", "Q-01")], Input(), pending);

        Assert.Single(accepted);
        Assert.Empty(pending);
    }

    [Fact]
    public void KaynaksizAlanElenirVarlikKalir()
    {
        var entity = Entity("Müşteri", "BR-01");
        entity.Attributes.Add(new EntityAttribute { Name = "Ad", SourceRuleIds = { "BR-01" } });
        entity.Attributes.Add(new EntityAttribute { Name = "Kredi Notu" });

        var pending = new List<PendingItem>();
        var accepted = SourceGuard.FilterEntities([entity], Input(), pending);

        var kept = Assert.Single(accepted);
        Assert.Single(kept.Attributes);
        Assert.Equal("Ad", kept.Attributes[0].Name);

        var item = Assert.Single(pending);
        Assert.Equal(PendingItemKind.Attribute, item.Kind);
        Assert.Equal("MUSTERI.KREDINOTU", item.Target);
    }

    [Fact]
    public void AnahtarAlanlariKaynakAranmadanKabulEdilir()
    {
        var entity = Entity("Müşteri", "BR-01");
        entity.Attributes.Add(new EntityAttribute { Name = "ID", IsPrimaryKey = true });
        entity.Attributes.Add(new EntityAttribute { Name = "BirimID", IsForeignKey = true, ReferencesEntity = "Birim" });

        var pending = new List<PendingItem>();
        var accepted = SourceGuard.FilterEntities([entity], Input(), pending);

        Assert.Equal(2, accepted[0].Attributes.Count);
        Assert.Empty(pending);
    }

    [Fact]
    public void BelirsizKardinaliteliIliskiModeleGirmez()
    {
        var model = new DataModel
        {
            Entities = { Entity("Müşteri", "BR-01"), Entity("Sipariş", "BR-01") }
        };

        var relationship = new Relationship
        {
            FromEntity = "Müşteri",
            ToEntity = "Sipariş",
            Cardinality = Cardinality.Unknown,
            SourceRuleIds = { "BR-01" }
        };

        var pending = new List<PendingItem>();
        var accepted = SourceGuard.FilterRelationships([relationship], Input(), model, pending);

        Assert.Empty(accepted);
        Assert.Equal(PendingReason.Uncertain, pending[0].Reason);
        Assert.Contains("1-N", pending[0].SuggestedAnswers[1]);
    }

    [Fact]
    public void KaynakliKesinIliskiKabulEdilir()
    {
        var model = new DataModel
        {
            Entities = { Entity("Müşteri", "BR-01"), Entity("Sipariş", "BR-01") }
        };

        var relationship = new Relationship
        {
            FromEntity = "Müşteri",
            ToEntity = "Sipariş",
            Cardinality = Cardinality.OneToMany,
            SourceRuleIds = { "BR-01" }
        };

        var pending = new List<PendingItem>();

        Assert.Single(SourceGuard.FilterRelationships([relationship], Input(), model, pending));
        Assert.Empty(pending);
    }

    [Fact]
    public void KaynakKimlikleriOnEkleriDogruUretilir()
    {
        var input = new ProjectInput();

        Assert.Equal("BR-1", input.NextRuleId(RuleOrigin.User));
        Assert.Equal("Q-1", input.NextRuleId(RuleOrigin.Clarification));
        Assert.Equal("CHAT-1", input.NextRuleId(RuleOrigin.Chat));
    }
}
