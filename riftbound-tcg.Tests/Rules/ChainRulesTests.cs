using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.Effects;
using riftbound_tcg.Engine.RulesEngine;
using riftbound_tcg.Tests.Helpers;

namespace riftbound_tcg.Tests.Rules;

[TestFixture]
public class ChainRulesTests
{
    // --- ChainRules.Open ---

    [Test]
    public void Open_ReturnsChainWindowWithNoPasses()
    {
        var window = ChainRules.Open();

        Assert.That(window.PassedByPlayer, Is.Empty);
    }

    // --- ChainRules.Pass ---

    [Test]
    public void Pass_SinglePlayer_WhenOnlyPlayerPasses_ReturnsNull()
    {
        var window = ChainRules.Open();
        var turnOrder = new[] { 0 };

        var result = ChainRules.Pass(window, playerId: 0, turnOrder);

        Assert.That(result, Is.Null, "All players passed — should signal resolve.");
    }

    [Test]
    public void Pass_TwoPlayers_FirstPassDoesNotResolve()
    {
        var window = ChainRules.Open();
        var turnOrder = new[] { 0, 1 };

        var result = ChainRules.Pass(window, playerId: 0, turnOrder);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PassedByPlayer[0], Is.True);
        Assert.That(result.PassedByPlayer.ContainsKey(1), Is.False);
    }

    [Test]
    public void Pass_TwoPlayers_BothPass_ReturnsNull()
    {
        var turnOrder = new[] { 0, 1 };
        var window = ChainRules.Open();

        var afterFirst = ChainRules.Pass(window, playerId: 0, turnOrder);
        var afterSecond = ChainRules.Pass(afterFirst!, playerId: 1, turnOrder);

        Assert.That(afterSecond, Is.Null, "Both players passed — should signal resolve.");
    }

    [Test]
    public void Pass_FourPlayers_ResolvesOnlyWhenAllFourPass()
    {
        var turnOrder = new[] { 0, 1, 2, 3 };
        var window = ChainRules.Open();

        var w1 = ChainRules.Pass(window, 0, turnOrder);
        Assert.That(w1, Is.Not.Null);
        var w2 = ChainRules.Pass(w1!, 1, turnOrder);
        Assert.That(w2, Is.Not.Null);
        var w3 = ChainRules.Pass(w2!, 2, turnOrder);
        Assert.That(w3, Is.Not.Null);
        var w4 = ChainRules.Pass(w3!, 3, turnOrder);
        Assert.That(w4, Is.Null);
    }

    // --- ChainRules.ValidateChainPlay ---

    [Test]
    public void ValidateChainPlay_ReactionSpell_WithOpenChain_IsValid()
    {
        var card = CardBuilder.Spell()
            .Text("[Reaction] Counter a spell.")
            .Build();

        var result = ChainRules.ValidateChainPlay(card, playerId: 1, turnPlayerId: 0, chainWindow: ChainRules.Open());

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Subtype, Is.EqualTo(SpellSubtype.Reaction));
    }

    [Test]
    public void ValidateChainPlay_ReactionSpell_WithNoChain_IsRejected()
    {
        var card = CardBuilder.Spell()
            .Text("[Reaction] Counter a spell.")
            .Build();

        var result = ChainRules.ValidateChainPlay(card, playerId: 0, turnPlayerId: 0, chainWindow: null);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error, Does.Contain("[Reaction]"));
    }

    [Test]
    public void ValidateChainPlay_ActionSpell_TurnPlayer_WithOpenChain_IsRejected()
    {
        var card = CardBuilder.Spell()
            .Text("[Action] Deal 3 to a unit.")
            .Build();

        var result = ChainRules.ValidateChainPlay(card, playerId: 0, turnPlayerId: 0, chainWindow: ChainRules.Open());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error, Does.Contain("[Reaction]"));
    }

    [Test]
    public void ValidateChainPlay_ActionSpell_NonTurnPlayer_WithOpenChain_IsRejected()
    {
        var card = CardBuilder.Spell()
            .Text("[Action] Deal 3 to a unit.")
            .Build();

        var result = ChainRules.ValidateChainPlay(card, playerId: 1, turnPlayerId: 0, chainWindow: ChainRules.Open());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error, Does.Contain("[Reaction]"));
    }

    [Test]
    public void ValidateChainPlay_ActionSpell_NoChain_IsValid()
    {
        var card = CardBuilder.Spell()
            .Text("[Action] Deal 3 to a unit.")
            .Build();

        // No chain open — action spells play normally on the turn player's turn.
        var result = ChainRules.ValidateChainPlay(card, playerId: 0, turnPlayerId: 0, chainWindow: null);

        Assert.That(result.IsValid, Is.True);
    }
}
