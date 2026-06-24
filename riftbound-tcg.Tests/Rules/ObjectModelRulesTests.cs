using System.Text.Json.Nodes;
using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Tests.Rules;

public class ObjectModelRulesTests
{
    [Test]
    public void token_disappears_instead_of_entering_owner_zones_when_banished()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var created = engine.ApplyAction(
            state,
            new EngineGameAction(0, "create-token", new Dictionary<string, object?> { ["cardId"] = "spark-token", ["name"] = "Spark" }),
            state.SequenceNumber);
        Assert.That(created.Accepted, Is.True);

        var tokenUid = Player(created.State, 0)["base"]!.AsArray().Single()!["uid"]!.GetValue<string>();
        var banished = engine.ApplyAction(
            created.State,
            new EngineGameAction(0, "banish-object", new Dictionary<string, object?> { ["objectUid"] = tokenUid }),
            created.State.SequenceNumber);

        Assert.That(banished.Accepted, Is.True);
        Assert.That(Player(banished.State, 0)["base"]!.AsArray(), Is.Empty);
        Assert.That(Player(banished.State, 0)["banished"]!.AsArray(), Is.Empty);
    }

    [Test]
    public void attached_card_detaches_to_its_owner_even_when_attached_to_enemy_unit()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var opponentUnit = Unit("enemy-unit", "enemy-card", 1);
        Player(state, 1)["base"]!.AsArray().Add(opponentUnit);
        PutCardInHand(state, 0, Gear("hook-a", "Hook A"));

        var attached = engine.ApplyAction(
            state,
            new EngineGameAction(0, "attach-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["targetUnitId"] = "enemy-unit" }),
            state.SequenceNumber);
        Assert.That(attached.Accepted, Is.True);

        var target = Player(attached.State, 1)["base"]!.AsArray().Single()!.AsObject();
        var attachedCard = target["attachedCards"]!.AsArray().Single()!.AsObject();
        Assert.That(attachedCard["ownerId"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(target["ownerId"]!.GetValue<int>(), Is.EqualTo(1));

        var detached = engine.ApplyAction(
            attached.State,
            new EngineGameAction(0, "detach-card", new Dictionary<string, object?> { ["attachedCardUid"] = attachedCard["uid"]!.GetValue<string>() }),
            attached.State.SequenceNumber);

        Assert.That(detached.Accepted, Is.True);
        Assert.That(Player(detached.State, 1)["base"]!.AsArray().Single()!["attachedCards"]!.AsArray(), Is.Empty);
        var ownerGear = Player(detached.State, 0)["baseGear"]!.AsArray().Single()!.AsObject();
        Assert.That(ownerGear["ownerId"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(ownerGear["location"]!["type"]!.GetValue<string>(), Is.EqualTo("base"));
    }

    [Test]
    public void facedown_objects_disable_rules_text_until_turned_faceup()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var created = engine.ApplyAction(state, new EngineGameAction(0, "create-token", new Dictionary<string, object?>()), state.SequenceNumber);
        var uid = Player(created.State, 0)["base"]!.AsArray().Single()!["uid"]!.GetValue<string>();

        var facedown = engine.ApplyAction(
            created.State,
            new EngineGameAction(0, "set-facedown", new Dictionary<string, object?> { ["objectUid"] = uid, ["faceDown"] = true }),
            created.State.SequenceNumber);
        Assert.That(facedown.Accepted, Is.True);

        var hidden = Player(facedown.State, 0)["base"]!.AsArray().Single()!.AsObject();
        Assert.That(hidden["isFaceDown"]!.GetValue<bool>(), Is.True);
        Assert.That(hidden["rulesTextActive"]!.GetValue<bool>(), Is.False);

        var faceup = engine.ApplyAction(
            facedown.State,
            new EngineGameAction(0, "set-facedown", new Dictionary<string, object?> { ["objectUid"] = uid, ["faceDown"] = false }),
            facedown.State.SequenceNumber);
        var revealed = Player(faceup.State, 0)["base"]!.AsArray().Single()!.AsObject();
        Assert.That(revealed["isFaceDown"]!.GetValue<bool>(), Is.False);
        Assert.That(revealed["rulesTextActive"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void banishing_non_token_permanent_moves_it_to_owner_banished_zone()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var played = engine.ApplyAction(state, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        Assert.That(played.Accepted, Is.True);
        var uid = Player(played.State, 0)["base"]!.AsArray().Single()!["uid"]!.GetValue<string>();

        var banished = engine.ApplyAction(
            played.State,
            new EngineGameAction(0, "banish-object", new Dictionary<string, object?> { ["objectUid"] = uid }),
            played.State.SequenceNumber);

        Assert.That(banished.Accepted, Is.True);
        Assert.That(Player(banished.State, 0)["base"]!.AsArray(), Is.Empty);
        var banishedObject = Player(banished.State, 0)["banished"]!.AsArray().Single()!.AsObject();
        Assert.That(banishedObject["uid"]!.GetValue<string>(), Is.EqualTo(uid));
        Assert.That(banishedObject["location"]!["type"]!.GetValue<string>(), Is.EqualTo("banished"));
    }

    [Test]
    public void top_card_reference_tracks_the_last_attached_card()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var played = engine.ApplyAction(state, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        var unit = Player(played.State, 0)["base"]!.AsArray().Single()!.AsObject();
        var unitUid = unit["uid"]!.GetValue<string>();
        var unitCardId = unit["id"]!.GetValue<string>();
        Assert.That(unit["topCardId"]!.GetValue<string>(), Is.EqualTo(unitCardId));

        PutCardInHand(played.State, 0, Gear("hook-a", "Hook A"), Gear("hook-b", "Hook B"));
        var firstAttach = engine.ApplyAction(
            played.State,
            new EngineGameAction(0, "attach-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["targetUnitId"] = unitUid }),
            played.State.SequenceNumber);
        var secondAttach = engine.ApplyAction(
            firstAttach.State,
            new EngineGameAction(0, "attach-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["targetUnitId"] = unitUid }),
            firstAttach.State.SequenceNumber);

        var updatedUnit = Player(secondAttach.State, 0)["base"]!.AsArray().Single()!.AsObject();
        var topAttachedCardId = updatedUnit["attachedCards"]!.AsArray().Last()!["id"]!.GetValue<string>();
        Assert.That(updatedUnit["topCardId"]!.GetValue<string>(), Is.EqualTo(topAttachedCardId));
    }

    private static EngineMatchState ReachMainPhase(DefaultRulesEngine engine)
    {
        var state = engine.CreateInitialState(Config(), Decks(), 123);
        var afterFirst = engine.ApplyAction(state, new EngineGameAction(0, "confirm-mulligan", new Dictionary<string, object?>()), 0);
        var afterSecond = engine.ApplyAction(afterFirst.State, new EngineGameAction(1, "confirm-mulligan", new Dictionary<string, object?>()), 1);

        var current = afterSecond.State;
        while (current.State["turnPhase"]?.GetValue<string>() != "main")
        {
            var turnPlayerId = current.State["turnPlayerId"]!.GetValue<int>();
            current = engine.ApplyAction(current, new EngineGameAction(turnPlayerId, "advance-phase", new Dictionary<string, object?>()), current.SequenceNumber).State;
        }

        return current;
    }

    private static JsonObject Player(EngineMatchState state, int playerId) =>
        state.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == playerId)!.AsObject();

    private static void PutCardInHand(EngineMatchState state, int playerId, params JsonObject[] cards)
    {
        var hand = Player(state, playerId)["hand"]!.AsArray();
        hand.Clear();
        foreach (var card in cards)
        {
            hand.Add(card);
        }
    }

    private static JsonObject Gear(string id, string name) => new()
    {
        ["id"] = $"{id}-test",
        ["catalogId"] = id,
        ["name"] = name,
        ["kind"] = "gear",
        ["tags"] = new JsonArray(),
        ["domain"] = "Fury",
        ["domains"] = new JsonArray("Fury"),
        ["cost"] = 0,
        ["might"] = 0,
        ["text"] = string.Empty,
        ["image"] = string.Empty,
        ["cardType"] = "Gear",
        ["supertype"] = null,
        ["effect"] = new JsonObject { ["type"] = "rally", ["amount"] = 0 }
    };

    private static JsonObject Unit(string uid, string id, int ownerId) => new()
    {
        ["id"] = id,
        ["catalogId"] = id,
        ["name"] = id,
        ["kind"] = "unit",
        ["tags"] = new JsonArray(),
        ["domain"] = "Fury",
        ["domains"] = new JsonArray("Fury"),
        ["cost"] = 0,
        ["might"] = 1,
        ["text"] = string.Empty,
        ["image"] = string.Empty,
        ["cardType"] = "Unit",
        ["supertype"] = null,
        ["effect"] = new JsonObject { ["type"] = "rally", ["amount"] = 0 },
        ["uid"] = uid,
        ["ownerId"] = ownerId,
        ["controllerId"] = ownerId,
        ["location"] = new JsonObject { ["type"] = "base", ["battlefieldId"] = null, ["attachedToUid"] = null },
        ["exhausted"] = false,
        ["damage"] = 0,
        ["attachedMight"] = 0,
        ["attacker"] = false,
        ["defender"] = false,
        ["isToken"] = false,
        ["isFaceDown"] = false,
        ["rulesTextActive"] = true,
        ["attachedCards"] = new JsonArray(),
        ["topCardId"] = id
    };

    private static EngineMatchConfig Config() => new(
        "match-object-model",
        "duel-1v1",
        [
            new EngineSeatConfig(0, "user-a", "Player A", 0),
            new EngineSeatConfig(1, "user-b", "Player B", 1)
        ],
        ["skybridge", "emberfield"],
        0);

    private static IReadOnlyList<EnginePlayerDeck> Decks() =>
    [
        new EnginePlayerDeck("deck-a", "legend-a", "champion-a", ["skybridge"], ["rune-a", "rune-a", "rune-a"], ["unit-a", "unit-b", "unit-c", "unit-d", "unit-e"]),
        new EnginePlayerDeck("deck-b", "legend-b", "champion-b", ["emberfield"], ["rune-b", "rune-b", "rune-b"], ["unit-f", "unit-g", "unit-h", "unit-i", "unit-j"])
    ];
}
