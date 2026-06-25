using System.Text.Json.Nodes;
using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Tests.Rules;

public sealed class PlayerViewRedactorTests
{
    [Test]
    public void player_view_keeps_own_hand_but_redacts_opponent_hand_and_all_deck_order()
    {
        var engine = new DefaultRulesEngine();
        var state = engine.CreateInitialState(Config(), Decks(), 123);

        var view = PlayerViewRedactor.Redact(state.State, viewerPlayerId: 0);
        var viewer = Player(view, 0);
        var opponent = Player(view, 1);
        var internalViewer = Player(state.State, 0);
        var internalOpponent = Player(state.State, 1);

        Assert.That(CatalogIds(viewer["hand"]!.AsArray()), Is.EqualTo(CatalogIds(internalViewer["hand"]!.AsArray())));
        Assert.That(CatalogIds(opponent["hand"]!.AsArray()), Is.All.Null);
        Assert.That(opponent["hand"]!.AsArray().Select(card => card!["hidden"]!.GetValue<bool>()), Is.All.True);
        Assert.That(viewer["hand"]!.AsArray(), Has.Count.EqualTo(internalViewer["hand"]!.AsArray().Count));
        Assert.That(opponent["hand"]!.AsArray(), Has.Count.EqualTo(internalOpponent["hand"]!.AsArray().Count));

        Assert.That(CatalogIds(viewer["deck"]!.AsArray()), Is.All.Null);
        Assert.That(CatalogIds(viewer["runeDeck"]!.AsArray()), Is.All.Null);
        Assert.That(CatalogIds(opponent["deck"]!.AsArray()), Is.All.Null);
        Assert.That(CatalogIds(opponent["runeDeck"]!.AsArray()), Is.All.Null);
        Assert.That(viewer["deck"]!.AsArray(), Has.Count.EqualTo(internalViewer["deck"]!.AsArray().Count));
        Assert.That(opponent["deck"]!.AsArray(), Has.Count.EqualTo(internalOpponent["deck"]!.AsArray().Count));

        Assert.That(CatalogIds(internalOpponent["hand"]!.AsArray()), Is.Not.All.Null);
        Assert.That(CatalogIds(internalOpponent["deck"]!.AsArray()), Is.Not.All.Null);
    }

    [Test]
    public void facedown_card_identity_is_hidden_until_revealed_to_viewer()
    {
        var state = BaseState();
        var facedownUnit = new JsonObject
        {
            ["id"] = "secret-unit-instance",
            ["catalogId"] = "secret-unit",
            ["name"] = "Secret Unit",
            ["kind"] = "unit",
            ["tags"] = new JsonArray(),
            ["domain"] = "Fury",
            ["domains"] = new JsonArray("Fury"),
            ["cost"] = 2,
            ["might"] = 3,
            ["text"] = "Ambush.",
            ["image"] = "*",
            ["cardType"] = "Unit",
            ["supertype"] = null,
            ["effect"] = new JsonObject { ["type"] = "rally", ["amount"] = 0 },
            ["uid"] = "unit-1",
            ["ownerId"] = 1,
            ["location"] = new JsonObject { ["type"] = "battlefield", ["battlefieldId"] = "field-a" },
            ["exhausted"] = false,
            ["damage"] = 0,
            ["attachedMight"] = 0,
            ["faceDown"] = true,
            ["revealedToPlayerIds"] = new JsonArray()
        };
        state["battlefields"]![0]!["units"]!.AsArray().Add(facedownUnit);

        var hiddenFromOpponent = PlayerViewRedactor.Redact(state, viewerPlayerId: 0);
        var hiddenUnit = hiddenFromOpponent["battlefields"]![0]!["units"]![0]!.AsObject();
        Assert.That(hiddenUnit["catalogId"], Is.Null);
        Assert.That(hiddenUnit["name"]!.GetValue<string>(), Is.EqualTo("Hidden card"));
        Assert.That(hiddenUnit["uid"]!.GetValue<string>(), Is.EqualTo("unit-1"));
        Assert.That(hiddenUnit["ownerId"]!.GetValue<int>(), Is.EqualTo(1));

        var ownerView = PlayerViewRedactor.Redact(state, viewerPlayerId: 1);
        Assert.That(ownerView["battlefields"]![0]!["units"]![0]!["catalogId"]!.GetValue<string>(), Is.EqualTo("secret-unit"));

        facedownUnit["revealedToPlayerIds"]!.AsArray().Add(0);
        var revealedToOpponent = PlayerViewRedactor.Redact(state, viewerPlayerId: 0);
        Assert.That(revealedToOpponent["battlefields"]![0]!["units"]![0]!["catalogId"]!.GetValue<string>(), Is.EqualTo("secret-unit"));

        facedownUnit["faceDown"] = false;
        facedownUnit["revealedToPlayerIds"] = new JsonArray();
        var faceUpView = PlayerViewRedactor.Redact(state, viewerPlayerId: 0);
        Assert.That(faceUpView["battlefields"]![0]!["units"]![0]!["catalogId"]!.GetValue<string>(), Is.EqualTo("secret-unit"));
    }

    [Test]
    public void spectator_view_redacts_all_player_private_and_secret_zones()
    {
        var state = BaseState();
        Player(state, 0)["hand"]!.AsArray().Add(Card("hand-a", "Player Zero Hand"));
        Player(state, 1)["hand"]!.AsArray().Add(Card("hand-b", "Player One Hand"));
        Player(state, 0)["deck"]!.AsArray().Add(Card("deck-a", "Player Zero Deck"));
        Player(state, 1)["runeDeck"]!.AsArray().Add(Card("rune-a", "Player One Rune"));

        var view = PlayerViewRedactor.RedactForSpectator(state);

        Assert.That(Player(view, 0)["hand"]![0]!["name"]!.GetValue<string>(), Is.EqualTo("Hidden card"));
        Assert.That(Player(view, 1)["hand"]![0]!["name"]!.GetValue<string>(), Is.EqualTo("Hidden card"));
        Assert.That(Player(view, 0)["deck"]![0]!["catalogId"], Is.Null);
        Assert.That(Player(view, 1)["runeDeck"]![0]!["catalogId"], Is.Null);
        Assert.That(view["viewerPlayerId"], Is.Null);
    }

    [Test]
    public void hidden_battlefield_cards_keep_public_ownership_but_hide_identity_from_non_controller()
    {
        var state = BaseState();
        var hiddenCard = Card("hidden-trick", "Hidden Trick");
        hiddenCard["uid"] = "hidden-1";
        hiddenCard["ownerId"] = 1;
        hiddenCard["controllerId"] = 1;
        hiddenCard["hiddenAtBattlefieldId"] = "field-a";
        hiddenCard["hiddenTurnNumber"] = 3;
        hiddenCard["facedown"] = true;
        hiddenCard["information"] = new JsonObject
        {
            ["privacy"] = "private",
            ["controllerId"] = 1,
            ["faceDownIdentityKnownToPlayerIds"] = new JsonArray(1)
        };
        state["battlefields"]![0]!["hiddenCards"]!.AsArray().Add(hiddenCard);

        var opponentView = PlayerViewRedactor.Redact(state, viewerPlayerId: 0);
        var redacted = opponentView["battlefields"]![0]!["hiddenCards"]![0]!.AsObject();
        Assert.That(redacted["name"]!.GetValue<string>(), Is.EqualTo("Hidden card"));
        Assert.That(redacted["catalogId"], Is.Null);
        Assert.That(redacted["ownerId"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(redacted["controllerId"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(redacted["hiddenAtBattlefieldId"]!.GetValue<string>(), Is.EqualTo("field-a"));
        Assert.That(redacted["hiddenTurnNumber"]!.GetValue<int>(), Is.EqualTo(3));

        var controllerView = PlayerViewRedactor.Redact(state, viewerPlayerId: 1);
        Assert.That(controllerView["battlefields"]![0]!["hiddenCards"]![0]!["catalogId"]!.GetValue<string>(), Is.EqualTo("hidden-trick"));
    }

    [Test]
    public void reveal_lifecycle_exposes_identity_while_active_then_redacts_when_removed()
    {
        var state = BaseState();
        Player(state, 1)["hand"]!.AsArray().Add(Card("hand-secret", "Revealed Hand Card"));
        Player(state, 0)["deck"]!.AsArray().Add(Card("deck-secret", "Revealed Deck Card"));
        state["revealedCards"] = new JsonArray(
            new JsonObject
            {
                ["playerId"] = 1,
                ["zone"] = "hand",
                ["index"] = 0,
                ["active"] = true,
                ["card"] = Player(state, 1)["hand"]![0]!.DeepClone()
            },
            new JsonObject
            {
                ["playerId"] = 0,
                ["zone"] = "deck",
                ["index"] = 0,
                ["active"] = true,
                ["card"] = Player(state, 0)["deck"]![0]!.DeepClone()
            });

        var opponentView = PlayerViewRedactor.Redact(state, viewerPlayerId: 0);
        Assert.That(Player(opponentView, 1)["hand"]![0]!["catalogId"]!.GetValue<string>(), Is.EqualTo("hand-secret"));
        Assert.That(Player(opponentView, 0)["deck"]![0]!["catalogId"]!.GetValue<string>(), Is.EqualTo("deck-secret"));
        Assert.That(opponentView["revealedCards"]!.AsArray().Select(entry => entry!["card"]!["catalogId"]!.GetValue<string>()), Is.EquivalentTo(new[] { "hand-secret", "deck-secret" }));

        state["revealedCards"] = new JsonArray();
        var expiredView = PlayerViewRedactor.Redact(state, viewerPlayerId: 0);
        Assert.That(Player(expiredView, 1)["hand"]![0]!["name"]!.GetValue<string>(), Is.EqualTo("Hidden card"));
        Assert.That(Player(expiredView, 0)["deck"]![0]!["name"]!.GetValue<string>(), Is.EqualTo("Hidden card"));
    }

    [Test]
    public void facedown_identity_history_can_make_a_facedown_object_known_to_specific_players()
    {
        var state = BaseState();
        var facedownUnit = Card("borrowed-unit", "Borrowed Unit");
        facedownUnit["uid"] = "unit-known";
        facedownUnit["ownerId"] = 0;
        facedownUnit["controllerId"] = 1;
        facedownUnit["location"] = new JsonObject { ["type"] = "battlefield", ["battlefieldId"] = "field-a" };
        facedownUnit["isFaceDown"] = true;
        facedownUnit["information"] = new JsonObject
        {
            ["privacy"] = "private",
            ["controllerId"] = 1,
            ["faceDownIdentityKnownToPlayerIds"] = new JsonArray(0, 1),
            ["visibilityHistory"] = new JsonArray(
                new JsonObject { ["action"] = "facedown", ["playerId"] = 1, ["turnNumber"] = 2 },
                new JsonObject { ["action"] = "revealed", ["playerId"] = 0, ["turnNumber"] = 3 })
        };
        state["battlefields"]![0]!["units"]!.AsArray().Add(facedownUnit);

        var knownOwnerView = PlayerViewRedactor.Redact(state, viewerPlayerId: 0);
        Assert.That(knownOwnerView["battlefields"]![0]!["units"]![0]!["catalogId"]!.GetValue<string>(), Is.EqualTo("borrowed-unit"));

        var controllerView = PlayerViewRedactor.Redact(state, viewerPlayerId: 1);
        Assert.That(controllerView["battlefields"]![0]!["units"]![0]!["catalogId"]!.GetValue<string>(), Is.EqualTo("borrowed-unit"));

        var spectatorView = PlayerViewRedactor.RedactForSpectator(state);
        var spectatorUnit = spectatorView["battlefields"]![0]!["units"]![0]!.AsObject();
        Assert.That(spectatorUnit["catalogId"], Is.Null);
        Assert.That(spectatorUnit["ownerId"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(spectatorUnit["controllerId"]!.GetValue<int>(), Is.EqualTo(1));
    }

    private static string?[] CatalogIds(JsonArray cards)
    {
        return cards.Select(card => card?["catalogId"]?.GetValue<string>()).ToArray();
    }

    private static JsonObject Player(JsonObject state, int playerId)
    {
        return state["players"]!.AsArray()
            .Select(player => player!.AsObject())
            .First(player => player["id"]!.GetValue<int>() == playerId);
    }

    private static JsonObject BaseState()
    {
        return new JsonObject
        {
            ["id"] = "match-privacy",
            ["players"] = new JsonArray(
                PlayerObject(0),
                PlayerObject(1)),
            ["battlefields"] = new JsonArray(new JsonObject
            {
                ["id"] = "field-a",
                ["units"] = new JsonArray(),
                ["hiddenCards"] = new JsonArray()
            })
        };
    }

    private static JsonObject PlayerObject(int playerId)
    {
        return new JsonObject
        {
            ["id"] = playerId,
            ["hand"] = new JsonArray(),
            ["deck"] = new JsonArray(),
            ["runeDeck"] = new JsonArray(),
            ["runes"] = new JsonObject { ["ready"] = new JsonArray(), ["exhausted"] = new JsonArray() },
            ["trash"] = new JsonArray(),
            ["base"] = new JsonArray(),
            ["baseGear"] = new JsonArray(),
            ["banished"] = new JsonArray(),
            ["champion"] = null,
            ["legend"] = null
        };
    }

    private static JsonObject Card(string catalogId, string name)
    {
        return new JsonObject
        {
            ["id"] = $"{catalogId}-instance",
            ["catalogId"] = catalogId,
            ["name"] = name,
            ["kind"] = "unit",
            ["tags"] = new JsonArray(),
            ["domain"] = "Fury",
            ["domains"] = new JsonArray("Fury"),
            ["cost"] = 1,
            ["might"] = 1,
            ["text"] = string.Empty,
            ["image"] = "*",
            ["cardType"] = "Unit",
            ["supertype"] = null,
            ["effect"] = new JsonObject { ["type"] = "rally", ["amount"] = 0 }
        };
    }

    private static EngineMatchConfig Config()
    {
        return new EngineMatchConfig(
            "match-privacy",
            "duel-1v1",
            [
                new EngineSeatConfig(0, "user-0", "Player 0", null),
                new EngineSeatConfig(1, "user-1", "Player 1", null)
            ],
            ["field-a", "field-b"],
            0);
    }

    private static IReadOnlyList<EnginePlayerDeck> Decks()
    {
        return
        [
            new EnginePlayerDeck("deck-0", "legend-a", "champion-a", ["field-a"], ["rune-a", "rune-b", "rune-c"], ["unit-a", "unit-b", "unit-c", "unit-d", "unit-e", "unit-f"]),
            new EnginePlayerDeck("deck-1", "legend-b", "champion-b", ["field-b"], ["rune-d", "rune-e", "rune-f"], ["unit-g", "unit-h", "unit-i", "unit-j", "unit-k", "unit-l"])
        ];
    }
}
