using riftbound_tcg.Core.Cards;

namespace riftbound_tcg.Tests.Helpers;

/// <summary>Fluent builder for CardDefinition test fixtures.</summary>
public sealed class CardBuilder
{
    private string _id = "test-card";
    private string _name = "Test Card";
    private CardKind _kind = CardKind.Unit;
    private string _text = "";
    private Effect _effect = new(EffectType.Rally, 0);
    private int _cost = 1;
    private int _might = 2;

    public CardBuilder Id(string id) { _id = id; return this; }
    public CardBuilder Name(string name) { _name = name; return this; }
    public CardBuilder Kind(CardKind kind) { _kind = kind; return this; }
    public CardBuilder Text(string text) { _text = text; return this; }
    public CardBuilder Effect(EffectType type, int amount) { _effect = new(type, amount); return this; }
    public CardBuilder Cost(int cost) { _cost = cost; return this; }
    public CardBuilder Might(int might) { _might = might; return this; }

    public CardDefinition Build() => new(
        Id: _id,
        Name: _name,
        Kind: _kind,
        Tags: [],
        Domain: "Fury",
        Domains: ["Fury"],
        Cost: _cost,
        Might: _might,
        Text: _text,
        Image: "",
        CardType: _kind.ToString(),
        Supertype: null,
        Effect: _effect);

    public static CardBuilder Spell() => new CardBuilder().Kind(CardKind.Spell);
    public static CardBuilder Unit() => new CardBuilder().Kind(CardKind.Unit);
    public static CardBuilder Champion() => new CardBuilder().Kind(CardKind.Champion);
    public static CardBuilder Legend() => new CardBuilder().Kind(CardKind.Legend);
}
