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

    [Test]
    public void board_effects_apply_only_to_matching_objects()
    {
        var source = Unit("source", 1, ownerId: 0);
        source["continuousEffects"] = new JsonArray(
            Effect("friendly-ganking", "ability", "add", "abilities", "Ganking", "friendly-units"),
            Effect("enemy-tank", "ability", "add", "abilities", "Tank", "enemy-units"));
        var friendly = Unit("friendly", 2, ownerId: 0);
        var enemy = Unit("enemy", 2, ownerId: 1);
        var effects = ContinuousEffectLayerResolver.EffectsFromSource(source);

        var friendlyResult = ContinuousEffectLayerResolver.EvaluateUnit(friendly, effects);
        var enemyResult = ContinuousEffectLayerResolver.EvaluateUnit(enemy, effects);

        Assert.That(friendlyResult.Abilities, Contains.Item("Ganking"));
        Assert.That(friendlyResult.Abilities, Does.Not.Contain("Tank"));
        Assert.That(enemyResult.Abilities, Contains.Item("Tank"));
        Assert.That(enemyResult.Abilities, Does.Not.Contain("Ganking"));
    }

    [Test]
    public void board_effects_use_source_layer_timestamp_order()
    {
        var earlySource = Unit("early-source", 1, ownerId: 0, layerTimestamp: 1);
        earlySource["continuousEffects"] = new JsonArray(Effect("early-set", "trait", "set", "might", 5, "friendly-units"));
        var lateSource = Unit("late-source", 1, ownerId: 0, layerTimestamp: 2);
        lateSource["continuousEffects"] = new JsonArray(Effect("late-set", "trait", "set", "might", 7, "friendly-units"));
        var target = Unit("target", 1, ownerId: 0);
        var effects = ContinuousEffectLayerResolver.EffectsFromSource(lateSource, 0)
            .Concat(ContinuousEffectLayerResolver.EffectsFromSource(earlySource, 1000));

        var result = ContinuousEffectLayerResolver.EvaluateUnit(target, effects);

        Assert.That(result.Might, Is.EqualTo(7));
    }

    [Test]
    public void same_timestamp_board_effects_use_source_order_after_timestamp()
    {
        var firstSource = Unit("first-source", 1, ownerId: 0, layerTimestamp: 4);
        firstSource["continuousEffects"] = new JsonArray(Effect("first-set", "trait", "set", "might", 5, "friendly-units"));
        var secondSource = Unit("second-source", 1, ownerId: 0, layerTimestamp: 4);
        secondSource["continuousEffects"] = new JsonArray(Effect("second-set", "trait", "set", "might", 7, "friendly-units"));
        var target = Unit("target", 1, ownerId: 0);
        var effects = ContinuousEffectLayerResolver.EffectsFromSource(firstSource, 0)
            .Concat(ContinuousEffectLayerResolver.EffectsFromSource(secondSource, 1000));

        var result = ContinuousEffectLayerResolver.EvaluateUnit(target, effects);

        Assert.That(result.Might, Is.EqualTo(7));
    }

    [Test]
    public void board_effect_dependencies_override_timestamp_inside_their_layer()
    {
        var buffSource = Unit("buff-source", 1, ownerId: 0, layerTimestamp: 2);
        buffSource["continuousEffects"] = new JsonArray(Effect("depended-on-buff", "arithmetic", "add", "might", 2, "friendly-units"));
        var setSource = Unit("set-source", 1, ownerId: 0, layerTimestamp: 1);
        var setEffect = Effect("dependent-set", "arithmetic", "set", "might", 10, "friendly-units");
        setEffect["dependsOn"] = new JsonArray("depended-on-buff");
        setSource["continuousEffects"] = new JsonArray(setEffect);
        var target = Unit("target", 4, ownerId: 0);
        var effects = ContinuousEffectLayerResolver.EffectsFromSource(setSource, 0)
            .Concat(ContinuousEffectLayerResolver.EffectsFromSource(buffSource, 1000));

        var result = ContinuousEffectLayerResolver.EvaluateUnit(target, effects);

        Assert.That(result.Might, Is.EqualTo(10));
    }

    private static JsonObject Unit(string uid, int might, int ownerId = 0, int layerTimestamp = 1) =>
        new()
        {
            ["uid"] = uid,
            ["ownerId"] = ownerId,
            ["controllerId"] = ownerId,
            ["kind"] = "unit",
            ["location"] = new JsonObject { ["type"] = "battlefield", ["battlefieldId"] = "field-a", ["attachedToUid"] = null },
            ["layerTimestamp"] = layerTimestamp,
            ["might"] = might,
            ["tags"] = new JsonArray(),
            ["abilities"] = new JsonArray(),
            ["attachedMight"] = 0
        };

    private static JsonObject Effect(string id, string layer, string operation, string property, string value, string appliesTo) =>
        new()
        {
            ["id"] = id,
            ["layer"] = layer,
            ["operation"] = operation,
            ["property"] = property,
            ["textValue"] = value,
            ["appliesTo"] = appliesTo
        };

    private static JsonObject Effect(string id, string layer, string operation, string property, int amount, string appliesTo) =>
        new()
        {
            ["id"] = id,
            ["layer"] = layer,
            ["operation"] = operation,
            ["property"] = property,
            ["amount"] = amount,
            ["appliesTo"] = appliesTo
        };
}
