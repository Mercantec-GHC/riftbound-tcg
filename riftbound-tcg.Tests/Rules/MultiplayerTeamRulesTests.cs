using System.Text.Json.Nodes;
using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Tests.Rules;

public class MultiplayerTeamRulesTests
{
    [Test]
    public void teams_2v2_uses_team_total_for_winning()
    {
        var engine = new DefaultRulesEngine();
        var state = PlayingState(engine, "teams-2v2", turnPhase: "beginning");
        state.State["turnPlayerId"] = 2;
        state.State["activePlayer"] = 2;
        SetPlayerPoints(state, 0, 10);
        Battlefield(state, "field-a-0")["controllerId"] = 2;

        var result = engine.ApplyAction(state, new EngineGameAction(2, "advance-phase", new Dictionary<string, object?>()), state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.Stage, Is.EqualTo("game-over"));
        Assert.That(result.State.State["winningTeamId"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(result.State.State["winner"]!.GetValue<int>(), Is.EqualTo(0));
    }

    [Test]
    public void teams_2v2_allows_friendly_teammate_targets_and_rejects_teammates_as_enemies()
    {
        var engine = new DefaultRulesEngine();
        var state = PlayingState(engine, "teams-2v2", turnPhase: "main");
        var teammateUnit = Unit("ally-unit", 2, 2);
        Player(state, 2)["base"]!.AsArray().Add(teammateUnit);
        PutCardInHand(state, 0, Card("team-rally", "Team Rally", "spell", "[Action] Ready a friendly unit.", "buff", 1, 0));

        var friendlyResult = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["targetUnitId"] = "ally-unit" }),
            state.SequenceNumber);

        Assert.That(friendlyResult.Accepted, Is.True);

        var damageState = PlayingState(engine, "teams-2v2", turnPhase: "main");
        Player(damageState, 2)["base"]!.AsArray().Add(Unit("ally-target", 2, 2));
        PutCardInHand(damageState, 0, Card("test-bolt", "Test Bolt", "spell", "[Action] Deal damage to an enemy unit.", "damage", 1, 0));

        var enemyResult = engine.ApplyAction(
            damageState,
            new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["targetUnitId"] = "ally-target" }),
            damageState.SequenceNumber);

        Assert.That(enemyResult.Accepted, Is.False);
    }

    [Test]
    public void teams_2v2_teammate_can_play_spell_on_teammates_priority()
    {
        var engine = new DefaultRulesEngine();
        var state = PlayingState(engine, "teams-2v2", turnPhase: "main");
        state.State["turnPlayerId"] = 0;
        state.State["activePlayer"] = 0;
        PutCardInHand(state, 2, Card("assist", "Assist", "spell", "[Action] Draw 1.", "draw", 1, 0));

        var legal = engine.GetLegalActions(state, 2);
        var result = engine.ApplyAction(state, new EngineGameAction(2, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);

        Assert.That(legal.Where(action => action.Type == "play-card").Select(action => action.Label), Contains.Item("Play Assist"));
        Assert.That(result.Accepted, Is.True);
    }

    [Test]
    public void teams_2v2_cannot_move_units_to_teammate_controlled_battlefields()
    {
        var engine = new DefaultRulesEngine();
        var state = PlayingState(engine, "teams-2v2", turnPhase: "main");
        var battlefield = Battlefield(state, "field-a-0");
        battlefield["controllerId"] = 2;
        var player = Player(state, 0);
        player["base"]!.AsArray().Add(Unit("mover", 0, 2, exhausted: false));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = "mover", ["battlefieldId"] = "field-a-0" }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void teams_2v2_combat_groups_teammates_on_the_same_side()
    {
        var engine = new DefaultRulesEngine();
        var state = PlayingState(engine, "teams-2v2", turnPhase: "main");
        var battlefield = Battlefield(state, "field-a-0");
        battlefield["controllerId"] = 1;
        battlefield["contestedByPlayerId"] = 0;
        battlefield["stagedCombat"] = true;
        battlefield["stagedShowdown"] = true;
        battlefield["units"] = new JsonArray(Unit("attacker-a", 0, 2), Unit("attacker-b", 2, 2), Unit("defender-a", 1, 4));
        state.State["activeCombat"] = new JsonObject { ["battlefieldId"] = "field-a-0", ["attackerPlayerId"] = 0, ["defenderPlayerId"] = 1, ["damageStep"] = true };
        state.State["activeShowdown"] = new JsonObject { ["battlefieldId"] = "field-a-0", ["kind"] = "combat" };

        var afterAttackers = engine.ApplyAction(
            state,
            new EngineGameAction(0, "resolve-combat", new Dictionary<string, object?>
            {
                ["battlefieldId"] = "field-a-0",
                ["assignments"] = new Dictionary<string, int> { ["defender-a"] = 4 }
            }),
            state.SequenceNumber);

        var result = engine.ApplyAction(
            afterAttackers.State,
            new EngineGameAction(1, "resolve-combat", new Dictionary<string, object?>
            {
                ["battlefieldId"] = "field-a-0",
                ["assignments"] = new Dictionary<string, int> { ["attacker-a"] = 2, ["attacker-b"] = 2 }
            }),
            afterAttackers.State.SequenceNumber);

        Assert.That(afterAttackers.Accepted, Is.True);
        Assert.That(result.Accepted, Is.True);
        Assert.That(Battlefield(result.State, "field-a-0")["units"]!.AsArray(), Is.Empty);
    }

    [Test]
    public void teams_2v2_final_conquer_point_ignores_battlefields_teammate_occupied_at_beginning()
    {
        var engine = new DefaultRulesEngine();
        var state = PlayingState(engine, "teams-2v2", turnPhase: "main");
        SetPlayerPoints(state, 0, 10);
        state.State["scoredBattlefieldIdsThisTurn"] = new JsonObject { ["0"] = new JsonArray(JsonValue.Create("field-c-2")) };
        state.State["teamFinalPointExemptBattlefieldIdsThisTurn"] = new JsonObject { ["0"] = new JsonArray(JsonValue.Create("field-a-0")) };
        var battlefield = Battlefield(state, "field-b-1");
        battlefield["controllerId"] = 1;
        battlefield["contestedByPlayerId"] = 0;
        battlefield["stagedCombat"] = true;
        battlefield["stagedShowdown"] = true;
        battlefield["units"] = new JsonArray(Unit("attacker-a", 0, 4), Unit("defender-a", 1, 2));
        state.State["activeCombat"] = new JsonObject { ["battlefieldId"] = "field-b-1", ["attackerPlayerId"] = 0, ["defenderPlayerId"] = 1, ["damageStep"] = true };
        state.State["activeShowdown"] = new JsonObject { ["battlefieldId"] = "field-b-1", ["kind"] = "combat" };

        var afterAttack = engine.ApplyAction(
            state,
            new EngineGameAction(0, "resolve-combat", new Dictionary<string, object?>
            {
                ["battlefieldId"] = "field-b-1",
                ["assignments"] = new Dictionary<string, int> { ["defender-a"] = 4 }
            }),
            state.SequenceNumber);
        var result = engine.ApplyAction(
            afterAttack.State,
            new EngineGameAction(1, "resolve-combat", new Dictionary<string, object?>
            {
                ["battlefieldId"] = "field-b-1",
                ["assignments"] = new Dictionary<string, int> { ["attacker-a"] = 2 }
            }),
            afterAttack.State.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.Stage, Is.EqualTo("game-over"));
        Assert.That(result.State.State["winningTeamId"]!.GetValue<int>(), Is.EqualTo(0));
    }

    [Test]
    public void ffa_concession_removes_player_and_continues_until_one_player_remains()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config("ffa-3"), Decks("ffa-3"), 123);
        var first = engine.ApplyAction(state, new EngineGameAction(0, "concede", new Dictionary<string, object?>()), state.SequenceNumber);

        Assert.That(first.Accepted, Is.True);
        Assert.That(first.State.Stage, Is.EqualTo("mulligan"));
        Assert.That(first.State.State["winner"], Is.Null);
        Assert.That(first.State.State["turnOrder"]!.AsArray().Select(node => node!.GetValue<int>()).ToArray(), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(first.State.State["players"]!.AsArray().Select(node => node!["id"]!.GetValue<int>()), Does.Not.Contain(0));

        var second = engine.ApplyAction(first.State, new EngineGameAction(1, "concede", new Dictionary<string, object?>()), first.State.SequenceNumber);

        Assert.That(second.Accepted, Is.True);
        Assert.That(second.State.Stage, Is.EqualTo("game-over"));
        Assert.That(second.State.State["winner"]!.GetValue<int>(), Is.EqualTo(2));
    }

    [Test]
    public void teams_2v2_concession_causes_the_entire_team_to_lose()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config("teams-2v2"), Decks("teams-2v2"), 123);

        var result = engine.ApplyAction(state, new EngineGameAction(2, "concede", new Dictionary<string, object?>()), state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.Stage, Is.EqualTo("game-over"));
        Assert.That(result.State.State["winningTeamId"]!.GetValue<int>(), Is.EqualTo(1));
    }

    private static EngineMatchState PlayingState(DefaultRulesEngine engine, string mode, string turnPhase)
    {
        var initial = engine.CreateInitialState(Config(mode), Decks(mode), 123);
        var state = initial.State.DeepClone().AsObject();
        state["stage"] = "playing";
        state["turnPhase"] = turnPhase;
        state["turnPlayerId"] = 0;
        state["activePlayer"] = 0;
        return new EngineMatchState(initial.MatchId, initial.Mode, "playing", initial.SequenceNumber, state, initial.Players);
    }

    private static EngineMatchConfig Config(string mode)
    {
        var playerCount = mode switch
        {
            "ffa-3" => 3,
            "teams-2v2" => 4,
            _ => 2
        };
        var seats = Enumerable.Range(0, playerCount)
            .Select(id => new EngineSeatConfig(id, $"user-{id}", $"Player {id}", mode == "teams-2v2" ? id % 2 : id))
            .ToArray();
        return new EngineMatchConfig("multiplayer-test", mode, seats, ["field-a", "field-b", "field-c"], 0);
    }

    private static IReadOnlyList<EnginePlayerDeck> Decks(string mode)
    {
        var playerCount = mode switch
        {
            "ffa-3" => 3,
            "teams-2v2" => 4,
            _ => 2
        };
        return Enumerable.Range(0, playerCount)
            .Select(id => new EnginePlayerDeck($"deck-{id}", $"legend-{id}", $"champion-{id}", [$"field-{id}"], [$"rune-{id}", $"rune-{id}", $"rune-{id}"], [$"unit-{id}-a", $"unit-{id}-b", $"unit-{id}-c", $"unit-{id}-d", $"unit-{id}-e"]))
            .ToArray();
    }

    private static JsonObject Card(string id, string name, string kind, string text, string effectType, int amount, int cost)
    {
        return new JsonObject
        {
            ["id"] = $"{id}-test",
            ["catalogId"] = id,
            ["name"] = name,
            ["kind"] = kind,
            ["tags"] = new JsonArray(),
            ["domain"] = "Fury",
            ["domains"] = new JsonArray("Fury"),
            ["cost"] = cost,
            ["might"] = 0,
            ["text"] = text,
            ["image"] = string.Empty,
            ["cardType"] = kind,
            ["supertype"] = null,
            ["effect"] = new JsonObject { ["type"] = effectType, ["amount"] = amount }
        };
    }

    private static JsonObject Unit(string uid, int ownerId, int might, bool exhausted = true)
    {
        return new JsonObject
        {
            ["id"] = $"{uid}-card",
            ["catalogId"] = $"{uid}-card",
            ["uid"] = uid,
            ["name"] = uid,
            ["kind"] = "unit",
            ["ownerId"] = ownerId,
            ["cost"] = 1,
            ["might"] = might,
            ["damage"] = 0,
            ["attachedMight"] = 0,
            ["exhausted"] = exhausted,
            ["attacker"] = false,
            ["defender"] = false
        };
    }

    private static void PutCardInHand(EngineMatchState state, int playerId, params JsonObject[] cards)
    {
        var hand = Player(state, playerId)["hand"]!.AsArray();
        hand.Clear();
        foreach (var card in cards)
        {
            hand.Add(card);
        }
    }

    private static JsonObject Player(EngineMatchState state, int playerId)
    {
        return state.State["players"]!.AsArray().Select(node => node!.AsObject()).Single(player => player["id"]!.GetValue<int>() == playerId);
    }

    private static JsonObject Battlefield(EngineMatchState state, string battlefieldId)
    {
        return state.State["battlefields"]!.AsArray().Select(node => node!.AsObject()).Single(battlefield => battlefield["id"]!.GetValue<string>() == battlefieldId);
    }

    private static void SetPlayerPoints(EngineMatchState state, int playerId, int points)
    {
        Player(state, playerId)["points"] = points;
    }
}
