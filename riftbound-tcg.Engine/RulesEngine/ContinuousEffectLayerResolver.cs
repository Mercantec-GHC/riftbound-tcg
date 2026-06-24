using System.Text.Json.Nodes;

namespace riftbound_tcg.Engine.RulesEngine;

public enum ContinuousEffectLayer
{
    Trait = 1,
    Ability = 2,
    Arithmetic = 3
}

public enum ContinuousEffectOperation
{
    Add,
    Remove,
    Set
}

public sealed record LayeredCharacteristics(
    int Might,
    IReadOnlySet<string> Traits,
    IReadOnlySet<string> Abilities);

public sealed record ContinuousEffect(
    string Id,
    ContinuousEffectLayer Layer,
    ContinuousEffectOperation Operation,
    string Property,
    int Timestamp,
    int SourceOrder,
    int? Amount = null,
    string? TextValue = null,
    IReadOnlyList<string>? TextValues = null,
    string? TargetUnitId = null,
    IReadOnlyList<string>? DependsOnEffectIds = null,
    IReadOnlyList<string>? RequiredTraits = null,
    IReadOnlyList<string>? RequiredAbilities = null,
    int? RequiredMinimumMight = null);

public static class ContinuousEffectLayerResolver
{
    private static readonly string[] EmptyStringArray = [];

    public static LayeredCharacteristics EvaluateUnit(JsonObject unit)
    {
        var baseMight = unit["might"]?.GetValue<int>() ?? 0;
        var baseTraits = ReadStringArray(unit["tags"]).Concat(ReadStringArray(unit["traits"]));
        var baseAbilities = ReadStringArray(unit["abilities"]).Concat(ReadStringArray(unit["keywords"]));
        var uid = unit["uid"]?.GetValue<string>();

        var effects = ReadContinuousEffects(unit, uid).ToList();
        if ((unit["attachedMight"]?.GetValue<int>() ?? 0) is var attachedMight && attachedMight != 0)
        {
            effects.Add(new ContinuousEffect(
                Id: "__legacy-attached-might",
                Layer: ContinuousEffectLayer.Arithmetic,
                Operation: ContinuousEffectOperation.Add,
                Property: "might",
                Timestamp: int.MaxValue,
                SourceOrder: int.MaxValue,
                Amount: attachedMight,
                TargetUnitId: uid));
        }

        return Evaluate(baseMight, baseTraits, baseAbilities, effects, uid);
    }

    public static LayeredCharacteristics Evaluate(
        int baseMight,
        IEnumerable<string>? baseTraits,
        IEnumerable<string>? baseAbilities,
        IEnumerable<ContinuousEffect> effects,
        string? unitId = null)
    {
        var current = new MutableCharacteristics(baseMight, baseTraits ?? EmptyStringArray, baseAbilities ?? EmptyStringArray);
        var relevantEffects = effects
            .Where(effect => string.IsNullOrWhiteSpace(effect.TargetUnitId) || effect.TargetUnitId == unitId)
            .Where(effect => IsSupported(effect))
            .ToList();
        var appliedIds = new HashSet<string>(StringComparer.Ordinal);

        while (appliedIds.Count < relevantEffects.Count)
        {
            var appliedInPass = false;

            foreach (var layer in new[] { ContinuousEffectLayer.Trait, ContinuousEffectLayer.Ability, ContinuousEffectLayer.Arithmetic })
            {
                var candidates = relevantEffects
                    .Where(effect => effect.Layer == layer && !appliedIds.Contains(effect.Id) && CanApply(effect, current))
                    .ToList();

                foreach (var effect in OrderWithinLayer(candidates, appliedIds))
                {
                    Apply(effect, current);
                    appliedIds.Add(effect.Id);
                    appliedInPass = true;
                }

                if (appliedInPass)
                {
                    break;
                }
            }

            if (!appliedInPass)
            {
                break;
            }
        }

        return current.ToImmutable();
    }

    private static IEnumerable<ContinuousEffect> ReadContinuousEffects(JsonObject unit, string? unitId)
    {
        if (unit["continuousEffects"] is not JsonArray effects)
        {
            yield break;
        }

        var sourceOrder = 0;
        foreach (var node in effects)
        {
            if (node is not JsonObject effect)
            {
                sourceOrder++;
                continue;
            }

            var layer = ReadLayer(effect["layer"]?.GetValue<string>());
            var operation = ReadOperation(effect["operation"]?.GetValue<string>());
            var property = effect["property"]?.GetValue<string>() ?? (layer == ContinuousEffectLayer.Arithmetic ? "might" : "traits");
            var id = effect["id"]?.GetValue<string>() ?? $"effect-{sourceOrder}";
            var targetUnitId = effect["targetUnitId"]?.GetValue<string>() ?? unitId;

            yield return new ContinuousEffect(
                Id: id,
                Layer: layer,
                Operation: operation,
                Property: property,
                Timestamp: effect["timestamp"]?.GetValue<int>() ?? sourceOrder,
                SourceOrder: sourceOrder,
                Amount: ReadAmount(effect["value"]) ?? effect["amount"]?.GetValue<int?>(),
                TextValue: ReadTextValue(effect["value"]) ?? effect["textValue"]?.GetValue<string>(),
                TextValues: ReadStringArray(effect["value"]).Concat(ReadStringArray(effect["values"])).ToArray(),
                TargetUnitId: targetUnitId,
                DependsOnEffectIds: ReadStringArray(effect["dependsOn"]).ToArray(),
                RequiredTraits: ReadStringArray(effect["requiresTraits"]).Concat(ReadStringArray(effect["requiresTags"])).ToArray(),
                RequiredAbilities: ReadStringArray(effect["requiresAbilities"]).Concat(ReadStringArray(effect["requiresKeywords"])).ToArray(),
                RequiredMinimumMight: effect["requiresMinimumMight"]?.GetValue<int?>());
            sourceOrder++;
        }
    }

    private static IReadOnlyList<ContinuousEffect> OrderWithinLayer(IReadOnlyList<ContinuousEffect> candidates, HashSet<string> alreadyAppliedIds)
    {
        var remaining = candidates.ToList();
        var ordered = new List<ContinuousEffect>();
        var availableIds = alreadyAppliedIds.Concat(candidates.Select(effect => effect.Id)).ToHashSet(StringComparer.Ordinal);

        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(effect => (effect.DependsOnEffectIds ?? EmptyStringArray)
                    .Where(availableIds.Contains)
                    .All(dependencyId => alreadyAppliedIds.Contains(dependencyId) || ordered.Any(applied => applied.Id == dependencyId)))
                .ToList();

            if (ready.Count == 0)
            {
                ready = remaining.ToList();
            }

            var next = ready
                .OrderBy(ArithmeticSublayer)
                .ThenBy(effect => effect.Timestamp)
                .ThenBy(effect => effect.SourceOrder)
                .ThenBy(effect => effect.Id, StringComparer.Ordinal)
                .First();

            ordered.Add(next);
            remaining.Remove(next);
        }

        return ordered;
    }

    private static int ArithmeticSublayer(ContinuousEffect effect)
    {
        if (effect.Layer != ContinuousEffectLayer.Arithmetic)
        {
            return 0;
        }

        return (effect.Amount ?? 0) >= 0 ? 0 : 1;
    }

    private static bool CanApply(ContinuousEffect effect, MutableCharacteristics current)
    {
        if ((effect.RequiredTraits ?? EmptyStringArray).Any(trait => !current.Traits.Contains(trait)))
        {
            return false;
        }

        if ((effect.RequiredAbilities ?? EmptyStringArray).Any(ability => !current.Abilities.Contains(ability)))
        {
            return false;
        }

        return effect.RequiredMinimumMight is null || current.Might >= effect.RequiredMinimumMight.Value;
    }

    private static void Apply(ContinuousEffect effect, MutableCharacteristics current)
    {
        var property = effect.Property.ToLowerInvariant();
        if (effect.Layer == ContinuousEffectLayer.Trait && property is "might")
        {
            if (effect.Operation == ContinuousEffectOperation.Set && effect.Amount is not null)
            {
                current.Might = effect.Amount.Value;
            }

            return;
        }

        if (effect.Layer == ContinuousEffectLayer.Arithmetic && property is "might")
        {
            if (effect.Operation == ContinuousEffectOperation.Set && effect.Amount is not null)
            {
                current.Might = effect.Amount.Value;
            }
            else if (effect.Amount is not null)
            {
                current.Might += effect.Amount.Value;
            }

            return;
        }

        if (effect.Layer == ContinuousEffectLayer.Trait && property is "trait" or "traits" or "tag" or "tags")
        {
            ApplyTextChange(effect, current.Traits);
            return;
        }

        if (effect.Layer == ContinuousEffectLayer.Ability && property is "ability" or "abilities" or "keyword" or "keywords")
        {
            ApplyTextChange(effect, current.Abilities);
        }
    }

    private static void ApplyTextChange(ContinuousEffect effect, HashSet<string> values)
    {
        var changedValues = (effect.TextValues ?? EmptyStringArray)
            .Concat(string.IsNullOrWhiteSpace(effect.TextValue) ? EmptyStringArray : [effect.TextValue])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (effect.Operation == ContinuousEffectOperation.Set)
        {
            values.Clear();
        }

        foreach (var value in changedValues)
        {
            if (effect.Operation == ContinuousEffectOperation.Remove)
            {
                values.Remove(value);
            }
            else
            {
                values.Add(value);
            }
        }
    }

    private static bool IsSupported(ContinuousEffect effect)
    {
        var property = effect.Property.ToLowerInvariant();
        return (effect.Layer == ContinuousEffectLayer.Trait && property is "might" or "trait" or "traits" or "tag" or "tags")
            || (effect.Layer == ContinuousEffectLayer.Ability && property is "ability" or "abilities" or "keyword" or "keywords")
            || (effect.Layer == ContinuousEffectLayer.Arithmetic && property is "might");
    }

    private static ContinuousEffectLayer ReadLayer(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "ability" or "abilities" or "ability-altering" => ContinuousEffectLayer.Ability,
            "arithmetic" => ContinuousEffectLayer.Arithmetic,
            _ => ContinuousEffectLayer.Trait
        };

    private static ContinuousEffectOperation ReadOperation(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "remove" or "lose" => ContinuousEffectOperation.Remove,
            "set" or "become" or "replace" => ContinuousEffectOperation.Set,
            _ => ContinuousEffectOperation.Add
        };

    private static int? ReadAmount(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<int>(out var amount))
        {
            return amount;
        }

        return null;
    }

    private static string? ReadTextValue(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return null;
    }

    private static IEnumerable<string> ReadStringArray(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                {
                    yield return text;
                }
            }
        }
        else if (node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
        {
            yield return text;
        }
    }

    private sealed class MutableCharacteristics
    {
        public MutableCharacteristics(int might, IEnumerable<string> traits, IEnumerable<string> abilities)
        {
            Might = might;
            Traits = new HashSet<string>(traits.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.Ordinal);
            Abilities = new HashSet<string>(abilities.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.Ordinal);
        }

        public int Might { get; set; }
        public HashSet<string> Traits { get; }
        public HashSet<string> Abilities { get; }

        public LayeredCharacteristics ToImmutable() =>
            new(Might, Traits.ToHashSet(StringComparer.Ordinal), Abilities.ToHashSet(StringComparer.Ordinal));
    }
}
