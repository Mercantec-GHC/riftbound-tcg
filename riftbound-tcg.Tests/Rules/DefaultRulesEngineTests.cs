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
    public void initial_setup_without_explicit_battlefields_selects_one_from_each_contributor_deck()
    {
        var engine = new DefaultRulesEngine();
        var config = Config() with { BattlefieldIds = [] };
        var decks = new[]
        {
            new EnginePlayerDeck("deck-a", "legend-a", "champion-a", ["a-field-0", "a-field-1", "a-field-2"], ["rune-a"], ["unit-a", "unit-b", "unit-c", "unit-d"]),
            new EnginePlayerDeck("deck-b", "legend-b", "champion-b", ["b-field-0", "b-field-1", "b-field-2"], ["rune-b"], ["unit-e", "unit-f", "unit-g", "unit-h"])
        };

        var state = engine.CreateInitialState(config, decks, 123);
        var battlefields = state.State["battlefields"]!.AsArray().Select(node => node!.AsObject()).ToArray();

        Assert.That(battlefields, Has.Length.EqualTo(2));
        Assert.That(battlefields[0]["chosenBy"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(battlefields[1]["chosenBy"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(battlefields[0]["catalogId"]!.GetValue<string>(), Does.StartWith("a-field-"));
        Assert.That(battlefields[1]["catalogId"]!.GetValue<string>(), Does.StartWith("b-field-"));
        Assert.That(FindPlayer(state, 0)["battlefieldId"]!.GetValue<string>(), Is.EqualTo(battlefields[0]["catalogId"]!.GetValue<string>()));
        Assert.That(FindPlayer(state, 1)["battlefieldId"]!.GetValue<string>(), Is.EqualTo(battlefields[1]["catalogId"]!.GetValue<string>()));
    }

    [Test]
    public void teams_setup_marks_selected_battlefields_as_contributed_by_non_first_players()
    {
        var engine = new DefaultRulesEngine();

        var state = engine.CreateInitialState(
            Config("teams-2v2", 4) with { BattlefieldIds = ["battlefield-1", "battlefield-2", "battlefield-3"] },
            Decks(4),
            123);
        var battlefields = state.State["battlefields"]!.AsArray().Select(node => node!.AsObject()).ToArray();

        Assert.That(battlefields.Select(field => field["chosenBy"]!.GetValue<int>()), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(Player(state, 0)["battlefieldId"]!.GetValue<string>(), Is.EqualTo(string.Empty));
        Assert.That(Player(state, 1)["battlefieldId"]!.GetValue<string>(), Is.EqualTo(battlefields[0]["catalogId"]!.GetValue<string>()));
        Assert.That(Player(state, 2)["battlefieldId"]!.GetValue<string>(), Is.EqualTo(battlefields[1]["catalogId"]!.GetValue<string>()));
        Assert.That(Player(state, 3)["battlefieldId"]!.GetValue<string>(), Is.EqualTo(battlefields[2]["catalogId"]!.GetValue<string>()));
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
    public void first_player_skips_their_first_draw_phase_in_ffa3()
    {
        AssertFirstPlayerFirstDrawAdvance("ffa-3", 3, shouldDraw: false);
    }

    [Test]
    public void first_player_skips_their_first_draw_phase_in_ffa4()
    {
        AssertFirstPlayerFirstDrawAdvance("ffa-4", 4, shouldDraw: false);
    }

    [Test]
    public void first_player_skips_their_first_draw_phase_in_teams_2v2()
    {
        AssertFirstPlayerFirstDrawAdvance("teams-2v2", 4, shouldDraw: false);
    }

    [Test]
    public void first_player_still_draws_during_their_first_draw_phase_in_duel()
    {
        AssertFirstPlayerFirstDrawAdvance("duel-1v1", 2, shouldDraw: true);
    }

    [Test]
    public void duel_concede_completes_match()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var result = engine.ApplyAction(state, new EngineGameAction(0, "concede", new Dictionary<string, object?>()), 0);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.Stage, Is.EqualTo("game-over"));
        Assert.That(result.State.State["winner"]!.GetValue<int>(), Is.EqualTo(1));
    }

    [Test]
    public void ffa_concede_removes_player_and_continues_when_multiple_players_remain()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config("ffa-3", 3), Decks(3), 123);
        var playerOneBattlefield = state.State["battlefields"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(battlefield => battlefield["chosenBy"]!.GetValue<int>() == 1);
        playerOneBattlefield["controllerId"] = 1;
        playerOneBattlefield["units"]!.AsArray().Add(Unit("unit-p1", 1));
        playerOneBattlefield["units"]!.AsArray().Add(Unit("unit-p2", 2));
        state.State["effectStack"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "stack-p1",
            ["playerId"] = 1,
            ["kind"] = "spell"
        });

        var result = engine.ApplyAction(state, new EngineGameAction(1, "concede", new Dictionary<string, object?>()), 0);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.Stage, Is.EqualTo("mulligan"));
        Assert.That(ActivePlayerIds(result.State), Is.EqualTo(new[] { 0, 2 }));
        Assert.That(result.State.Players.Select(player => player.PlayerId), Is.EqualTo(new[] { 0, 2 }));
        Assert.That(engine.GetLegalActions(result.State, 1), Is.Empty);
        Assert.That(result.State.State["turnOrder"]!.AsArray().Select(node => node!.GetValue<int>()), Is.EqualTo(new[] { 0, 2 }));
        Assert.That(result.State.State["effectStack"]!.AsArray(), Is.Empty);

        var replacedBattlefield = result.State.State["battlefields"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(battlefield => battlefield["id"]!.GetValue<string>() == playerOneBattlefield["id"]!.GetValue<string>());
        Assert.That(replacedBattlefield["catalogId"]!.GetValue<string>(), Is.EqualTo("token-battlefield"));
        Assert.That(replacedBattlefield["controllerId"]!.GetValue<int>(), Is.EqualTo(2));
        Assert.That(replacedBattlefield["units"]!.AsArray().Select(unit => unit!["ownerId"]!.GetValue<int>()), Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void ffa_concede_completes_match_when_only_one_player_remains()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config("ffa-3", 3), Decks(3), 123);

        var afterFirstConcede = engine.ApplyAction(state, new EngineGameAction(1, "concede", new Dictionary<string, object?>()), 0);
        var result = engine.ApplyAction(afterFirstConcede.State, new EngineGameAction(0, "concede", new Dictionary<string, object?>()), 1);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.Stage, Is.EqualTo("game-over"));
        Assert.That(result.State.State["winner"]!.GetValue<int>(), Is.EqualTo(2));
        Assert.That(ActivePlayerIds(result.State), Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void teams_2v2_concede_causes_conceding_team_to_lose()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config("teams-2v2", 4), Decks(4), 123);

        var result = engine.ApplyAction(state, new EngineGameAction(2, "concede", new Dictionary<string, object?>()), 0);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.Stage, Is.EqualTo("game-over"));
        Assert.That(result.State.State["winningTeamId"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(result.State.State["winner"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(ActivePlayerIds(result.State), Is.EqualTo(new[] { 1, 3 }));
        Assert.That(result.State.Players.Select(player => player.PlayerId), Is.EqualTo(new[] { 1, 3 }));
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
    public void champion_card_in_hand_can_be_played_to_base_without_summoning_champion_zone()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("main-deck-champion", "Main Deck Champion", "champion", "", "rally", 0, cost: 1));

        var legalActions = engine.GetLegalActions(state, 0);
        Assert.That(legalActions, Has.Some.Matches<EngineLegalAction>(action => action.Type == "play-unit"));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        var resultPlayer = FindPlayer(result.State, 0);
        Assert.That(resultPlayer["base"]!.AsArray().Single()!["kind"]!.GetValue<string>(), Is.EqualTo("champion"));
        Assert.That(resultPlayer["championSummoned"]!.GetValue<bool>(), Is.False);
        Assert.That(resultPlayer["champion"], Is.Not.Null);
    }

    [Test]
    public void legal_play_unit_actions_include_exact_base_and_battlefield_payloads()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("payload-unit", "Payload Unit", "unit", "", "rally", 0, cost: 0));

        var battlefield = state.State["battlefields"]![0]!.AsObject();
        battlefield["controllerId"] = 0;
        var battlefieldId = battlefield["id"]!.GetValue<string>();

        var actions = engine.GetLegalActions(state, 0)
            .Where(action => action.Type == "play-unit")
            .ToArray();

        Assert.That(actions, Has.Some.Matches<EngineLegalAction>(action =>
            action.PayloadSchema?["handIndex"]?.GetValue<int>() == 0 &&
            action.PayloadSchema?["battlefieldId"] is null));
        Assert.That(actions, Has.Some.Matches<EngineLegalAction>(action =>
            action.PayloadSchema?["handIndex"]?.GetValue<int>() == 0 &&
            action.PayloadSchema?["battlefieldId"]?.GetValue<string>() == battlefieldId));
        Assert.That(actions, Has.All.Matches<EngineLegalAction>(action =>
            action.PayloadSchema?["handIndex"]?.GetValue<int?>() is not null));
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
    public void card_can_be_played_with_energy_already_in_the_rune_pool()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var player = FindPlayer(state, 0);
        PutCardInHand(state, 0, Card("pool-spell", "Pool Spell", "spell", "[Action] Draw 1.", "draw", 1, cost: 2));
        player["runes"]!["ready"]!.AsArray().Clear();
        player["runePool"]!["energy"] = 2;

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        var resultPlayer = FindPlayer(result.State, 0);
        Assert.That(resultPlayer["runePool"]!["energy"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(resultPlayer["runes"]!["exhausted"]!.AsArray(), Is.Empty);
    }

    [Test]
    public void domain_power_cost_can_be_paid_by_recycling_a_matching_rune()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("fury-spell", "Fury Spell", "spell", "[Action] Draw 1.", "draw", 1, cost: 0, powerCost: new Dictionary<string, int> { ["Fury"] = 1 }));
        var player = FindPlayer(state, 0);
        var runeDeckBefore = player["runeDeck"]!.AsArray().Count;

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        var resultPlayer = FindPlayer(result.State, 0);
        Assert.That(resultPlayer["runes"]!["ready"]!.AsArray().Count + resultPlayer["runes"]!["exhausted"]!.AsArray().Count, Is.EqualTo(1));
        Assert.That(resultPlayer["runeDeck"]!.AsArray(), Has.Count.EqualTo(runeDeckBefore + 1));
    }

    [Test]
    public void universal_power_can_pay_a_domain_power_cost()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("fury-spell", "Fury Spell", "spell", "[Action] Draw 1.", "draw", 1, cost: 0, powerCost: new Dictionary<string, int> { ["Fury"] = 1 }));
        var player = FindPlayer(state, 0);
        player["runes"]!["ready"]!.AsArray().Clear();
        player["runes"]!["exhausted"]!.AsArray().Clear();
        player["runePool"]!["universalPower"] = 1;

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(FindPlayer(result.State, 0)["runePool"]!["universalPower"]!.GetValue<int>(), Is.EqualTo(0));
    }

    [Test]
    public void card_cannot_be_played_without_required_domain_power()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("fury-spell", "Fury Spell", "spell", "[Action] Draw 1.", "draw", 1, cost: 0, powerCost: new Dictionary<string, int> { ["Fury"] = 1 }));
        var player = FindPlayer(state, 0);
        player["runes"]!["ready"]!.AsArray().Clear();
        player["runes"]!["exhausted"]!.AsArray().Clear();
        player["runePool"]!["power"] = new JsonObject { ["Calm"] = 1 };

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.State.SequenceNumber, Is.EqualTo(state.SequenceNumber));
    }

    [Test]
    public void rune_pool_clears_after_draw_before_main()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        state.State["turnPhase"] = "draw";
        foreach (var player in state.State["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            player["runePool"] = new JsonObject
            {
                ["energy"] = 2,
                ["power"] = new JsonObject { ["Fury"] = 1 },
                ["universalPower"] = 1
            };
        }

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.State["turnPhase"]!.GetValue<string>(), Is.EqualTo("main"));
        foreach (var player in result.State.State["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            Assert.That(player["runePool"]!["energy"]!.GetValue<int>(), Is.EqualTo(0));
            Assert.That(player["runePool"]!["universalPower"]!.GetValue<int>(), Is.EqualTo(0));
            Assert.That(player["runePool"]!["power"]!.AsObject(), Is.Empty);
        }
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
    public void summoned_champion_consumes_the_champion_zone_source()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        FindPlayer(state, 0)["champion"]!["abilities"] = new JsonArray(new JsonObject
        {
            ["id"] = "champion-enter",
            ["kind"] = "triggered",
            ["event"] = "unit-entered",
            ["effect"] = new JsonObject { ["type"] = "draw", ["amount"] = 1 }
        });

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "summon-champion", new Dictionary<string, object?>()),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        var resultPlayer = FindPlayer(result.State, 0);
        Assert.That(resultPlayer["champion"], Is.Null);
        Assert.That(result.State.State["pendingTriggerGroups"]!.AsArray(), Is.Empty);
        Assert.That(result.State.State["effectStack"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(result.State.State["effectStack"]![0]!["cardName"]!.GetValue<string>(), Is.EqualTo(resultPlayer["base"]!.AsArray()[0]!["name"]!.GetValue<string>()));
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
    public void legal_move_unit_actions_include_exact_unit_and_destination_payloads()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var player = FindPlayer(state, 0);
        player["base"]!.AsArray().Clear();
        player["base"]!.AsArray().Add(Unit("base-mover", 0, exhausted: false));
        player["base"]!.AsArray().Add(Unit("tired-mover", 0, exhausted: true));

        var battlefield = state.State["battlefields"]![0]!.AsObject();
        battlefield["units"]!.AsArray().Add(Unit("field-mover", 0, exhausted: false));
        var battlefieldId = battlefield["id"]!.GetValue<string>();

        var actions = engine.GetLegalActions(state, 0)
            .Where(action => action.Type == "move-unit")
            .ToArray();

        Assert.That(actions, Has.Some.Matches<EngineLegalAction>(action =>
            action.PayloadSchema?["unitId"]?.GetValue<string>() == "base-mover" &&
            action.PayloadSchema?["battlefieldId"]?.GetValue<string>() == battlefieldId));
        Assert.That(actions, Has.Some.Matches<EngineLegalAction>(action =>
            action.PayloadSchema?["unitId"]?.GetValue<string>() == "field-mover" &&
            action.PayloadSchema?["battlefieldId"]?.GetValue<string>() == "base"));
        Assert.That(actions, Has.None.Matches<EngineLegalAction>(action =>
            action.PayloadSchema?["unitId"]?.GetValue<string>() == "tired-mover"));
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
        Assert.That(result.ResultMessage, Does.Contain("legal shared destination"));
        Assert.That(result.State.SequenceNumber, Is.EqualTo(afterPlay.State.SequenceNumber));
    }

    [Test]
    public void an_unexhausted_battlefield_unit_can_move_back_to_base()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var battlefieldId = state.State["battlefields"]![0]!["id"]!.GetValue<string>();

        var afterPlay = engine.ApplyAction(state, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        Assert.That(afterPlay.Accepted, Is.True);

        var player = FindPlayer(afterPlay.State, 0);
        var unit = player["base"]!.AsArray().Single()!.AsObject();
        unit["exhausted"] = false;
        var unitId = unit["uid"]!.GetValue<string>();

        var afterMoveOut = engine.ApplyAction(
            afterPlay.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = unitId, ["battlefieldId"] = battlefieldId }),
            afterPlay.State.SequenceNumber);
        Assert.That(afterMoveOut.Accepted, Is.True);

        var battlefieldUnit = afterMoveOut.State.State["battlefields"]![0]!["units"]!.AsArray().Single()!.AsObject();
        battlefieldUnit["exhausted"] = false;

        var result = engine.ApplyAction(
            afterMoveOut.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = unitId, ["destination"] = "base" }),
            afterMoveOut.State.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.State["battlefields"]![0]!["units"]!.AsArray(), Is.Empty);

        var resultPlayer = FindPlayer(result.State, 0);
        Assert.That(resultPlayer["base"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(resultPlayer["base"]!.AsArray()[0]!["uid"]!.GetValue<string>(), Is.EqualTo(unitId));
        Assert.That(resultPlayer["base"]!.AsArray()[0]!["exhausted"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void multiple_unexhausted_base_units_can_move_simultaneously_to_one_battlefield()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var battlefieldId = state.State["battlefields"]![0]!["id"]!.GetValue<string>();

        var afterFirstPlay = engine.ApplyAction(state, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        var afterSecondPlay = engine.ApplyAction(afterFirstPlay.State, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), afterFirstPlay.State.SequenceNumber);
        Assert.That(afterSecondPlay.Accepted, Is.True);

        var player = FindPlayer(afterSecondPlay.State, 0);
        var unitIds = player["base"]!.AsArray().Select(unit =>
        {
            unit!["exhausted"] = false;
            return unit["uid"]!.GetValue<string>();
        }).ToArray();

        var result = engine.ApplyAction(
            afterSecondPlay.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitIds"] = unitIds, ["battlefieldId"] = battlefieldId }),
            afterSecondPlay.State.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(FindPlayer(result.State, 0)["base"]!.AsArray(), Is.Empty);

        var resultBattlefield = result.State.State["battlefields"]![0]!.AsObject();
        Assert.That(resultBattlefield["units"]!.AsArray().Select(unit => unit!["uid"]!.GetValue<string>()), Is.EquivalentTo(unitIds));
        Assert.That(resultBattlefield["units"]!.AsArray(), Has.All.Matches<JsonNode?>(unit => unit!["exhausted"]!.GetValue<bool>()));
    }

    [Test]
    public void simultaneous_move_rejects_if_any_selected_unit_is_exhausted()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        var battlefieldId = state.State["battlefields"]![0]!["id"]!.GetValue<string>();

        var afterFirstPlay = engine.ApplyAction(state, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        var afterSecondPlay = engine.ApplyAction(afterFirstPlay.State, new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }), afterFirstPlay.State.SequenceNumber);
        Assert.That(afterSecondPlay.Accepted, Is.True);

        var player = FindPlayer(afterSecondPlay.State, 0);
        var units = player["base"]!.AsArray();
        units[0]!["exhausted"] = false;
        units[1]!["exhausted"] = true;
        var unitIds = units.Select(unit => unit!["uid"]!.GetValue<string>()).ToArray();

        var result = engine.ApplyAction(
            afterSecondPlay.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitIds"] = unitIds, ["battlefieldId"] = battlefieldId }),
            afterSecondPlay.State.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
        Assert.That(FindPlayer(result.State, 0)["base"]!.AsArray(), Has.Count.EqualTo(2));
        Assert.That(result.State.State["battlefields"]![0]!["units"]!.AsArray(), Is.Empty);
    }

    [Test]
    public void team_player_cannot_move_to_a_battlefield_controlled_by_their_teammate()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(TeamConfig(), TeamDecks(), 123);
        state.State["stage"] = "playing";
        state.State["turnPhase"] = "main";
        state.State["turnPlayerId"] = 0;
        state.State["activePlayer"] = 0;

        var player = FindPlayer(state, 0);
        player["base"]!.AsArray().Add(Unit("team-mover", 0, exhausted: false));

        var teammateControlledBattlefield = state.State["battlefields"]![0]!.AsObject();
        teammateControlledBattlefield["controllerId"] = 2;
        var battlefieldId = teammateControlledBattlefield["id"]!.GetValue<string>();

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = "team-mover", ["battlefieldId"] = battlefieldId }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
        Assert.That(FindPlayer(result.State, 0)["base"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(teammateControlledBattlefield["units"]!.AsArray(), Is.Empty);
    }

    [Test]
    public void multiplayer_player_cannot_move_to_a_battlefield_already_occupied_by_two_other_players()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(MultiplayerConfig(), MultiplayerDecks(), 123);
        state.State["stage"] = "playing";
        state.State["turnPhase"] = "main";
        state.State["turnPlayerId"] = 0;
        state.State["activePlayer"] = 0;

        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("ffa-mover", 0, exhausted: false));

        var battlefield = state.State["battlefields"]![0]!.AsObject();
        battlefield["units"]!.AsArray().Add(Unit("first-other", 1, exhausted: true));
        battlefield["units"]!.AsArray().Add(Unit("second-other", 2, exhausted: true));
        var battlefieldId = battlefield["id"]!.GetValue<string>();

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = "ffa-mover", ["battlefieldId"] = battlefieldId }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
        Assert.That(FindPlayer(result.State, 0)["base"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(battlefield["units"]!.AsArray(), Has.Count.EqualTo(2));
    }

    [Test]
    public void legal_attach_card_actions_include_exact_hand_and_target_payloads()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(
            state,
            0,
            Card("test-hook", "Test Hook", "gear", "", "buff", 1, cost: 0),
            Card("not-gear", "Not Gear", "unit", "", "rally", 0, cost: 0));

        var enemy = FindPlayer(state, 1);
        enemy["base"]!.AsArray().Add(Unit("enemy-target", 1, exhausted: false));

        var actions = engine.GetLegalActions(state, 0)
            .Where(action => action.Type == "attach-card")
            .ToArray();

        Assert.That(actions, Has.Some.Matches<EngineLegalAction>(action =>
            action.PayloadSchema?["handIndex"]?.GetValue<int>() == 0 &&
            action.PayloadSchema?["targetUnitId"]?.GetValue<string>() == "enemy-target"));
        Assert.That(actions, Has.None.Matches<EngineLegalAction>(action =>
            action.PayloadSchema?["handIndex"]?.GetValue<int>() == 1));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "attach-card", new Dictionary<string, object?> { ["handIndex"] = 1, ["targetUnitId"] = "enemy-target" }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
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
    public void non_targeted_burn_out_choice_uses_choice_payload_not_target_fields()
    {
        var engine = new DefaultRulesEngine();
        var state = ResolveBurnOutUntilChoice(123);

        var chosen = engine.ApplyAction(
            state,
            new EngineGameAction(0, "choose-burn-out-opponent", new Dictionary<string, object?> { ["opponentPlayerId"] = 1, ["targetUnitId"] = "not-a-target" }),
            state.SequenceNumber);

        Assert.That(chosen.Accepted, Is.True);
        Assert.That(chosen.State.State["pendingBurnOut"], Is.Null);
        Assert.That(PlayerPoints(chosen.State, 1), Is.EqualTo(1));
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
    public void reaction_added_to_chain_gets_newest_controller_priority_and_resolves_lifo()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("draw-two", "Draw Two", "spell", "[Action] Draw 2.", "draw", 2, cost: 0));
        PutCardInHand(state, 1, Card("quick-draw", "Quick Draw", "spell", "[Reaction] Draw 1.", "draw", 1, cost: 0));
        FindPlayer(state, 0)["deck"] = new JsonArray(
            Card("p0-a", "P0 A", "unit", "", "rally", 0, cost: 0),
            Card("p0-b", "P0 B", "unit", "", "rally", 0, cost: 0));
        FindPlayer(state, 1)["deck"] = new JsonArray(Card("p1-a", "P1 A", "unit", "", "rally", 0, cost: 0));

        var played = engine.ApplyAction(state, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);
        var afterTurnPlayerPass = engine.ApplyAction(played.State, new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()), played.State.SequenceNumber);
        var reaction = engine.ApplyAction(afterTurnPlayerPass.State, new EngineGameAction(1, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }), afterTurnPlayerPass.State.SequenceNumber);

        Assert.That(reaction.Accepted, Is.True);
        var reactionWindow = reaction.State.State["chainWindow"]!.AsObject();
        Assert.That(reactionWindow["priorityPlayerId"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(reaction.State.State["effectStack"]!.AsArray().Select(item => item!["cardName"]!.GetValue<string>()), Is.EqualTo(new[] { "Quick Draw", "Draw Two" }));
        Assert.That(reaction.State.State["effectStack"]![0]!["status"]!.GetValue<string>(), Is.EqualTo("pending"));

        var afterReactionPass = engine.ApplyAction(reaction.State, new EngineGameAction(1, "pass-chain-window", new Dictionary<string, object?>()), reaction.State.SequenceNumber);
        var afterOriginalControllerPass = engine.ApplyAction(afterReactionPass.State, new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()), afterReactionPass.State.SequenceNumber);

        Assert.That(FindPlayer(afterOriginalControllerPass.State, 1)["hand"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Contains.Item("p1-a"));
        Assert.That(afterOriginalControllerPass.State.State["effectStack"]!.AsArray().Select(item => item!["cardName"]!.GetValue<string>()), Is.EqualTo(new[] { "Draw Two" }));
        Assert.That(afterOriginalControllerPass.State.State["chainWindow"]!["priorityPlayerId"]!.GetValue<int>(), Is.EqualTo(0));

        var afterOriginalFirstPass = engine.ApplyAction(afterOriginalControllerPass.State, new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()), afterOriginalControllerPass.State.SequenceNumber);
        var resolved = engine.ApplyAction(afterOriginalFirstPass.State, new EngineGameAction(1, "pass-chain-window", new Dictionary<string, object?>()), afterOriginalFirstPass.State.SequenceNumber);

        Assert.That(resolved.State.State["effectStack"]!.AsArray(), Is.Empty);
        Assert.That(FindPlayer(resolved.State, 0)["hand"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Is.EquivalentTo(new[] { "p0-a", "p0-b" }));
    }

    [TestCase("triggered")]
    [TestCase("add-created")]
    public void non_focus_passing_chain_sources_keep_current_focus_after_resolution(string source)
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        FindPlayer(state, 0)["deck"] = new JsonArray(Card("drawn", "Drawn", "unit", "", "rally", 0, cost: 0));
        state.State["activeShowdown"] = new JsonObject { ["battlefieldId"] = "test-field", ["kind"] = "non-combat" };
        state.State["focusPlayerId"] = 0;
        state.State["priorityPlayerId"] = 0;
        state.State["activePlayer"] = 0;
        state.State["hasPassedFocusByPlayer"] = new JsonObject();
        state.State["effectStack"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "stack-test",
            ["card"] = Card("trigger-draw", "Trigger Draw", "spell", "", "draw", 1, cost: 0),
            ["cardId"] = "trigger-draw",
            ["cardName"] = "Trigger Draw",
            ["kind"] = "spell",
            ["playerId"] = 0,
            ["effect"] = new JsonObject { ["type"] = "draw", ["amount"] = 1 },
            ["targetUnitId"] = null,
            ["targetLaneId"] = null,
            ["status"] = "pending",
            ["source"] = source
        });
        state.State["chainWindow"] = new JsonObject
        {
            ["priorityPlayerId"] = 0,
            ["startedByPlayerId"] = 0,
            ["source"] = source,
            ["passesFocusOnClose"] = false,
            ["passedByPlayer"] = new JsonObject()
        };

        var afterFirstPass = engine.ApplyAction(state, new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()), state.SequenceNumber);
        var resolved = engine.ApplyAction(afterFirstPass.State, new EngineGameAction(1, "pass-chain-window", new Dictionary<string, object?>()), afterFirstPass.State.SequenceNumber);

        Assert.That(resolved.Accepted, Is.True);
        Assert.That(resolved.State.State["chainWindow"], Is.Null);
        Assert.That(resolved.State.State["focusPlayerId"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(resolved.State.State["activePlayer"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(FindPlayer(resolved.State, 0)["hand"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Contains.Item("drawn"));
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
        Assert.That(FindPlayer(resolved.State, 0)["trash"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Contains.Item("bolt"));
        Assert.That(FindPlayer(resolved.State, 1)["trash"]!.AsArray().Single()!["catalogId"]!.GetValue<string>(), Is.EqualTo("target-card"));
    }

    [Test]
    public void enemy_targeting_uses_controller_not_owner()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("bolt", "Bolt", "spell", "[Action] Deal 2 to an enemy unit.", "damage", 2, cost: 0));
        var borrowed = Unit("borrowed-unit", ownerId: 1);
        borrowed["controllerId"] = 0;
        FindPlayer(state, 0)["base"]!.AsArray().Add(borrowed);

        var played = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["targetUnitId"] = "borrowed-unit" }),
            state.SequenceNumber);

        Assert.That(played.Accepted, Is.False);
    }

    [Test]
    public void buff_effect_resolves_through_authoritative_stack_pipeline()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("battle-chant", "Battle Chant", "spell", "[Action] Give a friendly unit +2 Might this turn.", "buff", 2, cost: 0));
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("friendly-unit", ownerId: 0, might: 2));

        var played = engine.ApplyAction(state, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["targetUnitId"] = "friendly-unit" }), state.SequenceNumber);
        Assert.That(played.Accepted, Is.True);

        var resolved = PassChain(engine, played.State);

        var unit = FindPlayer(resolved.State, 0)["base"]!.AsArray().Single(node => node!["uid"]!.GetValue<string>() == "friendly-unit")!.AsObject();
        Assert.That(unit["attachedMight"]!.GetValue<int>(), Is.EqualTo(2));
        Assert.That(FindPlayer(resolved.State, 0)["trash"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Contains.Item("battle-chant"));
    }

    [Test]
    public void rally_effect_resolves_through_authoritative_stack_pipeline()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("stand-ready", "Stand Ready", "spell", "[Action] Ready a friendly unit.", "rally", 1, cost: 0));
        FindPlayer(state, 0)["base"]!.AsArray().Add(Unit("friendly-unit", ownerId: 0, exhausted: true));

        var played = engine.ApplyAction(state, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["targetUnitId"] = "friendly-unit" }), state.SequenceNumber);
        Assert.That(played.Accepted, Is.True);

        var resolved = PassChain(engine, played.State);

        var unit = FindPlayer(resolved.State, 0)["base"]!.AsArray().Single(node => node!["uid"]!.GetValue<string>() == "friendly-unit")!.AsObject();
        Assert.That(unit["exhausted"]!.GetValue<bool>(), Is.False);
        Assert.That(FindPlayer(resolved.State, 0)["trash"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Contains.Item("stand-ready"));
    }

    [Test]
    public void targeted_effect_does_not_resolve_when_target_becomes_illegal()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("bolt", "Bolt", "spell", "[Action] Deal 2 to an enemy unit.", "damage", 2, cost: 0));
        var target = Unit("target-unit", ownerId: 1);
        state.State["battlefields"]![0]!["units"]!.AsArray().Add(target);

        var played = engine.ApplyAction(state, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["targetUnitId"] = "target-unit" }), state.SequenceNumber);
        Assert.That(played.Accepted, Is.True);

        var liveTarget = played.State.State["battlefields"]![0]!["units"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(unit => unit["uid"]!.GetValue<string>() == "target-unit");
        liveTarget["ownerId"] = 0;
        var resolved = PassChain(engine, played.State);

        var resultUnit = resolved.State.State["battlefields"]![0]!["units"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(unit => unit["uid"]!.GetValue<string>() == "target-unit");
        Assert.That(resultUnit["damage"]!.GetValue<int>(), Is.EqualTo(0));
    }

    [Test]
    public void targeted_effect_does_not_follow_unit_that_leaves_and_returns_through_non_board_zone()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("bolt", "Bolt", "spell", "[Action] Deal 2 to an enemy unit.", "damage", 2, cost: 0));
        var target = Unit("target-unit", ownerId: 1);
        var battlefieldUnits = state.State["battlefields"]![0]!["units"]!.AsArray();
        battlefieldUnits.Add(target);

        var played = engine.ApplyAction(state, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["targetUnitId"] = "target-unit" }), state.SequenceNumber);
        Assert.That(played.Accepted, Is.True);

        var liveBattlefieldUnits = played.State.State["battlefields"]![0]!["units"]!.AsArray();
        var liveTarget = liveBattlefieldUnits.Single(unit => unit!["uid"]!.GetValue<string>() == "target-unit")!.AsObject();
        liveBattlefieldUnits.Remove(liveTarget);
        var returned = liveTarget.DeepClone().AsObject();
        FindPlayer(played.State, 1)["trash"]!.AsArray().Add(liveTarget.DeepClone());
        FindPlayer(played.State, 1)["base"]!.AsArray().Add(returned);

        var resolved = PassChain(engine, played.State);

        var baseUnit = FindPlayer(resolved.State, 1)["base"]!.AsArray().Single()!.AsObject();
        Assert.That(baseUnit["damage"]!.GetValue<int>(), Is.EqualTo(0));
    }

    [Test]
    public void lane_targeted_effect_partially_resolves_remaining_legal_targets()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("shockwave", "Shockwave", "spell", "[Action] Deal 1 to enemy units at a battlefield.", "damage", 1, cost: 0));
        var battlefield = state.State["battlefields"]![0]!.AsObject();
        var battlefieldId = battlefield["id"]!.GetValue<string>();
        var units = battlefield["units"]!.AsArray();
        units.Add(Unit("enemy-a", ownerId: 1, might: 2));
        units.Add(Unit("enemy-b", ownerId: 1, might: 2));

        var played = engine.ApplyAction(state, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["targetLaneId"] = battlefieldId }), state.SequenceNumber);
        Assert.That(played.Accepted, Is.True);

        var liveUnits = played.State.State["battlefields"]![0]!["units"]!.AsArray();
        var removed = liveUnits.Single(unit => unit!["uid"]!.GetValue<string>() == "enemy-a");
        liveUnits.Remove(removed);
        var resolved = PassChain(engine, played.State);

        var remaining = resolved.State.State["battlefields"]![0]!["units"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(unit => unit["uid"]!.GetValue<string>() == "enemy-b");
        Assert.That(remaining["uid"]!.GetValue<string>(), Is.EqualTo("enemy-b"));
        Assert.That(remaining["damage"]!.GetValue<int>(), Is.EqualTo(1));
    }

    [Test]
    public void up_to_targeted_effect_can_be_played_with_zero_targets()
    {
        var engine = new DefaultRulesEngine();
        var state = ReachMainPhase(engine);
        PutCardInHand(state, 0, Card("optional-rally", "Optional Rally", "spell", "[Action] Ready up to one friendly unit.", "rally", 1, cost: 0));

        var played = engine.ApplyAction(state, new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0 }), state.SequenceNumber);

        Assert.That(played.Accepted, Is.True);
        Assert.That(played.State.State["effectStack"]!.AsArray().Single()!["targets"]!.AsArray(), Is.Empty);
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

    private static void AssertFirstPlayerFirstDrawAdvance(string mode, int playerCount, bool shouldDraw)
    {
        var engine = new DefaultRulesEngine();
        var current = engine.CreateInitialState(MultiplayerConfig(mode, playerCount), MultiplayerDecks(playerCount), 123);
        current = ConfirmAllMulligans(engine, current, playerCount);

        Assert.That(current.State["turnPlayerId"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(current.State["turnPhase"]!.GetValue<string>(), Is.EqualTo("draw"));

        var playerBefore = Player(current, 0);
        var handBefore = playerBefore["hand"]!.AsArray().Count;
        var deckBefore = playerBefore["deck"]!.AsArray().Count;

        var result = engine.ApplyAction(
            current,
            new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()),
            current.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.State["turnPhase"]!.GetValue<string>(), Is.EqualTo("main"));

        var playerAfter = Player(result.State, 0);
        var expectedHand = shouldDraw ? handBefore + 1 : handBefore;
        var expectedDeck = shouldDraw ? deckBefore - 1 : deckBefore;
        Assert.That(playerAfter["hand"]!.AsArray(), Has.Count.EqualTo(expectedHand));
        Assert.That(playerAfter["deck"]!.AsArray(), Has.Count.EqualTo(expectedDeck));
    }

    private static EngineMatchState ConfirmAllMulligans(DefaultRulesEngine engine, EngineMatchState state, int playerCount)
    {
        var current = state;
        var turnOrder = current.State["turnOrder"]!.AsArray()
            .Select(node => node!.GetValue<int>())
            .Take(playerCount)
            .ToArray();
        foreach (var playerId in turnOrder)
        {
            var result = engine.ApplyAction(
                current,
                new EngineGameAction(playerId, "confirm-mulligan", new Dictionary<string, object?>()),
                current.SequenceNumber);

            Assert.That(result.Accepted, Is.True);
            current = result.State;
        }

        return current;
    }

    private static EngineActionResult PassChain(DefaultRulesEngine engine, EngineMatchState state)
    {
        var afterFirstPass = engine.ApplyAction(state, new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()), state.SequenceNumber);
        Assert.That(afterFirstPass.Accepted, Is.True);
        var afterSecondPass = engine.ApplyAction(afterFirstPass.State, new EngineGameAction(1, "pass-chain-window", new Dictionary<string, object?>()), afterFirstPass.State.SequenceNumber);
        Assert.That(afterSecondPass.Accepted, Is.True);
        return afterSecondPass;
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

    private static JsonObject Card(
        string id,
        string name,
        string kind,
        string text,
        string effectType,
        int amount,
        int cost,
        IReadOnlyDictionary<string, int>? powerCost = null,
        int universalPower = 0)
    {
        JsonNode costNode = powerCost is null && universalPower == 0
            ? JsonValue.Create(cost)!
            : new JsonObject
            {
                ["energy"] = cost,
                ["power"] = powerCost is null ? new JsonObject() : new JsonObject(powerCost.Select(item => KeyValuePair.Create<string, JsonNode?>(item.Key, JsonValue.Create(item.Value))).ToArray()),
                ["universalPower"] = universalPower
            };

        return new JsonObject
        {
            ["id"] = $"{id}-test",
            ["catalogId"] = id,
            ["name"] = name,
            ["kind"] = kind,
            ["tags"] = new JsonArray(),
            ["domain"] = "Fury",
            ["domains"] = new JsonArray("Fury"),
            ["cost"] = costNode,
            ["might"] = 0,
            ["text"] = text,
            ["image"] = string.Empty,
            ["cardType"] = kind,
            ["supertype"] = null,
            ["effect"] = new JsonObject { ["type"] = effectType, ["amount"] = amount }
        };
    }

    private static JsonObject Unit(string uid, int ownerId, int might = 2)
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
            ["exhausted"] = false,
            ["attacker"] = false,
            ["defender"] = false
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

    private static EngineMatchConfig MultiplayerConfig(string mode, int playerCount)
    {
        return new EngineMatchConfig(
            $"match-{mode}",
            mode,
            Enumerable.Range(0, playerCount)
                .Select(playerId => new EngineSeatConfig(
                    playerId,
                    $"user-demo-{playerId + 1:000}",
                    $"Demo {playerId + 1}",
                    mode == "teams-2v2" ? playerId / 2 : playerId))
                .ToArray(),
            Enumerable.Range(0, playerCount).Select(playerId => $"battlefield-{playerId}").ToArray(),
            0);
    }

    private static EngineMatchConfig Config(string mode, int playerCount)
    {
        return new EngineMatchConfig(
            "match-demo-001",
            mode,
            Enumerable.Range(0, playerCount)
                .Select(playerId => new EngineSeatConfig(playerId, $"user-demo-{playerId + 1:000}", $"Demo {playerId + 1}", mode == "teams-2v2" ? playerId % 2 : playerId))
                .ToArray(),
            Enumerable.Range(0, playerCount).Select(playerId => $"battlefield-{playerId}").ToArray(),
            0);
    }

    private static EngineMatchConfig MultiplayerConfig()
    {
        return new EngineMatchConfig(
            "match-demo-ffa",
            "ffa-3",
            Enumerable.Range(0, 3).Select(id => new EngineSeatConfig(id, $"user-demo-{id}", $"Demo {id}", id)).ToArray(),
            ["skybridge", "emberfield", "mistfield"],
            0);
    }

    private static EngineMatchConfig TeamConfig()
    {
        return new EngineMatchConfig(
            "match-demo-teams",
            "teams-2v2",
            [
                new EngineSeatConfig(0, "user-demo-001", "Demo One", 0),
                new EngineSeatConfig(1, "user-demo-002", "Demo Two", 1),
                new EngineSeatConfig(2, "user-demo-003", "Demo Three", 0),
                new EngineSeatConfig(3, "user-demo-004", "Demo Four", 1)
            ],
            ["skybridge", "emberfield", "mistfield"],
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

    private static IReadOnlyList<EnginePlayerDeck> MultiplayerDecks(int playerCount)
    {
        return Enumerable.Range(0, playerCount)
            .Select(playerId => new EnginePlayerDeck(
                $"deck-{playerId}",
                $"legend-{playerId}",
                $"champion-{playerId}",
                [$"battlefield-{playerId}"],
                [$"rune-{playerId}", $"rune-{playerId}", $"rune-{playerId}"],
                Enumerable.Range(0, 5).Select(index => $"unit-{playerId}-{index}").ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<EnginePlayerDeck> Decks(int playerCount) => MultiplayerDecks(playerCount);

    private static IReadOnlyList<EnginePlayerDeck> DecksWithLargerLibrary()
    {
        return
        [
            new EnginePlayerDeck("deck-a", "legend-a", "champion-a", ["skybridge"], ["rune-a", "rune-a", "rune-a"], Enumerable.Range(0, 12).Select(i => $"unit-a{i}").ToArray()),
            new EnginePlayerDeck("deck-b", "legend-b", "champion-b", ["emberfield"], ["rune-b", "rune-b", "rune-b"], Enumerable.Range(0, 12).Select(i => $"unit-b{i}").ToArray())
        ];
    }

    private static IReadOnlyList<EnginePlayerDeck> MultiplayerDecks() =>
        Enumerable.Range(0, 3)
            .Select(id => new EnginePlayerDeck($"deck-{id}", $"legend-{id}", $"champion-{id}", [$"field-{id}"], [$"rune-{id}", $"rune-{id}", $"rune-{id}"], [$"unit-{id}-a", $"unit-{id}-b", $"unit-{id}-c", $"unit-{id}-d"]))
            .ToArray();

    private static IReadOnlyList<EnginePlayerDeck> TeamDecks() =>
        Enumerable.Range(0, 4)
            .Select(id => new EnginePlayerDeck($"deck-{id}", $"legend-{id}", $"champion-{id}", [$"field-{id}"], [$"rune-{id}", $"rune-{id}", $"rune-{id}"], [$"unit-{id}-a", $"unit-{id}-b", $"unit-{id}-c", $"unit-{id}-d"]))
            .ToArray();

    private static JsonObject Unit(string uid, int ownerId, bool exhausted) =>
        new()
        {
            ["id"] = $"{uid}-card",
            ["catalogId"] = $"{uid}-card",
            ["uid"] = uid,
            ["name"] = uid,
            ["kind"] = "unit",
            ["ownerId"] = ownerId,
            ["cost"] = 1,
            ["might"] = 1,
            ["damage"] = 0,
            ["attachedMight"] = 0,
            ["exhausted"] = exhausted,
            ["attacker"] = false,
            ["defender"] = false
        };

    private static JsonObject Unit(string uid, int ownerId) => Unit(uid, ownerId, exhausted: false);

    private static int[] ActivePlayerIds(EngineMatchState state)
    {
        return state.State["players"]!.AsArray()
            .Select(node => node!["id"]!.GetValue<int>())
            .ToArray();
    }
}
