using System.Text.Json.Nodes;
using riftbound_tcg.Engine.GameActions;

namespace riftbound_tcg.Tests.Rules;

[TestFixture]
public class InternalGameActionExecutorTests
{
    [Test]
    public void recycle_moves_trash_to_deck_and_clears_trash()
    {
        var state = TestState();
        Player(state, 0)["trash"] = new JsonArray(Card("trash-a"), Card("trash-b"));

        var result = Apply(state, InternalGameActionType.Recycle);

        Assert.That(result.Accepted, Is.True);
        Assert.That(Player(result.State, 0)["trash"]!.AsArray(), Is.Empty);
        Assert.That(Player(result.State, 0)["deck"]!.AsArray(), Has.Count.EqualTo(4));
    }

    [Test]
    public void discard_moves_hand_card_to_trash()
    {
        var state = TestState();

        var result = Apply(state, InternalGameActionType.Discard, new() { ["handIndex"] = 0 });

        Assert.That(result.Accepted, Is.True);
        Assert.That(Player(result.State, 0)["hand"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(Player(result.State, 0)["trash"]!.AsArray().Single()!["id"]!.GetValue<string>(), Is.EqualTo("hand-a"));
    }

    [Test]
    public void reveal_records_public_card_reference_without_moving_card()
    {
        var state = TestState();

        var result = Apply(state, InternalGameActionType.Reveal, new() { ["zone"] = "hand", ["index"] = 1 });

        Assert.That(result.Accepted, Is.True);
        Assert.That(Player(result.State, 0)["hand"]!.AsArray(), Has.Count.EqualTo(2));
        Assert.That(result.State["revealedCards"]!.AsArray().Single()!["card"]!["id"]!.GetValue<string>(), Is.EqualTo("hand-b"));
    }

    [Test]
    public void kill_moves_unit_to_owners_trash()
    {
        var state = TestState();
        Player(state, 0)["base"]!.AsArray().Add(Unit("unit-a", 0));

        var result = Apply(state, InternalGameActionType.Kill, new() { ["unitId"] = "unit-a" });

        Assert.That(result.Accepted, Is.True);
        Assert.That(Player(result.State, 0)["base"]!.AsArray(), Is.Empty);
        Assert.That(Player(result.State, 0)["trash"]!.AsArray().Single()!["id"]!.GetValue<string>(), Is.EqualTo("card-unit-a"));
    }

    [Test]
    public void banish_removes_unit_to_banished_zone()
    {
        var state = TestState();
        Player(state, 0)["base"]!.AsArray().Add(Unit("unit-a", 0));

        var result = Apply(state, InternalGameActionType.Banish, new() { ["unitId"] = "unit-a" });

        Assert.That(result.Accepted, Is.True);
        Assert.That(Player(result.State, 0)["base"]!.AsArray(), Is.Empty);
        Assert.That(result.State["banished"]!.AsArray().Single()!["id"]!.GetValue<string>(), Is.EqualTo("card-unit-a"));
    }

    [Test]
    public void stun_exhausts_unit()
    {
        var state = TestState();
        Player(state, 0)["base"]!.AsArray().Add(Unit("unit-a", 0, exhausted: false));

        var result = Apply(state, InternalGameActionType.Stun, new() { ["unitId"] = "unit-a" });

        Assert.That(result.Accepted, Is.True);
        Assert.That(Player(result.State, 0)["base"]!.AsArray().Single()!["exhausted"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void counter_removes_stack_item_and_trashes_source_card()
    {
        var state = TestState();
        state["effectStack"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "stack-a",
            ["playerId"] = 0,
            ["card"] = Card("spell-a")
        });

        var result = Apply(state, InternalGameActionType.Counter, new() { ["stackItemId"] = "stack-a" });

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State["effectStack"]!.AsArray(), Is.Empty);
        Assert.That(Player(result.State, 0)["trash"]!.AsArray().Single()!["id"]!.GetValue<string>(), Is.EqualTo("spell-a"));
    }

    [Test]
    public void prevent_records_damage_prevention_marker()
    {
        var state = TestState();
        Player(state, 0)["base"]!.AsArray().Add(Unit("unit-a", 0));

        var result = Apply(state, InternalGameActionType.Prevent, new() { ["unitId"] = "unit-a", ["amount"] = 2 });

        Assert.That(result.Accepted, Is.True);
        var marker = result.State["damagePrevention"]!.AsArray().Single()!;
        Assert.That(marker["unitId"]!.GetValue<string>(), Is.EqualTo("unit-a"));
        Assert.That(marker["amount"]!.GetValue<int>(), Is.EqualTo(2));
    }

    [Test]
    public void create_adds_unit_to_requested_destination()
    {
        var state = TestState();

        var result = Apply(state, InternalGameActionType.Create, new()
        {
            ["destination"] = "base",
            ["cardId"] = "spark-token",
            ["name"] = "Spark Token",
            ["kind"] = "unit",
            ["might"] = 2
        });

        Assert.That(result.Accepted, Is.True);
        var unit = Player(result.State, 0)["base"]!.AsArray().Single()!;
        Assert.That(unit["uid"]!.GetValue<string>(), Is.EqualTo("unit-10"));
        Assert.That(unit["name"]!.GetValue<string>(), Is.EqualTo("Spark Token"));
        Assert.That(result.State["nextUid"]!.GetValue<int>(), Is.EqualTo(11));
    }

    [Test]
    public void predict_reorders_top_deck_cards()
    {
        var state = TestState();

        var result = Apply(state, InternalGameActionType.Predict, new() { ["count"] = 2, ["order"] = new[] { 1, 0 } });

        Assert.That(result.Accepted, Is.True);
        var deckIds = Player(result.State, 0)["deck"]!.AsArray().Select(card => card!["id"]!.GetValue<string>()).ToArray();
        Assert.That(deckIds, Is.EqualTo(new[] { "deck-b", "deck-a" }));
    }

    [Test]
    public void attach_links_gear_to_unit_and_adds_might()
    {
        var state = TestState();
        Player(state, 0)["base"]!.AsArray().Add(Unit("unit-a", 0, might: 2));
        Player(state, 0)["baseGear"]!.AsArray().Add(Gear("gear-a", 0, might: 3));

        var result = Apply(state, InternalGameActionType.Attach, new() { ["gearId"] = "gear-a", ["unitId"] = "unit-a" });

        Assert.That(result.Accepted, Is.True);
        Assert.That(Player(result.State, 0)["baseGear"]!.AsArray().Single()!["attachedUnitId"]!.GetValue<string>(), Is.EqualTo("unit-a"));
        Assert.That(Player(result.State, 0)["base"]!.AsArray().Single()!["attachedMight"]!.GetValue<int>(), Is.EqualTo(3));
    }

    [Test]
    public void detach_unlinks_gear_and_removes_might()
    {
        var state = TestState();
        Player(state, 0)["base"]!.AsArray().Add(Unit("unit-a", 0, might: 2, attachedMight: 3));
        Player(state, 0)["baseGear"]!.AsArray().Add(Gear("gear-a", 0, might: 3, attachedUnitId: "unit-a"));

        var result = Apply(state, InternalGameActionType.Detach, new() { ["gearId"] = "gear-a" });

        Assert.That(result.Accepted, Is.True);
        Assert.That(Player(result.State, 0)["baseGear"]!.AsArray().Single()!["attachedUnitId"], Is.Null);
        Assert.That(Player(result.State, 0)["base"]!.AsArray().Single()!["attachedMight"]!.GetValue<int>(), Is.EqualTo(0));
    }

    [Test]
    public void swap_exchanges_unit_locations()
    {
        var state = TestState();
        Player(state, 0)["base"]!.AsArray().Add(Unit("unit-a", 0));
        Battlefield(state, "field-a")["units"]!.AsArray().Add(Unit("unit-b", 1, location: "battlefield", battlefieldId: "field-a"));

        var result = Apply(state, InternalGameActionType.Swap, new() { ["firstUnitId"] = "unit-a", ["secondUnitId"] = "unit-b" });

        Assert.That(result.Accepted, Is.True);
        Assert.That(Player(result.State, 0)["base"]!.AsArray().Single()!["uid"]!.GetValue<string>(), Is.EqualTo("unit-b"));
        Assert.That(Battlefield(result.State, "field-a")["units"]!.AsArray().Single()!["uid"]!.GetValue<string>(), Is.EqualTo("unit-a"));
    }

    [Test]
    public void double_doubles_current_unit_might_with_modifier()
    {
        var state = TestState();
        Player(state, 0)["base"]!.AsArray().Add(Unit("unit-a", 0, might: 2, attachedMight: 1));

        var result = Apply(state, InternalGameActionType.Double, new() { ["unitId"] = "unit-a" });

        Assert.That(result.Accepted, Is.True);
        Assert.That(Player(result.State, 0)["base"]!.AsArray().Single()!["attachedMight"]!.GetValue<int>(), Is.EqualTo(4));
    }

    [Test]
    public void recall_returns_unit_to_owners_hand()
    {
        var state = TestState();
        Battlefield(state, "field-a")["units"]!.AsArray().Add(Unit("unit-a", 0, location: "battlefield", battlefieldId: "field-a"));

        var result = Apply(state, InternalGameActionType.Recall, new() { ["unitId"] = "unit-a" });

        Assert.That(result.Accepted, Is.True);
        Assert.That(Battlefield(result.State, "field-a")["units"]!.AsArray(), Is.Empty);
        Assert.That(Player(result.State, 0)["hand"]!.AsArray().Select(card => card!["id"]!.GetValue<string>()), Does.Contain("card-unit-a"));
    }

    [TestCaseSource(nameof(InvalidActions))]
    public void invalid_internal_actions_are_rejected(InternalGameActionType type, Dictionary<string, object?> payload)
    {
        var state = TestState();

        var result = Apply(state, type, payload);

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.State, Is.SameAs(state));
    }

    private static IEnumerable<TestCaseData> InvalidActions()
    {
        yield return new TestCaseData(InternalGameActionType.Recycle, new Dictionary<string, object?>()).SetName("invalid_recycle_rejects_empty_trash");
        yield return new TestCaseData(InternalGameActionType.Discard, new Dictionary<string, object?> { ["handIndex"] = 99 }).SetName("invalid_discard_rejects_missing_hand_index");
        yield return new TestCaseData(InternalGameActionType.Reveal, new Dictionary<string, object?> { ["zone"] = "void", ["index"] = 0 }).SetName("invalid_reveal_rejects_missing_zone");
        yield return new TestCaseData(InternalGameActionType.Kill, new Dictionary<string, object?> { ["unitId"] = "missing" }).SetName("invalid_kill_rejects_missing_unit");
        yield return new TestCaseData(InternalGameActionType.Banish, new Dictionary<string, object?> { ["zone"] = "hand", ["index"] = 99 }).SetName("invalid_banish_rejects_missing_card");
        yield return new TestCaseData(InternalGameActionType.Stun, new Dictionary<string, object?> { ["unitId"] = "missing" }).SetName("invalid_stun_rejects_missing_unit");
        yield return new TestCaseData(InternalGameActionType.Counter, new Dictionary<string, object?> { ["stackItemId"] = "missing" }).SetName("invalid_counter_rejects_missing_stack_item");
        yield return new TestCaseData(InternalGameActionType.Prevent, new Dictionary<string, object?> { ["unitId"] = "missing", ["amount"] = 1 }).SetName("invalid_prevent_rejects_missing_unit");
        yield return new TestCaseData(InternalGameActionType.Create, new Dictionary<string, object?> { ["destination"] = "battlefield", ["battlefieldId"] = "missing" }).SetName("invalid_create_rejects_missing_battlefield");
        yield return new TestCaseData(InternalGameActionType.Predict, new Dictionary<string, object?> { ["count"] = 2, ["order"] = new[] { 0, 0 } }).SetName("invalid_predict_rejects_duplicate_order");
        yield return new TestCaseData(InternalGameActionType.Attach, new Dictionary<string, object?> { ["gearId"] = "missing", ["unitId"] = "missing" }).SetName("invalid_attach_rejects_missing_gear");
        yield return new TestCaseData(InternalGameActionType.Detach, new Dictionary<string, object?> { ["gearId"] = "missing" }).SetName("invalid_detach_rejects_missing_gear");
        yield return new TestCaseData(InternalGameActionType.Swap, new Dictionary<string, object?> { ["firstUnitId"] = "unit-a", ["secondUnitId"] = "unit-a" }).SetName("invalid_swap_rejects_same_unit");
        yield return new TestCaseData(InternalGameActionType.Double, new Dictionary<string, object?> { ["unitId"] = "missing" }).SetName("invalid_double_rejects_missing_unit");
        yield return new TestCaseData(InternalGameActionType.Recall, new Dictionary<string, object?> { ["unitId"] = "missing" }).SetName("invalid_recall_rejects_missing_unit");
    }

    private static InternalGameActionResult Apply(
        JsonObject state,
        InternalGameActionType type,
        Dictionary<string, object?>? payload = null) =>
        InternalGameActionExecutor.Apply(state, new InternalGameAction(type, 0, payload ?? new Dictionary<string, object?>()));

    private static JsonObject TestState()
    {
        return new JsonObject
        {
            ["rngState"] = 123,
            ["nextUid"] = 10,
            ["players"] = new JsonArray(Player(0), Player(1)),
            ["battlefields"] = new JsonArray(new JsonObject
            {
                ["id"] = "field-a",
                ["name"] = "Field A",
                ["units"] = new JsonArray()
            }),
            ["effectStack"] = new JsonArray()
        };
    }

    private static JsonObject Player(int id)
    {
        return new JsonObject
        {
            ["id"] = id,
            ["name"] = $"Player {id}",
            ["deck"] = new JsonArray(Card("deck-a"), Card("deck-b")),
            ["hand"] = new JsonArray(Card("hand-a"), Card("hand-b")),
            ["trash"] = new JsonArray(),
            ["base"] = new JsonArray(),
            ["baseGear"] = new JsonArray()
        };
    }

    private static JsonObject Card(string id)
    {
        return new JsonObject
        {
            ["id"] = id,
            ["catalogId"] = id,
            ["name"] = id,
            ["kind"] = "unit",
            ["might"] = 1
        };
    }

    private static JsonObject Unit(
        string uid,
        int ownerId,
        int might = 1,
        int attachedMight = 0,
        bool exhausted = false,
        string location = "base",
        string? battlefieldId = null)
    {
        return new JsonObject
        {
            ["id"] = $"card-{uid}",
            ["catalogId"] = $"card-{uid}",
            ["name"] = uid,
            ["kind"] = "unit",
            ["uid"] = uid,
            ["ownerId"] = ownerId,
            ["might"] = might,
            ["attachedMight"] = attachedMight,
            ["damage"] = 0,
            ["exhausted"] = exhausted,
            ["attacker"] = false,
            ["defender"] = false,
            ["location"] = new JsonObject { ["type"] = location, ["battlefieldId"] = battlefieldId }
        };
    }

    private static JsonObject Gear(string uid, int ownerId, int might, string? attachedUnitId = null)
    {
        return new JsonObject
        {
            ["id"] = $"card-{uid}",
            ["catalogId"] = $"card-{uid}",
            ["name"] = uid,
            ["kind"] = "gear",
            ["uid"] = uid,
            ["ownerId"] = ownerId,
            ["might"] = might,
            ["exhausted"] = false,
            ["attachedUnitId"] = attachedUnitId,
            ["location"] = new JsonObject { ["type"] = "base", ["battlefieldId"] = null }
        };
    }

    private static JsonObject Player(JsonObject state, int playerId) =>
        state["players"]!.AsArray().Select(node => node!.AsObject()).Single(player => player["id"]!.GetValue<int>() == playerId);

    private static JsonObject Battlefield(JsonObject state, string battlefieldId) =>
        state["battlefields"]!.AsArray().Select(node => node!.AsObject()).Single(field => field["id"]!.GetValue<string>() == battlefieldId);
}
