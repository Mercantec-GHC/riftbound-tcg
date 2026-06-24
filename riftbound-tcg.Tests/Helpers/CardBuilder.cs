using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.GameState;

namespace riftbound_tcg.Tests.Helpers;

/// <summary>Fluent builder for CardDefinition test fixtures.</summary>
public sealed class CardBuilder
{
    private string _id = "test-card";
    private string _name = "Test Card";
    private CardKind _kind = CardKind.Unit;
    private string _text = "";
    private CardEffectDefinition _effect = new(CardEffectType.Rally, 0);
    private int _cost = 1;
    private int _might = 2;
    private IReadOnlyList<string> _tags = [];
    private Domain _domain = riftbound_tcg.Core.Cards.Domain.Fury;
    private IReadOnlyList<Domain> _domains = [riftbound_tcg.Core.Cards.Domain.Fury];
    private string? _supertype;
    private string? _cardType;

    public CardBuilder Id(string id) { _id = id; return this; }
    public CardBuilder Name(string name) { _name = name; return this; }
    public CardBuilder Kind(CardKind kind) { _kind = kind; return this; }
    public CardBuilder Text(string text) { _text = text; return this; }
    public CardBuilder Effect(CardEffectType type, int amount) { _effect = new(type, amount); return this; }
    public CardBuilder Cost(int cost) { _cost = cost; return this; }
    public CardBuilder Might(int might) { _might = might; return this; }
    public CardBuilder Tags(params string[] tags) { _tags = tags; return this; }
    public CardBuilder Domain(Domain domain) { _domain = domain; _domains = [domain]; return this; }
    public CardBuilder Domains(params Domain[] domains) { _domains = domains; _domain = domains.FirstOrDefault(); return this; }
    public CardBuilder Supertype(string? supertype) { _supertype = supertype; return this; }
    public CardBuilder CardType(string cardType) { _cardType = cardType; return this; }

    public CardDefinition Build() => new(
        Id: _id,
        Name: _name,
        Kind: _kind,
        Tags: _tags,
        Domain: _domain,
        Domains: _domains,
        Cost: _cost,
        Might: _might,
        Text: _text,
        Image: "",
        CardType: _cardType ?? _kind.ToString(),
        Supertype: _supertype,
        Effect: _effect);

    public static CardBuilder Spell() => new CardBuilder().Kind(CardKind.Spell);
    public static CardBuilder Gear() => new CardBuilder().Kind(CardKind.Gear);
    public static CardBuilder Unit() => new CardBuilder().Kind(CardKind.Unit);
    public static CardBuilder Champion() => new CardBuilder().Kind(CardKind.Champion);
    public static CardBuilder Legend() => new CardBuilder().Kind(CardKind.Legend);
}

/// <summary>Builder for PlayerState using main branch's string-ID model.</summary>
public static class StateBuilder
{
    public static PlayerState Player(int id, int deckSize = 5, int handSize = 0) =>
        new(
            Id: id,
            Name: $"Player {id}",
            Points: 0,
            Runes: new RunePools([], [], 0),
            RuneDeckCardIds: [],
            DeckCardIds: Enumerable.Range(0, deckSize).Select(i => $"deck-{id}-{i}").ToList(),
            HandCardIds: Enumerable.Range(0, handSize).Select(i => $"hand-{id}-{i}").ToList(),
            TrashCardIds: [],
            Base: [],
            BaseGear: [],
            ChampionCardId: null,
            LegendCardId: null,
            ChampionSummoned: false,
            BattlefieldId: "field-0");

    public static UnitState Unit(string uid, int owner, int damage = 0) =>
        new(
            Uid: uid,
            CardId: $"card-{uid}",
            OwnerPlayerId: owner,
            Location: new CardLocation(CardLocationType.Base, null),
            Exhausted: false,
            Damage: damage,
            AttachedMight: 0,
            Attacker: false,
            Defender: false);

    public static BattlefieldState Field(string id, params UnitState[] units) =>
        new(id, id, 2, 0, null, null, false, false, units.ToList());
}
