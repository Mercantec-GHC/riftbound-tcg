using System.Text.Json.Nodes;
using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Tests.Rules;

public class BattlefieldScoringTests
{
    [Test]
    public void conquer_scores_one_point_when_player_wins_combat()
    {
        var engine = new DefaultRulesEngine();
        var state = CombatState(engine);

        var result = Conquer(engine, state);

        Assert.That(result.Accepted, Is.True);
        Assert.That(PlayerPoints(result.State, 0), Is.EqualTo(1));
        Assert.That(Battlefield(result.State, "skybridge-0")["controllerId"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(ScoredBattlefields(result.State, 0), Does.Contain("skybridge-0"));
    }

    [Test]
    public void hold_scores_one_point_during_beginning_phase()
    {
        var engine = new DefaultRulesEngine();
        var state = PlayingState(engine, turnPhase: "beginning");
        Battlefield(state, "skybridge-0")["controllerId"] = 0;

        var result = engine.ApplyAction(state, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), 0);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.State["turnPhase"]!.GetValue<string>(), Is.EqualTo("channel"));
        Assert.That(PlayerPoints(result.State, 0), Is.EqualTo(1));
        Assert.That(ScoredBattlefields(result.State, 0), Does.Contain("skybridge-0"));
    }

    [Test]
    public void player_cannot_score_same_battlefield_twice_in_one_turn()
    {
        var engine = new DefaultRulesEngine();
        var state = CombatState(engine);
        // Pre-mark the battlefield as already scored this turn via hold
        state.State["scoredBattlefieldIdsThisTurn"] = new JsonObject { ["0"] = new JsonArray(JsonValue.Create("skybridge-0")) };

        var result = Conquer(engine, state);

        Assert.That(result.Accepted, Is.True);
        Assert.That(PlayerPoints(result.State, 0), Is.EqualTo(0));
        var outcome = ScoreOutcomes(result).Single();
        Assert.That(outcome["pointsAwarded"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(outcome["skippedReason"]!.GetValue<string>(), Is.EqualTo("already-scored-this-turn"));
    }

    [Test]
    public void scored_battlefields_reset_when_turn_ends()
    {
        var engine = new DefaultRulesEngine();
        var state = CombatState(engine);

        var conquered = Conquer(engine, state);
        Assert.That(conquered.Accepted, Is.True);
        var ended = engine.ApplyAction(conquered.State, new EngineGameAction(0, "end-turn", new Dictionary<string, object?>()), conquered.State.SequenceNumber);

        Assert.That(ended.Accepted, Is.True);
        Assert.That(ended.State.State["scoredBattlefieldIdsThisTurn"]!.AsObject(), Is.Empty);
    }

    [Test]
    public void win_condition_runs_after_conquer_score()
    {
        var engine = new DefaultRulesEngine();
        var state = CombatState(engine, victoryScore: 1);
        // Pre-mark all other battlefields as scored so draw-instead logic doesn't apply
        var otherIds = state.State["battlefields"]!.AsArray()
            .Select(f => f!["id"]!.GetValue<string>())
            .Where(id => id != "skybridge-0")
            .ToArray();
        state.State["scoredBattlefieldIdsThisTurn"] = new JsonObject { ["0"] = new JsonArray(otherIds.Select(id => JsonValue.Create(id)).ToArray()) };

        var result = Conquer(engine, state);

        Assert.That(result.State.Stage, Is.EqualTo("game-over"));
        Assert.That(result.State.State["winner"]!.GetValue<int>(), Is.EqualTo(0));
    }

    [Test]
    public void win_condition_runs_after_hold_score()
    {
        var engine = new DefaultRulesEngine();
        var state = PlayingState(engine, turnPhase: "beginning", victoryScore: 1);
        Battlefield(state, "skybridge-0")["controllerId"] = 0;

        var result = engine.ApplyAction(state, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), 0);

        Assert.That(result.State.Stage, Is.EqualTo("game-over"));
        Assert.That(result.State.State["winner"]!.GetValue<int>(), Is.EqualTo(0));
    }

    [Test]
    public void score_outcome_includes_source_battlefield_player_and_awarded_amount()
    {
        var engine = new DefaultRulesEngine();
        var state = CombatState(engine);

        var result = Conquer(engine, state);

        var outcome = ScoreOutcomes(result).Single();
        Assert.That(outcome["playerId"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(outcome["battlefieldId"]!.GetValue<string>(), Is.EqualTo("skybridge-0"));
        Assert.That(outcome["source"]!.GetValue<string>(), Is.EqualTo("conquer"));
        Assert.That(outcome["pointsAwarded"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(outcome["skippedReason"], Is.Null);
    }

    private static EngineActionResult Conquer(DefaultRulesEngine engine, EngineMatchState state)
    {
        var attacker = new JsonObject
        {
            ["id"] = "attacker-card", ["catalogId"] = "attacker-card", ["uid"] = "attacker-a",
            ["name"] = "Attacker", ["kind"] = "unit", ["ownerId"] = 0,
            ["cost"] = 1, ["might"] = 4, ["damage"] = 0, ["attachedMight"] = 0,
            ["exhausted"] = true, ["attacker"] = true, ["defender"] = false
        };
        var defender = new JsonObject
        {
            ["id"] = "defender-card", ["catalogId"] = "defender-card", ["uid"] = "defender-a",
            ["name"] = "Defender", ["kind"] = "unit", ["ownerId"] = 1,
            ["cost"] = 1, ["might"] = 2, ["damage"] = 0, ["attachedMight"] = 0,
            ["exhausted"] = true, ["attacker"] = false, ["defender"] = true
        };
        var battlefield = Battlefield(state, "skybridge-0");
        battlefield["contestedByPlayerId"] = 0;
        battlefield["stagedCombat"] = true;
        battlefield["stagedShowdown"] = true;
        battlefield["units"] = new JsonArray { attacker, defender };
        state.State["activeCombat"] = new JsonObject
        {
            ["battlefieldId"] = "skybridge-0",
            ["attackerPlayerId"] = 0,
            ["defenderPlayerId"] = 1
        };
        state.State["activeShowdown"] = new JsonObject { ["battlefieldId"] = "skybridge-0", ["kind"] = "combat" };

        var afterAttacker = engine.ApplyAction(state, new EngineGameAction(0, "resolve-combat", new Dictionary<string, object?>
        {
            ["battlefieldId"] = "skybridge-0",
            ["assignments"] = new Dictionary<string, int> { ["defender-a"] = 4 }
        }), state.SequenceNumber);
        Assert.That(afterAttacker.Accepted, Is.True);

        return engine.ApplyAction(afterAttacker.State, new EngineGameAction(1, "resolve-combat", new Dictionary<string, object?>
        {
            ["battlefieldId"] = "skybridge-0",
            ["assignments"] = new Dictionary<string, int> { ["attacker-a"] = 2 }
        }), afterAttacker.State.SequenceNumber);
    }

    private static EngineMatchState CombatState(DefaultRulesEngine engine, int victoryScore = 8)
    {
        var state = PlayingState(engine, turnPhase: "main", victoryScore: victoryScore);
        state.State["turnPlayerId"] = 0;
        return state;
    }

    private static EngineMatchState PlayingState(DefaultRulesEngine engine, string turnPhase, int victoryScore = 8)
    {
        var initial = engine.CreateInitialState(Config(), Decks(), 123);
        var state = initial.State.DeepClone().AsObject();
        state["stage"] = "playing";
        state["turnPhase"] = turnPhase;
        state["victoryScore"] = victoryScore;
        state["turnPlayerId"] = 0;
        state["activePlayer"] = 0;
        return new EngineMatchState(initial.MatchId, initial.Mode, "playing", initial.SequenceNumber, state, initial.Players);
    }

    private static int PlayerPoints(EngineMatchState state, int playerId)
    {
        return state.State["players"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(player => player["id"]!.GetValue<int>() == playerId)["points"]!.GetValue<int>();
    }

    private static JsonObject Battlefield(EngineMatchState state, string battlefieldId)
    {
        return state.State["battlefields"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(battlefield => battlefield["id"]!.GetValue<string>() == battlefieldId);
    }

    private static string[] ScoredBattlefields(EngineMatchState state, int playerId)
    {
        return state.State["scoredBattlefieldIdsThisTurn"]![playerId.ToString()]?.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToArray() ?? [];
    }

    private static JsonObject[] ScoreOutcomes(EngineActionResult result)
    {
        return result.ResultPayload!["scoreOutcomes"]!.AsArray()
            .Select(node => node!.AsObject())
            .ToArray();
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
