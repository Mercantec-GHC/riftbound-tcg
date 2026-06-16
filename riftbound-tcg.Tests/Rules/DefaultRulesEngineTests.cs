using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Tests.Rules;

public class DefaultRulesEngineTests
{
    [Test]
    public void initial_state_is_deterministic_for_same_seed_and_decks()
    {
        var engine = new DefaultRulesEngine();

        var first = engine.CreateInitialState(Config(), Decks(), 123);
        var second = engine.CreateInitialState(Config(), Decks(), 123);

        Assert.That(second.State.ToJsonString(), Is.EqualTo(first.State.ToJsonString()));
    }

    [Test]
    public void legal_actions_are_returned_for_current_mulligan_player()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var actions = engine.GetLegalActions(state, 0);

        Assert.That(actions.Select(action => action.Type), Contains.Item("confirm-mulligan"));
        Assert.That(actions.Select(action => action.Type), Contains.Item("concede"));
    }

    [Test]
    public void non_seated_player_action_is_rejected()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var result = engine.ApplyAction(state, new EngineGameAction(4, "concede", new Dictionary<string, object?>()), 0);

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.State.SequenceNumber, Is.EqualTo(0));
    }

    [Test]
    public void stale_sequence_action_is_rejected()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var result = engine.ApplyAction(state, new EngineGameAction(0, "confirm-mulligan", new Dictionary<string, object?>()), 2);

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.State.SequenceNumber, Is.EqualTo(0));
    }

    [Test]
    public void mulligan_progresses_to_next_player()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var result = engine.ApplyAction(state, new EngineGameAction(0, "confirm-mulligan", new Dictionary<string, object?>()), 0);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.SequenceNumber, Is.EqualTo(1));
        Assert.That(result.State.State["activePlayer"]!.GetValue<int>(), Is.EqualTo(1));
    }

    [Test]
    public void concede_completes_match()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var result = engine.ApplyAction(state, new EngineGameAction(0, "concede", new Dictionary<string, object?>()), 0);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.Stage, Is.EqualTo("game-over"));
        Assert.That(result.State.State["winner"]!.GetValue<int>(), Is.EqualTo(1));
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
