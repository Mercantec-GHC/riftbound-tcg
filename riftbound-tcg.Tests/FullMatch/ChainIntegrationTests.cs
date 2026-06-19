using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.Effects;
using riftbound_tcg.Engine.EffectResolver;
using riftbound_tcg.Engine.RulesEngine;
using riftbound_tcg.Tests.Helpers;

namespace riftbound_tcg.Tests.FullMatch;

/// <summary>
/// End-to-end tests for the full chain lifecycle:
/// spell played → chain window opens → players respond or pass → effect resolves.
/// </summary>
[TestFixture]
public class ChainIntegrationTests
{
    private static StackItem DrawItem(int playerId, int amount) =>
        new($"s-{playerId}", "spell-id", "Test Spell", playerId,
            new CardEffectDefinition(CardEffectType.Draw, amount), null, null);

    // Scenario: 2-player game, player 0 plays a draw spell.
    // Player 1 plays a [Reaction] spell on top.
    // Both pass — reaction resolves first (LIFO), then the original spell.

    [Test]
    public void ChainLifecycle_ReactionResolvesBeforeOriginalSpell()
    {
        var players = new[] { StateBuilder.Player(0, deckSize: 10), StateBuilder.Player(1, deckSize: 10) };
        var turnOrder = new[] { 0, 1 };

        // Player 0 plays a draw-2 spell → goes on the stack.
        var originalItem = DrawItem(playerId: 0, amount: 2);
        var stack = new List<StackItem> { originalItem };
        var chainWindow = ChainRules.Open();

        // Validate player 1 can play a [Reaction] spell.
        var reactionCard = CardBuilder.Spell().Text("[Reaction] Draw 1.").Build();
        var validation = ChainRules.ValidateChainPlay(reactionCard, playerId: 1, turnPlayerId: 0, chainWindow);
        Assert.That(validation.IsValid, Is.True);

        // Player 1 adds a reaction draw-1 on top — new chain window opens.
        var reactionItem = DrawItem(playerId: 1, amount: 1);
        stack.Insert(0, reactionItem);
        chainWindow = ChainRules.Open();

        // Both players pass.
        var afterP0 = ChainRules.Pass(chainWindow, playerId: 0, turnOrder);
        Assert.That(afterP0, Is.Not.Null, "Not everyone has passed yet.");
        var afterP1 = ChainRules.Pass(afterP0!, playerId: 1, turnOrder);
        Assert.That(afterP1, Is.Null, "All passed — should resolve.");

        // Resolve top (reaction from player 1 — draws 1).
        var res1 = EffectResolver.ResolveTop(stack, players, [])!;
        Assert.That(res1.ResolvedItem, Is.EqualTo(reactionItem));
        Assert.That(res1.UpdatedPlayers.Single(p => p.Id == 1).HandCardIds, Has.Count.EqualTo(1));
        Assert.That(res1.RemainingStack, Has.Count.EqualTo(1));

        // Reopen chain window; both pass immediately.
        chainWindow = ChainRules.Open();
        var w0 = ChainRules.Pass(chainWindow, 0, turnOrder);
        var w1 = ChainRules.Pass(w0!, 1, turnOrder);
        Assert.That(w1, Is.Null);

        // Resolve original spell (draw 2 for player 0).
        var res2 = EffectResolver.ResolveTop(res1.RemainingStack, res1.UpdatedPlayers, [])!;
        Assert.That(res2.ResolvedItem, Is.EqualTo(originalItem));
        Assert.That(res2.UpdatedPlayers.Single(p => p.Id == 0).HandCardIds, Has.Count.EqualTo(2));
        Assert.That(res2.RemainingStack, Is.Empty);
    }

    [Test]
    public void ChainLifecycle_NoResponses_SpellResolvesAfterAllPass()
    {
        var players = new[] { StateBuilder.Player(0, deckSize: 5) };
        var turnOrder = new[] { 0 };
        var stack = new List<StackItem> { DrawItem(playerId: 0, amount: 3) };
        var chainWindow = ChainRules.Open();

        var afterPass = ChainRules.Pass(chainWindow, playerId: 0, turnOrder);
        Assert.That(afterPass, Is.Null, "Only player passed — resolve immediately.");

        var result = EffectResolver.ResolveTop(stack, players, [])!;
        Assert.That(result.UpdatedPlayers[0].HandCardIds, Has.Count.EqualTo(3));
        Assert.That(result.RemainingStack, Is.Empty);
    }

    [Test]
    public void ChainLifecycle_NonTurnPlayerCannotChainActionSpell()
    {
        var chainWindow = ChainRules.Open();
        var actionSpell = CardBuilder.Spell().Text("[Action] Deal 3 to a unit.").Build();

        var result = ChainRules.ValidateChainPlay(actionSpell, playerId: 1, turnPlayerId: 0, chainWindow);

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void ChainLifecycle_UnitOnPlayTrigger_ClassifiedCorrectly()
    {
        var champion = CardBuilder.Champion()
            .Text("When you play me, choose an opponent. They reveal their hand.")
            .Build();

        Assert.That(SpellClassifier.HasOnPlayEffect(champion), Is.True,
            "Champion with on-play trigger should push to the chain.");

        var vanilla = CardBuilder.Unit().Text("[Ambush]").Build();
        Assert.That(SpellClassifier.HasOnPlayEffect(vanilla), Is.False,
            "Unit without an on-play trigger should not push to the chain.");
    }
}
