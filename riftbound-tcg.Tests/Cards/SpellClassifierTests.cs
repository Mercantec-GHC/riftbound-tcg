using riftbound_tcg.Core.Cards;
using riftbound_tcg.Engine.RulesEngine;
using riftbound_tcg.Tests.Helpers;

namespace riftbound_tcg.Tests.Cards;

[TestFixture]
public class SpellClassifierTests
{
    // --- GetSpellSubtype ---

    [Test]
    public void GetSpellSubtype_ReactionKeywordInText_ReturnsReaction()
    {
        var card = CardBuilder.Spell()
            .Text("[Reaction] (Play any time, even before spells and abilities resolve.) Counter a spell.")
            .Build();

        Assert.That(SpellClassifier.GetSpellSubtype(card), Is.EqualTo(SpellSubtype.Reaction));
    }

    [Test]
    public void GetSpellSubtype_ActionKeywordInText_ReturnsAction()
    {
        var card = CardBuilder.Spell()
            .Text("[Action] (Play on your turn or in showdowns.) Deal 3 to a unit.")
            .Build();

        Assert.That(SpellClassifier.GetSpellSubtype(card), Is.EqualTo(SpellSubtype.Action));
    }

    [Test]
    public void GetSpellSubtype_NoKeyword_ReturnsAction()
    {
        var card = CardBuilder.Spell()
            .Text("Move a unit you control to a battlefield you control.")
            .Build();

        Assert.That(SpellClassifier.GetSpellSubtype(card), Is.EqualTo(SpellSubtype.Action));
    }

    [Test]
    public void GetSpellSubtype_EmptyText_ReturnsAction()
    {
        var card = CardBuilder.Spell().Text("").Build();

        Assert.That(SpellClassifier.GetSpellSubtype(card), Is.EqualTo(SpellSubtype.Action));
    }

    [Test]
    public void GetSpellSubtype_NonSpellCard_AlwaysReturnsAction()
    {
        var unit = CardBuilder.Unit()
            .Text("[Reaction] this text should be ignored for non-spells")
            .Build();

        Assert.That(SpellClassifier.GetSpellSubtype(unit), Is.EqualTo(SpellSubtype.Action));
    }

    [Test]
    public void GetSpellSubtype_ReactionCaseSensitive_OnlyMatchesBracketedForm()
    {
        // Text contains "reaction" but NOT "[Reaction]" — should still be Action
        var card = CardBuilder.Spell()
            .Text("This card has a reaction-like effect when played.")
            .Build();

        Assert.That(SpellClassifier.GetSpellSubtype(card), Is.EqualTo(SpellSubtype.Action));
    }

    // --- HasOnPlayEffect ---

    [Test]
    public void HasOnPlayEffect_WhenYouPlayMe_ReturnsTrue()
    {
        var card = CardBuilder.Champion()
            .Text("When you play me, choose an opponent. They reveal their hand.")
            .Build();

        Assert.That(SpellClassifier.HasOnPlayEffect(card), Is.True);
    }

    [Test]
    public void HasOnPlayEffect_AsYouPlayMe_ReturnsTrue()
    {
        var card = CardBuilder.Champion()
            .Text("As you play me, choose Bird, Cat, Dog, or Poro. I gain that tag.")
            .Build();

        Assert.That(SpellClassifier.HasOnPlayEffect(card), Is.True);
    }

    [Test]
    public void HasOnPlayEffect_WhenYouPlayMeCaseInsensitive_ReturnsTrue()
    {
        var card = CardBuilder.Unit()
            .Text("WHEN YOU PLAY ME, draw 2.")
            .Build();

        Assert.That(SpellClassifier.HasOnPlayEffect(card), Is.True);
    }

    [Test]
    public void HasOnPlayEffect_NoOnPlayTrigger_ReturnsFalse()
    {
        var card = CardBuilder.Unit()
            .Text("When I hold, draw 1.")
            .Build();

        Assert.That(SpellClassifier.HasOnPlayEffect(card), Is.False);
    }

    [Test]
    public void HasOnPlayEffect_SpellCard_AlwaysReturnsFalse()
    {
        var card = CardBuilder.Spell()
            .Text("When you play me, draw 1.")
            .Build();

        Assert.That(SpellClassifier.HasOnPlayEffect(card), Is.False);
    }

    [TestCase(CardKind.Rune)]
    [TestCase(CardKind.Battlefield)]
    [TestCase(CardKind.Token)]
    public void HasOnPlayEffect_ExcludedKinds_ReturnsFalse(CardKind kind)
    {
        var card = new CardBuilder().Kind(kind)
            .Text("When you play me, draw 1.")
            .Build();

        Assert.That(SpellClassifier.HasOnPlayEffect(card), Is.False);
    }

    // --- CanPlayDuringChainWindow ---

    [Test]
    public void CanPlayDuringChainWindow_ReactionSpell_AnyPlayerCanPlay()
    {
        var card = CardBuilder.Spell()
            .Text("[Reaction] Counter a spell.")
            .Build();

        Assert.That(SpellClassifier.CanPlayDuringChainWindow(card, playerId: 1, turnPlayerId: 0), Is.True);
    }

    [Test]
    public void CanPlayDuringChainWindow_ActionSpell_OnlyTurnPlayerCanPlay()
    {
        var card = CardBuilder.Spell()
            .Text("[Action] Deal 3 to a unit.")
            .Build();

        Assert.That(SpellClassifier.CanPlayDuringChainWindow(card, playerId: 0, turnPlayerId: 0), Is.True);
        Assert.That(SpellClassifier.CanPlayDuringChainWindow(card, playerId: 1, turnPlayerId: 0), Is.False);
    }
}
