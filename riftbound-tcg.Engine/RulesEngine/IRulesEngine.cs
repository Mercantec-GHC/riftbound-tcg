using System.Text.Json.Nodes;
using riftbound_tcg.Core.Cards;

namespace riftbound_tcg.Engine.RulesEngine;

public interface IRulesEngine
{
    EngineMatchState CreateInitialState(EngineMatchConfig config, IReadOnlyList<EnginePlayerDeck> playerDecks, int seed, IReadOnlyDictionary<string, CardDefinition>? catalog = null);

    IReadOnlyList<EngineLegalAction> GetLegalActions(EngineMatchState state, int playerId);

    EngineActionResult ApplyAction(EngineMatchState state, EngineGameAction action, int? expectedSequenceNumber);
}

public sealed record EngineMatchConfig(
    string MatchId,
    string Mode,
    IReadOnlyList<EngineSeatConfig> Seats,
    IReadOnlyList<string> BattlefieldIds,
    int FirstPlayerId);

public sealed record EngineSeatConfig(
    int PlayerId,
    string UserId,
    string DisplayName,
    int? TeamId);

public sealed record EnginePlayerDeck(
    string DeckId,
    string LegendId,
    string ChampionId,
    IReadOnlyList<string> BattlefieldDeckIds,
    IReadOnlyList<string> RuneDeckIds,
    IReadOnlyList<string> MainDeckIds);

public sealed record EngineMatchState(
    string MatchId,
    string Mode,
    string Stage,
    int SequenceNumber,
    JsonObject State,
    IReadOnlyList<EnginePlayerState> Players);

public sealed record EnginePlayerState(
    int PlayerId,
    string UserId,
    int Points,
    bool Connected);

public sealed record EngineLegalAction(
    string Id,
    string Type,
    string Label,
    int PlayerId,
    JsonObject? PayloadSchema = null);

public sealed record EngineGameAction(
    int PlayerId,
    string ActionType,
    IReadOnlyDictionary<string, object?>? Payload);

public sealed record EngineActionResult(
    bool Accepted,
    string Status,
    string ResultMessage,
    EngineMatchState State,
    IReadOnlyList<EngineLegalAction> LegalActions);
