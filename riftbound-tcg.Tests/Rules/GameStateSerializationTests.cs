using System.Text.Json;
using riftbound_tcg.DomainModels.Models;

namespace riftbound_tcg.Tests.Rules;

public class GameStateSerializationTests
{
    [Test]
    public void GameState_Roundtrips_WithoutDataLoss()
    {
        var gameState = new GameState(
            Players:
            [
                new PlayerState(
                    PlayerId: new PlayerId("player-1"),
                    Hand:
                    [
                        new CardInstance(
                            InstanceId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                            DefinitionId: "unit-guard",
                            CurrentStats: new CardStats(Power: 3, Health: 4))
                    ],
                    Deck: [],
                    Discard: [],
                    Champion: new CardInstance(
                        InstanceId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        DefinitionId: "champion-light",
                        CurrentStats: new CardStats(Power: 5, Health: 6)),
                    Battlefield:
                    [
                        new BattlefieldZone(
                            Lane: BattlefieldLane.Center,
                            Units:
                            [
                                new CardInstance(
                                    InstanceId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                                    DefinitionId: "unit-scout",
                                    CurrentStats: new CardStats(Power: 2, Health: 2))
                            ])
                    ],
                    Mana: 4),
                new PlayerState(
                    PlayerId: new PlayerId("player-2"),
                    Hand: [],
                    Deck: [],
                    Discard: [],
                    Champion: null,
                    Battlefield: [],
                    Mana: 2)
            ],
            Turn: new TurnState(
                ActivePlayerId: new PlayerId("player-1"),
                CurrentPhase: TurnPhase.Main,
                PriorityHolderId: new PlayerId("player-2")),
            Score: new ScoreState(
                PointsByPlayer:
                [
                    new PlayerScore(new PlayerId("player-1"), Points: 3),
                    new PlayerScore(new PlayerId("player-2"), Points: 1)
                ],
                WinThreshold: 8),
            PendingEffects:
            [
                new PendingEffect(
                    EffectId: "resolve-combat",
                    Description: "Resolve pending combat damage",
                    SourcePlayerId: new PlayerId("player-1"))
            ]);

        var serialized = JsonSerializer.Serialize(gameState);
        var deserialized = JsonSerializer.Deserialize<GameState>(serialized);

        Assert.That(deserialized, Is.Not.Null);

        var reserialized = JsonSerializer.Serialize(deserialized);

        Assert.That(reserialized, Is.EqualTo(serialized));
    }
}
