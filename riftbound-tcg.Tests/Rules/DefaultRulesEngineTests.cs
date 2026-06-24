using System.Text.Json.Nodes;
using riftbound_tcg.Core.Cards;
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
    public void mulligan_stays_in_mulligan_stage_until_all_players_confirm()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var result = engine.ApplyAction(state, new EngineGameAction(0, "confirm-mulligan", new Dictionary<string, object?>()), 0);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.SequenceNumber, Is.EqualTo(1));
        Assert.That(result.State.Stage, Is.EqualTo("mulligan"));
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
    public void next_player_can_confirm_mulligan_after_previous_player_confirms()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var actionsForSecondPlayerBeforeFirstConfirms = engine.GetLegalActions(state, 1);
        Assert.That(actionsForSecondPlayerBeforeFirstConfirms.Select(action => action.Type), Has.None.EqualTo("confirm-mulligan"));

        var afterFirstPlayerConfirms = engine.ApplyAction(
            state,
            new EngineGameAction(0, "confirm-mulligan", new Dictionary<string, object?>()),
            0);

        Assert.That(afterFirstPlayerConfirms.Accepted, Is.True);
        Assert.That(afterFirstPlayerConfirms.State.Stage, Is.EqualTo("mulligan"));

        var secondPlayerActionsAfter = engine.GetLegalActions(afterFirstPlayerConfirms.State, 1);
        Assert.That(secondPlayerActionsAfter.Select(action => action.Type), Contains.Item("confirm-mulligan"));
    }

    [Test]
    public void out_of_order_mulligan_confirmation_is_rejected()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(1, "confirm-mulligan", new Dictionary<string, object?>()),
            0);

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.State.SequenceNumber, Is.EqualTo(0));
        Assert.That(result.State.Stage, Is.EqualTo("mulligan"));
    }

    [Test]
    public void player_cannot_confirm_mulligan_twice()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var firstConfirm = engine.ApplyAction(state, new EngineGameAction(0, "confirm-mulligan", new Dictionary<string, object?>()), 0);
        var actionsAfter = engine.GetLegalActions(firstConfirm.State, 0);

        Assert.That(actionsAfter.Select(action => action.Type), Has.None.EqualTo("confirm-mulligan"));
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
    public void mulligan_recycles_returned_cards_to_bottom_in_deterministic_random_order()
    {
        var engine = new DefaultRulesEngine();
        var first = engine.CreateInitialState(Config(), DecksWithLargerLibrary(), 123);
        var second = engine.CreateInitialState(Config(), DecksWithLargerLibrary(), 123);

        var firstPlayer = FindPlayer(first, 0);
        var originalHandIds = firstPlayer["hand"]!.AsArray().Select(card => card!["id"]!.GetValue<string>()).ToArray();
        var returnedIds = new[] { originalHandIds[0], originalHandIds[1] };

        var firstResult = engine.ApplyAction(
            first,
            new EngineGameAction(0, "confirm-mulligan", new Dictionary<string, object?> { ["handIndexes"] = new[] { 0, 1 } }),
            0);
        var secondResult = engine.ApplyAction(
            second,
            new EngineGameAction(0, "confirm-mulligan", new Dictionary<string, object?> { ["handIndexes"] = new[] { 0, 1 } }),
            0);

        Assert.That(firstResult.Accepted, Is.True);
        Assert.That(secondResult.Accepted, Is.True);

        var firstBottomIds = FindPlayer(firstResult.State, 0)["deck"]!.AsArray().TakeLast(2).Select(card => card!["id"]!.GetValue<string>()).ToArray();
        var secondBottomIds = FindPlayer(secondResult.State, 0)["deck"]!.AsArray().TakeLast(2).Select(card => card!["id"]!.GetValue<string>()).ToArray();

        Assert.That(firstBottomIds, Is.EquivalentTo(returnedIds));
        Assert.That(secondBottomIds, Is.EqualTo(firstBottomIds));
        Assert.That(firstBottomIds, Is.EqualTo(new[] { returnedIds[1], returnedIds[0] }));
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

    [Test]
    public void unit_can_be_played_from_hand_to_base()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var player = state.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var handCardId = player["hand"]!.AsArray()[0]!["id"]!.GetValue<string>();

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);

        var resultPlayer = result.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var baseUnits = resultPlayer["base"]!.AsArray();
        Assert.That(baseUnits, Has.Count.EqualTo(1));
        Assert.That(baseUnits[0]!["id"]!.GetValue<string>(), Is.EqualTo(handCardId));
        Assert.That(resultPlayer["hand"]!.AsArray(), Has.None.Matches<JsonNode?>(card => card!["id"]!.GetValue<string>() == handCardId));
    }

    [Test]
    public void unit_can_be_played_to_a_battlefield_controlled_by_the_player()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var battlefield = state.State["battlefields"]!.AsArray()[0]!.AsObject();
        battlefield["controllerId"] = 0;
        var battlefieldId = battlefield["id"]!.GetValue<string>();

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0, ["battlefieldId"] = battlefieldId }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);

        var resultBattlefield = result.State.State["battlefields"]!.AsArray().First(b => b!["id"]!.GetValue<string>() == battlefieldId)!.AsObject();
        Assert.That(resultBattlefield["units"]!.AsArray(), Has.Count.EqualTo(1));
    }

    [Test]
    public void unit_cannot_be_played_to_an_uncontrolled_battlefield()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var battlefield = state.State["battlefields"]!.AsArray()[0]!.AsObject();
        var battlefieldId = battlefield["id"]!.GetValue<string>();
        battlefield["controllerId"] = 1;

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0, ["battlefieldId"] = battlefieldId }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.State.SequenceNumber, Is.EqualTo(state.SequenceNumber));
    }

    [Test]
    public void unit_cannot_be_played_without_enough_ready_runes_to_cover_cost()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var player = state.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        player["runes"]!["ready"]!.AsArray().Clear();
        player["runePool"]!["energy"] = 0;

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.State.SequenceNumber, Is.EqualTo(state.SequenceNumber));
    }

    [Test]
    public void playing_a_unit_exhausts_ready_runes_to_pay_its_cost()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var player = state.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var readyBefore = player["runes"]!["ready"]!.AsArray().Count;
        var cardCost = player["hand"]!.AsArray()[0]!["cost"]!.GetValue<int>();

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);

        var resultPlayer = result.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        Assert.That(resultPlayer["runes"]!["ready"]!.AsArray(), Has.Count.EqualTo(readyBefore - cardCost));
        Assert.That(resultPlayer["runes"]!["exhausted"]!.AsArray(), Has.Count.EqualTo(cardCost));
    }

    [Test]
    public void champion_can_be_summoned_to_base_when_player_has_enough_runes()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var player = state.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var championId = player["champion"]!["id"]!.GetValue<string>();

        var legalActions = engine.GetLegalActions(state, 0);
        Assert.That(legalActions, Has.Some.Matches<EngineLegalAction>(action => action.Type == "summon-champion"));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "summon-champion", new Dictionary<string, object?>()),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);

        var resultPlayer = result.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        Assert.That(resultPlayer["championSummoned"]!.GetValue<bool>(), Is.True);
        var baseUnits = resultPlayer["base"]!.AsArray();
        Assert.That(baseUnits, Has.Count.EqualTo(1));
        Assert.That(baseUnits[0]!["id"]!.GetValue<string>(), Is.EqualTo(championId));
    }

    [Test]
    public void champion_cannot_be_summoned_without_enough_ready_runes_to_cover_cost()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var player = state.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        player["runes"]!["ready"]!.AsArray().Clear();
        player["runePool"]!["energy"] = 0;

        var legalActions = engine.GetLegalActions(state, 0);
        Assert.That(legalActions, Has.None.Matches<EngineLegalAction>(action => action.Type == "summon-champion"));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "summon-champion", new Dictionary<string, object?>()),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.State.SequenceNumber, Is.EqualTo(state.SequenceNumber));
    }

    [Test]
    public void champion_cannot_be_summoned_twice()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var afterSummon = engine.ApplyAction(
            state,
            new EngineGameAction(0, "summon-champion", new Dictionary<string, object?>()),
            state.SequenceNumber);
        Assert.That(afterSummon.Accepted, Is.True);

        var legalActions = engine.GetLegalActions(afterSummon.State, 0);
        Assert.That(legalActions, Has.None.Matches<EngineLegalAction>(action => action.Type == "summon-champion"));

        var result = engine.ApplyAction(
            afterSummon.State,
            new EngineGameAction(0, "summon-champion", new Dictionary<string, object?>()),
            afterSummon.State.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void exhausted_runes_and_units_ready_again_when_the_player_reaches_awaken_on_their_next_turn()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var afterPlay = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);
        Assert.That(afterPlay.Accepted, Is.True);

        var playerAfterPlay = afterPlay.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        Assert.That(playerAfterPlay["runes"]!["exhausted"]!.AsArray(), Has.Count.GreaterThan(0));

        var current = afterPlay.State;
        while (current.State["turnPlayerId"]!.GetValue<int>() != 0 || current.State["turnPhase"]?.GetValue<string>() != "draw")
        {
            var turnPlayerId = current.State["turnPlayerId"]!.GetValue<int>();
            var advanced = engine.ApplyAction(current, new EngineGameAction(turnPlayerId, "advance-phase", new Dictionary<string, object?>()), current.SequenceNumber);
            Assert.That(advanced.Accepted, Is.True);
            current = advanced.State;
        }

        var playerOnNextTurn = current.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        Assert.That(playerOnNextTurn["runes"]!["exhausted"]!.AsArray(), Has.Count.EqualTo(0));
        Assert.That(playerOnNextTurn["base"]!.AsArray(), Has.All.Matches<JsonNode?>(unit => unit!["exhausted"]!.GetValue<bool>() == false));
    }

    [Test]
    public void played_unit_enters_play_exhausted()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);

        var resultPlayer = result.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var unit = resultPlayer["base"]!.AsArray().Single();
        Assert.That(unit!["exhausted"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void an_unexhausted_base_unit_can_move_to_a_battlefield_the_player_controls()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var battlefield = state.State["battlefields"]!.AsArray()[0]!.AsObject();
        battlefield["controllerId"] = 0;
        var battlefieldId = battlefield["id"]!.GetValue<string>();

        var afterPlay = engine.ApplyAction(state, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        Assert.That(afterPlay.Accepted, Is.True);

        var playedPlayer = afterPlay.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var unitId = playedPlayer["base"]!.AsArray().Single()!["uid"]!.GetValue<string>();
        playedPlayer["base"]!.AsArray().Single()!["exhausted"] = false;

        var result = engine.ApplyAction(
            afterPlay.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = unitId, ["battlefieldId"] = battlefieldId }),
            afterPlay.State.SequenceNumber);

        Assert.That(result.Accepted, Is.True);

        var resultPlayer = result.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        Assert.That(resultPlayer["base"]!.AsArray(), Has.Count.EqualTo(0));

        var resultBattlefield = result.State.State["battlefields"]!.AsArray().First(b => b!["id"]!.GetValue<string>() == battlefieldId)!.AsObject();
        Assert.That(resultBattlefield["units"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(resultBattlefield["units"]!.AsArray()[0]!["uid"]!.GetValue<string>(), Is.EqualTo(unitId));
        Assert.That(resultBattlefield["units"]!.AsArray()[0]!["exhausted"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void an_exhausted_base_unit_cannot_move_to_a_battlefield()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var battlefield = state.State["battlefields"]!.AsArray()[0]!.AsObject();
        battlefield["controllerId"] = 0;
        var battlefieldId = battlefield["id"]!.GetValue<string>();

        var afterPlay = engine.ApplyAction(state, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        Assert.That(afterPlay.Accepted, Is.True);

        var playedPlayer = afterPlay.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var unitId = playedPlayer["base"]!.AsArray().Single()!["uid"]!.GetValue<string>();

        var result = engine.ApplyAction(
            afterPlay.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = unitId, ["battlefieldId"] = battlefieldId }),
            afterPlay.State.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.State.SequenceNumber, Is.EqualTo(afterPlay.State.SequenceNumber));
    }

    [Test]
    public void legal_actions_include_reaction_spell_from_hand_during_chain_window()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 1, Card("quick-draw", "Quick Draw", "spell", "[Reaction] Draw 1.", "draw", 1, cost: 0));
        state.State["chainWindow"] = EmptyChainWindow();

        var actions = engine.GetLegalActions(state, 1);

        Assert.That(actions.Select(action => action.Type), Contains.Item("pass-chain-window"));
        Assert.That(actions.Where(action => action.Type == "play-card").Select(action => action.Label), Contains.Item("Play Quick Draw"));
    }

    [Test]
    public void reaction_gear_from_hand_resolves_to_owner_base()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 1, Card("gold-token", "Gold Token", "gear", "[Reaction] Add 1.", "draw", 0, cost: 0));
        state.State["chainWindow"] = EmptyChainWindow();

        var played = engine.ApplyAction(state, new EngineGameAction(1, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        Assert.That(played.Accepted, Is.True);

        var player = FindPlayer(played.State, 1);
        Assert.That(player["baseGear"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(player["baseGear"]!.AsArray()[0]!["name"]!.GetValue<string>(), Is.EqualTo("Gold Token"));
        Assert.That(played.State.State["chainWindow"], Is.Null);
    }

    [Test]
    public void non_reaction_spell_is_illegal_during_chain_window()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 1, Card("slow-spell", "Slow Spell", "spell", "[Action] Draw 1.", "draw", 1, cost: 0));
        state.State["chainWindow"] = EmptyChainWindow();

        var actions = engine.GetLegalActions(state, 1);
        var result = engine.ApplyAction(state, new EngineGameAction(1, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);

        Assert.That(actions.Select(action => action.Type), Has.None.EqualTo("play-card"));
        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void opponent_can_play_reaction_but_not_action_on_turn_players_chain()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(
            state,
            1,
            Card("react", "Reactive Spell", "spell", "[Reaction] Draw 1.", "draw", 1, cost: 0),
            Card("act", "Action Spell", "spell", "[Action] Draw 1.", "draw", 1, cost: 0));
        state.State["chainWindow"] = EmptyChainWindow();

        var actions = engine.GetLegalActions(state, 1);

        Assert.That(actions.Where(action => action.Type == "play-card").Select(action => action.Label), Contains.Item("Play Reactive Spell"));
        Assert.That(actions.Where(action => action.Type == "play-card").Select(action => action.Label), Has.None.EqualTo("Play Action Spell"));
    }

    [Test]
    public void passing_chain_window_resolves_top_after_all_players_pass()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("draw-two", "Draw Two", "spell", "[Action] Draw 2.", "draw", 2, cost: 0));
        var initialPlayer = FindPlayer(state, 0);
        initialPlayer["deck"] = new JsonArray(
            Card("deck-draw-a", "Deck Draw A", "unit", "", "rally", 0, cost: 0),
            Card("deck-draw-b", "Deck Draw B", "unit", "", "rally", 0, cost: 0));

        var played = engine.ApplyAction(state, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        Assert.That(played.Accepted, Is.True);
        Assert.That(played.State.State["effectStack"]!.AsArray(), Has.Count.EqualTo(1));

        var afterFirstPass = engine.ApplyAction(played.State, new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()), played.State.SequenceNumber);
        Assert.That(afterFirstPass.State.State["effectStack"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(afterFirstPass.State.State["chainWindow"], Is.Not.Null);

        var afterSecondPass = engine.ApplyAction(afterFirstPass.State, new EngineGameAction(1, "pass-chain-window", new Dictionary<string, object?>()), afterFirstPass.State.SequenceNumber);

        var player = FindPlayer(afterSecondPass.State, 0);
        Assert.That(afterSecondPass.Accepted, Is.True);
        Assert.That(afterSecondPass.State.State["effectStack"]!.AsArray(), Is.Empty);
        Assert.That(afterSecondPass.State.State["chainWindow"], Is.Null);
        Assert.That(player["hand"]!.AsArray(), Has.Count.EqualTo(2));
        Assert.That(player["trash"]!.AsArray(), Has.Count.EqualTo(1));
    }

    [Test]
    public void drawing_past_empty_deck_recycles_trash_and_waits_for_burn_out_choice()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("draw-two", "Draw Two", "spell", "[Action] Draw 2.", "draw", 2, cost: 0));
        var player = FindPlayer(state, 0);
        player["deck"] = new JsonArray(Card("deck-card", "Deck Card", "unit", "", "rally", 0, cost: 0));
        player["trash"] = new JsonArray(
            Card("trash-a", "Trash A", "unit", "", "rally", 0, cost: 0),
            Card("trash-b", "Trash B", "unit", "", "rally", 0, cost: 0));

        var played = engine.ApplyAction(state, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        var afterFirstPass = engine.ApplyAction(played.State, new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()), played.State.SequenceNumber);
        var afterSecondPass = engine.ApplyAction(afterFirstPass.State, new EngineGameAction(1, "pass-chain-window", new Dictionary<string, object?>()), afterFirstPass.State.SequenceNumber);

        player = FindPlayer(afterSecondPass.State, 0);
        Assert.That(afterSecondPass.State.State["pendingBurnOut"], Is.Not.Null);
        Assert.That(player["hand"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Contains.Item("deck-card"));
        Assert.That(player["trash"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Is.EqualTo(new[] { "draw-two" }));
        Assert.That(player["deck"]!.AsArray(), Has.Count.EqualTo(2));
        Assert.That(engine.GetLegalActions(afterSecondPass.State, 0).Select(action => action.Type), Contains.Item("choose-burn-out-opponent"));

        var chosen = engine.ApplyAction(afterSecondPass.State, new EngineGameAction(0, "choose-burn-out-opponent", new Dictionary<string, object?> { ["opponentPlayerId"] = 1 }), afterSecondPass.State.SequenceNumber);

        player = FindPlayer(chosen.State, 0);
        Assert.That(chosen.Accepted, Is.True);
        Assert.That(chosen.State.State["pendingBurnOut"], Is.Null);
        Assert.That(PlayerPoints(chosen.State, 1), Is.EqualTo(1));
        Assert.That(player["hand"]!.AsArray(), Has.Count.EqualTo(2));
        Assert.That(player["deck"]!.AsArray(), Has.Count.EqualTo(1));
    }

    [Test]
    public void repeated_burn_out_with_empty_trash_can_award_immediate_win()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("draw-two", "Draw Two", "spell", "[Action] Draw 2.", "draw", 2, cost: 0));
        FindPlayer(state, 0)["deck"] = new JsonArray();
        SetPlayerPoints(state, 1, 7);

        var played = engine.ApplyAction(state, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        var afterFirstPass = engine.ApplyAction(played.State, new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()), played.State.SequenceNumber);
        var afterSecondPass = engine.ApplyAction(afterFirstPass.State, new EngineGameAction(1, "pass-chain-window", new Dictionary<string, object?>()), afterFirstPass.State.SequenceNumber);
        var chosen = engine.ApplyAction(afterSecondPass.State, new EngineGameAction(0, "choose-burn-out-opponent", new Dictionary<string, object?> { ["opponentPlayerId"] = 1 }), afterSecondPass.State.SequenceNumber);

        Assert.That(chosen.State.State["stage"]!.GetValue<string>(), Is.EqualTo("game-over"));
        Assert.That(chosen.State.State["winner"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(PlayerPoints(chosen.State, 1), Is.EqualTo(8));
    }

    [Test]
    public void burn_out_recycle_order_is_deterministic_for_same_seed()
    {
        var first = ResolveBurnOutUntilChoice(123);
        var second = ResolveBurnOutUntilChoice(123);

        var firstDeck = FindPlayer(first, 0)["deck"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()).ToArray();
        var secondDeck = FindPlayer(second, 0)["deck"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()).ToArray();

        Assert.That(secondDeck, Is.EqualTo(firstDeck));
    }

    [Test]
    public void chain_window_tracks_priority_and_rejects_non_priority_passes()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("draw-one", "Draw One", "spell", "[Action] Draw 1.", "draw", 1, cost: 0));

        var played = engine.ApplyAction(state, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        Assert.That(played.Accepted, Is.True);

        var chainWindow = played.State.State["chainWindow"]!.AsObject();
        Assert.That(chainWindow["priorityPlayerId"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(chainWindow["startedByPlayerId"]!.GetValue<int>(), Is.EqualTo(0));

        var wrongPass = engine.ApplyAction(played.State, new EngineGameAction(1, "pass-chain-window", new Dictionary<string, object?>()), played.State.SequenceNumber);
        Assert.That(wrongPass.Accepted, Is.False);
    }

    [Test]
    public void cleanup_after_spell_damage_kills_lethal_units()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("bolt", "Bolt", "spell", "[Action] Deal 2.", "damage", 2, cost: 0));

        var battlefield = state.State["battlefields"]![0]!.AsObject();
        battlefield["units"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "target-card",
            ["catalogId"] = "target-card",
            ["uid"] = "target-unit",
            ["name"] = "Target Unit",
            ["kind"] = "unit",
            ["ownerId"] = 1,
            ["cost"] = 1,
            ["might"] = 2,
            ["damage"] = 0,
            ["attachedMight"] = 0,
            ["exhausted"] = true,
            ["attacker"] = false,
            ["defender"] = false
        });

        var played = engine.ApplyAction(state, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["targetUnitId"] = "target-unit" }), state.SequenceNumber);
        var afterFirstPass = engine.ApplyAction(played.State, new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()), played.State.SequenceNumber);
        var resolved = engine.ApplyAction(afterFirstPass.State, new EngineGameAction(1, "pass-chain-window", new Dictionary<string, object?>()), afterFirstPass.State.SequenceNumber);

        Assert.That(resolved.Accepted, Is.True);
        var resultBattlefield = resolved.State.State["battlefields"]![0]!.AsObject();
        Assert.That(resultBattlefield["units"]!.AsArray(), Is.Empty);
        Assert.That(FindPlayer(resolved.State, 1)["trash"]!.AsArray(), Has.Count.EqualTo(1));
    }

    [Test]
    public void action_spell_is_legal_for_focus_player_during_showdown_open_state()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 1, Card("showdown-action", "Showdown Action", "spell", "[Action] Draw 1.", "draw", 1, cost: 0));
        state.State["activeShowdown"] = new JsonObject { ["battlefieldId"] = "skybridge-0", ["kind"] = "non-combat" };
        state.State["focusPlayerId"] = 1;

        var actions = engine.GetLegalActions(state, 1);

        Assert.That(actions.Where(action => action.Type == "play-card").Select(action => action.Label), Contains.Item("Play Showdown Action"));
    }

    [Test]
    public void play_card_rejects_invalid_hand_index_and_unaffordable_card()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("expensive", "Expensive Spell", "spell", "[Action] Draw 1.", "draw", 1, cost: 99));

        var invalidIndex = engine.ApplyAction(state, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 4 }), state.SequenceNumber);
        var unaffordable = engine.ApplyAction(state, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);

        Assert.That(invalidIndex.Accepted, Is.False);
        Assert.That(unaffordable.Accepted, Is.False);
    }

    [Test]
    public void moving_into_an_undefended_enemy_controlled_battlefield_flips_control_uncontested()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var battlefield = state.State["battlefields"]!.AsArray()[0]!.AsObject();
        battlefield["controllerId"] = 1;
        var battlefieldId = battlefield["id"]!.GetValue<string>();

        var afterPlay = engine.ApplyAction(state, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        Assert.That(afterPlay.Accepted, Is.True);

        var playedPlayer = afterPlay.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        playedPlayer["base"]!.AsArray().Single()!["exhausted"] = false;
        var unitId = playedPlayer["base"]!.AsArray().Single()!["uid"]!.GetValue<string>();

        var result = engine.ApplyAction(
            afterPlay.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = unitId, ["battlefieldId"] = battlefieldId }),
            afterPlay.State.SequenceNumber);

        Assert.That(result.Accepted, Is.True);

        var resultBattlefield = result.State.State["battlefields"]!.AsArray().First(b => b!["id"]!.GetValue<string>() == battlefieldId)!.AsObject();
        Assert.That(resultBattlefield["controllerId"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(resultBattlefield["contestedByPlayerId"], Is.Null);
        Assert.That(resultBattlefield["stagedShowdown"]?.GetValue<bool>() ?? false, Is.False);
        Assert.That(PlayerPoints(result.State, 0), Is.EqualTo(0));
    }

    [Test]
    public void moving_a_unit_to_an_uncontrolled_battlefield_grants_uncontested_control()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var battlefieldId = state.State["battlefields"]![0]!["id"]!.GetValue<string>();

        var afterPlay = engine.ApplyAction(state, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        Assert.That(afterPlay.Accepted, Is.True);

        var playedPlayer = afterPlay.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        playedPlayer["base"]!.AsArray().Single()!["exhausted"] = false;
        var unitId = playedPlayer["base"]!.AsArray().Single()!["uid"]!.GetValue<string>();

        var result = engine.ApplyAction(
            afterPlay.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = unitId, ["battlefieldId"] = battlefieldId }),
            afterPlay.State.SequenceNumber);

        Assert.That(result.Accepted, Is.True);

        var resultBattlefield = result.State.State["battlefields"]!.AsArray().First(b => b!["id"]!.GetValue<string>() == battlefieldId)!.AsObject();
        Assert.That(resultBattlefield["controllerId"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(resultBattlefield["contestedByPlayerId"], Is.Null);
        Assert.That(resultBattlefield["stagedShowdown"]?.GetValue<bool>() ?? false, Is.False);
    }

    [Test]
    public void moving_a_unit_to_an_uncontrolled_battlefield_changes_control_without_scoring()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var battlefieldId = state.State["battlefields"]![0]!["id"]!.GetValue<string>();

        var afterPlay = engine.ApplyAction(state, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        Assert.That(afterPlay.Accepted, Is.True);

        var player = afterPlay.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        player["base"]!.AsArray().Single()!["exhausted"] = false;
        var unitId = player["base"]!.AsArray().Single()!["uid"]!.GetValue<string>();

        var result = engine.ApplyAction(
            afterPlay.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = unitId, ["battlefieldId"] = battlefieldId }),
            afterPlay.State.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(PlayerPoints(result.State, 0), Is.EqualTo(0));
        Assert.That(ScoredBattlefields(result.State, 0), Is.Empty);
    }

    [Test]
    public void reinforcing_a_battlefield_you_already_control_does_not_score_another_conquest_point()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var battlefieldId = state.State["battlefields"]![0]!["id"]!.GetValue<string>();

        var afterFirstPlay = engine.ApplyAction(state, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        var afterSecondPlay = engine.ApplyAction(afterFirstPlay.State, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), afterFirstPlay.State.SequenceNumber);
        Assert.That(afterSecondPlay.Accepted, Is.True);

        var player = afterSecondPlay.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var units = player["base"]!.AsArray();
        foreach (var unit in units)
        {
            unit!["exhausted"] = false;
        }

        var firstMove = engine.ApplyAction(
            afterSecondPlay.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = units[0]!["uid"]!.GetValue<string>(), ["battlefieldId"] = battlefieldId }),
            afterSecondPlay.State.SequenceNumber);
        Assert.That(firstMove.Accepted, Is.True);

        var secondMove = engine.ApplyAction(
            firstMove.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = units[1]!["uid"]!.GetValue<string>(), ["battlefieldId"] = battlefieldId }),
            firstMove.State.SequenceNumber);

        Assert.That(secondMove.Accepted, Is.True);
        Assert.That(PlayerPoints(secondMove.State, 0), Is.EqualTo(0));
    }

    [Test]
    public void controlled_battlefields_score_hold_points_during_beginning()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var battlefield = state.State["battlefields"]![0]!.AsObject();
        var battlefieldId = battlefield["id"]!.GetValue<string>();
        battlefield["controllerId"] = 0;
        state.State["turnPhase"] = "beginning";

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(PlayerPoints(result.State, 0), Is.EqualTo(1));
        Assert.That(ScoredBattlefields(result.State, 0), Does.Contain(battlefieldId));
    }

    [Test]
    public void hold_can_gain_the_final_point()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var battlefield = state.State["battlefields"]![0]!.AsObject();
        battlefield["controllerId"] = 0;
        state.State["turnPhase"] = "beginning";
        SetPlayerPoints(state, 0, 7);

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(PlayerPoints(result.State, 0), Is.EqualTo(8));
        Assert.That(result.State.Stage, Is.EqualTo("game-over"));
    }

    [Test]
    public void score_point_is_not_a_legal_player_action()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);

        var actions = engine.GetLegalActions(state, 0).Select(action => action.Type);

        Assert.That(actions, Has.None.EqualTo("score-point"));
    }

    [Test]
    public void moving_a_unit_onto_an_opponent_occupied_battlefield_starts_a_contest()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var battlefieldId = state.State["battlefields"]![0]!["id"]!.GetValue<string>();

        var afterFirstPlay = engine.ApplyAction(state, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        Assert.That(afterFirstPlay.Accepted, Is.True);
        var playerZero = afterFirstPlay.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        playerZero["base"]!.AsArray().Single()!["exhausted"] = false;
        var unitZeroId = playerZero["base"]!.AsArray().Single()!["uid"]!.GetValue<string>();

        var afterMoveZero = engine.ApplyAction(
            afterFirstPlay.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = unitZeroId, ["battlefieldId"] = battlefieldId }),
            afterFirstPlay.State.SequenceNumber);
        Assert.That(afterMoveZero.Accepted, Is.True);

        var current = afterMoveZero.State;
        while (current.State["turnPlayerId"]!.GetValue<int>() != 1 || current.State["turnPhase"]?.GetValue<string>() != "main")
        {
            var turnPlayerId = current.State["turnPlayerId"]!.GetValue<int>();
            var advanced = engine.ApplyAction(current, new EngineGameAction(turnPlayerId, "advance-phase", new Dictionary<string, object?>()), current.SequenceNumber);
            Assert.That(advanced.Accepted, Is.True);
            current = advanced.State;
        }

        var afterSecondPlay = engine.ApplyAction(current, new EngineGameAction(1, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), current.SequenceNumber);
        Assert.That(afterSecondPlay.Accepted, Is.True);
        var playerOne = afterSecondPlay.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 1)!.AsObject();
        playerOne["base"]!.AsArray().Single()!["exhausted"] = false;
        var unitOneId = playerOne["base"]!.AsArray().Single()!["uid"]!.GetValue<string>();

        var result = engine.ApplyAction(
            afterSecondPlay.State,
            new EngineGameAction(1, "move-unit", new Dictionary<string, object?> { ["unitId"] = unitOneId, ["battlefieldId"] = battlefieldId }),
            afterSecondPlay.State.SequenceNumber);

        Assert.That(result.Accepted, Is.True);

        var resultBattlefield = result.State.State["battlefields"]!.AsArray().First(b => b!["id"]!.GetValue<string>() == battlefieldId)!.AsObject();
        Assert.That(resultBattlefield["units"]!.AsArray(), Has.Count.EqualTo(2));
        Assert.That(resultBattlefield["controllerId"], Is.Null);

        var activeCombat = result.State.State["activeCombat"]!.AsObject();
        Assert.That(activeCombat["battlefieldId"]!.GetValue<string>(), Is.EqualTo(battlefieldId));
        Assert.That(activeCombat["attackerPlayerId"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(activeCombat["defenderPlayerId"]!.GetValue<int>(), Is.EqualTo(0));

        var activeShowdown = result.State.State["activeShowdown"]!.AsObject();
        Assert.That(activeShowdown["battlefieldId"]!.GetValue<string>(), Is.EqualTo(battlefieldId));
        Assert.That(activeShowdown["kind"]!.GetValue<string>(), Is.EqualTo("combat"));
        Assert.That(resultBattlefield["stagedShowdown"]?.GetValue<bool>() ?? false, Is.False);

        var focusActions = engine.GetLegalActions(result.State, 1);
        Assert.That(focusActions.Select(action => action.Type), Contains.Item("pass-focus"));
        var afterCombatShowdown = PassAllFocus(engine, result.State);
        var resolveActions = engine.GetLegalActions(afterCombatShowdown, 0);
        Assert.That(resolveActions.Select(action => action.Type), Contains.Item("resolve-combat"));
    }

    [Test]
    public void a_player_can_stack_multiple_of_their_own_units_on_the_same_battlefield()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var battlefieldId = state.State["battlefields"]![0]!["id"]!.GetValue<string>();

        var afterFirstPlay = engine.ApplyAction(state, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        Assert.That(afterFirstPlay.Accepted, Is.True);
        var afterSecondPlay = engine.ApplyAction(afterFirstPlay.State, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), afterFirstPlay.State.SequenceNumber);
        Assert.That(afterSecondPlay.Accepted, Is.True);

        var player = afterSecondPlay.State.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == 0)!.AsObject();
        var unitIds = player["base"]!.AsArray().Select(unit => unit!["uid"]!.GetValue<string>()).ToArray();
        Assert.That(unitIds, Has.Length.EqualTo(2));
        foreach (var unitId in unitIds)
        {
            player["base"]!.AsArray().First(unit => unit!["uid"]!.GetValue<string>() == unitId)!["exhausted"] = false;
        }

        var afterFirstMove = engine.ApplyAction(
            afterSecondPlay.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = unitIds[0], ["battlefieldId"] = battlefieldId }),
            afterSecondPlay.State.SequenceNumber);
        Assert.That(afterFirstMove.Accepted, Is.True);

        var result = engine.ApplyAction(
            afterFirstMove.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = unitIds[1], ["battlefieldId"] = battlefieldId }),
            afterFirstMove.State.SequenceNumber);
        Assert.That(result.Accepted, Is.True);

        var resultBattlefield = result.State.State["battlefields"]!.AsArray().First(b => b!["id"]!.GetValue<string>() == battlefieldId)!.AsObject();
        Assert.That(resultBattlefield["units"]!.AsArray(), Has.Count.EqualTo(2));
        Assert.That(resultBattlefield["controllerId"]!.GetValue<int>(), Is.EqualTo(0));
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
            var advanced = engine.ApplyAction(current, new EngineGameAction(turnPlayerId, "advance-phase", new Dictionary<string, object?>()), current.SequenceNumber);
            current = advanced.State;
        }

        return current;
    }

    private static EngineMatchState ResolveBurnOutUntilChoice(int seed)
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), seed);
        var afterFirst = engine.ApplyAction(state, new EngineGameAction(0, "confirm-mulligan", new Dictionary<string, object?>()), 0);
        var afterSecond = engine.ApplyAction(afterFirst.State, new EngineGameAction(1, "confirm-mulligan", new Dictionary<string, object?>()), 1);
        var current = afterSecond.State;
        while (current.State["turnPhase"]?.GetValue<string>() != "main")
        {
            var turnPlayerId = current.State["turnPlayerId"]!.GetValue<int>();
            current = engine.ApplyAction(current, new EngineGameAction(turnPlayerId, "advance-phase", new Dictionary<string, object?>()), current.SequenceNumber).State;
        }

        PutCardInHand(current, 0, Card("draw-two", "Draw Two", "spell", "[Action] Draw 2.", "draw", 2, cost: 0));
        var player = FindPlayer(current, 0);
        player["deck"] = new JsonArray(Card("deck-card", "Deck Card", "unit", "", "rally", 0, cost: 0));
        player["trash"] = new JsonArray(
            Card("trash-a", "Trash A", "unit", "", "rally", 0, cost: 0),
            Card("trash-b", "Trash B", "unit", "", "rally", 0, cost: 0),
            Card("trash-c", "Trash C", "unit", "", "rally", 0, cost: 0));

        var played = engine.ApplyAction(current, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }), current.SequenceNumber);
        var afterFirstPass = engine.ApplyAction(played.State, new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()), played.State.SequenceNumber);
        return engine.ApplyAction(afterFirstPass.State, new EngineGameAction(1, "pass-chain-window", new Dictionary<string, object?>()), afterFirstPass.State.SequenceNumber).State;
    }

    private static EngineMatchState PassAllFocus(DefaultRulesEngine engine, EngineMatchState state)
    {
        var current = state;
        var guard = 0;
        while (current.State["activeShowdown"] is not null && current.State["activeCombat"]?["damageStep"]?.GetValue<bool>() != true && guard++ < 8)
        {
            var focusPlayerId = current.State["focusPlayerId"]?.GetValue<int>() ?? current.State["activePlayer"]?.GetValue<int>() ?? 0;
            var result = engine.ApplyAction(current, new EngineGameAction(focusPlayerId, "pass-focus", new Dictionary<string, object?>()), current.SequenceNumber);
            Assert.That(result.Accepted, Is.True);
            current = result.State;
        }

        return current;
    }

    private static JsonObject FindPlayer(EngineMatchState state, int playerId)
    {
        return state.State["players"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(player => player["id"]!.GetValue<int>() == playerId);
    }

    private static void PutCardInHand(EngineMatchState state, int playerId, params JsonObject[] cards)
    {
        var player = FindPlayer(state, playerId);
        var hand = player["hand"]!.AsArray();
        hand.Clear();
        foreach (var card in cards)
        {
            hand.Add(card);
        }
    }

    private static JsonObject EmptyChainWindow()
    {
        return new JsonObject { ["passedByPlayer"] = new JsonObject() };
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

    private static int PlayerPoints(EngineMatchState state, int playerId) =>
        state.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == playerId)!["points"]!.GetValue<int>();

    private static JsonObject Player(EngineMatchState state, int playerId) =>
        state.State["players"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == playerId)!.AsObject();

    private static void SetPlayerPoints(EngineMatchState state, int playerId, int points) =>
        Player(state, playerId)["points"] = points;

    private static string[] ScoredBattlefields(EngineMatchState state, int playerId) =>
        state.State["scoredBattlefieldIdsThisTurn"]?[playerId.ToString()]?.AsArray().Select(node => node!.GetValue<string>()).ToArray() ?? [];

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
