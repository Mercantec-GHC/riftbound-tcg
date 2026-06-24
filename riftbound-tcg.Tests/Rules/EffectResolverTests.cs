using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.Effects;
using riftbound_tcg.Core.GameState;
using riftbound_tcg.Engine.EffectResolver;
using riftbound_tcg.Tests.Helpers;

namespace riftbound_tcg.Tests.Rules;

[TestFixture]
public class EffectResolverTests
{
    private static StackItem Item(int playerId, CardEffectType type, int amount,
        string? targetUnitId = null, string? targetLaneId = null) =>
        new($"stack-{type}", "card-id", "Test Card", playerId,
            new CardEffectDefinition(type, amount), targetUnitId, targetLaneId);

    // --- Empty stack ---

    [Test]
    public void ResolveTop_EmptyStack_ReturnsNull()
    {
        var result = EffectResolver.ResolveTop([], [StateBuilder.Player(0)], []);

        Assert.That(result, Is.Null);
    }

    // --- Draw ---

    [Test]
    public void ResolveTop_Draw_MovesCardsFromDeckToHand()
    {
        var player = StateBuilder.Player(id: 0, deckSize: 5, handSize: 1);

        var result = EffectResolver.ResolveTop([Item(0, CardEffectType.Draw, 2)], [player], [])!;

        var updated = result.UpdatedPlayers.Single(p => p.Id == 0);
        Assert.That(updated.HandCardIds, Has.Count.EqualTo(3));
        Assert.That(updated.DeckCardIds, Has.Count.EqualTo(3));
    }

    [Test]
    public void ResolveTop_Draw_OtherPlayersUnaffected()
    {
        var p0 = StateBuilder.Player(0, deckSize: 5);
        var p1 = StateBuilder.Player(1, deckSize: 5);

        var result = EffectResolver.ResolveTop([Item(0, CardEffectType.Draw, 1)], [p0, p1], [])!;

        Assert.That(result.UpdatedPlayers.Single(p => p.Id == 1).DeckCardIds, Has.Count.EqualTo(5));
    }

    [Test]
    public void ResolveTop_Draw_DrawingMoreThanDeckDrawsAll()
    {
        var player = StateBuilder.Player(0, deckSize: 2);

        var result = EffectResolver.ResolveTop([Item(0, CardEffectType.Draw, 10)], [player], [])!;

        var updated = result.UpdatedPlayers.Single();
        Assert.That(updated.HandCardIds, Has.Count.EqualTo(2));
        Assert.That(updated.DeckCardIds, Is.Empty);
    }

    // --- Buff ---

    [Test]
    public void ResolveTop_Buff_IncreasesAttachedMightOnTargetUnit()
    {
        var unit = StateBuilder.Unit("u-1", owner: 0);
        var player = StateBuilder.Player(0) with { Base = [unit] };

        var result = EffectResolver.ResolveTop([Item(0, CardEffectType.Buff, 2, targetUnitId: "u-1")], [player], [])!;

        Assert.That(result.UpdatedPlayers.Single().Base.Single().AttachedMight, Is.EqualTo(2));
    }

    [Test]
    public void ResolveTop_Buff_NoTarget_NoChange()
    {
        var player = StateBuilder.Player(0);

        var result = EffectResolver.ResolveTop([Item(0, CardEffectType.Buff, 2)], [player], [])!;

        Assert.That(result.UpdatedPlayers.Single().Base, Is.Empty);
    }

    // --- Rally ---

    [Test]
    public void ResolveTop_Rally_ReadiesExhaustedUnit()
    {
        var unit = StateBuilder.Unit("u-2", owner: 0) with { Exhausted = true };
        var player = StateBuilder.Player(0) with { Base = [unit] };

        var result = EffectResolver.ResolveTop([Item(0, CardEffectType.Rally, 1, targetUnitId: "u-2")], [player], [])!;

        Assert.That(result.UpdatedPlayers.Single().Base.Single().Exhausted, Is.False);
    }

    // --- Damage (via lane) ---

    [Test]
    public void ResolveTop_Damage_DealsDamageToEnemyUnitInLane()
    {
        var enemy = StateBuilder.Unit("u-enemy", owner: 1);
        var field = StateBuilder.Field("lane-1", enemy);
        var p0 = StateBuilder.Player(0);
        var p1 = StateBuilder.Player(1);

        var result = EffectResolver.ResolveTop([Item(0, CardEffectType.Damage, 3, targetLaneId: "lane-1")], [p0, p1], [field])!;

        Assert.That(result.UpdatedBattlefields.Single().Units.Single().Damage, Is.EqualTo(3));
    }

    [Test]
    public void ResolveTop_Damage_DoesNotDamageFriendlyUnits()
    {
        var friendly = StateBuilder.Unit("u-friend", owner: 0);
        var field = StateBuilder.Field("lane-1", friendly);

        var result = EffectResolver.ResolveTop([Item(0, CardEffectType.Damage, 3, targetLaneId: "lane-1")], [StateBuilder.Player(0)], [field])!;

        Assert.That(result.UpdatedBattlefields.Single().Units.Single().Damage, Is.EqualTo(0));
    }

    [Test]
    public void ResolveTop_Damage_EmptyLane_NoChange()
    {
        var field = StateBuilder.Field("lane-1");

        var result = EffectResolver.ResolveTop([Item(0, CardEffectType.Damage, 3, targetLaneId: "lane-1")], [StateBuilder.Player(0)], [field])!;

        Assert.That(result.UpdatedBattlefields.Single().Units, Is.Empty);
    }

    // --- Stack management ---

    [Test]
    public void ResolveTop_ReturnsRemainingStackWithoutResolvedItem()
    {
        var item1 = Item(0, CardEffectType.Draw, 1);
        var item2 = Item(0, CardEffectType.Rally, 1, targetUnitId: "u-1");

        var result = EffectResolver.ResolveTop([item1, item2], [StateBuilder.Player(0, deckSize: 5)], [])!;

        Assert.That(result.ResolvedItem, Is.EqualTo(item1 with { Status = ChainItemStatus.Finalized }));
        Assert.That(result.RemainingStack, Has.Count.EqualTo(1));
        Assert.That(result.RemainingStack[0], Is.EqualTo(item2));
    }
}
