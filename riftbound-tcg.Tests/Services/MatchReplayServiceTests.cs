using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RiftboundTcg.Server.Api.Data;
using RiftboundTcg.Server.Api.Services;
using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Tests.Services;

public sealed class MatchReplayServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task replay_from_creation_events_reconstructs_final_state()
    {
        await using var db = CreateDbContext();
        var engine = new DefaultRulesEngine();
        var initial = CreateInitialState(engine);
        var afterFirst = Apply(engine, initial, 0, "confirm-mulligan");
        var afterSecond = Apply(engine, afterFirst, 1, "confirm-mulligan");
        SeedMatch(db, initial);
        AddCreationEvent(db, initial);
        AddAcceptedEvent(db, "event-1", 1, 0, "confirm-mulligan", afterFirst);
        AddAcceptedEvent(db, "event-2", 2, 1, "confirm-mulligan", afterSecond);
        await db.SaveChangesAsync();

        var rebuilt = await new MatchReplayService(db, engine).RebuildAsync(initial.MatchId, useLatestSnapshot: false, CancellationToken.None);

        Assert.That(rebuilt, Is.Not.Null);
        Assert.That(rebuilt!.SequenceNumber, Is.EqualTo(afterSecond.SequenceNumber));
        Assert.That(rebuilt.State.ToJsonString(JsonOptions), Is.EqualTo(afterSecond.State.ToJsonString(JsonOptions)));
    }

    [Test]
    public async Task replay_from_latest_snapshot_applies_only_tail_events()
    {
        await using var db = CreateDbContext();
        var engine = new DefaultRulesEngine();
        var initial = CreateInitialState(engine);
        var afterFirst = Apply(engine, initial, 0, "confirm-mulligan");
        var afterSecond = Apply(engine, afterFirst, 1, "confirm-mulligan");
        SeedMatch(db, initial);
        AddCreationEvent(db, initial);
        AddAcceptedEvent(db, "event-1", 1, 0, "confirm-mulligan", afterFirst);
        AddAcceptedEvent(db, "event-2", 2, 1, "confirm-mulligan", afterSecond);
        db.MatchSnapshots.Add(new MatchSnapshotEntity
        {
            Id = "snapshot-1",
            MatchId = initial.MatchId,
            SequenceNumber = afterFirst.SequenceNumber,
            StateJson = afterFirst.State.ToJsonString(JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var rebuilt = await new MatchReplayService(db, engine).RebuildAsync(initial.MatchId, useLatestSnapshot: true, CancellationToken.None);

        Assert.That(rebuilt, Is.Not.Null);
        Assert.That(rebuilt!.SequenceNumber, Is.EqualTo(afterSecond.SequenceNumber));
        Assert.That(rebuilt.State.ToJsonString(JsonOptions), Is.EqualTo(afterSecond.State.ToJsonString(JsonOptions)));
    }

    [Test]
    public async Task rejected_events_are_excluded_from_replay()
    {
        await using var db = CreateDbContext();
        var engine = new DefaultRulesEngine();
        var initial = CreateInitialState(engine);
        var afterFirst = Apply(engine, initial, 0, "confirm-mulligan");
        var afterSecond = Apply(engine, afterFirst, 1, "confirm-mulligan");
        SeedMatch(db, initial);
        AddCreationEvent(db, initial);
        AddAcceptedEvent(db, "event-1", 1, 0, "confirm-mulligan", afterFirst);
        db.MatchEvents.Add(new MatchEventEntity
        {
            Id = "event-rejected",
            MatchId = initial.MatchId,
            SequenceNumber = 2,
            PlayerId = 0,
            ActionType = "action.rejected",
            ActionPayloadJson = Serialize(new Dictionary<string, object?>()),
            ResultPayloadJson = Serialize(new { status = "rejected" }),
            StateAfterJson = null,
            CreatedAt = DateTimeOffset.UtcNow
        });
        AddAcceptedEvent(db, "event-3", 3, 1, "confirm-mulligan", afterSecond);
        await db.SaveChangesAsync();

        var rebuilt = await new MatchReplayService(db, engine).RebuildAsync(initial.MatchId, useLatestSnapshot: false, CancellationToken.None);

        Assert.That(rebuilt, Is.Not.Null);
        Assert.That(rebuilt!.SequenceNumber, Is.EqualTo(afterSecond.SequenceNumber));
        Assert.That(rebuilt.State.ToJsonString(JsonOptions), Is.EqualTo(afterSecond.State.ToJsonString(JsonOptions)));
    }

    [Test]
    public void accepted_event_that_cannot_replay_reports_sequence_mismatch()
    {
        using var db = CreateDbContext();
        var engine = new DefaultRulesEngine();
        var initial = CreateInitialState(engine);
        SeedMatch(db, initial);
        AddCreationEvent(db, initial);
        db.MatchEvents.Add(new MatchEventEntity
        {
            Id = "event-invalid",
            MatchId = initial.MatchId,
            SequenceNumber = 1,
            PlayerId = 0,
            ActionType = "advance-phase",
            ActionPayloadJson = Serialize(new Dictionary<string, object?>()),
            ResultPayloadJson = "{}",
            StateAfterJson = null,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();

        var replay = new MatchReplayService(db, engine);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => replay.RebuildAsync(initial.MatchId, useLatestSnapshot: false, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("could not be replayed"));
    }

    private static GameDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseInMemoryDatabase($"match-replay-{Guid.NewGuid():N}")
            .Options;
        return new GameDbContext(options);
    }

    private static EngineMatchState CreateInitialState(DefaultRulesEngine engine)
    {
        return engine.CreateInitialState(
            new EngineMatchConfig(
                "match-replay-001",
                "duel-1v1",
                [
                    new EngineSeatConfig(0, "user-a", "Player A", 0),
                    new EngineSeatConfig(1, "user-b", "Player B", 1)
                ],
                ["lane-a", "lane-b"],
                0),
            [
                new EnginePlayerDeck("deck-a", "legend-a", "champion-a", ["lane-a"], ["rune-a", "rune-a", "rune-a"], ["unit-a", "unit-b", "unit-c", "unit-d", "unit-e"]),
                new EnginePlayerDeck("deck-b", "legend-b", "champion-b", ["lane-b"], ["rune-b", "rune-b", "rune-b"], ["unit-f", "unit-g", "unit-h", "unit-i", "unit-j"])
            ],
            123);
    }

    private static EngineMatchState Apply(DefaultRulesEngine engine, EngineMatchState state, int playerId, string actionType)
    {
        var result = engine.ApplyAction(state, new EngineGameAction(playerId, actionType, new Dictionary<string, object?>()), state.SequenceNumber);
        Assert.That(result.Accepted, Is.True);
        return result.State;
    }

    private static void SeedMatch(GameDbContext db, EngineMatchState initial)
    {
        db.Matches.Add(new MatchEntity
        {
            Id = initial.MatchId,
            Mode = initial.Mode,
            Status = initial.Stage,
            SequenceNumber = initial.SequenceNumber,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.MatchPlayers.AddRange(
            new MatchPlayerEntity { MatchId = initial.MatchId, PlayerId = 0, UserId = "user-a", DisplayName = "Player A", DeckId = "deck-a", TeamId = 0 },
            new MatchPlayerEntity { MatchId = initial.MatchId, PlayerId = 1, UserId = "user-b", DisplayName = "Player B", DeckId = "deck-b", TeamId = 1 });
    }

    private static void AddCreationEvent(GameDbContext db, EngineMatchState state)
    {
        db.MatchEvents.Add(new MatchEventEntity
        {
            Id = "event-created",
            MatchId = state.MatchId,
            SequenceNumber = 0,
            ActionType = "match.created",
            ActionPayloadJson = "{}",
            ResultPayloadJson = "{}",
            StateAfterJson = state.State.ToJsonString(JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private static void AddAcceptedEvent(GameDbContext db, string id, int sequenceNumber, int playerId, string actionType, EngineMatchState stateAfter)
    {
        db.MatchEvents.Add(new MatchEventEntity
        {
            Id = id,
            MatchId = stateAfter.MatchId,
            SequenceNumber = sequenceNumber,
            PlayerId = playerId,
            ActionType = actionType,
            ActionPayloadJson = Serialize(new Dictionary<string, object?>()),
            ResultPayloadJson = Serialize(new { status = "accepted" }),
            StateAfterJson = stateAfter.State.ToJsonString(JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
}
