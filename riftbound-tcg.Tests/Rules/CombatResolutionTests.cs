using System.Text.Json.Nodes;
using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Tests.Rules;

public class CombatResolutionTests
{
    [Test]
    public void resolve_combat_is_legal_for_combat_participants()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 3)], [Unit("defender-a", 1, 3)]);

        var attackerActions = engine.GetLegalActions(state, 0).Select(action => action.Type);
        var defenderActions = engine.GetLegalActions(state, 1).Select(action => action.Type);

        Assert.That(attackerActions, Contains.Item("resolve-combat"));
        Assert.That(defenderActions, Contains.Item("resolve-combat"));
    }

    [Test]
    public void player_assignment_does_not_resolve_combat_until_the_opponent_assigns()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 3)], [Unit("defender-a", 1, 3)]);

        var first = SubmitAssignment(engine, state, 0, Attack(("defender-a", 3)));

        Assert.That(first.Accepted, Is.True);
        Assert.That(first.State.State["activeCombat"], Is.Not.Null);
        Assert.That(engine.GetLegalActions(first.State, 0).Select(action => action.Type), Has.None.EqualTo("resolve-combat"));
        Assert.That(engine.GetLegalActions(first.State, 1).Select(action => action.Type), Contains.Item("resolve-combat"));
    }

    [Test]
    public void one_on_one_combat_can_be_won_by_the_attacker()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 4)], [Unit("defender-a", 1, 2)]);

        var result = Resolve(engine, state, Attack(("defender-a", 4)), Defend(("attacker-a", 2)));

        Assert.That(result.Accepted, Is.True);
        var battlefield = Battlefield(result.State);
        Assert.That(battlefield["controllerId"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(UnitIds(battlefield), Is.EqualTo(new[] { "attacker-a" }));
        Assert.That(Player(result.State, 1)["trash"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(PlayerPoints(result.State, 0), Is.EqualTo(1));
    }

    [Test]
    public void one_on_one_combat_can_be_won_by_the_defender()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 2)], [Unit("defender-a", 1, 4)]);

        var result = Resolve(engine, state, Attack(("defender-a", 2)), Defend(("attacker-a", 4)));

        Assert.That(result.Accepted, Is.True);
        var battlefield = Battlefield(result.State);
        Assert.That(battlefield["controllerId"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(UnitIds(battlefield), Is.EqualTo(new[] { "defender-a" }));
        Assert.That(Player(result.State, 0)["trash"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(PlayerPoints(result.State, 1), Is.EqualTo(1));
    }

    [Test]
    public void mutual_destruction_leaves_the_battlefield_uncontrolled()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 3)], [Unit("defender-a", 1, 3)]);

        var result = Resolve(engine, state, Attack(("defender-a", 3)), Defend(("attacker-a", 3)));

        Assert.That(result.Accepted, Is.True);
        var battlefield = Battlefield(result.State);
        Assert.That(battlefield["controllerId"], Is.Null);
        Assert.That(battlefield["units"]!.AsArray(), Is.Empty);
        Assert.That(PlayerPoints(result.State, 0), Is.EqualTo(0));
        Assert.That(PlayerPoints(result.State, 1), Is.EqualTo(0));
    }

    [Test]
    public void combat_conquest_can_gain_the_final_point_after_every_battlefield_was_scored_this_turn()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 4)], [Unit("defender-a", 1, 2)]);
        SetPlayerPoints(state, 0, 7);
        var otherBattlefieldIds = state.State["battlefields"]!.AsArray()
            .Select(field => field!["id"]!.GetValue<string>())
            .Where(id => id != "field-a")
            .ToArray();
        state.State["scoredBattlefieldIdsThisTurn"] = new JsonObject { ["0"] = new JsonArray(otherBattlefieldIds.Select(id => JsonValue.Create(id)).ToArray()) };

        var result = Resolve(engine, state, Attack(("defender-a", 4)), Defend(("attacker-a", 2)));

        Assert.That(result.Accepted, Is.True);
        Assert.That(PlayerPoints(result.State, 0), Is.EqualTo(8));
        Assert.That(result.State.Stage, Is.EqualTo("game-over"));
    }

    [Test]
    public void many_attackers_can_combine_damage_into_one_defender()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 2), Unit("attacker-b", 0, 2)], [Unit("defender-a", 1, 4)]);

        var result = Resolve(engine, state, Attack(("defender-a", 4)), Defend(("attacker-a", 2), ("attacker-b", 2)));

        Assert.That(result.Accepted, Is.True);
        Assert.That(Battlefield(result.State)["units"]!.AsArray(), Is.Empty);
        Assert.That(Player(result.State, 0)["trash"]!.AsArray(), Has.Count.EqualTo(2));
        Assert.That(Player(result.State, 1)["trash"]!.AsArray(), Has.Count.EqualTo(1));
    }

    [Test]
    public void one_attacker_can_assign_lethal_then_spill_over_to_another_defender()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 5)], [Unit("defender-a", 1, 3), Unit("defender-b", 1, 3)]);

        var result = Resolve(engine, state, Attack(("defender-a", 3), ("defender-b", 2)), Defend(("attacker-a", 6)));

        Assert.That(result.Accepted, Is.True);
        var battlefield = Battlefield(result.State);
        Assert.That(UnitIds(battlefield), Is.EqualTo(new[] { "defender-b" }));
        Assert.That(battlefield["controllerId"]!.GetValue<int>(), Is.EqualTo(1));
    }

    [Test]
    public void many_vs_many_damage_is_dealt_simultaneously()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(
            engine,
            [Unit("attacker-a", 0, 3), Unit("attacker-b", 0, 2)],
            [Unit("defender-a", 1, 2), Unit("defender-b", 1, 3)]);

        var result = Resolve(engine, state, Attack(("defender-a", 2), ("defender-b", 3)), Defend(("attacker-a", 3), ("attacker-b", 2)));

        Assert.That(result.Accepted, Is.True);
        Assert.That(Battlefield(result.State)["units"]!.AsArray(), Is.Empty);
        Assert.That(Player(result.State, 0)["trash"]!.AsArray(), Has.Count.EqualTo(2));
        Assert.That(Player(result.State, 1)["trash"]!.AsArray(), Has.Count.EqualTo(2));
    }

    [Test]
    public void no_result_re_stages_combat_when_both_sides_survive()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, -1)], [Unit("defender-a", 1, -1)]);

        var result = Resolve(engine, state, Attack(("defender-a", 0)), Defend(("attacker-a", 0)));

        Assert.That(result.Accepted, Is.True);
        var battlefield = Battlefield(result.State);
        Assert.That(UnitIds(battlefield), Is.EqualTo(new[] { "attacker-a", "defender-a" }));
        Assert.That(battlefield["stagedCombat"]!.GetValue<bool>(), Is.True);
        Assert.That(result.State.State["activeCombat"], Is.Null);
        Assert.That(battlefield["controllerId"], Is.Null);
    }

    [Test]
    public void surviving_units_are_healed_after_combat_cleanup()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 5, damage: 1)], [Unit("defender-a", 1, 2)]);

        var result = Resolve(engine, state, Attack(("defender-a", 5)), Defend(("attacker-a", 2)));

        Assert.That(result.Accepted, Is.True);
        var attacker = Battlefield(result.State)["units"]!.AsArray().First(unit => unit!["uid"]!.GetValue<string>() == "attacker-a")!.AsObject();
        Assert.That(attacker["damage"]!.GetValue<int>(), Is.EqualTo(0));
    }

    [Test]
    public void attached_might_counts_for_combat_damage_and_lethal_damage()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 2, attachedMight: 2)], [Unit("defender-a", 1, 4)]);

        var result = Resolve(engine, state, Attack(("defender-a", 4)), Defend(("attacker-a", 4)));

        Assert.That(result.Accepted, Is.True);
        Assert.That(Battlefield(result.State)["units"]!.AsArray(), Is.Empty);
    }

    [Test]
    public void negative_current_might_contributes_zero_combat_damage()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 2, attachedMight: -4)], [Unit("defender-a", 1, 3)]);

        var result = Resolve(engine, state, Attack(("defender-a", 0)), Defend(("attacker-a", 3)));

        Assert.That(result.Accepted, Is.True);
        var battlefield = Battlefield(result.State);
        Assert.That(UnitIds(battlefield), Is.EqualTo(new[] { "defender-a" }));
        Assert.That(battlefield["controllerId"]!.GetValue<int>(), Is.EqualTo(1));
    }

    [Test]
    public void resolve_combat_is_rejected_without_active_combat()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);
        state.State["stage"] = "playing";

        var result = SubmitAssignment(engine, state, 0, Attack(("defender-a", 1)));

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void resolve_combat_is_rejected_for_the_wrong_battlefield()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 3)], [Unit("defender-a", 1, 3)]);

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "resolve-combat", new Dictionary<string, object?>
            {
                ["battlefieldId"] = "wrong-field",
                ["assignments"] = Attack(("defender-a", 3))
            }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void resolve_combat_is_rejected_when_more_than_two_players_have_units_at_the_battlefield()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 3)], [Unit("defender-a", 1, 3)], "ffa-3");
        Battlefield(state)["units"]!.AsArray().Add(Unit("third-a", 2, 3));

        var result = SubmitAssignment(engine, state, 0, Attack(("defender-a", 3)));

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void resolve_combat_is_rejected_when_assignment_targets_a_friendly_unit()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 3)], [Unit("defender-a", 1, 3)]);

        var result = SubmitAssignment(engine, state, 0, Attack(("attacker-a", 3)));

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void resolve_combat_is_rejected_when_assignment_targets_a_missing_unit()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 3)], [Unit("defender-a", 1, 3)]);

        var result = SubmitAssignment(engine, state, 0, Attack(("missing", 3)));

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void resolve_combat_is_rejected_when_assigned_total_does_not_match_summed_might()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 3)], [Unit("defender-a", 1, 3)]);

        var result = SubmitAssignment(engine, state, 0, Attack(("defender-a", 2)));

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void resolve_combat_is_rejected_when_damage_is_spread_without_lethal_assignment()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 4)], [Unit("defender-a", 1, 3), Unit("defender-b", 1, 3)]);

        var result = SubmitAssignment(engine, state, 0, Attack(("defender-a", 2), ("defender-b", 2)));

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void resolve_combat_is_rejected_when_over_assigning_before_all_opposing_units_are_lethal()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 5)], [Unit("defender-a", 1, 3), Unit("defender-b", 1, 3)]);

        var result = SubmitAssignment(engine, state, 0, Attack(("defender-a", 4), ("defender-b", 1)));

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void resolve_combat_is_rejected_for_a_non_participant()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 3)], [Unit("defender-a", 1, 3)], "ffa-3");

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(2, "resolve-combat", Payload(Attack(("defender-a", 3)))),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
    }

    [Test]
    public void resolve_combat_is_rejected_when_player_submits_the_opponent_assignment()
    {
        var engine = new DefaultRulesEngine();
        var state = SeedCombat(engine, [Unit("attacker-a", 0, 3)], [Unit("defender-a", 1, 3)]);

        var result = engine.ApplyAction(
            state,
            new EngineGameAction(0, "resolve-combat", new Dictionary<string, object?>
            {
                ["battlefieldId"] = "field-a",
                ["assignments"] = Attack(("defender-a", 3)),
                ["defenderAssignments"] = Defend(("attacker-a", 3))
            }),
            state.SequenceNumber);

        Assert.That(result.Accepted, Is.False);
    }

    private static EngineActionResult Resolve(DefaultRulesEngine engine, EngineMatchState state, Dictionary<string, int> attackerAssignments, Dictionary<string, int> defenderAssignments)
    {
        var afterAttacker = SubmitAssignment(engine, state, 0, attackerAssignments);
        Assert.That(afterAttacker.Accepted, Is.True);
        return SubmitAssignment(engine, afterAttacker.State, 1, defenderAssignments);
    }

    private static EngineActionResult SubmitAssignment(DefaultRulesEngine engine, EngineMatchState state, int playerId, Dictionary<string, int> assignments)
    {
        return engine.ApplyAction(
            state,
            new EngineGameAction(playerId, "resolve-combat", Payload(assignments)),
            state.SequenceNumber);
    }

    private static Dictionary<string, object?> Payload(Dictionary<string, int> assignments) =>
        new()
        {
            ["battlefieldId"] = "field-a",
            ["assignments"] = assignments
        };

    private static Dictionary<string, int> Attack(params (string Uid, int Damage)[] assignments) =>
        assignments.ToDictionary(assignment => assignment.Uid, assignment => assignment.Damage);

    private static Dictionary<string, int> Defend(params (string Uid, int Damage)[] assignments) =>
        assignments.ToDictionary(assignment => assignment.Uid, assignment => assignment.Damage);

    private static EngineMatchState SeedCombat(DefaultRulesEngine engine, JsonObject[] attackers, JsonObject[] defenders, string mode = "duel-1v1")
    {
        var state = engine.CreateInitialState(Config(mode), Decks(mode), 123);
        state.State["stage"] = "playing";
        state.State["turnPhase"] = "main";
        state.State["activeCombat"] = new JsonObject
        {
            ["battlefieldId"] = "field-a",
            ["attackerPlayerId"] = 0,
            ["defenderPlayerId"] = 1
        };
        state.State["activeShowdown"] = new JsonObject
        {
            ["battlefieldId"] = "field-a",
            ["kind"] = "combat"
        };

        var battlefield = state.State["battlefields"]!.AsArray()[0]!.AsObject();
        battlefield["id"] = "field-a";
        battlefield["name"] = "Field A";
        battlefield["controllerId"] = 1;
        battlefield["contestedByPlayerId"] = 0;
        battlefield["stagedCombat"] = true;
        battlefield["stagedShowdown"] = true;
        battlefield["units"] = new JsonArray();
        foreach (var unit in attackers.Concat(defenders))
        {
            battlefield["units"]!.AsArray().Add(unit);
        }

        return state;
    }

    private static JsonObject Unit(string uid, int ownerId, int might, int damage = 0, int attachedMight = 0) =>
        new()
        {
            ["id"] = $"{uid}-card",
            ["catalogId"] = $"{uid}-card",
            ["uid"] = uid,
            ["name"] = uid,
            ["kind"] = "unit",
            ["ownerId"] = ownerId,
            ["cost"] = 1,
            ["might"] = might,
            ["damage"] = damage,
            ["attachedMight"] = attachedMight,
            ["exhausted"] = true,
            ["attacker"] = ownerId == 0,
            ["defender"] = ownerId == 1
        };

    private static JsonObject Battlefield(EngineMatchState state) =>
        state.State["battlefields"]!.AsArray().First(field => field!["id"]!.GetValue<string>() == "field-a")!.AsObject();

    private static JsonObject Player(EngineMatchState state, int playerId) =>
        state.State["players"]!.AsArray().First(player => player!["id"]!.GetValue<int>() == playerId)!.AsObject();

    private static int PlayerPoints(EngineMatchState state, int playerId) =>
        Player(state, playerId)["points"]!.GetValue<int>();

    private static void SetPlayerPoints(EngineMatchState state, int playerId, int points) =>
        Player(state, playerId)["points"] = points;

    private static JsonObject TestUnitCard(string id) =>
        new()
        {
            ["id"] = id,
            ["catalogId"] = id,
            ["name"] = id,
            ["kind"] = "unit",
            ["cost"] = 1,
            ["might"] = 1,
            ["attachedMight"] = 0,
            ["damage"] = 0
        };

    private static string[] UnitIds(JsonObject battlefield) =>
        battlefield["units"]!.AsArray().Select(unit => unit!["uid"]!.GetValue<string>()).ToArray();

    private static EngineMatchConfig Config(string mode = "duel-1v1")
    {
        var playerCount = mode == "ffa-3" ? 3 : 2;
        return new EngineMatchConfig(
            "combat-test",
            mode,
            Enumerable.Range(0, playerCount).Select(id => new EngineSeatConfig(id, $"user-{id}", $"Player {id}", id)).ToArray(),
            ["field-a", "field-b", "field-c"],
            0);
    }

    private static IReadOnlyList<EnginePlayerDeck> Decks(string mode = "duel-1v1")
    {
        var playerCount = mode == "ffa-3" ? 3 : 2;
        return Enumerable.Range(0, playerCount)
            .Select(id => new EnginePlayerDeck($"deck-{id}", $"legend-{id}", $"champion-{id}", [$"field-{id}"], [$"rune-{id}", $"rune-{id}", $"rune-{id}"], [$"unit-{id}-a", $"unit-{id}-b", $"unit-{id}-c", $"unit-{id}-d"]))
            .ToArray();
    }
}
