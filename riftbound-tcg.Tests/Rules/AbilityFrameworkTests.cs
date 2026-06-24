using System.Text.Json.Nodes;
using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Tests.Rules;

public class AbilityFrameworkTests
{
    [Test]
    public void triggered_abilities_are_collected_and_queued_after_matching_events()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("watcher", "Watcher", 0, "unit-watcher", Triggered("after-action", "action-applied", Effect("draw", 1))));

        var result = engine.ApplyAction(state, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.State["chainWindow"], Is.Not.Null);
        Assert.That(result.State.State["effectStack"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(result.State.State["effectStack"]![0]!["kind"]!.GetValue<string>(), Is.EqualTo("ability"));
        Assert.That(AbilityEvents(result.State, "trigger-collected"), Has.Some.EqualTo("after-action"));
    }

    [Test]
    public void delayed_triggered_abilities_fire_once_when_their_event_occurs()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        state.State["delayedAbilities"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "delayed-draw",
            ["kind"] = "delayed-triggered",
            ["event"] = "action-applied",
            ["playerId"] = 0,
            ["sourceUid"] = "delayed-source",
            ["sourceCardId"] = "delayed-source-card",
            ["sourceName"] = "Delayed Source",
            ["consume"] = true,
            ["effect"] = Effect("draw", 1)
        });

        var result = engine.ApplyAction(state, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.State["delayedAbilities"]!.AsArray(), Is.Empty);
        Assert.That(result.State.State["effectStack"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(AbilityEvents(result.State, "delayed-fired"), Has.Some.EqualTo("delayed-draw"));
    }

    [Test]
    public void replacement_abilities_intercept_matching_score_events()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var battlefield = state.State["battlefields"]![0]!.AsObject();
        battlefield["controllerId"] = 0;
        state.State["turnPhase"] = "beginning";
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("steward", "Steward", 0, "unit-steward", Replacement("score-bonus", "score-point", Effect("modify-amount", 1))));

        var result = engine.ApplyAction(state, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(PlayerPoints(result.State, 0), Is.EqualTo(2));
        Assert.That(AbilityEvents(result.State, "replacement-applied"), Has.Some.EqualTo("score-bonus"));
    }

    [Test]
    public void activated_ability_requires_its_cost_before_it_is_legal()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var source = Unit("focus-engine", "Focus Engine", 0, "unit-focus-engine", Activated("draw-focus", Effect("draw", 1), exhaust: true, runes: 1));
        source["exhausted"] = true;
        FindPlayer(state, 0)["base"]!.AsArray().Add(source);

        Assert.That(engine.GetLegalActions(state, 0).Select(action => action.Type), Has.None.EqualTo("activate-ability"));

        source["exhausted"] = false;
        var legal = engine.GetLegalActions(state, 0);
        Assert.That(legal.Select(action => action.Type), Contains.Item("activate-ability"));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "activate-ability", new Dictionary<string, object?> { ["sourceUid"] = "unit-focus-engine", ["abilityId"] = "draw-focus" }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(FindUnit(result.State, "unit-focus-engine")!["exhausted"]!.GetValue<bool>(), Is.True);
        Assert.That(result.State.State["effectStack"]!.AsArray(), Has.Count.EqualTo(1));
    }

    [Test]
    public void passive_abilities_contribute_to_unit_state()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("standard", "Standard", 0, "unit-standard", Passive("line-drill", Effect("modify-own-units-might", 2))));
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("vanguard", "Vanguard", 0, "unit-vanguard"));

        var result = engine.ApplyAction(state, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.State["abilityContributions"]!["unit-vanguard"]!["might"]!.GetValue<int>(), Is.EqualTo(2));
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

    private static JsonObject FindPlayer(EngineMatchState state, int playerId)
    {
        return state.State["players"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(player => player["id"]!.GetValue<int>() == playerId);
    }

    private static JsonObject? FindUnit(EngineMatchState state, string uid)
    {
        return state.State["players"]!.AsArray()
            .Select(node => node!.AsObject())
            .SelectMany(player => player["base"]!.AsArray().Select(card => card!.AsObject()))
            .Concat(state.State["battlefields"]!.AsArray().Select(node => node!.AsObject()).SelectMany(field => field["units"]!.AsArray().Select(card => card!.AsObject())))
            .FirstOrDefault(unit => unit["uid"]?.GetValue<string>() == uid);
    }

    private static string[] AbilityEvents(EngineMatchState state, string type)
    {
        return state.State["abilityEvents"]!.AsArray()
            .Select(node => node!.AsObject())
            .Where(evt => evt["type"]?.GetValue<string>() == type)
            .Select(evt => evt["abilityId"]?.GetValue<string>() ?? string.Empty)
            .ToArray();
    }

    private static int PlayerPoints(EngineMatchState state, int playerId) =>
        FindPlayer(state, playerId)["points"]!.GetValue<int>();

    private static JsonObject Unit(string id, string name, int ownerId, string uid, params JsonObject[] abilities)
    {
        return new JsonObject
        {
            ["id"] = $"{id}-test",
            ["catalogId"] = id,
            ["name"] = name,
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
            ["effect"] = Effect("rally", 0),
            ["uid"] = uid,
            ["ownerId"] = ownerId,
            ["exhausted"] = false,
            ["damage"] = 0,
            ["attachedMight"] = 0,
            ["attacker"] = false,
            ["defender"] = false,
            ["abilities"] = ToArray(abilities)
        };
    }

    private static JsonObject Triggered(string id, string evt, JsonObject effect) =>
        Ability(id, "triggered", effect, evt);

    private static JsonObject Replacement(string id, string evt, JsonObject effect) =>
        Ability(id, "replacement", effect, evt);

    private static JsonObject Passive(string id, JsonObject effect) =>
        Ability(id, "passive", effect);

    private static JsonObject Activated(string id, JsonObject effect, bool exhaust, int runes)
    {
        var ability = Ability(id, "activated", effect);
        ability["cost"] = new JsonObject { ["exhaust"] = exhaust, ["runes"] = runes };
        return ability;
    }

    private static JsonObject Ability(string id, string kind, JsonObject effect, string? evt = null)
    {
        var ability = new JsonObject
        {
            ["id"] = id,
            ["kind"] = kind,
            ["label"] = id,
            ["effect"] = effect
        };
        if (evt is not null)
        {
            ability["event"] = evt;
        }

        return ability;
    }

    private static JsonObject Effect(string type, int amount) =>
        new() { ["type"] = type, ["amount"] = amount };

    private static JsonArray ToArray(IEnumerable<JsonObject> nodes)
    {
        var array = new JsonArray();
        foreach (var node in nodes)
        {
            array.Add(node);
        }

        return array;
    }

    private static EngineMatchConfig Config()
    {
        return new EngineMatchConfig(
            "match-demo-001",
            "duel-1v1",
            [
                new EngineSeatConfig(0, "user-demo-001", "Demo One", 0),
                new EngineSeatConfig(1, "user-demo-002", "Demo Two", 1)
            ],
            ["skybridge", "emberfield"],
            0);
    }

    private static IReadOnlyList<EnginePlayerDeck> Decks()
    {
        return
        [
            new EnginePlayerDeck("deck-a", "legend-a", "champion-a", ["skybridge"], ["rune-a", "rune-a", "rune-a"], ["unit-a", "unit-b", "unit-c", "unit-d", "unit-e"]),
            new EnginePlayerDeck("deck-b", "legend-b", "champion-b", ["emberfield"], ["rune-b", "rune-b", "rune-b"], ["unit-f", "unit-g", "unit-h", "unit-i", "unit-j"])
        ];
    }
}
