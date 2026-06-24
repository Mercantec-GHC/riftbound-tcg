using System.Text.Json.Nodes;
using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Tests.Rules;

public class ContinuousEffectLayerResolverTests
{
    [Test]
    public void layers_recur_when_later_arithmetic_enables_trait_and_ability_effects()
    {
        var effects = new[]
        {
            new ContinuousEffect("grant-mighty", ContinuousEffectLayer.Trait, ContinuousEffectOperation.Add, "traits", 1, 0, TextValue: "Mighty", RequiredMinimumMight: 5),
            new ContinuousEffect("grant-shield", ContinuousEffectLayer.Ability, ContinuousEffectOperation.Add, "abilities", 2, 1, TextValue: "Shield", RequiredTraits: ["Mighty"]),
            new ContinuousEffect("buff", ContinuousEffectLayer.Arithmetic, ContinuousEffectOperation.Add, "might", 3, 2, Amount: 1)
        };

        var result = ContinuousEffectLayerResolver.Evaluate(4, [], [], effects, "unit-a");

        Assert.That(result.Might, Is.EqualTo(5));
        Assert.That(result.Traits, Contains.Item("Mighty"));
        Assert.That(result.Abilities, Contains.Item("Shield"));
    }

    [Test]
    public void same_timestamp_effects_use_source_order_then_id_for_determinism()
    {
        var effects = new[]
        {
            new ContinuousEffect("later-source", ContinuousEffectLayer.Trait, ContinuousEffectOperation.Set, "might", 7, 1, Amount: 3),
            new ContinuousEffect("earlier-source", ContinuousEffectLayer.Trait, ContinuousEffectOperation.Set, "might", 7, 0, Amount: 5)
        };

        var result = ContinuousEffectLayerResolver.Evaluate(1, [], [], effects, "unit-a");

        Assert.That(result.Might, Is.EqualTo(3));
    }

    [Test]
    public void dependency_order_overrides_timestamp_within_the_same_layer()
    {
        var effects = new[]
        {
            new ContinuousEffect("dependent-set", ContinuousEffectLayer.Arithmetic, ContinuousEffectOperation.Set, "might", 1, 0, Amount: 10, DependsOnEffectIds: ["depended-on-buff"]),
            new ContinuousEffect("depended-on-buff", ContinuousEffectLayer.Arithmetic, ContinuousEffectOperation.Add, "might", 2, 1, Amount: 2)
        };

        var result = ContinuousEffectLayerResolver.Evaluate(4, [], [], effects, "unit-a");

        Assert.That(result.Might, Is.EqualTo(10));
    }

    [Test]
    public void arithmetic_applies_increases_before_decreases_inside_the_layer()
    {
        var effects = new[]
        {
            new ContinuousEffect("decrease", ContinuousEffectLayer.Arithmetic, ContinuousEffectOperation.Add, "might", 1, 0, Amount: -3),
            new ContinuousEffect("increase", ContinuousEffectLayer.Arithmetic, ContinuousEffectOperation.Add, "might", 2, 1, Amount: 4)
        };

        var result = ContinuousEffectLayerResolver.Evaluate(2, [], [], effects, "unit-a");

        Assert.That(result.Might, Is.EqualTo(3));
    }

    [Test]
    public void json_effects_apply_trait_ability_and_arithmetic_changes()
    {
        var unit = Unit("unit-a", 2);
        unit["continuousEffects"] = new JsonArray
        {
            new JsonObject
            {
                ["id"] = "tag",
                ["layer"] = "trait",
                ["operation"] = "add",
                ["property"] = "tags",
                ["value"] = "Elite",
                ["timestamp"] = 1
            },
            new JsonObject
            {
                ["id"] = "ability",
                ["layer"] = "ability",
                ["operation"] = "add",
                ["property"] = "abilities",
                ["value"] = "Deflect",
                ["requiresTags"] = new JsonArray("Elite"),
                ["timestamp"] = 2
            },
            new JsonObject
            {
                ["id"] = "might",
                ["layer"] = "arithmetic",
                ["operation"] = "add",
                ["property"] = "might",
                ["value"] = 2,
                ["timestamp"] = 3
            }
        };

        var result = ContinuousEffectLayerResolver.EvaluateUnit(unit);

        Assert.That(result.Might, Is.EqualTo(4));
        Assert.That(result.Traits, Contains.Item("Elite"));
        Assert.That(result.Abilities, Contains.Item("Deflect"));
    }

    [Test]
    public void attached_might_still_counts_as_arithmetic_might()
    {
        var unit = Unit("unit-a", 2);
        unit["attachedMight"] = 3;

        var result = ContinuousEffectLayerResolver.EvaluateUnit(unit);

        Assert.That(result.Might, Is.EqualTo(5));
    }

    private static JsonObject Unit(string uid, int might) =>
        new()
        {
            ["uid"] = uid,
            ["might"] = might,
            ["tags"] = new JsonArray(),
            ["abilities"] = new JsonArray(),
            ["attachedMight"] = 0
        };
}
