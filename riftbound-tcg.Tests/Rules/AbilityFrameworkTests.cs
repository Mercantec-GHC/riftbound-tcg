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
    public void simultaneous_triggers_controlled_by_one_player_require_an_order_choice()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("first-source", "First Source", 0, "unit-first", Triggered("first-trigger", "action-applied", Effect("draw", 1))));
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("second-source", "Second Source", 0, "unit-second", Triggered("second-trigger", "action-applied", Effect("draw", 1))));

        var result = engine.ApplyAction(state, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.State["chainWindow"], Is.Null);
        Assert.That(result.State.State["effectStack"]!.AsArray(), Is.Empty);
        var legal = engine.GetLegalActions(result.State, 0).Single(action => action.Type == "order-triggered-abilities");
        Assert.That(legal.PayloadSchema?["abilityIds"]!.AsArray().Select(id => id!.GetValue<string>()), Is.EqualTo(new[] { "unit-first:first-trigger:0", "unit-second:second-trigger:1" }));
    }

    [Test]
    public void triggered_ability_order_rejects_missing_duplicate_or_wrong_controller_submissions()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("first-source", "First Source", 0, "unit-first", Triggered("first-trigger", "action-applied", Effect("draw", 1))));
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("second-source", "Second Source", 0, "unit-second", Triggered("second-trigger", "action-applied", Effect("draw", 1))));

        var pending = engine.ApplyAction(state, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), state.SequenceNumber).State;
        var groupId = engine.GetLegalActions(pending, 0).Single(action => action.Type == "order-triggered-abilities").PayloadSchema!["groupId"]!.GetValue<string>();

        var wrongController = engine.ApplyAction(
            pending,
            new EngineGameAction(1, "order-triggered-abilities", new Dictionary<string, object?> { ["groupId"] = groupId, ["abilityIds"] = new[] { "unit-first:first-trigger:0", "unit-second:second-trigger:1" } }),
            pending.SequenceNumber);
        var missing = engine.ApplyAction(
            pending,
            new EngineGameAction(0, "order-triggered-abilities", new Dictionary<string, object?> { ["groupId"] = groupId, ["abilityIds"] = new[] { "unit-first:first-trigger:0" } }),
            pending.SequenceNumber);
        var duplicate = engine.ApplyAction(
            pending,
            new EngineGameAction(0, "order-triggered-abilities", new Dictionary<string, object?> { ["groupId"] = groupId, ["abilityIds"] = new[] { "unit-first:first-trigger:0", "unit-first:first-trigger:0" } }),
            pending.SequenceNumber);

        Assert.That(wrongController.Accepted, Is.False);
        Assert.That(missing.Accepted, Is.False);
        Assert.That(duplicate.Accepted, Is.False);
    }

    [Test]
    public void chosen_trigger_order_is_placed_on_the_chain_and_resolves_last_in_first_out()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("first-source", "First Source", 0, "unit-first", Triggered("first-trigger", "action-applied", Effect("draw", 1))));
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("second-source", "Second Source", 0, "unit-second", Triggered("second-trigger", "action-applied", Effect("draw", 1))));

        var pending = engine.ApplyAction(state, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), state.SequenceNumber).State;
        var groupId = engine.GetLegalActions(pending, 0).Single(action => action.Type == "order-triggered-abilities").PayloadSchema!["groupId"]!.GetValue<string>();
        var ordered = engine.ApplyAction(
            pending,
            new EngineGameAction(0, "order-triggered-abilities", new Dictionary<string, object?>
            {
                ["groupId"] = groupId,
                ["abilityIds"] = new[] { "unit-first:first-trigger:0", "unit-second:second-trigger:1" }
            }),
            pending.SequenceNumber);

        Assert.That(ordered.Accepted, Is.True);
        Assert.That(StackNames(ordered.State), Is.EqualTo(new[] { "Second Source", "First Source" }));
        var afterFirstPass = engine.ApplyAction(ordered.State, new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()), ordered.State.SequenceNumber);
        var afterSecondPass = engine.ApplyAction(afterFirstPass.State, new EngineGameAction(1, "pass-chain-window", new Dictionary<string, object?>()), afterFirstPass.State.SequenceNumber);
        Assert.That(afterSecondPass.State.State["log"]![0]!["text"]!.GetValue<string>(), Is.EqualTo("Second Source resolved."));
    }

    [Test]
    public void simultaneous_triggers_from_multiple_controllers_are_placed_starting_with_turn_player()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("turn-source", "Turn Source", 0, "unit-turn", Triggered("turn-trigger", "action-applied", Effect("draw", 1))));
        FindPlayer(state, 1)["base"]!.AsArray().Add(Unit("next-source", "Next Source", 1, "unit-next", Triggered("next-trigger", "action-applied", Effect("draw", 1))));

        var result = engine.ApplyAction(state, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(StackNames(result.State), Is.EqualTo(new[] { "Next Source", "Turn Source" }));
        Assert.That(result.State.State["chainWindow"]!["priorityPlayerId"]!.GetValue<int>(), Is.EqualTo(1));
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

    [Test]
    public void controlled_abilities_use_current_controller_not_card_owner()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var borrowed = Unit("borrowed", "Borrowed Relay", 1, "unit-borrowed", Activated("draw-borrowed", Effect("draw", 1), exhaust: false, runes: 0));
        borrowed["controllerId"] = 0;
        state.State["battlefields"]![0]!["units"]!.AsArray().Add(borrowed);

        Assert.That(engine.GetLegalActions(state, 0).Select(action => action.Type), Contains.Item("activate-ability"));
        Assert.That(engine.GetLegalActions(state, 1).Select(action => action.Type), Does.Not.Contain("activate-ability"));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "activate-ability", new Dictionary<string, object?> { ["sourceUid"] = "unit-borrowed", ["abilityId"] = "draw-borrowed" }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.State["effectStack"]![0]!["playerId"]!.GetValue<int>(), Is.EqualTo(0));
    }

    [Test]
    public void triggers_created_by_resolving_a_trigger_are_queued_before_older_chain_items_continue()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("action-drawer", "Action Drawer", 0, "unit-action-drawer", Triggered("draw-after-action", "action-applied", Effect("draw", 1))));
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("draw-watcher", "Draw Watcher", 0, "unit-draw-watcher", Triggered("draw-after-draw", "cards-drawn", Effect("draw", 1))));
        FindPlayer(state, 0)["deck"]!.AsArray().Add(DeckCard("nested-a"));
        FindPlayer(state, 0)["deck"]!.AsArray().Add(DeckCard("nested-b"));

        var triggered = engine.ApplyAction(state, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), state.SequenceNumber);
        var afterControllerPass = engine.ApplyAction(triggered.State, new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()), triggered.State.SequenceNumber);
        var afterResolve = engine.ApplyAction(afterControllerPass.State, new EngineGameAction(1, "pass-chain-window", new Dictionary<string, object?>()), afterControllerPass.State.SequenceNumber);

        Assert.That(afterResolve.Accepted, Is.True);
        Assert.That(StackNames(afterResolve.State), Is.EqualTo(new[] { "Draw Watcher" }));
        Assert.That(afterResolve.State.State["chainWindow"]!["priorityPlayerId"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(AbilityEvents(afterResolve.State, "trigger-collected"), Has.Some.EqualTo("draw-after-draw"));
    }

    [Test]
    public void modal_activated_abilities_validate_targets_for_selected_mode()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("modalist", "Modalist", 0, "unit-modalist", Modal("modal-burst",
            Mode("harm", Effect("damage", 2)),
            Mode("aid", Effect("buff", 2)))));
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("ally", "Ally", 0, "unit-ally"));
        FindPlayer(state, 1)["base"]!.AsArray().Add(Unit("enemy", "Enemy", 1, "unit-enemy"));

        var rejected = engine.ApplyAction(
            state,
            new EngineGameAction(0, "activate-ability", new Dictionary<string, object?> { ["sourceUid"] = "unit-modalist", ["abilityId"] = "modal-burst", ["modeId"] = "harm", ["targetUnitId"] = "unit-ally" }),
            state.SequenceNumber);
        var accepted = engine.ApplyAction(
            state,
            new EngineGameAction(0, "activate-ability", new Dictionary<string, object?> { ["sourceUid"] = "unit-modalist", ["abilityId"] = "modal-burst", ["modeId"] = "harm", ["targetUnitId"] = "unit-enemy" }),
            state.SequenceNumber);

        Assert.That(rejected.Accepted, Is.False);
        Assert.That(accepted.Accepted, Is.True);
        Assert.That(accepted.State.State["effectStack"]![0]!["targets"]!.AsArray().Single()!["unitId"]!.GetValue<string>(), Is.EqualTo("unit-enemy"));
    }

    [Test]
    public void delayed_replacements_can_be_scoped_and_used_multiple_times()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        state.State["turnPhase"] = "draw";
        FindPlayer(state, 0)["deck"]!.AsArray().Add(DeckCard("delayed-a"));
        FindPlayer(state, 0)["deck"]!.AsArray().Add(DeckCard("delayed-b"));
        FindPlayer(state, 0)["deck"]!.AsArray().Add(DeckCard("delayed-c"));
        FindPlayer(state, 0)["deck"]!.AsArray().Add(DeckCard("delayed-d"));
        state.State["delayedAbilities"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "double-next-two-draws",
            ["kind"] = "delayed-replacement",
            ["event"] = "draw-cards",
            ["playerId"] = 0,
            ["targetPlayerId"] = 0,
            ["sourceUid"] = "delayed-source",
            ["sourceCardId"] = "delayed-source-card",
            ["sourceName"] = "Delayed Source",
            ["uses"] = 2,
            ["effect"] = Effect("modify-amount", 1)
        });

        var first = engine.ApplyAction(state, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), state.SequenceNumber);
        first.State.State["turnPhase"] = "draw";
        var second = engine.ApplyAction(first.State, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), first.State.SequenceNumber);

        Assert.That(first.Accepted, Is.True);
        Assert.That(second.Accepted, Is.True);
        Assert.That(FindPlayer(second.State, 0)["hand"]!.AsArray(), Has.Count.EqualTo(9));
        Assert.That(second.State.State["delayedAbilities"]!.AsArray(), Is.Empty);
        Assert.That(AbilityEvents(second.State, "delayed-fired").Count(id => id == "double-next-two-draws"), Is.EqualTo(2));
    }

    [Test]
    public void priority_and_meta_actions_do_not_create_action_applied_triggers()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("watcher", "Watcher", 0, "unit-watcher", Triggered("after-action", "action-applied", Effect("draw", 1))));
        state.State["effectStack"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "manual-stack",
            ["card"] = Unit("manual", "Manual", 0, "unit-manual"),
            ["cardId"] = "manual",
            ["cardName"] = "Manual",
            ["kind"] = "ability",
            ["playerId"] = 0,
            ["effect"] = Effect("rally", 0)
        });
        state.State["chainWindow"] = new JsonObject
        {
            ["priorityPlayerId"] = 0,
            ["startedByPlayerId"] = 0,
            ["source"] = "triggered",
            ["passesFocusOnClose"] = false,
            ["passedByPlayer"] = new JsonObject()
        };

        var result = engine.ApplyAction(state, new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()), state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(AbilityEvents(result.State, "trigger-collected"), Does.Not.Contain("after-action"));
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

    private static string[] StackNames(EngineMatchState state)
    {
        return state.State["effectStack"]!.AsArray()
            .Select(item => item!["cardName"]!.GetValue<string>())
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
            ["controllerId"] = ownerId,
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

    private static JsonObject Modal(string id, params JsonObject[] modes)
    {
        var ability = Ability(id, "modal", Effect("rally", 0));
        ability["modes"] = ToArray(modes);
        return ability;
    }

    private static JsonObject Mode(string id, JsonObject effect) =>
        new()
        {
            ["id"] = id,
            ["effect"] = effect
        };

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

    private static JsonObject DeckCard(string id) =>
        new()
        {
            ["id"] = $"{id}-test",
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
            ["effect"] = Effect("rally", 0)
        };

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
