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
            new CardEffectDefinition(CardEffectType.Draw, 1),
            [Keyword(KeywordKind.Reaction)]);

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

    private static EngineMatchState ReadyMainState(DefaultRulesEngine engine)
    {
        var state = engine.CreateInitialState(Config(), Decks(), 123);
        state.State["stage"] = "playing";
        state.State["turnPhase"] = "main";
        state.State["turnPlayerId"] = 0;
        state.State["activePlayer"] = 0;
        return state;
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

    private static CardKeywordDefinition Keyword(KeywordKind kind, int? value = null, string? text = null) =>
        new(kind, Behavior(kind), value, null, text);

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
}
