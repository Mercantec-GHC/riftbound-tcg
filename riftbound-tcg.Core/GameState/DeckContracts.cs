using riftbound_tcg.Core.Cards;

namespace riftbound_tcg.Core.GameState;

public enum DeckVisibility
{
    Private,
    Public
}

public sealed record DeckList(
    string Id,
    string Name,
    string OwnerUserId,
    DeckVisibility Visibility,
    string LegendId,
    string ChampionId,
    IReadOnlyList<string> BattlefieldDeckIds,
    IReadOnlyList<string> RuneDeckIds,
    IReadOnlyList<string> MainDeckIds);

public sealed record SharedDeckList(
    string Id,
    string Name,
    string OwnerUserId,
    DeckVisibility Visibility,
    string Author,
    string LegendId,
    string ChampionId,
    IReadOnlyList<string> BattlefieldDeckIds,
    IReadOnlyList<string> RuneDeckIds,
    IReadOnlyList<string> MainDeckIds,
    IReadOnlyList<string> Tags,
    IReadOnlyList<Domain> Domains,
    string LegendName,
    string ChampionName,
    DeckCardCounts CardCounts,
    string? Description,
    long? UpdatedAtUnixTimeMilliseconds);

public sealed record DeckCardCounts(
    int Main,
    int Runes,
    int Battlefields);

public sealed record GameDeckAssignment(
    string UserId,
    DeckList? Deck);
