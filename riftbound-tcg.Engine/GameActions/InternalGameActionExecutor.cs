using System.Text.Json;
using System.Text.Json.Nodes;

namespace riftbound_tcg.Engine.GameActions;

public enum InternalGameActionType
{
    Recycle,
    Discard,
    Reveal,
    Kill,
    Banish,
    Stun,
    Counter,
    Prevent,
    Create,
    Predict,
    Attach,
    Detach,
    Swap,
    Double,
    Recall
}

public sealed record InternalGameAction(
    InternalGameActionType Type,
    int PlayerId,
    IReadOnlyDictionary<string, object?> Payload);

public sealed record InternalGameActionResult(
    bool Accepted,
    string Message,
    JsonObject State);

public static class InternalGameActionExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] RuntimeCardKeys =
    [
        "uid",
        "ownerId",
        "location",
        "exhausted",
        "damage",
        "attachedMight",
        "attacker",
        "defender",
        "attachedUnitId"
    ];

    public static InternalGameActionResult Apply(JsonObject state, InternalGameAction action)
    {
        if (FindPlayer(state, action.PlayerId) is null)
        {
            return Reject(state, $"Player '{action.PlayerId}' is not seated in this match.");
        }

        var next = Clone(state);
        var result = action.Type switch
        {
            InternalGameActionType.Recycle => Recycle(next, action),
            InternalGameActionType.Discard => Discard(next, action),
            InternalGameActionType.Reveal => Reveal(next, action),
            InternalGameActionType.Kill => Kill(next, action),
            InternalGameActionType.Banish => Banish(next, action),
            InternalGameActionType.Stun => Stun(next, action),
            InternalGameActionType.Counter => Counter(next, action),
            InternalGameActionType.Prevent => Prevent(next, action),
            InternalGameActionType.Create => Create(next, action),
            InternalGameActionType.Predict => Predict(next, action),
            InternalGameActionType.Attach => Attach(next, action),
            InternalGameActionType.Detach => Detach(next, action),
            InternalGameActionType.Swap => Swap(next, action),
            InternalGameActionType.Double => Double(next, action),
            InternalGameActionType.Recall => Recall(next, action),
            _ => null
        };

        return result is null
            ? Reject(state, $"Invalid {action.Type.ToString().ToLowerInvariant()} internal action.")
            : new InternalGameActionResult(true, "accepted", result);
    }

    private static JsonObject? Recycle(JsonObject state, InternalGameAction action)
    {
        var player = FindPlayer(state, action.PlayerId);
        var trash = player?["trash"]?.AsArray();
        if (player is null || trash is null || trash.Count == 0)
        {
            return null;
        }

        var recycled = trash.Select(card => card?.DeepClone()).Where(card => card is not null).ToList();
        trash.Clear();
        ShuffleInPlace(state, recycled);

        var deck = player["deck"]!.AsArray();
        foreach (var card in recycled)
        {
            deck.Add(card);
        }

        return state;
    }

    private static JsonObject? Discard(JsonObject state, InternalGameAction action)
    {
        var handIndex = ReadInt(action.Payload, "handIndex");
        var player = FindPlayer(state, action.PlayerId);
        var hand = player?["hand"]?.AsArray();
        if (handIndex is null || hand is null || handIndex.Value < 0 || handIndex.Value >= hand.Count)
        {
            return null;
        }

        var card = hand[handIndex.Value]?.DeepClone();
        hand.RemoveAt(handIndex.Value);
        player!["trash"]!.AsArray().Add(card);
        return state;
    }

    private static JsonObject? Reveal(JsonObject state, InternalGameAction action)
    {
        var zone = ReadString(action.Payload, "zone") ?? "hand";
        var index = ReadInt(action.Payload, "index");
        var player = FindPlayer(state, action.PlayerId);
        var source = Zone(player, zone);
        if (index is null || source is null || index.Value < 0 || index.Value >= source.Count)
        {
            return null;
        }

        var revealed = EnsureArray(state, "revealedCards");
        revealed.Add(new JsonObject
        {
            ["playerId"] = action.PlayerId,
            ["zone"] = zone,
            ["index"] = index.Value,
            ["card"] = source[index.Value]?.DeepClone()
        });
        return state;
    }

    private static JsonObject? Kill(JsonObject state, InternalGameAction action)
    {
        var unitId = ReadString(action.Payload, "unitId");
        if (string.IsNullOrWhiteSpace(unitId) || FindUnit(state, unitId) is not { } located)
        {
            return null;
        }

        DetachGearFromUnit(state, unitId);
        var ownerId = located.Unit["ownerId"]?.GetValue<int>() ?? action.PlayerId;
        located.Container.Remove(located.Unit);
        FindPlayer(state, ownerId)?["trash"]!.AsArray().Add(CardWithoutRuntimeState(located.Unit));
        return state;
    }

    private static JsonObject? Banish(JsonObject state, InternalGameAction action)
    {
        var unitId = ReadString(action.Payload, "unitId");
        if (!string.IsNullOrWhiteSpace(unitId) && FindUnit(state, unitId) is { } locatedUnit)
        {
            DetachGearFromUnit(state, unitId);
            locatedUnit.Container.Remove(locatedUnit.Unit);
            EnsureArray(state, "banished").Add(CardWithoutRuntimeState(locatedUnit.Unit));
            return state;
        }

        var zone = ReadString(action.Payload, "zone");
        var index = ReadInt(action.Payload, "index");
        var player = FindPlayer(state, action.PlayerId);
        var source = Zone(player, zone);
        if (index is null || source is null || index.Value < 0 || index.Value >= source.Count)
        {
            return null;
        }

        var card = source[index.Value]?.DeepClone();
        source.RemoveAt(index.Value);
        EnsureArray(state, "banished").Add(card);
        return state;
    }

    private static JsonObject? Stun(JsonObject state, InternalGameAction action)
    {
        var unitId = ReadString(action.Payload, "unitId");
        if (string.IsNullOrWhiteSpace(unitId) || FindUnit(state, unitId) is not { } located)
        {
            return null;
        }

        located.Unit["exhausted"] = true;
        return state;
    }

    private static JsonObject? Counter(JsonObject state, InternalGameAction action)
    {
        var stackItemId = ReadString(action.Payload, "stackItemId");
        var stack = state["effectStack"]?.AsArray();
        if (string.IsNullOrWhiteSpace(stackItemId) || stack is null)
        {
            return null;
        }

        for (var i = 0; i < stack.Count; i++)
        {
            if (stack[i]?.AsObject() is not { } item
                || !string.Equals(item["id"]?.GetValue<string>(), stackItemId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ownerId = item["playerId"]?.GetValue<int>() ?? action.PlayerId;
            var card = item["card"]?.DeepClone();
            stack.RemoveAt(i);
            if (card is not null)
            {
                FindPlayer(state, ownerId)?["trash"]!.AsArray().Add(card);
            }

            return state;
        }

        return null;
    }

    private static JsonObject? Prevent(JsonObject state, InternalGameAction action)
    {
        var unitId = ReadString(action.Payload, "unitId");
        var amount = ReadInt(action.Payload, "amount");
        if (string.IsNullOrWhiteSpace(unitId) || amount is null || amount.Value <= 0 || FindUnit(state, unitId) is null)
        {
            return null;
        }

        EnsureArray(state, "damagePrevention").Add(new JsonObject
        {
            ["sourcePlayerId"] = action.PlayerId,
            ["unitId"] = unitId,
            ["amount"] = amount.Value
        });
        return state;
    }

    private static JsonObject? Create(JsonObject state, InternalGameAction action)
    {
        var destination = ReadString(action.Payload, "destination") ?? "base";
        var kind = ReadString(action.Payload, "kind") ?? "unit";
        var card = new JsonObject
        {
            ["id"] = ReadString(action.Payload, "cardInstanceId") ?? $"{ReadString(action.Payload, "cardId") ?? "created-card"}-{state["nextUid"]?.GetValue<int>() ?? 1}",
            ["catalogId"] = ReadString(action.Payload, "cardId") ?? "created-card",
            ["name"] = ReadString(action.Payload, "name") ?? "Created Card",
            ["kind"] = kind,
            ["tags"] = new JsonArray(),
            ["domain"] = ReadString(action.Payload, "domain") ?? "Fury",
            ["domains"] = new JsonArray(ReadString(action.Payload, "domain") ?? "Fury"),
            ["cost"] = ReadInt(action.Payload, "cost") ?? 0,
            ["might"] = ReadInt(action.Payload, "might") ?? 0,
            ["text"] = ReadString(action.Payload, "text") ?? string.Empty,
            ["image"] = ReadString(action.Payload, "image") ?? string.Empty,
            ["cardType"] = ReadString(action.Payload, "cardType") ?? kind,
            ["supertype"] = null,
            ["effect"] = new JsonObject { ["type"] = "rally", ["amount"] = 0 }
        };

        if (destination is "hand" or "deck" or "trash")
        {
            var zone = Zone(FindPlayer(state, action.PlayerId), destination);
            if (zone is null)
            {
                return null;
            }

            zone.Add(card);
            return state;
        }

        if (destination != "base" && destination != "battlefield")
        {
            return null;
        }

        card["uid"] = $"{kind}-{state["nextUid"]?.GetValue<int>() ?? 1}";
        card["ownerId"] = action.PlayerId;
        card["location"] = new JsonObject
        {
            ["type"] = destination,
            ["battlefieldId"] = destination == "battlefield" ? ReadString(action.Payload, "battlefieldId") : null
        };
        card["exhausted"] = ReadBool(action.Payload, "exhausted") ?? false;

        state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;
        if (kind == "gear")
        {
            card["attachedUnitId"] = null;
            FindPlayer(state, action.PlayerId)!["baseGear"]!.AsArray().Add(card);
            return state;
        }

        card["damage"] = 0;
        card["attachedMight"] = 0;
        card["attacker"] = false;
        card["defender"] = false;

        if (destination == "base")
        {
            FindPlayer(state, action.PlayerId)!["base"]!.AsArray().Add(card);
            return state;
        }

        var battlefield = FindBattlefield(state, ReadString(action.Payload, "battlefieldId"));
        if (battlefield is null)
        {
            return null;
        }

        battlefield["units"]!.AsArray().Add(card);
        return state;
    }

    private static JsonObject? Predict(JsonObject state, InternalGameAction action)
    {
        var count = ReadInt(action.Payload, "count");
        var order = ReadIntArray(action.Payload, "order");
        var deck = FindPlayer(state, action.PlayerId)?["deck"]?.AsArray();
        if (count is null || count.Value <= 0 || deck is null || deck.Count < count.Value || order.Length != count.Value)
        {
            return null;
        }

        if (order.Any(index => index < 0 || index >= count.Value) || order.Distinct().Count() != count.Value)
        {
            return null;
        }

        var top = deck.Take(count.Value).Select(card => card?.DeepClone()).ToArray();
        for (var i = 0; i < count.Value; i++)
        {
            deck[i] = top[order[i]];
        }

        return state;
    }

    private static JsonObject? Attach(JsonObject state, InternalGameAction action)
    {
        var gearId = ReadString(action.Payload, "gearId");
        var unitId = ReadString(action.Payload, "unitId");
        if (string.IsNullOrWhiteSpace(gearId) || string.IsNullOrWhiteSpace(unitId) || FindUnit(state, unitId) is not { } unit)
        {
            return null;
        }

        var gear = FindGear(state, gearId);
        if (gear is null || gear.Gear["ownerId"]?.GetValue<int>() != action.PlayerId || gear.Gear["attachedUnitId"] is not null)
        {
            return null;
        }

        gear.Gear["attachedUnitId"] = unitId;
        unit.Unit["attachedMight"] = (unit.Unit["attachedMight"]?.GetValue<int>() ?? 0) + (gear.Gear["might"]?.GetValue<int>() ?? 0);
        return state;
    }

    private static JsonObject? Detach(JsonObject state, InternalGameAction action)
    {
        var gearId = ReadString(action.Payload, "gearId");
        if (string.IsNullOrWhiteSpace(gearId) || FindGear(state, gearId) is not { } gear)
        {
            return null;
        }

        var unitId = gear.Gear["attachedUnitId"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(unitId) || FindUnit(state, unitId) is not { } unit)
        {
            return null;
        }

        unit.Unit["attachedMight"] = (unit.Unit["attachedMight"]?.GetValue<int>() ?? 0) - (gear.Gear["might"]?.GetValue<int>() ?? 0);
        gear.Gear["attachedUnitId"] = null;
        return state;
    }

    private static JsonObject? Swap(JsonObject state, InternalGameAction action)
    {
        var firstUnitId = ReadString(action.Payload, "firstUnitId");
        var secondUnitId = ReadString(action.Payload, "secondUnitId");
        if (string.IsNullOrWhiteSpace(firstUnitId) || string.IsNullOrWhiteSpace(secondUnitId) || firstUnitId == secondUnitId)
        {
            return null;
        }

        var first = FindUnit(state, firstUnitId);
        var second = FindUnit(state, secondUnitId);
        if (first is null || second is null)
        {
            return null;
        }

        var firstIndex = IndexOf(first.Container, first.Unit);
        var secondIndex = IndexOf(second.Container, second.Unit);
        if (firstIndex < 0 || secondIndex < 0)
        {
            return null;
        }

        var firstClone = Clone(first.Unit);
        var secondClone = Clone(second.Unit);
        firstClone["location"] = second.Unit["location"]?.DeepClone();
        secondClone["location"] = first.Unit["location"]?.DeepClone();

        if (ReferenceEquals(first.Container, second.Container))
        {
            first.Container[firstIndex] = secondClone;
            first.Container[secondIndex] = firstClone;
        }
        else
        {
            first.Container[firstIndex] = secondClone;
            second.Container[secondIndex] = firstClone;
        }

        return state;
    }

    private static JsonObject? Double(JsonObject state, InternalGameAction action)
    {
        var unitId = ReadString(action.Payload, "unitId");
        if (string.IsNullOrWhiteSpace(unitId) || FindUnit(state, unitId) is not { } unit)
        {
            return null;
        }

        var might = unit.Unit["might"]?.GetValue<int>() ?? 0;
        var attachedMight = unit.Unit["attachedMight"]?.GetValue<int>() ?? 0;
        unit.Unit["attachedMight"] = attachedMight + might + attachedMight;
        return state;
    }

    private static JsonObject? Recall(JsonObject state, InternalGameAction action)
    {
        var unitId = ReadString(action.Payload, "unitId");
        if (string.IsNullOrWhiteSpace(unitId) || FindUnit(state, unitId) is not { } located)
        {
            return null;
        }

        DetachGearFromUnit(state, unitId);
        var ownerId = located.Unit["ownerId"]?.GetValue<int>() ?? action.PlayerId;
        located.Container.Remove(located.Unit);
        FindPlayer(state, ownerId)?["hand"]!.AsArray().Add(CardWithoutRuntimeState(located.Unit));
        return state;
    }

    private static void DetachGearFromUnit(JsonObject state, string unitId)
    {
        foreach (var gear in state["players"]!.AsArray()
            .Select(player => player!["baseGear"]?.AsArray())
            .Where(baseGear => baseGear is not null)
            .SelectMany(baseGear => baseGear!)
            .Select(node => node!.AsObject()))
        {
            if (gear["attachedUnitId"]?.GetValue<string>() == unitId)
            {
                gear["attachedUnitId"] = null;
            }
        }
    }

    private static JsonArray? Zone(JsonObject? player, string? zone)
    {
        return zone switch
        {
            "deck" => player?["deck"]?.AsArray(),
            "hand" => player?["hand"]?.AsArray(),
            "trash" => player?["trash"]?.AsArray(),
            _ => null
        };
    }

    private static JsonObject? FindPlayer(JsonObject state, int playerId)
    {
        return state["players"]?.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(candidate => candidate["id"]?.GetValue<int>() == playerId);
    }

    private static JsonObject? FindBattlefield(JsonObject state, string? battlefieldId)
    {
        if (string.IsNullOrWhiteSpace(battlefieldId))
        {
            return null;
        }

        return state["battlefields"]?.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(candidate => string.Equals(candidate["id"]?.GetValue<string>(), battlefieldId, StringComparison.OrdinalIgnoreCase));
    }

    private static LocatedUnit? FindUnit(JsonObject state, string unitId)
    {
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            var units = player["base"]!.AsArray();
            foreach (var unit in units.Select(node => node!.AsObject()))
            {
                if (unit["uid"]?.GetValue<string>() == unitId)
                {
                    return new LocatedUnit(unit, units);
                }
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var units = battlefield["units"]!.AsArray();
            foreach (var unit in units.Select(node => node!.AsObject()))
            {
                if (unit["uid"]?.GetValue<string>() == unitId)
                {
                    return new LocatedUnit(unit, units);
                }
            }
        }

        return null;
    }

    private static LocatedGear? FindGear(JsonObject state, string gearId)
    {
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            var baseGear = player["baseGear"]!.AsArray();
            foreach (var gear in baseGear.Select(node => node!.AsObject()))
            {
                if (gear["uid"]?.GetValue<string>() == gearId)
                {
                    return new LocatedGear(gear, baseGear);
                }
            }
        }

        return null;
    }

    private static JsonObject CardWithoutRuntimeState(JsonObject card)
    {
        var copy = Clone(card);
        foreach (var key in RuntimeCardKeys)
        {
            copy.Remove(key);
        }

        return copy;
    }

    private static JsonArray EnsureArray(JsonObject state, string key)
    {
        if (state[key] is JsonArray array)
        {
            return array;
        }

        array = new JsonArray();
        state[key] = array;
        return array;
    }

    private static void ShuffleInPlace(JsonObject state, IList<JsonNode?> cards)
    {
        for (var index = cards.Count - 1; index > 0; index--)
        {
            var swapIndex = NextRandomIndex(state, index + 1);
            (cards[index], cards[swapIndex]) = (cards[swapIndex], cards[index]);
        }
    }

    private static int NextRandomIndex(JsonObject state, int maxExclusive)
    {
        var current = state["rngState"]?.GetValue<int>() ?? 1;
        var next = unchecked(current * 1664525 + 1013904223) & int.MaxValue;
        state["rngState"] = next;
        return maxExclusive <= 1 ? 0 : next % maxExclusive;
    }

    private static int IndexOf(JsonArray array, JsonObject item)
    {
        for (var i = 0; i < array.Count; i++)
        {
            if (ReferenceEquals(array[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    private static InternalGameActionResult Reject(JsonObject state, string message)
    {
        return new InternalGameActionResult(false, message, state);
    }

    private static JsonObject Clone(JsonObject value)
    {
        return value.DeepClone().AsObject();
    }

    private static int? ReadInt(IReadOnlyDictionary<string, object?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            JsonElement element when element.TryGetInt32(out var intValue) => intValue,
            int intValue => intValue,
            _ => null
        };
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, object?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            bool boolValue => boolValue,
            _ => null
        };
    }

    private static int[] ReadIntArray(IReadOnlyDictionary<string, object?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        return value switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.Array => element.Deserialize<int[]>(JsonOptions) ?? [],
            IEnumerable<int> values => values.ToArray(),
            _ => []
        };
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => null
        };
    }

    private sealed record LocatedUnit(JsonObject Unit, JsonArray Container);

    private sealed record LocatedGear(JsonObject Gear, JsonArray Container);
}
