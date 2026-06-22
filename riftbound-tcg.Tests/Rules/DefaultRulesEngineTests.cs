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
    public void mulligan_with_two_indexes_returns_selected_cards_to_deck_and_redraws()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), DecksWithLargerLibrary(), 123);

        var player = state.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var originalHand = player["hand"]!.AsArray().Select(card => card!["id"]!.GetValue<string>()).ToArray();
        var originalDeckSize = player["deck"]!.AsArray().Count;
        var keptCardId = originalHand[2];
        var mulliganedCardIds = new[] { originalHand[0], originalHand[1] };

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "confirm-mulligan", new Dictionary<string, object?> { ["handIndexes"] = new[] { 0, 1 } }),
            0);

        Assert.That(result.Accepted, Is.True);

        var resultPlayer = result.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var newHandIds = resultPlayer["hand"]!.AsArray().Select(card => card!["id"]!.GetValue<string>()).ToArray();
        var newDeckIds = resultPlayer["deck"]!.AsArray().Select(card => card!["id"]!.GetValue<string>()).ToArray();

        Assert.That(newHandIds, Has.Length.EqualTo(4));
        Assert.That(newDeckIds, Has.Length.EqualTo(originalDeckSize));
        Assert.That(newHandIds, Does.Contain(keptCardId));
        Assert.That(newHandIds, Has.None.EqualTo(mulliganedCardIds[0]));
        Assert.That(newHandIds, Has.None.EqualTo(mulliganedCardIds[1]));
        Assert.That(newDeckIds, Does.Contain(mulliganedCardIds[0]));
        Assert.That(newDeckIds, Does.Contain(mulliganedCardIds[1]));
    }

    [Test]
    public void mulligan_with_no_indexes_keeps_same_hand()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var player = state.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var originalHandIds = player["hand"]!.AsArray().Select(card => card!["id"]!.GetValue<string>()).ToArray();

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "confirm-mulligan", new Dictionary<string, object?> { ["handIndexes"] = Array.Empty<int>() }),
            0);

        var resultPlayer = result.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var newHandIds = resultPlayer["hand"]!.AsArray().Select(card => card!["id"]!.GetValue<string>()).ToArray();

        Assert.That(newHandIds, Is.EqualTo(originalHandIds));
    }

    [Test]
    public void mulligan_ignores_out_of_range_and_duplicate_indexes()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var player = state.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var originalHandIds = player["hand"]!.AsArray().Select(card => card!["id"]!.GetValue<string>()).ToArray();

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "confirm-mulligan", new Dictionary<string, object?> { ["handIndexes"] = new[] { 0, 0, 99 } }),
            0);

        Assert.That(result.Accepted, Is.True);

        var resultPlayer = result.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var newHandIds = resultPlayer["hand"]!.AsArray().Select(card => card!["id"]!.GetValue<string>()).ToArray();

        Assert.That(newHandIds, Has.Length.EqualTo(4));
        Assert.That(newHandIds, Has.None.EqualTo(originalHandIds[0]));
    }

    [Test]
    public void second_player_cannot_mulligan_before_first_player_confirms()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var actions = engine.GetLegalActions(state, 1);

        Assert.That(actions.Select(action => action.Type), Has.None.EqualTo("confirm-mulligan"));
    }

    [Test]
    public void stage_becomes_playing_after_all_players_confirm_mulligan()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var afterFirst = engine.ApplyAction(
            state,
            new EngineGameAction(0, "confirm-mulligan", new Dictionary<string, object?> { ["handIndexes"] = new[] { 0, 1 } }),
            0);
        var afterSecond = engine.ApplyAction(
            afterFirst.State,
            new EngineGameAction(1, "confirm-mulligan", new Dictionary<string, object?> { ["handIndexes"] = new[] { 0 } }),
            1);

        Assert.That(afterSecond.Accepted, Is.True);
        Assert.That(afterSecond.State.Stage, Is.EqualTo("playing"));
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

    private static IReadOnlyList<EnginePlayerDeck> DecksWithLargerLibrary()
    {
        return
        [
            new EnginePlayerDeck("deck-a", "legend-a", "champion-a", ["skybridge"], ["rune-a", "rune-a", "rune-a"], Enumerable.Range(0, 12).Select(i => $"unit-a{i}").ToArray()),
            new EnginePlayerDeck("deck-b", "legend-b", "champion-b", ["emberfield"], ["rune-b", "rune-b", "rune-b"], Enumerable.Range(0, 12).Select(i => $"unit-b{i}").ToArray())
        ];
    }
}
