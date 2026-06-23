using System.Text.Json.Nodes;
using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Tests.Rules;

public class BattlefieldScoringTests
{
    [Test]
    public void conquer_scores_one_point_when_player_gains_control()
    {
        var engine = new DefaultRulesEngine();
        var state = PlayingState(engine, turnPhase: "main");

        var result = engine.ApplyAction(state, ScoreAction(0, "skybridge-0", "conquer"), 0);

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
        var state = PlayingState(engine, turnPhase: "main");

        var first = engine.ApplyAction(state, ScoreAction(0, "skybridge-0", "conquer"), 0);
        var second = engine.ApplyAction(first.State, ScoreAction(0, "skybridge-0", "hold"), 1);

        Assert.That(second.Accepted, Is.True);
        Assert.That(PlayerPoints(second.State, 0), Is.EqualTo(1));
        var outcome = ScoreOutcomes(second).Single();
        Assert.That(outcome["pointsAwarded"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(outcome["skippedReason"]!.GetValue<string>(), Is.EqualTo("already-scored-this-turn"));
    }

    [Test]
    public void scored_battlefields_reset_when_turn_ends()
    {
        var engine = new DefaultRulesEngine();
        var state = PlayingState(engine, turnPhase: "main");

        var scored = engine.ApplyAction(state, ScoreAction(0, "skybridge-0", "conquer"), 0);
        var ended = engine.ApplyAction(scored.State, new EngineGameAction(0, "end-turn", new Dictionary<string, object?>()), 1);

        Assert.That(ended.Accepted, Is.True);
        Assert.That(ended.State.State["scoredBattlefieldIdsThisTurn"]!.AsObject(), Is.Empty);
    }

    [Test]
    public void win_condition_runs_after_conquer_score()
    {
        var engine = new DefaultRulesEngine();
        var state = PlayingState(engine, turnPhase: "main", victoryScore: 1);

        var result = engine.ApplyAction(state, ScoreAction(0, "skybridge-0", "conquer"), 0);

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
        var state = PlayingState(engine, turnPhase: "main");

        var result = engine.ApplyAction(state, ScoreAction(0, "skybridge-0", "conquer"), 0);

        var outcome = ScoreOutcomes(result).Single();
        Assert.That(outcome["playerId"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(outcome["battlefieldId"]!.GetValue<string>(), Is.EqualTo("skybridge-0"));
        Assert.That(outcome["source"]!.GetValue<string>(), Is.EqualTo("conquer"));
        Assert.That(outcome["pointsAwarded"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(outcome["skippedReason"], Is.Null);
    }

    private static EngineGameAction ScoreAction(int playerId, string battlefieldId, string source)
    {
        return new EngineGameAction(playerId, "score-point", new Dictionary<string, object?>
        {
            ["battlefieldId"] = battlefieldId,
            ["source"] = source
        });
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
