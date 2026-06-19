using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.Effects;
using riftbound_tcg.Core.GameState;
using riftbound_tcg.Engine.EffectResolver;
using riftbound_tcg.Tests.Helpers;

namespace riftbound_tcg.Tests.Rules;

[TestFixture]
public class EffectResolverTests
{
    // --- Helpers ---

    private static PlayerState MakePlayer(int id, int deckSize = 5, int handSize = 0) =>
        new(
            Id: id,
            Name: $"Player {id}",
            Points: 0,
            ReadyRunes: [],
            ExhaustedRunes: [],
            RuneDeck: [],
            PoolEnergy: 0,
            Deck: Enumerable.Range(0, deckSize).Select(i => CardBuilder.Unit().Id($"deck-{id}-{i}").Build()).ToList(),
            Hand: Enumerable.Range(0, handSize).Select(i => CardBuilder.Unit().Id($"hand-{id}-{i}").Build()).ToList(),
            Trash: [],
            Base: [],
            Champion: null,
            Legend: null,
            ChampionSummoned: false,
            BattlefieldId: "field-0");

    private static UnitInstance MakeUnit(string uid, int owner, int might = 3, int damage = 0) =>
        new(
            Uid: uid,
            Card: CardBuilder.Unit().Might(might).Build(),
            Owner: owner,
            Location: new BaseLocation(),
            Exhausted: false,
            Damage: damage,
            AttachedMight: 0);

    private static BattlefieldState MakeField(string id, params UnitInstance[] units) =>
        new(id, id, 2, 0, null, null, false, false, units.ToList());

    private static StackItem MakeItem(int playerId, EffectType type, int amount,
        string? targetUnitId = null, string? targetLaneId = null) =>
        new($"stack-{type}", "card-id", "Test Card", playerId,
            new Effect(type, amount), targetUnitId, targetLaneId);

    // --- ResolveTop returns null for empty stack ---

    [Test]
    public void ResolveTop_EmptyStack_ReturnsNull()
    {
        var result = EffectResolver.ResolveTop([], [MakePlayer(0)], []);

        Assert.That(result, Is.Null);
    }

    // --- Draw ---

    [Test]
    public void ResolveTop_DrawEffect_MovesCardsFromDeckToHand()
    {
        var player = MakePlayer(id: 0, deckSize: 5, handSize: 1);
        var item = MakeItem(playerId: 0, EffectType.Draw, amount: 2);

        var result = EffectResolver.ResolveTop([item], [player], [])!;

        var updated = result.UpdatedPlayers.Single(p => p.Id == 0);
        Assert.That(updated.Hand, Has.Count.EqualTo(3));
        Assert.That(updated.Deck, Has.Count.EqualTo(3));
    }

    [Test]
    public void ResolveTop_DrawEffect_OtherPlayersUnaffected()
    {
        var player0 = MakePlayer(id: 0, deckSize: 5);
        var player1 = MakePlayer(id: 1, deckSize: 5);
        var item = MakeItem(playerId: 0, EffectType.Draw, amount: 1);

        var result = EffectResolver.ResolveTop([item], [player0, player1], [])!;

        Assert.That(result.UpdatedPlayers.Single(p => p.Id == 1).Deck, Has.Count.EqualTo(5));
    }

    [Test]
    public void ResolveTop_DrawEffect_DrawingMoreThanDeckDrawsAll()
    {
        var player = MakePlayer(id: 0, deckSize: 2);
        var item = MakeItem(playerId: 0, EffectType.Draw, amount: 10);

        var result = EffectResolver.ResolveTop([item], [player], [])!;

        var updated = result.UpdatedPlayers.Single(p => p.Id == 0);
        Assert.That(updated.Hand, Has.Count.EqualTo(2));
        Assert.That(updated.Deck, Is.Empty);
    }

    // --- Buff ---

    [Test]
    public void ResolveTop_BuffEffect_IncreasesAttachedMightOnTargetUnit()
    {
        var unit = MakeUnit("u-1", owner: 0);
        var player = MakePlayer(0) with { Base = [unit] };
        var item = MakeItem(playerId: 0, EffectType.Buff, amount: 2, targetUnitId: "u-1");

        var result = EffectResolver.ResolveTop([item], [player], [])!;

        var updatedUnit = result.UpdatedPlayers.Single(p => p.Id == 0).Base.Single();
        Assert.That(updatedUnit.AttachedMight, Is.EqualTo(2));
    }

    [Test]
    public void ResolveTop_BuffEffect_NoTarget_NoChange()
    {
        var player = MakePlayer(0);
        var item = MakeItem(playerId: 0, EffectType.Buff, amount: 2, targetUnitId: null);

        var result = EffectResolver.ResolveTop([item], [player], [])!;

        Assert.That(result.UpdatedPlayers.Single().Base, Is.Empty);
    }

    // --- Rally ---

    [Test]
    public void ResolveTop_RallyEffect_ReadiesExhaustedUnit()
    {
        var unit = MakeUnit("u-2", owner: 0) with { Exhausted = true };
        var player = MakePlayer(0) with { Base = [unit] };
        var item = MakeItem(playerId: 0, EffectType.Rally, amount: 1, targetUnitId: "u-2");

        var result = EffectResolver.ResolveTop([item], [player], [])!;

        var updatedUnit = result.UpdatedPlayers.Single().Base.Single();
        Assert.That(updatedUnit.Exhausted, Is.False);
    }

    // --- Damage (via lane target) ---

    [Test]
    public void ResolveTop_DamageEffect_DealsDamageToEnemyUnitInLane()
    {
        var enemyUnit = MakeUnit("u-enemy", owner: 1);
        var field = MakeField("lane-1", enemyUnit);
        var player0 = MakePlayer(0);
        var player1 = MakePlayer(1);
        var item = MakeItem(playerId: 0, EffectType.Damage, amount: 3, targetLaneId: "lane-1");

        var result = EffectResolver.ResolveTop([item], [player0, player1], [field])!;

        var damagedUnit = result.UpdatedBattlefields.Single(f => f.Id == "lane-1").Units.Single();
        Assert.That(damagedUnit.Damage, Is.EqualTo(3));
    }

    [Test]
    public void ResolveTop_DamageEffect_DoesNotDamageFriendlyUnits()
    {
        var friendlyUnit = MakeUnit("u-friend", owner: 0);
        var field = MakeField("lane-1", friendlyUnit);
        var player0 = MakePlayer(0);
        var item = MakeItem(playerId: 0, EffectType.Damage, amount: 3, targetLaneId: "lane-1");

        var result = EffectResolver.ResolveTop([item], [player0], [field])!;

        var unit = result.UpdatedBattlefields.Single().Units.Single();
        Assert.That(unit.Damage, Is.EqualTo(0));
    }

    [Test]
    public void ResolveTop_DamageEffect_EmptyLane_NoChange()
    {
        var field = MakeField("lane-1");
        var player0 = MakePlayer(0);
        var item = MakeItem(playerId: 0, EffectType.Damage, amount: 3, targetLaneId: "lane-1");

        var result = EffectResolver.ResolveTop([item], [player0], [field])!;

        Assert.That(result.UpdatedBattlefields.Single().Units, Is.Empty);
    }

    // --- Stack management ---

    [Test]
    public void ResolveTop_ReturnsRemainingStackWithoutResolvedItem()
    {
        var item1 = MakeItem(playerId: 0, EffectType.Draw, amount: 1);
        var item2 = MakeItem(playerId: 0, EffectType.Rally, amount: 1, targetUnitId: "u-1");
        var player = MakePlayer(0, deckSize: 5);

        var result = EffectResolver.ResolveTop([item1, item2], [player], [])!;

        Assert.That(result.ResolvedItem, Is.EqualTo(item1));
        Assert.That(result.RemainingStack, Has.Count.EqualTo(1));
        Assert.That(result.RemainingStack[0], Is.EqualTo(item2));
    }
}
