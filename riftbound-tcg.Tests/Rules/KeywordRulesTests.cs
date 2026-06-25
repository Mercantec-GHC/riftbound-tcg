using System.Text.Json.Nodes;
using riftbound_tcg.Core.Cards;
using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Tests.Rules;

public class KeywordRulesTests
{
    [Test]
    public void keyword_parser_models_the_rules_800_glossary()
    {
        var parsed = KeywordCatalog.Parse("[Accelerate] [Action] Assault 2 [Deathknell][>] Draw 1. Deflect 3 Ganking Hidden [Legion][>] Gain [Shield 2]. [Reaction] Shield 4 Tank Temporary Vision Equip [1] Quick-Draw Repeat [2] Weaponmaster Ambush Hunt 5 [Level 3][>] Gain Tank. Unique Backline");
        var kinds = parsed.Select(keyword => keyword.Kind).Distinct().ToArray();

        Assert.That(kinds, Is.SupersetOf(Enum.GetValues<KeywordKind>()));
        Assert.That(parsed.Single(keyword => keyword.Kind == KeywordKind.Assault).Value, Is.EqualTo(2));
        Assert.That(parsed.Single(keyword => keyword.Kind == KeywordKind.Deflect).Value, Is.EqualTo(3));
        Assert.That(parsed.Single(keyword => keyword.Kind == KeywordKind.Deathknell).Text, Does.Contain("Draw 1"));
    }

    [Test]
    public void reaction_keyword_definition_allows_chain_play_without_text_marker()
    {
        var card = new CardDefinition(
            "reactive-test",
            "Reactive Test",
            CardKind.Spell,
            [],
            Domain.Fury,
            [Domain.Fury],
            0,
            0,
            "",
            "",
            "Spell",
            null,
            new CardEffectDefinition(CardEffectType.Draw, 1))
        {
            Keywords = [Keyword(KeywordKind.Reaction)]
        };

        var result = ChainRules.ValidateChainPlay(card, playerId: 1, turnPlayerId: 0, chainWindow: ChainRules.Open());

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Subtype, Is.EqualTo(SpellSubtype.Reaction));
    }

    [Test]
    public void accelerate_additional_cost_makes_played_unit_enter_ready()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        var player = Player(state, 0);
        player["hand"] = new JsonArray(Card("swift-unit", "Swift Unit", "unit", 0, 2, Keyword(KeywordKind.Accelerate)));
        SetReadyRunes(player, 2);

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0, ["accelerate"] = true }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(Player(result.State, 0)["base"]!.AsArray().Single()!["exhausted"]!.GetValue<bool>(), Is.False);
        Assert.That(Player(result.State, 0)["runes"]!["exhausted"]!.AsArray(), Has.Count.EqualTo(2));
    }

    [Test]
    public void deflect_adds_server_validated_cost_to_opponent_targeting_spell()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        var target = Unit("deflect-unit", 1, 2, Keyword(KeywordKind.Deflect, value: 1));
        Player(state, 1)["base"]!.AsArray().Add(target);
        Player(state, 0)["hand"] = new JsonArray(Card("spark", "Spark", "spell", 0, 0, effectType: "damage", amount: 1));
        SetReadyRunes(Player(state, 0), 0);

        var rejected = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["targetUnitId"] = "deflect-unit" }),
            state.SequenceNumber);

        Assert.That(rejected.Accepted, Is.False);

        SetReadyRunes(Player(state, 0), 1);
        var accepted = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["targetUnitId"] = "deflect-unit" }),
            state.SequenceNumber);

        Assert.That(accepted.Accepted, Is.True);
    }

    [Test]
    public void ganking_allows_standard_move_between_battlefields()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        var first = Battlefield(state, 0);
        var second = Battlefield(state, 1);
        first["controllerId"] = 0;
        second["controllerId"] = 0;
        first["units"]!.AsArray().Add(Unit("ganker", 0, 2, exhausted: false, Keyword(KeywordKind.Ganking)));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = "ganker", ["battlefieldId"] = second["id"]!.GetValue<string>() }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(Battlefield(result.State, 1)["units"]!.AsArray().Single()!["uid"]!.GetValue<string>(), Is.EqualTo("ganker"));
    }

    [Test]
    public void unit_without_ganking_cannot_standard_move_between_battlefields()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        var first = Battlefield(state, 0);
        var second = Battlefield(state, 1);
        first["controllerId"] = 0;
        second["controllerId"] = 0;
        first["units"]!.AsArray().Add(Unit("walker", 0, 2, exhausted: false));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?> { ["unitId"] = "walker", ["battlefieldId"] = second["id"]!.GetValue<string>() }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void hidden_card_can_only_be_hidden_by_server_action_at_controlled_battlefield()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        var battlefield = Battlefield(state, 0);
        battlefield["controllerId"] = 0;
        Player(state, 0)["hand"] = new JsonArray(Card("hidden-trick", "Hidden Trick", "spell", 0, 0, Keyword(KeywordKind.Hidden)));
        SetReadyRunes(Player(state, 0), 1);

        Assert.That(engine.GetLegalActions(state, 0).Select(action => action.Type), Contains.Item("hide-card"));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "hide-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["battlefieldId"] = battlefield["id"]!.GetValue<string>() }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(Battlefield(result.State, 0)["hiddenCards"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(Player(result.State, 0)["hand"]!.AsArray(), Is.Empty);
    }

    [Test]
    public void shield_increases_defender_combat_might_and_lethal_damage()
    {
        var engine = new DefaultRulesEngine();
        var state = CombatState(engine, [Unit("attacker", 0, 2)], [Unit("shielded", 1, 2, Keyword(KeywordKind.Shield, value: 2))]);

        var result = ResolveCombat(engine, state, Attack(("shielded", 2)), Defend(("attacker", 4)));

        Assert.That(result.Accepted, Is.True);
        Assert.That(Battlefield(result.State, 0)["units"]!.AsArray().Single()!["uid"]!.GetValue<string>(), Is.EqualTo("shielded"));
    }

    [Test]
    public void tank_requires_lethal_assignment_before_non_tank_units()
    {
        var engine = new DefaultRulesEngine();
        var state = CombatState(engine, [Unit("attacker", 0, 4)], [Unit("tank", 1, 3, Keyword(KeywordKind.Tank)), Unit("other", 1, 2)]);

        var result = SubmitCombat(engine, state, 0, Attack(("tank", 2), ("other", 2)));

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void backline_requires_frontline_lethal_assignment_first()
    {
        var engine = new DefaultRulesEngine();
        var state = CombatState(engine, [Unit("attacker", 0, 4)], [Unit("front", 1, 3), Unit("back", 1, 2, Keyword(KeywordKind.Backline))]);

        var result = SubmitCombat(engine, state, 0, Attack(("front", 2), ("back", 2)));

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void temporary_kills_permanent_at_start_of_controller_beginning_before_hold_scoring()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        state.State["turnPhase"] = "beginning";
        Player(state, 0)["base"]!.AsArray().Add(Unit("temporary", 0, 2, Keyword(KeywordKind.Temporary)));
        Battlefield(state, 0)["controllerId"] = 0;

        var result = engine.ApplyAction(state, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(Player(result.State, 0)["base"]!.AsArray(), Is.Empty);
        Assert.That(Player(result.State, 0)["trash"]!.AsArray().Single()!["catalogId"]!.GetValue<string>(), Is.EqualTo("temporary-card"));
        Assert.That(Player(result.State, 0)["points"]!.GetValue<int>(), Is.EqualTo(1));
    }

    [Test]
    public void deathknell_resolves_when_permanent_is_killed()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        state.State["turnPhase"] = "beginning";
        Player(state, 0)["base"]!.AsArray().Add(Unit("omen", 0, 2, Keyword(KeywordKind.Temporary), Keyword(KeywordKind.Deathknell, text: "Draw 1")));
        Player(state, 0)["deck"] = new JsonArray(Card("drawn", "Drawn", "unit", 0, 1));

        var result = engine.ApplyAction(state, new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()), state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(Player(result.State, 0)["hand"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Contains.Item("drawn"));
    }

    [Test]
    public void ambush_legal_actions_include_reaction_play_to_battlefield_with_controlled_unit()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        OpenChainWindow(state, priorityPlayerId: 0);
        var battlefield = Battlefield(state, 0);
        battlefield["controllerId"] = 1;
        battlefield["units"]!.AsArray().Add(Unit("scout", 0, 2));
        Player(state, 0)["hand"] = new JsonArray(Card("ambusher", "Ambusher", "unit", 0, 2, Keyword(KeywordKind.Ambush)));

        var actions = engine.GetLegalActions(state, 0)
            .Where(action => action.Type == "play-unit")
            .ToArray();

        Assert.That(actions, Has.Some.Matches<EngineLegalAction>(action =>
            action.PayloadSchema?["handIndex"]?.GetValue<int>() == 0 &&
            action.PayloadSchema?["battlefieldId"]?.GetValue<string>() == battlefield["id"]!.GetValue<string>()));
        Assert.That(actions, Has.None.Matches<EngineLegalAction>(action => action.PayloadSchema?["battlefieldId"] is null));
    }

    [Test]
    public void ambush_unit_can_be_played_to_battlefield_where_controller_has_a_unit()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        var battlefield = Battlefield(state, 0);
        battlefield["controllerId"] = 1;
        battlefield["units"]!.AsArray().Add(Unit("forward-unit", 0, 2));
        Player(state, 0)["hand"] = new JsonArray(Card("ambusher", "Ambusher", "unit", 0, 2, Keyword(KeywordKind.Ambush)));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0, ["battlefieldId"] = battlefield["id"]!.GetValue<string>() }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(Battlefield(result.State, 0)["units"]!.AsArray().Select(unit => unit!["catalogId"]!.GetValue<string>()), Contains.Item("ambusher"));
        Assert.That(Player(result.State, 0)["hand"]!.AsArray(), Is.Empty);
    }

    [Test]
    public void ambush_unit_cannot_be_played_to_empty_uncontrolled_battlefield()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        var battlefield = Battlefield(state, 0);
        battlefield["controllerId"] = 1;
        Player(state, 0)["hand"] = new JsonArray(Card("ambusher", "Ambusher", "unit", 0, 2, Keyword(KeywordKind.Ambush)));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0, ["battlefieldId"] = battlefield["id"]!.GetValue<string>() }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void ambush_reaction_timing_rejects_non_ambush_unit_even_when_another_ambush_play_is_legal()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        OpenChainWindow(state, priorityPlayerId: 0);
        var battlefield = Battlefield(state, 0);
        battlefield["controllerId"] = 1;
        battlefield["units"]!.AsArray().Add(Unit("forward-unit", 0, 2));
        Player(state, 0)["hand"] = new JsonArray(
            Card("plain-unit", "Plain Unit", "unit", 0, 2),
            Card("ambusher", "Ambusher", "unit", 0, 2, Keyword(KeywordKind.Ambush)));

        Assert.That(engine.GetLegalActions(state, 0).Select(action => action.Type), Contains.Item("play-unit"));
        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0, ["battlefieldId"] = battlefield["id"]!.GetValue<string>() }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void ambush_requires_controlled_unit_at_finalization()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        var battlefield = Battlefield(state, 0);
        battlefield["controllerId"] = 1;
        battlefield["units"]!.AsArray().Add(Unit("forward-unit", 0, 2));
        Player(state, 0)["hand"] = new JsonArray(Card("ambusher", "Ambusher", "unit", 0, 2, Keyword(KeywordKind.Ambush)));

        Assert.That(engine.GetLegalActions(state, 0).Select(action => action.Type), Contains.Item("play-unit"));
        battlefield["units"] = new JsonArray();

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0, ["battlefieldId"] = battlefield["id"]!.GetValue<string>() }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void ambush_can_combine_with_accelerate_when_played_as_reaction()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        OpenChainWindow(state, priorityPlayerId: 0);
        var battlefield = Battlefield(state, 0);
        battlefield["controllerId"] = 1;
        battlefield["units"]!.AsArray().Add(Unit("forward-unit", 0, 2));
        Player(state, 0)["hand"] = new JsonArray(Card("swift-ambusher", "Swift Ambusher", "unit", 0, 2, Keyword(KeywordKind.Ambush), Keyword(KeywordKind.Accelerate)));
        SetReadyRunes(Player(state, 0), 2);

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0, ["battlefieldId"] = battlefield["id"]!.GetValue<string>(), ["accelerate"] = true }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        var ambusher = Battlefield(result.State, 0)["units"]!.AsArray()
            .Select(unit => unit!.AsObject())
            .Single(unit => unit["catalogId"]!.GetValue<string>() == "swift-ambusher");
        Assert.That(ambusher["exhausted"]!.GetValue<bool>(), Is.False);
        Assert.That(Player(result.State, 0)["runes"]!["exhausted"]!.AsArray(), Has.Count.EqualTo(2));
    }

    [Test]
    public void card_definition_continuous_effect_grants_ganking_to_engine_move_queries()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine, Catalog(
            UnitDefinition("captain", "Captain", 2, new CardContinuousEffectDefinition(
                "captain-grants-ganking",
                "ability",
                "add",
                "abilities",
                TextValue: "Ganking",
                AppliesTo: "other-friendly-units")),
            UnitDefinition("runner", "Runner", 2)),
            DecksWithPlayerZeroMain("captain", "runner", "unit-c", "unit-d"));
        var first = Battlefield(state, 0);
        var second = Battlefield(state, 1);
        first["controllerId"] = 0;
        second["controllerId"] = 0;
        first["units"] = new JsonArray(
            UnitFromCatalog(state, 0, "captain", "captain-unit", ownerId: 0, layerTimestamp: 1, battlefieldId: first["id"]!.GetValue<string>()),
            UnitFromCatalog(state, 0, "runner", "runner-unit", ownerId: 0, layerTimestamp: 2, battlefieldId: first["id"]!.GetValue<string>()));

        var legalMove = engine.GetLegalActions(state, 0)
            .SingleOrDefault(action =>
                action.Type == "move-unit" &&
                action.PayloadSchema?["unitId"]?.GetValue<string>() == "runner-unit" &&
                action.PayloadSchema?["battlefieldId"]?.GetValue<string>() == second["id"]!.GetValue<string>());

        Assert.That(legalMove, Is.Not.Null);

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?>
            {
                ["unitId"] = "runner-unit",
                ["battlefieldId"] = second["id"]!.GetValue<string>()
            }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(Battlefield(result.State, 1)["units"]!.AsArray().Select(unit => unit!["uid"]!.GetValue<string>()), Contains.Item("runner-unit"));
    }

    [Test]
    public void inactive_continuous_effect_source_does_not_grant_ganking_to_move_queries()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine, Catalog(
            UnitDefinition("captain", "Captain", 2, new CardContinuousEffectDefinition(
                "captain-grants-ganking",
                "ability",
                "add",
                "abilities",
                TextValue: "Ganking",
                AppliesTo: "other-friendly-units")),
            UnitDefinition("runner", "Runner", 2)),
            DecksWithPlayerZeroMain("captain", "runner", "unit-c", "unit-d"));
        var first = Battlefield(state, 0);
        var second = Battlefield(state, 1);
        first["controllerId"] = 0;
        second["controllerId"] = 0;
        var captain = UnitFromCatalog(state, 0, "captain", "captain-unit", ownerId: 0, layerTimestamp: 1, battlefieldId: first["id"]!.GetValue<string>());
        captain["isFaceDown"] = true;
        captain["rulesTextActive"] = false;
        first["units"] = new JsonArray(
            captain,
            UnitFromCatalog(state, 0, "runner", "runner-unit", ownerId: 0, layerTimestamp: 2, battlefieldId: first["id"]!.GetValue<string>()));

        var legalMove = engine.GetLegalActions(state, 0)
            .SingleOrDefault(action =>
                action.Type == "move-unit" &&
                action.PayloadSchema?["unitId"]?.GetValue<string>() == "runner-unit" &&
                action.PayloadSchema?["battlefieldId"]?.GetValue<string>() == second["id"]!.GetValue<string>());

        Assert.That(legalMove, Is.Null);
    }

    [Test]
    public void inactive_printed_keyword_does_not_enable_ganking_move_query()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        var first = Battlefield(state, 0);
        var second = Battlefield(state, 1);
        first["controllerId"] = 0;
        second["controllerId"] = 0;
        var runner = Unit("runner-unit", 0, 2, false, Keyword(KeywordKind.Ganking));
        runner["location"] = new JsonObject { ["type"] = "battlefield", ["battlefieldId"] = first["id"]!.GetValue<string>(), ["attachedToUid"] = null };
        runner["controllerId"] = 0;
        runner["isFaceDown"] = true;
        runner["rulesTextActive"] = false;
        first["units"] = new JsonArray(runner);

        var legalMove = engine.GetLegalActions(state, 0)
            .SingleOrDefault(action =>
                action.Type == "move-unit" &&
                action.PayloadSchema?["unitId"]?.GetValue<string>() == "runner-unit" &&
                action.PayloadSchema?["battlefieldId"]?.GetValue<string>() == second["id"]!.GetValue<string>());

        Assert.That(legalMove, Is.Null);
    }

    [Test]
    public void card_definition_continuous_effect_grants_tank_to_combat_assignment_query()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine, Catalog(
            UnitDefinition("warden", "Warden", 2, new CardContinuousEffectDefinition(
                "warden-grants-tank",
                "ability",
                "add",
                "abilities",
                TextValue: "Tank",
                AppliesTo: "other-friendly-units"))),
            DecksWithPlayerZeroMain("warden", "unit-b", "unit-c", "unit-d"));
        var warden = UnitFromCatalog(state, 0, "warden", "warden-unit", ownerId: 1, layerTimestamp: 1, battlefieldId: "field-a");
        var guard = Unit("guard", 1, 2);
        var attacker = Unit("attacker", 0, 4);
        state = CombatState(engine, [attacker], [warden, guard]);

        var result = SubmitCombat(engine, state, 0, Attack(("warden-unit", 4)));

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void vision_legal_actions_force_controller_to_keep_or_recycle_top_card()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        Player(state, 0)["hand"] = new JsonArray(Card("seer", "Seer", "unit", 0, 2, Keyword(KeywordKind.Vision)));
        Player(state, 0)["deck"] = new JsonArray(Card("top-card", "Top Card", "unit", 0, 1));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.State["pendingVision"]?["card"]?["catalogId"]?.GetValue<string>(), Is.EqualTo("top-card"));
        var controllerActions = engine.GetLegalActions(result.State, 0);
        Assert.That(controllerActions.Select(action => action.Type), Does.Contain("choose-vision"));
        Assert.That(controllerActions.Select(action => action.Type), Does.Not.Contain("play-unit"));
        Assert.That(controllerActions.Where(action => action.Type == "choose-vision").Select(action => action.PayloadSchema?["recycle"]?.GetValue<bool>()), Is.EquivalentTo(new[] { false, true }));
        Assert.That(engine.GetLegalActions(result.State, 1).Select(action => action.Type), Does.Not.Contain("choose-vision"));
    }

    [Test]
    public void vision_recycles_top_card_to_bottom_when_controller_chooses_recycle()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        Player(state, 0)["hand"] = new JsonArray(Card("seer", "Seer", "unit", 0, 2, Keyword(KeywordKind.Vision)));
        Player(state, 0)["deck"] = new JsonArray(
            Card("top-card", "Top Card", "unit", 0, 1),
            Card("second-card", "Second Card", "unit", 0, 1));

        var played = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        var resolved = engine.ApplyAction(
            played.State,
            new EngineGameAction(0, "choose-vision", new Dictionary<string, object?> { ["recycle"] = true }),
            played.State.SequenceNumber);

        Assert.That(resolved.Accepted, Is.True);
        Assert.That(resolved.State.State["pendingVision"], Is.Null);
        Assert.That(Player(resolved.State, 0)["deck"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Is.EqualTo(new[] { "second-card", "top-card" }));
    }

    [Test]
    public void vision_keep_leaves_top_card_for_repeated_vision_instance()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        Player(state, 0)["hand"] = new JsonArray(Card("seer", "Seer", "unit", 0, 2, Keyword(KeywordKind.Vision), Keyword(KeywordKind.Vision)));
        Player(state, 0)["deck"] = new JsonArray(
            Card("top-card", "Top Card", "unit", 0, 1),
            Card("second-card", "Second Card", "unit", 0, 1));

        var played = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        var firstChoice = engine.ApplyAction(
            played.State,
            new EngineGameAction(0, "choose-vision", new Dictionary<string, object?> { ["recycle"] = false }),
            played.State.SequenceNumber);

        Assert.That(firstChoice.Accepted, Is.True);
        Assert.That(firstChoice.State.State["pendingVision"]?["remainingChoices"]?.GetValue<int>(), Is.EqualTo(1));
        Assert.That(firstChoice.State.State["pendingVision"]?["card"]?["catalogId"]?.GetValue<string>(), Is.EqualTo("top-card"));

        var secondChoice = engine.ApplyAction(
            firstChoice.State,
            new EngineGameAction(0, "choose-vision", new Dictionary<string, object?> { ["recycle"] = false }),
            firstChoice.State.SequenceNumber);

        Assert.That(secondChoice.Accepted, Is.True);
        Assert.That(secondChoice.State.State["pendingVision"], Is.Null);
        Assert.That(Player(secondChoice.State, 0)["deck"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Is.EqualTo(new[] { "top-card", "second-card" }));
    }

    [Test]
    public void vision_pending_choice_rejects_other_actions_until_resolved()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        Player(state, 0)["hand"] = new JsonArray(
            Card("seer", "Seer", "unit", 0, 2, Keyword(KeywordKind.Vision)),
            Card("second-unit", "Second Unit", "unit", 0, 1));
        Player(state, 0)["deck"] = new JsonArray(Card("top-card", "Top Card", "unit", 0, 1));

        var played = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        var rejectedOtherAction = engine.ApplyAction(
            played.State,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            played.State.SequenceNumber);
        var rejectedOpponentChoice = engine.ApplyAction(
            played.State,
            new EngineGameAction(1, "choose-vision", new Dictionary<string, object?> { ["recycle"] = true }),
            played.State.SequenceNumber);

        Assert.That(rejectedOtherAction.Accepted, Is.False);
        Assert.That(rejectedOpponentChoice.Accepted, Is.False);
    }

    [Test]
    public void vision_pending_card_identity_is_redacted_from_opponents()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        Player(state, 0)["hand"] = new JsonArray(Card("seer", "Seer", "unit", 0, 2, Keyword(KeywordKind.Vision)));
        Player(state, 0)["deck"] = new JsonArray(Card("top-card", "Top Card", "unit", 0, 1));

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);

        var controllerView = PlayerViewRedactor.Redact(result.State.State, viewerPlayerId: 0);
        var opponentView = PlayerViewRedactor.Redact(result.State.State, viewerPlayerId: 1);

        Assert.That(controllerView["pendingVision"]?["card"]?["catalogId"]?.GetValue<string>(), Is.EqualTo("top-card"));
        Assert.That(opponentView["pendingVision"]?["card"]?["hidden"]?.GetValue<bool>(), Is.True);
    }

    [Test]
    public void legion_dependent_cost_reduction_turns_on_after_another_card_was_played_this_turn()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        Player(state, 0)["hand"] = new JsonArray(
            Card("legion-recruit", "Legion Recruit", "unit", 2, 2, Keyword(KeywordKind.Legion, text: "I cost [2] less")),
            Card("primer", "Primer", "unit", 0, 1));
        SetReadyRunes(Player(state, 0), 0);

        var rejectedBeforeLegion = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);
        Assert.That(rejectedBeforeLegion.Accepted, Is.False);

        var primer = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 1 }),
            state.SequenceNumber);
        var legion = engine.ApplyAction(
            primer.State,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            primer.State.SequenceNumber);

        Assert.That(legion.Accepted, Is.True);
        Assert.That(Player(legion.State, 0)["base"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Contains.Item("legion-recruit"));
    }

    [Test]
    public void hunt_xp_satisfies_level_dependent_keyword_for_live_move_queries()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        var first = Battlefield(state, 0);
        var second = Battlefield(state, 1);
        first["controllerId"] = 0;
        second["controllerId"] = 0;
        first["units"] = new JsonArray(
            Unit("hunter", 0, 2, exhausted: false, Keyword(KeywordKind.Hunt, value: 3)),
            Unit("trainee", 0, 2, exhausted: false, Keyword(KeywordKind.Level, value: 3, text: "I have Ganking")));
        state.State["turnPhase"] = "beginning";

        var scored = engine.ApplyAction(
            state,
            new EngineGameAction(0, "advance-phase", new Dictionary<string, object?>()),
            state.SequenceNumber);

        Assert.That(scored.Accepted, Is.True);
        Assert.That(Player(scored.State, 0)["xp"]!.GetValue<int>(), Is.EqualTo(3));

        scored.State.State["turnPhase"] = "main";
        var legalMove = engine.GetLegalActions(scored.State, 0)
            .SingleOrDefault(action =>
                action.Type == "move-unit" &&
                action.PayloadSchema?["unitId"]?.GetValue<string>() == "trainee" &&
                action.PayloadSchema?["battlefieldId"]?.GetValue<string>() == second["id"]!.GetValue<string>());
        Assert.That(legalMove, Is.Not.Null);

        var moved = engine.ApplyAction(
            scored.State,
            new EngineGameAction(0, "move-unit", new Dictionary<string, object?>
            {
                ["unitId"] = "trainee",
                ["battlefieldId"] = second["id"]!.GetValue<string>()
            }),
            scored.State.SequenceNumber);

        Assert.That(moved.Accepted, Is.True);
        Assert.That(Battlefield(moved.State, 1)["units"]!.AsArray().Select(unit => unit!["uid"]!.GetValue<string>()), Contains.Item("trainee"));
    }

    [Test]
    public void equip_action_attaches_controlled_gear_with_equip_to_controlled_unit_and_pays_cost()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        Player(state, 0)["base"]!.AsArray().Add(Unit("bearer", 0, 2, exhausted: false));
        Player(state, 0)["baseGear"]!.AsArray().Add(Gear("blade", 0, Keyword(KeywordKind.Equip, cost: "[1]")));
        SetReadyRunes(Player(state, 0), 1);

        Assert.That(engine.GetLegalActions(state, 0).Select(action => action.Type), Contains.Item("equip"));
        var equipped = engine.ApplyAction(
            state,
            new EngineGameAction(0, "equip", new Dictionary<string, object?> { ["gearUid"] = "blade", ["targetUnitId"] = "bearer" }),
            state.SequenceNumber);

        Assert.That(equipped.Accepted, Is.True);
        Assert.That(Player(equipped.State, 0)["baseGear"]!.AsArray(), Is.Empty);
        Assert.That(Player(equipped.State, 0)["base"]![0]!["attachedCards"]!.AsArray().Single()!["uid"]!.GetValue<string>(), Is.EqualTo("blade"));
        Assert.That(Player(equipped.State, 0)["runes"]!["exhausted"]!.AsArray(), Has.Count.EqualTo(1));
    }

    [Test]
    public void equip_action_rejects_enemy_unit_target()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        Player(state, 1)["base"]!.AsArray().Add(Unit("enemy", 1, 2, exhausted: false));
        Player(state, 0)["baseGear"]!.AsArray().Add(Gear("blade", 0, Keyword(KeywordKind.Equip, cost: "[0]")));

        var equipped = engine.ApplyAction(
            state,
            new EngineGameAction(0, "equip", new Dictionary<string, object?> { ["gearUid"] = "blade", ["targetUnitId"] = "enemy" }),
            state.SequenceNumber);

        Assert.That(equipped.Accepted, Is.False);
    }

    [Test]
    public void repeat_optional_cost_executes_spell_effect_an_additional_time()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        Player(state, 0)["hand"] = new JsonArray(Card("echo-draw", "Echo Draw", "spell", 0, 0, "draw", 1, Keyword(KeywordKind.Repeat, cost: "[1]")));
        Player(state, 0)["deck"] = new JsonArray(Card("first-draw", "First Draw", "unit", 0, 1), Card("second-draw", "Second Draw", "unit", 0, 1));
        SetReadyRunes(Player(state, 0), 1);

        var played = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["repeatCount"] = 1 }),
            state.SequenceNumber);
        Assert.That(played.Accepted, Is.True);

        var afterFirstPass = engine.ApplyAction(played.State, new EngineGameAction(0, "pass-chain-window", new Dictionary<string, object?>()), played.State.SequenceNumber);
        var resolved = engine.ApplyAction(afterFirstPass.State, new EngineGameAction(1, "pass-chain-window", new Dictionary<string, object?>()), afterFirstPass.State.SequenceNumber);

        Assert.That(resolved.Accepted, Is.True);
        Assert.That(Player(resolved.State, 0)["hand"]!.AsArray().Select(card => card!["catalogId"]!.GetValue<string>()), Is.EqualTo(new[] { "first-draw", "second-draw" }));
        Assert.That(Player(resolved.State, 0)["runes"]!["exhausted"]!.AsArray(), Has.Count.EqualTo(1));
    }

    [Test]
    public void repeat_optional_cost_is_rejected_when_unpaid()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        Player(state, 0)["hand"] = new JsonArray(Card("echo-draw", "Echo Draw", "spell", 0, 0, "draw", 1, Keyword(KeywordKind.Repeat, cost: "[1]")));
        SetReadyRunes(Player(state, 0), 0);

        var played = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-card", new Dictionary<string, object?> { ["handIndex"] = 0, ["repeatCount"] = 1 }),
            state.SequenceNumber);

        Assert.That(played.Accepted, Is.False);
    }

    [Test]
    public void weaponmaster_pays_discounted_equip_cost_and_attaches_equipment_when_unit_is_played()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        Player(state, 0)["hand"] = new JsonArray(Card("arms-caller", "Arms Caller", "unit", 0, 2, Keyword(KeywordKind.Weaponmaster)));
        Player(state, 0)["baseGear"]!.AsArray().Add(Gear("training-blade", 0, Keyword(KeywordKind.Equip, cost: "[2]")));
        SetReadyRunes(Player(state, 0), 1);

        var played = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0, ["weaponmasterGearUid"] = "training-blade" }),
            state.SequenceNumber);

        Assert.That(played.Accepted, Is.True);
        var unit = Player(played.State, 0)["base"]!.AsArray().Single()!.AsObject();
        Assert.That(unit["attachedCards"]!.AsArray().Single()!["uid"]!.GetValue<string>(), Is.EqualTo("training-blade"));
        Assert.That(Player(played.State, 0)["baseGear"]!.AsArray(), Is.Empty);
        Assert.That(Player(played.State, 0)["runes"]!["exhausted"]!.AsArray(), Has.Count.EqualTo(1));
    }

    [Test]
    public void unique_has_no_additional_in_game_restriction_after_deck_construction()
    {
        var engine = new DefaultRulesEngine();
        var state = ReadyMainState(engine);
        Player(state, 0)["hand"] = new JsonArray(
            Card("singular-copy", "Singular Copy", "unit", 0, 2, Keyword(KeywordKind.Unique)),
            Card("singular-copy", "Singular Copy", "unit", 0, 2, Keyword(KeywordKind.Unique)));

        var first = engine.ApplyAction(
            state,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            state.SequenceNumber);
        var second = engine.ApplyAction(
            first.State,
            new EngineGameAction(0, "play-unit", new Dictionary<string, object?> { ["handIndex"] = 0 }),
            first.State.SequenceNumber);

        Assert.That(first.Accepted, Is.True);
        Assert.That(second.Accepted, Is.True);
        Assert.That(Player(second.State, 0)["base"]!.AsArray(), Has.Count.EqualTo(2));
    }

    private static EngineActionResult ResolveCombat(DefaultRulesEngine engine, EngineMatchState state, Dictionary<string, int> attackerAssignments, Dictionary<string, int> defenderAssignments)
    {
        var afterAttacker = SubmitCombat(engine, state, 0, attackerAssignments);
        Assert.That(afterAttacker.Accepted, Is.True);
        return SubmitCombat(engine, afterAttacker.State, 1, defenderAssignments);
    }

    private static EngineActionResult SubmitCombat(DefaultRulesEngine engine, EngineMatchState state, int playerId, Dictionary<string, int> assignments) =>
        engine.ApplyAction(state, new EngineGameAction(playerId, "resolve-combat", new Dictionary<string, object?> { ["battlefieldId"] = "field-a", ["assignments"] = assignments }), state.SequenceNumber);

    private static Dictionary<string, int> Attack(params (string Uid, int Damage)[] assignments) =>
        assignments.ToDictionary(assignment => assignment.Uid, assignment => assignment.Damage);

    private static Dictionary<string, int> Defend(params (string Uid, int Damage)[] assignments) =>
        assignments.ToDictionary(assignment => assignment.Uid, assignment => assignment.Damage);

    private static EngineMatchState ReadyMainState(
        DefaultRulesEngine engine,
        IReadOnlyDictionary<string, CardDefinition>? catalog = null,
        IReadOnlyList<EnginePlayerDeck>? decks = null)
    {
        var state = engine.CreateInitialState(Config(), decks ?? Decks(), 123, catalog);
        state.State["stage"] = "playing";
        state.State["turnPhase"] = "main";
        state.State["turnPlayerId"] = 0;
        state.State["activePlayer"] = 0;
        return state;
    }

    private static JsonObject UnitFromCatalog(EngineMatchState state, int playerId, string catalogId, string uid, int ownerId, int layerTimestamp, string battlefieldId)
    {
        var unit = FindCatalogCard(state, playerId, catalogId).DeepClone().AsObject();
        unit["uid"] = uid;
        unit["ownerId"] = ownerId;
        unit["controllerId"] = ownerId;
        unit["location"] = new JsonObject { ["type"] = "battlefield", ["battlefieldId"] = battlefieldId, ["attachedToUid"] = null };
        unit["layerTimestamp"] = layerTimestamp;
        unit["damage"] = 0;
        unit["attachedMight"] = 0;
        unit["exhausted"] = false;
        unit["attacker"] = ownerId == 0;
        unit["defender"] = ownerId == 1;
        unit["isToken"] = false;
        unit["isFaceDown"] = false;
        unit["rulesTextActive"] = true;
        unit["attachedCards"] = new JsonArray();
        return unit;
    }

    private static JsonObject FindCatalogCard(EngineMatchState state, int playerId, string catalogId)
    {
        var player = Player(state, playerId);
        foreach (var zone in new[] { "hand", "deck" })
        {
            var card = player[zone]!.AsArray()
                .Select(node => node!.AsObject())
                .FirstOrDefault(card => card["catalogId"]?.GetValue<string>() == catalogId);
            if (card is not null)
            {
                return card;
            }
        }

        throw new InvalidOperationException($"Could not find catalog card '{catalogId}'.");
    }

    private static EngineMatchState CombatState(DefaultRulesEngine engine, JsonObject[] attackers, JsonObject[] defenders)
    {
        var state = ReadyMainState(engine);
        var battlefield = Battlefield(state, 0);
        battlefield["id"] = "field-a";
        battlefield["controllerId"] = 1;
        battlefield["contestedByPlayerId"] = 0;
        battlefield["units"] = new JsonArray();
        foreach (var unit in attackers.Concat(defenders))
        {
            battlefield["units"]!.AsArray().Add(unit);
        }

        state.State["activeCombat"] = new JsonObject { ["battlefieldId"] = "field-a", ["attackerPlayerId"] = 0, ["defenderPlayerId"] = 1, ["damageStep"] = true };
        state.State["activeShowdown"] = new JsonObject { ["battlefieldId"] = "field-a", ["kind"] = "combat" };
        return state;
    }

    private static void OpenChainWindow(EngineMatchState state, int priorityPlayerId)
    {
        state.State["chainWindow"] = new JsonObject
        {
            ["priorityPlayerId"] = priorityPlayerId,
            ["startedByPlayerId"] = priorityPlayerId,
            ["source"] = "played-card",
            ["passesFocusOnClose"] = true,
            ["passedByPlayer"] = new JsonObject()
        };
        state.State["priorityPlayerId"] = priorityPlayerId;
        state.State["activePlayer"] = priorityPlayerId;
    }

    private static JsonObject Card(string id, string name, string kind, int cost, int might, params CardKeywordDefinition[] keywords) =>
        Card(id, name, kind, cost, might, "rally", 0, keywords);

    private static JsonObject Card(string id, string name, string kind, int cost, int might, string effectType, int amount, params CardKeywordDefinition[] keywords) =>
        new()
        {
            ["id"] = $"{id}-test",
            ["catalogId"] = id,
            ["name"] = name,
            ["kind"] = kind,
            ["tags"] = new JsonArray(),
            ["domain"] = "Fury",
            ["domains"] = new JsonArray("Fury"),
            ["cost"] = cost,
            ["might"] = might,
            ["text"] = string.Empty,
            ["image"] = string.Empty,
            ["cardType"] = kind,
            ["supertype"] = null,
            ["keywords"] = new JsonArray(keywords.Select(KeywordObject).ToArray<JsonNode?>()),
            ["effect"] = new JsonObject { ["type"] = effectType, ["amount"] = amount }
        };

    private static JsonObject Unit(string uid, int ownerId, int might, params CardKeywordDefinition[] keywords) =>
        Unit(uid, ownerId, might, true, keywords);

    private static JsonObject Unit(string uid, int ownerId, int might, bool exhausted = true, params CardKeywordDefinition[] keywords)
    {
        var unit = Card($"{uid}-card", uid, "unit", 0, might, keywords);
        unit["uid"] = uid;
        unit["ownerId"] = ownerId;
        unit["damage"] = 0;
        unit["attachedMight"] = 0;
        unit["exhausted"] = exhausted;
        unit["attacker"] = ownerId == 0;
        unit["defender"] = ownerId == 1;
        return unit;
    }

    private static JsonObject Gear(string uid, int ownerId, params CardKeywordDefinition[] keywords)
    {
        var gear = Card($"{uid}-card", uid, "gear", 0, 0, keywords);
        gear["uid"] = uid;
        gear["ownerId"] = ownerId;
        gear["controllerId"] = ownerId;
        gear["tags"] = new JsonArray("Equipment");
        gear["cardType"] = "Equipment";
        gear["location"] = new JsonObject { ["type"] = "base", ["battlefieldId"] = null, ["attachedToUid"] = null };
        gear["attachedUnitId"] = null;
        gear["isToken"] = false;
        gear["isFaceDown"] = false;
        gear["rulesTextActive"] = true;
        gear["attachedCards"] = new JsonArray();
        gear["topCardId"] = gear["id"]?.GetValue<string>() ?? string.Empty;
        return gear;
    }

    private static CardKeywordDefinition Keyword(KeywordKind kind, int? value = null, string? text = null, string? cost = null) =>
        new(kind, Behavior(kind), value, cost, text);

    private static KeywordBehavior Behavior(KeywordKind kind) =>
        kind switch
        {
            KeywordKind.Action or KeywordKind.Reaction or KeywordKind.Hidden => KeywordBehavior.Permissive,
            KeywordKind.Accelerate => KeywordBehavior.OptionalAdditionalCost,
            KeywordKind.Deflect => KeywordBehavior.MandatoryAdditionalCost,
            KeywordKind.Deathknell or KeywordKind.Temporary => KeywordBehavior.Triggered,
            KeywordKind.Tank or KeywordKind.Backline or KeywordKind.Ganking or KeywordKind.Shield or KeywordKind.Assault => KeywordBehavior.Passive,
            KeywordKind.Unique => KeywordBehavior.DeckConstraint,
            KeywordKind.Equip => KeywordBehavior.Activated,
            KeywordKind.Legion or KeywordKind.Level => KeywordBehavior.Dependent,
            _ => KeywordBehavior.Triggered
        };

    private static JsonObject KeywordObject(CardKeywordDefinition keyword)
    {
        var json = new JsonObject { ["kind"] = keyword.Kind.ToString(), ["behavior"] = keyword.Behavior.ToString() };
        if (keyword.Value is not null) json["value"] = keyword.Value.Value;
        if (keyword.Cost is not null) json["cost"] = keyword.Cost;
        if (keyword.Text is not null) json["text"] = keyword.Text;
        return json;
    }

    private static JsonObject Player(EngineMatchState state, int playerId) =>
        state.State["players"]!.AsArray().First(player => player!["id"]!.GetValue<int>() == playerId)!.AsObject();

    private static JsonObject Battlefield(EngineMatchState state, int index) =>
        state.State["battlefields"]!.AsArray()[index]!.AsObject();

    private static void SetReadyRunes(JsonObject player, int count)
    {
        player["runes"]!["ready"] = new JsonArray(Enumerable.Range(0, count).Select(index => JsonValue.Create($"rune-{index}")).ToArray<JsonNode?>());
        player["runes"]!["exhausted"] = new JsonArray();
        player["runePool"]!["energy"] = 0;
    }

    private static EngineMatchConfig Config() =>
        new(
            "keyword-test",
            "duel-1v1",
            [new EngineSeatConfig(0, "user-0", "Player 0", 0), new EngineSeatConfig(1, "user-1", "Player 1", 1)],
            ["field-a", "field-b"],
            0);

    private static IReadOnlyList<EnginePlayerDeck> Decks() =>
        [
            new EnginePlayerDeck("deck-a", "legend-a", "champion-a", ["field-a"], ["rune-a", "rune-a", "rune-a"], ["unit-a", "unit-b", "unit-c", "unit-d"]),
            new EnginePlayerDeck("deck-b", "legend-b", "champion-b", ["field-b"], ["rune-b", "rune-b", "rune-b"], ["unit-e", "unit-f", "unit-g", "unit-h"])
        ];

    private static IReadOnlyList<EnginePlayerDeck> DecksWithPlayerZeroMain(params string[] mainDeckIds) =>
        [
            new EnginePlayerDeck("deck-a", "legend-a", "champion-a", ["field-a"], ["rune-a", "rune-a", "rune-a"], mainDeckIds),
            new EnginePlayerDeck("deck-b", "legend-b", "champion-b", ["field-b"], ["rune-b", "rune-b", "rune-b"], ["unit-e", "unit-f", "unit-g", "unit-h"])
        ];

    private static IReadOnlyDictionary<string, CardDefinition> Catalog(params CardDefinition[] definitions) =>
        definitions.ToDictionary(card => card.Id, StringComparer.OrdinalIgnoreCase);

    private static CardDefinition UnitDefinition(string id, string name, int might, params CardContinuousEffectDefinition[] continuousEffects) =>
        new(
            id,
            name,
            CardKind.Unit,
            [],
            Domain.Fury,
            [Domain.Fury],
            0,
            might,
            string.Empty,
            string.Empty,
            "Unit",
            null,
            new CardEffectDefinition(CardEffectType.Rally, 0))
        {
            ContinuousEffects = continuousEffects
        };
}
