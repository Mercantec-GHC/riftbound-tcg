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
                ["units"] = new JsonArray()
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
            ["champion"] = null,
            ["legend"] = null
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
