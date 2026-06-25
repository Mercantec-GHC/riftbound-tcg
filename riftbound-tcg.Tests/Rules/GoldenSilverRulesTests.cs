using System.Text.Json.Nodes;
using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Tests.Rules;

public class GoldenSilverRulesTests
{
    [Test]
    public void card_text_can_grant_play_permission_but_cannot_modifier_removes_legal_action()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        OpenChainWindow(state, priorityPlayerId: 0);
        Player(state, 0)["hand"] = new JsonArray(CardWithEffect(
            "window-tactic",
            "Window Tactic",
            "spell",
            "draw",
            1,
            RuleModifier("self-chain-permission", "can", "play-card", timing: "chain")));

        var permitted = engine.GetLegalActions(state, 0)
            .SingleOrDefault(action => action.Type == "play-card" && action.PayloadSchema?["handIndex"]?.GetValue<int>() == 0);

        Assert.That(permitted, Is.Not.Null);

        Player(state, 0)["base"]!.AsArray().Add(UnitWithModifiers(
            "lock-unit",
            0,
            RuleModifier("controller-chain-lock", "cannot", "play-card", appliesTo: "controller", timing: "chain")));

        var restricted = engine.GetLegalActions(state, 0)
            .Where(action => action.Type == "play-card" && action.PayloadSchema?["handIndex"]?.GetValue<int>() == 0)
            .ToArray();

        Assert.That(restricted, Is.Empty);
    }

    [Test]
    public void cannot_modifier_rejects_payload_validated_play_unit_attempt()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        Player(state, 0)["hand"] = new JsonArray(Card("blocked-unit", "Blocked Unit", "unit", 0, 1));
        Player(state, 0)["base"]!.AsArray().Add(UnitWithModifiers(
            "mustering-lock",
            0,
            RuleModifier("no-unit-play", "cannot", "play-unit", appliesTo: "controller", timing: "main")));

        Assert.That(engine.GetLegalActions(state, 0).Select(action => action.Type), Has.None.EqualTo("play-unit"));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
        Assert.That(Player(result.State, 0)["hand"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(Player(result.State, 0)["base"]!.AsArray().Select(card => card!["catalogId"]?.GetValue<string>()), Has.None.EqualTo("blocked-unit"));
    }

    [Test]
    public void stack_effect_resolution_does_as_much_as_possible_when_one_target_becomes_impossible()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        var battlefield = Battlefield(state, 0);
        var battlefieldId = battlefield["id"]!.GetValue<string>();
        battlefield["units"] = new JsonArray(
            BattlefieldUnit("enemy-a", 1, battlefieldId),
            BattlefieldUnit("enemy-b", 1, battlefieldId));
        Player(state, 0)["hand"] = new JsonArray(CardWithEffect("arc-line", "Arc Line", "spell", "damage", 2));

        var played = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-card", new Dictionary<string, object?>
            {
                ["handIndex"] = 0,
                ["targetLaneId"] = battlefieldId
            }),
            state.SequenceNumber);

        Assert.That(played.Accepted, Is.True);
        Assert.That(played.State.State["effectStack"]![0]!["targets"]!.AsArray(), Has.Count.EqualTo(2));

        Battlefield(played.State, 0)["units"]!.AsArray().RemoveAt(0);

        var afterControllerPass = engine.ApplyAction(
            played.State,
            new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()),
            played.State.SequenceNumber);
        var resolved = engine.ApplyAction(
            afterControllerPass.State,
            new EngineGameAction(1, "pass-chain-window", new Dictionary<string, object?>()),
            afterControllerPass.State.SequenceNumber);

        Assert.That(resolved.Accepted, Is.True);
        Assert.That(Battlefield(resolved.State, 0)["units"]!.AsArray(), Is.Empty);
        Assert.That(Player(resolved.State, 1)["trash"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Contains.Item("enemy-b-card"));
        Assert.That(Player(resolved.State, 1)["trash"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Has.None.EqualTo("enemy-a-card"));
        Assert.That(Player(resolved.State, 0)["trash"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Contains.Item("arc-line"));
    }

    private static EngineMatchState ReadyMainState(DefaultRulesEngine engine)
    {
        var state = engine.CreateInitialState(Config(), Decks(), 123);
        state.State["stage"] = "playing";
        state.State["turnPhase"] = "main";
        state.State["turnPlayerId"] = 0;
        state.State["activePlayer"] = 0;
        return state;
    }

    private static void OpenChainWindow(EngineMatchState state, int priorityPlayerId)
    {
        state.State["chainWindow"] = new JsonObject
        {
            ["priorityPlayerId"] = priorityPlayerId,
            ["startedByPlayerId"] = priorityPlayerId,
            ["source"] = "played-card",
            ["passesFocusOnClose"] = true,
            ["passedByPlayer"] = new JsonObject()
        };
        state.State["priorityPlayerId"] = priorityPlayerId;
        state.State["activePlayer"] = priorityPlayerId;
    }

    private static JsonObject Card(string id, string name, string kind, int cost, int might, params JsonObject[] ruleModifiers) =>
        new()
        {
            ["id"] = $"{id}-test",
            ["catalogId"] = id,
            ["name"] = name,
            ["kind"] = kind,
            ["tags"] = new JsonArray(),
            ["domain"] = "Fury",
            ["domains"] = new JsonArray("Fury"),
            ["cost"] = cost,
            ["might"] = might,
            ["text"] = string.Empty,
            ["image"] = string.Empty,
            ["cardType"] = kind,
            ["supertype"] = null,
            ["keywords"] = new JsonArray(),
            ["continuousEffects"] = new JsonArray(),
            ["ruleModifiers"] = ToArray(ruleModifiers),
            ["effect"] = new JsonObject { ["type"] = "rally", ["amount"] = 0 }
        };

    private static JsonObject CardWithEffect(string id, string name, string kind, string effectType, int amount, params JsonObject[] ruleModifiers)
    {
        var card = Card(id, name, kind, 0, 0, ruleModifiers);
        card["effect"] = new JsonObject { ["type"] = effectType, ["amount"] = amount };
        return card;
    }

    private static JsonObject UnitWithModifiers(string uid, int ownerId, params JsonObject[] ruleModifiers)
    {
        var unit = Card($"{uid}-card", uid, "unit", 0, 1, ruleModifiers);
        unit["uid"] = uid;
        unit["ownerId"] = ownerId;
        unit["controllerId"] = ownerId;
        unit["location"] = new JsonObject { ["type"] = "base", ["battlefieldId"] = null, ["attachedToUid"] = null };
        unit["damage"] = 0;
        unit["attachedMight"] = 0;
        unit["exhausted"] = false;
        unit["attacker"] = false;
        unit["defender"] = false;
        unit["isToken"] = false;
        unit["isFaceDown"] = false;
        unit["rulesTextActive"] = true;
        unit["attachedCards"] = new JsonArray();
        return unit;
    }

    private static JsonObject BattlefieldUnit(string uid, int ownerId, string battlefieldId)
    {
        var unit = UnitWithModifiers(uid, ownerId);
        unit["location"] = new JsonObject { ["type"] = "battlefield", ["battlefieldId"] = battlefieldId, ["attachedToUid"] = null };
        return unit;
    }

    private static JsonObject RuleModifier(
        string id,
        string polarity,
        string actionType,
        string appliesTo = "self",
        string? timing = null,
        string? destination = null,
        string? effectType = null,
        string? target = null,
        string? cardKind = null)
    {
        var modifier = new JsonObject
        {
            ["id"] = id,
            ["polarity"] = polarity,
            ["actionType"] = actionType,
            ["appliesTo"] = appliesTo
        };

        if (!string.IsNullOrWhiteSpace(timing)) modifier["timing"] = timing;
        if (!string.IsNullOrWhiteSpace(destination)) modifier["destination"] = destination;
        if (!string.IsNullOrWhiteSpace(effectType)) modifier["effectType"] = effectType;
        if (!string.IsNullOrWhiteSpace(target)) modifier["target"] = target;
        if (!string.IsNullOrWhiteSpace(cardKind)) modifier["cardKind"] = cardKind;
        return modifier;
    }

    private static JsonArray ToArray(params JsonObject[] nodes)
    {
        var array = new JsonArray();
        foreach (var node in nodes)
        {
            array.Add(node);
        }

        return array;
    }

    private static JsonObject Player(EngineMatchState state, int playerId) =>
        state.State["players"]!.AsArray().First(player => player!["id"]!.GetValue<int>() == playerId)!.AsObject();

    private static JsonObject Battlefield(EngineMatchState state, int index) =>
        state.State["battlefields"]!.AsArray()[index]!.AsObject();

    private static EngineMatchConfig Config() =>
        new(
            "golden-silver-test",
            "duel-1v1",
            [new EngineSeatConfig(0, "user-0", "Player 0", 0), new EngineSeatConfig(1, "user-1", "Player 1", 1)],
            ["field-a", "field-b"],
            0);

    private static IReadOnlyList<EnginePlayerDeck> Decks() =>
        [
            new EnginePlayerDeck("deck-a", "legend-a", "champion-a", ["field-a"], ["rune-a", "rune-a", "rune-a"], ["unit-a", "unit-b", "unit-c", "unit-d"]),
            new EnginePlayerDeck("deck-b", "legend-b", "champion-b", ["field-b"], ["rune-b", "rune-b", "rune-b"], ["unit-e", "unit-f", "unit-g", "unit-h"])
        ];
}
