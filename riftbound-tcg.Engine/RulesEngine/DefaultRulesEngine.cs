using System.Text.Json;
using System.Text.Json.Nodes;

namespace riftbound_tcg.Engine.RulesEngine;

public sealed class DefaultRulesEngine : IRulesEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] PhaseOrder = ["awaken", "beginning", "channel", "draw", "main", "ending"];

    public EngineMatchState CreateInitialState(EngineMatchConfig config, IReadOnlyList<EnginePlayerDeck> playerDecks, int seed)
    {
        var mode = ModeConfig.For(config.Mode);
        var turnOrder = OrderedPlayerIds(mode.PlayerCount, config.FirstPlayerId);
        var players = config.Seats
            .OrderBy(seat => seat.PlayerId)
            .Select(seat =>
            {
                var deck = playerDecks[seat.PlayerId];
                var mainDeck = Shuffle(deck.MainDeckIds.Select((id, index) => Card(id, $"main-{seat.PlayerId}-{index}")).ToList(), seed + seat.PlayerId + 31);
                var hand = mainDeck.Take(4).ToArray();
                var library = mainDeck.Skip(4).ToArray();
                return new JsonObject
                {
                    ["id"] = seat.PlayerId,
                    ["name"] = seat.DisplayName,
                    ["points"] = 0,
                    ["runes"] = new JsonObject { ["ready"] = new JsonArray(), ["exhausted"] = new JsonArray() },
                    ["runeDeck"] = ToArray(Shuffle(deck.RuneDeckIds.Select((id, index) => Card(id, $"rune-{seat.PlayerId}-{index}")).ToList(), seed + seat.PlayerId + 47)),
                    ["runePool"] = new JsonObject { ["energy"] = 0 },
                    ["deck"] = ToArray(library),
                    ["hand"] = ToArray(hand),
                    ["trash"] = new JsonArray(),
                    ["base"] = new JsonArray(),
                    ["champion"] = string.IsNullOrWhiteSpace(deck.ChampionId) ? null : Card(deck.ChampionId, $"champion-{seat.PlayerId}"),
                    ["legend"] = string.IsNullOrWhiteSpace(deck.LegendId) ? null : Card(deck.LegendId, $"legend-{seat.PlayerId}"),
                    ["championSummoned"] = false,
                    ["battlefieldId"] = deck.BattlefieldDeckIds.FirstOrDefault() ?? config.BattlefieldIds.ElementAtOrDefault(seat.PlayerId) ?? string.Empty
                };
            })
            .ToArray();

        var battlefieldIds = config.BattlefieldIds.Count > 0
            ? config.BattlefieldIds
            : playerDecks.SelectMany(deck => deck.BattlefieldDeckIds).Take(mode.BattlefieldCount).ToArray();

        var battlefields = battlefieldIds.Take(mode.BattlefieldCount).Select((id, index) => new JsonObject
        {
            ["id"] = $"{id}-{index}",
            ["catalogId"] = id,
            ["name"] = DisplayName(id),
            ["claim"] = 2,
            ["chosenBy"] = index % mode.PlayerCount,
            ["controllerId"] = null,
            ["units"] = new JsonArray()
        }).ToArray();

        var state = new JsonObject
        {
            ["id"] = config.MatchId,
            ["mode"] = config.Mode,
            ["victoryScore"] = mode.VictoryScore,
            ["players"] = ToArray(players),
            ["battlefields"] = ToArray(battlefields),
            ["stage"] = "mulligan",
            ["turnPhase"] = "awaken",
            ["turnNumber"] = 1,
            ["firstPlayerId"] = turnOrder[0],
            ["turnPlayerId"] = turnOrder[0],
            ["activePlayer"] = turnOrder[0],
            ["priorityPlayerId"] = null,
            ["focusPlayerId"] = null,
            ["winner"] = null,
            ["winningTeamId"] = null,
            ["turnOrder"] = ToArray(turnOrder),
            ["teamIds"] = ToArray(config.Seats.OrderBy(seat => seat.PlayerId).Select(seat => seat.TeamId ?? seat.PlayerId).ToArray()),
            ["hasPassedFocusByPlayer"] = new JsonObject(),
            ["scoredBattlefieldIdsThisTurn"] = new JsonObject(),
            ["firstTurnCompletedByPlayer"] = new JsonObject(),
            ["mulliganPlayerIndex"] = 0,
            ["activeShowdown"] = null,
            ["activeCombat"] = null,
            ["selectedCard"] = null,
            ["selectedUnit"] = null,
            ["nextUid"] = 1,
            ["nextLogId"] = 1,
            ["log"] = new JsonArray(new JsonObject { ["id"] = 0, ["text"] = $"{mode.Label} online match created. Players drew 4 and entered mulligan." }),
            ["passShield"] = true
        };

        return ToEngineState(config.MatchId, config.Mode, 0, state, config.Seats);
    }

    public IReadOnlyList<EngineLegalAction> GetLegalActions(EngineMatchState state, int playerId)
    {
        if (state.Players.All(player => player.PlayerId != playerId))
        {
            return [];
        }

        var stage = state.State["stage"]?.GetValue<string>() ?? state.Stage;
        var turnPlayerId = state.State["turnPlayerId"]?.GetValue<int>() ?? 0;
        var mulliganPlayerIndex = state.State["mulliganPlayerIndex"]?.GetValue<int>() ?? 0;
        var mulliganPlayerId = state.State["turnOrder"]?.AsArray().ElementAtOrDefault(mulliganPlayerIndex)?.GetValue<int>() ?? -1;

        if (stage == "game-over")
        {
            return [];
        }

        var actions = new List<EngineLegalAction> { new("concede", "concede", "Concede", playerId) };

        if (stage == "mulligan" && mulliganPlayerId == playerId)
        {
            actions.Add(new("confirm-mulligan", "confirm-mulligan", "Confirm mulligan", playerId));
        }

        if (stage == "playing" && turnPlayerId == playerId)
        {
            actions.Add(new("advance-phase", "advance-phase", "Advance phase", playerId));
            actions.Add(new("draw", "draw", "Draw 1", playerId));
            actions.Add(new("score-point", "score-point", "Score point", playerId));
        }

        return actions;
    }

    public EngineActionResult ApplyAction(EngineMatchState state, EngineGameAction action, int? expectedSequenceNumber)
    {
        if (state.Players.All(player => player.PlayerId != action.PlayerId))
        {
            return Reject(state, $"Player '{action.PlayerId}' is not seated in match '{state.MatchId}'.");
        }

        if (expectedSequenceNumber is not null && expectedSequenceNumber.Value != state.SequenceNumber)
        {
            return Reject(state, $"Expected sequence {expectedSequenceNumber.Value}, but match is at {state.SequenceNumber}.");
        }

        var legal = GetLegalActions(state, action.PlayerId);
        if (legal.All(candidate => !candidate.Type.Equals(action.ActionType, StringComparison.OrdinalIgnoreCase)))
        {
            return Reject(state, $"Action '{action.ActionType}' is not legal for player '{action.PlayerId}'.");
        }

        var nextState = Clone(state.State);
        switch (action.ActionType)
        {
            case "confirm-mulligan":
                nextState = ConfirmMulligan(nextState, action.PlayerId, ReadIntArray(action.Payload, "handIndexes").Take(2).ToArray());
                break;
            case "advance-phase":
                nextState = AdvancePhase(nextState);
                break;
            case "draw":
                nextState = UpdatePlayer(nextState, action.PlayerId, player => Draw(player, 1));
                nextState = AddLog(nextState, $"{PlayerName(nextState, action.PlayerId)} drew 1.");
                break;
            case "score-point":
                nextState = UpdatePlayer(nextState, action.PlayerId, player =>
                {
                    player["points"] = Math.Max(0, (player["points"]?.GetValue<int>() ?? 0) + 1);
                    return player;
                });
                nextState = CheckWinners(AddLog(nextState, $"{PlayerName(nextState, action.PlayerId)} scored 1 point."));
                break;
            case "concede":
                var winner = state.Players.First(player => player.PlayerId != action.PlayerId).PlayerId;
                nextState["stage"] = "game-over";
                nextState["winner"] = winner;
                nextState = AddLog(nextState, $"{PlayerName(nextState, action.PlayerId)} conceded.");
                break;
            default:
                return Reject(state, $"Action '{action.ActionType}' is not supported.");
        }

        var next = ToEngineState(state.MatchId, state.Mode, state.SequenceNumber + 1, nextState, state.Players.Select(player => new EngineSeatConfig(player.PlayerId, player.UserId, PlayerName(nextState, player.PlayerId), null)).ToArray());
        return new EngineActionResult(true, "accepted", $"Accepted {action.ActionType}.", next, GetLegalActions(next, action.PlayerId));
    }

    private static JsonObject ConfirmMulligan(JsonObject state, int playerId, IReadOnlyList<int> handIndexes)
    {
        var order = state["turnOrder"]!.Deserialize<int[]>(JsonOptions) ?? [];
        var index = state["mulliganPlayerIndex"]?.GetValue<int>() ?? 0;
        if (order.ElementAtOrDefault(index) != playerId)
        {
            return state;
        }

        state = UpdatePlayer(state, playerId, player =>
        {
            var hand = player["hand"]!.AsArray();
            var deck = player["deck"]!.AsArray();
            var selected = handIndexes.Distinct().Where(i => i >= 0 && i < hand.Count).OrderDescending().ToArray();
            var redrawCount = selected.Length;
            var returned = new List<JsonNode?>();
            foreach (var handIndex in selected)
            {
                returned.Add(hand[handIndex]?.DeepClone());
                hand.RemoveAt(handIndex);
            }

            for (var i = 0; i < redrawCount; i++)
            {
                if (deck.Count == 0) break;
                hand.Add(deck[0]?.DeepClone());
                deck.RemoveAt(0);
            }

            foreach (var card in returned)
            {
                deck.Add(card);
            }

            return player;
        });

        var nextIndex = index + 1;
        if (nextIndex >= order.Length)
        {
            state["stage"] = "playing";
            state["activePlayer"] = state["turnPlayerId"]?.GetValue<int>() ?? order[0];
        }
        else
        {
            state["mulliganPlayerIndex"] = nextIndex;
            state["activePlayer"] = order[nextIndex];
        }

        return AddLog(state, $"{PlayerName(state, playerId)} confirmed mulligan.");
    }

    private static JsonObject AdvancePhase(JsonObject state)
    {
        if (state["stage"]?.GetValue<string>() != "playing")
        {
            return state;
        }

        var currentPhase = state["turnPhase"]?.GetValue<string>() ?? "awaken";
        var playerId = state["turnPlayerId"]?.GetValue<int>() ?? 0;
        if (currentPhase == "channel")
        {
            state = UpdatePlayer(state, playerId, player =>
            {
                var runeDeck = player["runeDeck"]!.AsArray();
                var ready = player["runes"]!["ready"]!.AsArray();
                for (var i = 0; i < 2 && runeDeck.Count > 0; i++)
                {
                    ready.Add(runeDeck[0]?.DeepClone());
                    runeDeck.RemoveAt(0);
                }

                return player;
            });
        }
        else if (currentPhase == "draw")
        {
            state = UpdatePlayer(state, playerId, player => Draw(player, 1));
        }
        else if (currentPhase == "ending")
        {
            return EndTurn(state);
        }

        var nextIndex = Array.IndexOf(PhaseOrder, currentPhase) + 1;
        if (nextIndex <= 0 || nextIndex >= PhaseOrder.Length)
        {
            return EndTurn(state);
        }

        state["turnPhase"] = PhaseOrder[nextIndex];
        return AddLog(state, $"{PlayerName(state, playerId)} advanced to {PhaseOrder[nextIndex]}.");
    }

    private static JsonObject EndTurn(JsonObject state)
    {
        var order = state["turnOrder"]!.Deserialize<int[]>(JsonOptions) ?? [0, 1];
        var current = state["turnPlayerId"]?.GetValue<int>() ?? order[0];
        var currentIndex = Array.IndexOf(order, current);
        var next = order[(currentIndex + 1) % order.Length];
        state["turnPlayerId"] = next;
        state["activePlayer"] = next;
        state["turnPhase"] = "awaken";
        if (next == order[0])
        {
            state["turnNumber"] = (state["turnNumber"]?.GetValue<int>() ?? 1) + 1;
        }

        return AddLog(state, $"{PlayerName(state, next)} begins their turn.");
    }

    private static JsonObject CheckWinners(JsonObject state)
    {
        var victoryScore = state["victoryScore"]?.GetValue<int>() ?? 8;
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            if ((player["points"]?.GetValue<int>() ?? 0) < victoryScore) continue;
            state["stage"] = "game-over";
            state["winner"] = player["id"]!.GetValue<int>();
            return AddLog(state, $"{player["name"]?.GetValue<string>() ?? "Player"} wins the match.");
        }

        return state;
    }

    private static JsonObject Draw(JsonObject player, int amount)
    {
        var deck = player["deck"]!.AsArray();
        var hand = player["hand"]!.AsArray();
        for (var i = 0; i < amount && deck.Count > 0; i++)
        {
            hand.Add(deck[0]?.DeepClone());
            deck.RemoveAt(0);
        }

        return player;
    }

    private static JsonObject UpdatePlayer(JsonObject state, int playerId, Func<JsonObject, JsonObject> update)
    {
        var players = state["players"]!.AsArray();
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i]!.AsObject();
            if (player["id"]?.GetValue<int>() != playerId) continue;
            players[i] = update(Clone(player));
            break;
        }

        return state;
    }

    private static JsonObject AddLog(JsonObject state, string text)
    {
        var nextLogId = state["nextLogId"]?.GetValue<int>() ?? 1;
        state["nextLogId"] = nextLogId + 1;
        var log = state["log"]!.AsArray();
        log.Insert(0, new JsonObject { ["id"] = nextLogId, ["text"] = text });
        while (log.Count > 14)
        {
            log.RemoveAt(log.Count - 1);
        }

        return state;
    }

    private static EngineActionResult Reject(EngineMatchState state, string message)
    {
        return new EngineActionResult(false, "rejected", message, state, []);
    }

    private static EngineMatchState ToEngineState(string matchId, string mode, int sequenceNumber, JsonObject state, IReadOnlyList<EngineSeatConfig> seats)
    {
        var players = seats.Select(seat => new EnginePlayerState(seat.PlayerId, seat.UserId, ReadPlayerPoints(state, seat.PlayerId), false)).ToArray();
        return new EngineMatchState(matchId, mode, state["stage"]?.GetValue<string>() ?? "mulligan", sequenceNumber, state, players);
    }

    private static int ReadPlayerPoints(JsonObject state, int playerId)
    {
        return state["players"]!.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(player => player["id"]?.GetValue<int>() == playerId)?["points"]?.GetValue<int>() ?? 0;
    }

    private static string PlayerName(JsonObject state, int playerId)
    {
        return state["players"]!.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(player => player["id"]?.GetValue<int>() == playerId)?["name"]?.GetValue<string>() ?? $"Player {playerId + 1}";
    }

    private static JsonObject Card(string id, string instanceSuffix)
    {
        return new JsonObject
        {
            ["id"] = $"{id}-{instanceSuffix}",
            ["catalogId"] = id,
            ["name"] = DisplayName(id),
            ["kind"] = id.Contains("rune", StringComparison.OrdinalIgnoreCase) ? "rune" : "unit",
            ["tags"] = new JsonArray(),
            ["domain"] = "Fury",
            ["domains"] = new JsonArray("Fury"),
            ["cost"] = 1,
            ["might"] = 1,
            ["text"] = string.Empty,
            ["image"] = "*",
            ["cardType"] = "Unit",
            ["supertype"] = null,
            ["effect"] = new JsonObject { ["type"] = "rally", ["amount"] = 0 }
        };
    }

    private static string DisplayName(string id)
    {
        return string.Join(' ', id.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static int[] OrderedPlayerIds(int playerCount, int firstPlayerId)
    {
        var ids = Enumerable.Range(0, playerCount).ToArray();
        var first = firstPlayerId >= 0 && firstPlayerId < playerCount ? firstPlayerId : 0;
        return ids.Skip(first).Concat(ids.Take(first)).ToArray();
    }

    private static List<T> Shuffle<T>(List<T> items, int seed)
    {
        var state = (uint)(seed == 0 ? 1 : seed);
        for (var index = items.Count - 1; index > 0; index--)
        {
            state = unchecked((state * 1664525) + 1013904223);
            var swapIndex = (int)(state % (uint)(index + 1));
            (items[index], items[swapIndex]) = (items[swapIndex], items[index]);
        }

        return items;
    }

    private static JsonArray ToArray(IEnumerable<JsonObject> nodes)
    {
        var array = new JsonArray();
        foreach (var node in nodes)
        {
            array.Add(node);
        }

        return array;
    }

    private static JsonArray ToArray(IEnumerable<int> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonObject Clone(JsonObject value)
    {
        return value.DeepClone().AsObject();
    }

    private static int[] ReadIntArray(IReadOnlyDictionary<string, object?>? payload, string key)
    {
        if (payload is null || !payload.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        return value switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.Array => element.EnumerateArray().Where(item => item.TryGetInt32(out _)).Select(item => item.GetInt32()).ToArray(),
            IEnumerable<int> values => values.ToArray(),
            _ => []
        };
    }

    private sealed record ModeConfig(string Label, int PlayerCount, int VictoryScore, int BattlefieldCount)
    {
        public static ModeConfig For(string mode)
        {
            return mode switch
            {
                "teams-2v2" => new ModeConfig("2v2 Magma Chamber", 4, 11, 3),
                "ffa-3" => new ModeConfig("FFA3 Skirmish", 3, 8, 3),
                "ffa-4" => new ModeConfig("FFA4 War", 4, 8, 3),
                _ => new ModeConfig("1v1 Duel", 2, 8, 2)
            };
        }
    }
}
