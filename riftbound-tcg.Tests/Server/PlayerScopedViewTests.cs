using System.Text.Json.Nodes;
using RiftboundTcg.Server.Api.Models;
using RiftboundTcg.Server.Api.Realtime;
using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Tests.Server;

public sealed class PlayerScopedViewTests
{
    [Test]
    public void api_snapshot_state_can_hold_distinct_player_specific_views()
    {
        var state = StateWithHands();
        var playerZeroView = PlayerViewRedactor.Redact(state, 0);
        var playerOneView = PlayerViewRedactor.Redact(state, 1);
        var snapshotForPlayerZero = Snapshot(playerZeroView);
        var snapshotForPlayerOne = Snapshot(playerOneView);

        var zeroState = (JsonObject)snapshotForPlayerZero.State;
        var oneState = (JsonObject)snapshotForPlayerOne.State;

        Assert.That(HandName(zeroState, 0, 0), Is.EqualTo("Player Zero Card"));
        Assert.That(HandName(zeroState, 1, 0), Is.EqualTo("Hidden card"));
        Assert.That(HandName(oneState, 0, 0), Is.EqualTo("Hidden card"));
        Assert.That(HandName(oneState, 1, 0), Is.EqualTo("Player One Card"));
    }

    [Test]
    public void action_response_state_can_hold_acting_players_redacted_view()
    {
        var eventDto = new MatchEventDto("event-1", "match-1", 1, 0, "advance-phase", new JsonObject(), new JsonObject(), DateTimeOffset.UtcNow);
        var response = new SubmitActionResponseDto(
            true,
            eventDto,
            PlayerViewRedactor.Redact(StateWithHands(), 0),
            1,
            []);

        var state = (JsonObject)response.State;
        Assert.That(HandName(state, 0, 0), Is.EqualTo("Player Zero Card"));
        Assert.That(HandName(state, 1, 0), Is.EqualTo("Hidden card"));
    }

    [Test]
    public void signalr_match_user_group_names_partition_connections_by_match_and_user()
    {
        Assert.That(MatchHub.MatchGroupName("match-1"), Is.EqualTo("match:match-1"));
        Assert.That(MatchHub.MatchUserGroupName("match-1", "user-a"), Is.EqualTo("match:match-1:user:user-a"));
        Assert.That(MatchHub.MatchUserGroupName("match-1", "user-a"), Is.Not.EqualTo(MatchHub.MatchUserGroupName("match-1", "user-b")));
        Assert.That(MatchHub.MatchUserGroupName("match-1", "user-a"), Is.Not.EqualTo(MatchHub.MatchUserGroupName("match-2", "user-a")));
    }

    private static MatchSnapshotDto Snapshot(JsonObject state)
    {
        return new MatchSnapshotDto(
            "match-1",
            "duel-1v1",
            "playing",
            [
                new MatchPlayerDto(0, "user-0", "Player 0", null, "deck-0", null),
                new MatchPlayerDto(1, "user-1", "Player 1", null, "deck-1", null)
            ],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            state,
            0);
    }

    private static string HandName(JsonObject state, int playerId, int index)
    {
        return state["players"]!.AsArray()
            .Select(player => player!.AsObject())
            .First(player => player["id"]!.GetValue<int>() == playerId)["hand"]![index]!["name"]!.GetValue<string>();
    }

    private static JsonObject StateWithHands()
    {
        return new JsonObject
        {
            ["players"] = new JsonArray(
                Player(0, Card("p0-card", "Player Zero Card")),
                Player(1, Card("p1-card", "Player One Card")))
        };
    }

    private static JsonObject Player(int playerId, JsonObject handCard)
    {
        return new JsonObject
        {
            ["id"] = playerId,
            ["hand"] = new JsonArray(handCard),
            ["deck"] = new JsonArray(Card($"p{playerId}-deck", $"Player {playerId} Deck Card")),
            ["runeDeck"] = new JsonArray(Card($"p{playerId}-rune", $"Player {playerId} Rune Card")),
            ["runes"] = new JsonObject { ["ready"] = new JsonArray(), ["exhausted"] = new JsonArray() },
            ["trash"] = new JsonArray(),
            ["base"] = new JsonArray(),
            ["baseGear"] = new JsonArray(),
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
}
