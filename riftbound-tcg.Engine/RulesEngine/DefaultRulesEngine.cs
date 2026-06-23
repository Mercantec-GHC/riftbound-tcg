using System.Text.Json;
using System.Text.Json.Nodes;
using riftbound_tcg.Core.Cards;

namespace riftbound_tcg.Engine.RulesEngine;

public sealed class DefaultRulesEngine : IRulesEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] PhaseOrder = ["awaken", "beginning", "channel", "draw", "main", "ending"];

    public EngineMatchState CreateInitialState(EngineMatchConfig config, IReadOnlyList<EnginePlayerDeck> playerDecks, int seed, IReadOnlyDictionary<string, CardDefinition>? catalog = null)
    {
        var mode = ModeConfig.For(config.Mode);
        var turnOrder = OrderedPlayerIds(mode.PlayerCount, config.FirstPlayerId);
        var players = config.Seats
            .OrderBy(seat => seat.PlayerId)
            .Select(seat =>
            {
                var deck = playerDecks[seat.PlayerId];
                var mainDeck = Shuffle(deck.MainDeckIds.Select((id, index) => Card(id, $"main-{seat.PlayerId}-{index}", catalog)).ToList(), seed + seat.PlayerId + 31);
                var hand = mainDeck.Take(4).ToArray();
                var library = mainDeck.Skip(4).ToArray();
                return new JsonObject
                {
                    ["id"] = seat.PlayerId,
                    ["name"] = seat.DisplayName,
                    ["points"] = 0,
                    ["runes"] = new JsonObject { ["ready"] = new JsonArray(), ["exhausted"] = new JsonArray() },
                    ["runeDeck"] = ToArray(Shuffle(deck.RuneDeckIds.Select((id, index) => Card(id, $"rune-{seat.PlayerId}-{index}", catalog)).ToList(), seed + seat.PlayerId + 47)),
                    ["runePool"] = new JsonObject { ["energy"] = 0 },
                    ["deck"] = ToArray(library),
                    ["hand"] = ToArray(hand),
                    ["trash"] = new JsonArray(),
                    ["base"] = new JsonArray(),
                    ["champion"] = string.IsNullOrWhiteSpace(deck.ChampionId) ? null : Card(deck.ChampionId, $"champion-{seat.PlayerId}", catalog),
                    ["legend"] = string.IsNullOrWhiteSpace(deck.LegendId) ? null : Card(deck.LegendId, $"legend-{seat.PlayerId}", catalog),
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
            ["mulliganConfirmedPlayerIds"] = new JsonArray(),
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
        var turnPhase = state.State["turnPhase"]?.GetValue<string>() ?? "awaken";
        var mulliganConfirmedPlayerIds = state.State["mulliganConfirmedPlayerIds"]?.Deserialize<int[]>(JsonOptions) ?? [];

        if (stage == "game-over")
        {
            return [];
        }

        var actions = new List<EngineLegalAction> { new("concede", "concede", "Concede", playerId) };

        if (stage == "mulligan" && !mulliganConfirmedPlayerIds.Contains(playerId))
        {
            actions.Add(new("confirm-mulligan", "confirm-mulligan", "Confirm mulligan", playerId));
        }

        if (stage == "playing" && turnPlayerId == playerId)
        {
            actions.Add(new("advance-phase", "advance-phase", "Advance phase", playerId));
            actions.Add(new("end-turn", "end-turn", "End turn", playerId));
            actions.Add(new("score-point", "score-point", "Score point", playerId));

            if (turnPhase == "main")
            {
                actions.Add(new("play-unit", "play-unit", "Play unit", playerId));
                actions.Add(new("move-unit", "move-unit", "Move unit", playerId));
            }
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
            case "end-turn":
                nextState = EndCurrentTurn(nextState, action.PlayerId);
                break;
            case "score-point":
                nextState = UpdatePlayer(nextState, action.PlayerId, player =>
                {
                    player["points"] = Math.Max(0, (player["points"]?.GetValue<int>() ?? 0) + 1);
                    return player;
                });
                nextState = CheckWinners(AddLog(nextState, $"{PlayerName(nextState, action.PlayerId)} scored 1 point."));
                break;
            case "play-unit":
                var playUnitResult = PlayUnit(nextState, action.PlayerId, ReadInt(action.Payload, "handIndex"), ReadString(action.Payload, "battlefieldId"));
                if (playUnitResult is null)
                {
                    return Reject(state, "Invalid play-unit action: unit can only be played to your base or a battlefield you control.");
                }

                nextState = playUnitResult;
                break;
            case "move-unit":
                var moveUnitResult = MoveUnit(nextState, action.PlayerId, ReadString(action.Payload, "unitId"), ReadString(action.Payload, "battlefieldId"));
                if (moveUnitResult is null)
                {
                    return Reject(state, "Invalid move-unit action: only your own, unexhausted base units can move to a battlefield you control.");
                }

                nextState = moveUnitResult;
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
        var confirmed = state["mulliganConfirmedPlayerIds"]!.Deserialize<int[]>(JsonOptions) ?? [];
        if (confirmed.Contains(playerId))
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

        var nextConfirmed = confirmed.Append(playerId).ToArray();
        state["mulliganConfirmedPlayerIds"] = ToArray(nextConfirmed);
        if (order.All(nextConfirmed.Contains))
        {
            state["stage"] = "playing";
            state["activePlayer"] = state["turnPlayerId"]?.GetValue<int>() ?? order.ElementAtOrDefault(0);
        }

        state = AddLog(state, $"{PlayerName(state, playerId)} confirmed mulligan.");
        return order.All(nextConfirmed.Contains) ? AutoAdvanceToDraw(state) : state;
    }

    private static JsonObject AutoAdvanceToDraw(JsonObject state)
    {
        while (state["stage"]?.GetValue<string>() == "playing")
        {
            var phase = state["turnPhase"]?.GetValue<string>() ?? "awaken";
            if (phase != "awaken" && phase != "beginning" && phase != "channel")
            {
                break;
            }

            state = AdvancePhase(state);
        }

        return state;
    }

    private static JsonObject EndCurrentTurn(JsonObject state, int playerId)
    {
        while (state["stage"]?.GetValue<string>() == "playing" && (state["turnPlayerId"]?.GetValue<int>() ?? -1) == playerId)
        {
            state = AdvancePhase(state);
        }

        return state;
    }

    private static JsonObject AdvancePhase(JsonObject state)
    {
        if (state["stage"]?.GetValue<string>() != "playing")
        {
            return state;
        }

        var currentPhase = state["turnPhase"]?.GetValue<string>() ?? "awaken";
        var playerId = state["turnPlayerId"]?.GetValue<int>() ?? 0;
        if (currentPhase == "awaken")
        {
            state = UpdatePlayer(state, playerId, player =>
            {
                var ready = player["runes"]!["ready"]!.AsArray();
                var exhausted = player["runes"]!["exhausted"]!.AsArray();
                while (exhausted.Count > 0)
                {
                    ready.Add(exhausted[0]?.DeepClone());
                    exhausted.RemoveAt(0);
                }

                foreach (var unit in player["base"]!.AsArray())
                {
                    unit!["exhausted"] = false;
                    unit["damage"] = 0;
                }

                return player;
            });

            foreach (var battlefield in state["battlefields"]!.AsArray())
            {
                foreach (var unit in battlefield!["units"]!.AsArray())
                {
                    if (unit!["ownerId"]?.GetValue<int>() != playerId) continue;
                    unit["exhausted"] = false;
                    unit["damage"] = 0;
                }
            }
        }
        else if (currentPhase == "channel")
        {
            var firstTurnCompleted = state["firstTurnCompletedByPlayer"]?[playerId.ToString()]?.GetValue<bool>() ?? false;
            var order = state["turnOrder"]!.Deserialize<int[]>(JsonOptions) ?? [];
            var firstPlayerId = state["firstPlayerId"]?.GetValue<int>() ?? order.ElementAtOrDefault(0);
            var lastPlayer = order.Length > 0 ? order[^1] : playerId;
            var mode = state["mode"]?.GetValue<string>() ?? "";
            var isSecondPlayer = mode == "duel-1v1" ? playerId != firstPlayerId : playerId == lastPlayer;
            var extra = !firstTurnCompleted && isSecondPlayer ? 1 : 0;
            var amount = 2 + extra;

            state = UpdatePlayer(state, playerId, player =>
            {
                var runeDeck = player["runeDeck"]!.AsArray();
                var ready = player["runes"]!["ready"]!.AsArray();
                for (var i = 0; i < amount && runeDeck.Count > 0; i++)
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
        state["firstTurnCompletedByPlayer"]![current.ToString()] = true;
        state["turnPlayerId"] = next;
        state["activePlayer"] = next;
        state["turnPhase"] = "awaken";
        if (next == order[0])
        {
            state["turnNumber"] = (state["turnNumber"]?.GetValue<int>() ?? 1) + 1;
        }

        state = AddLog(state, $"{PlayerName(state, next)} begins their turn.");
        return AutoAdvanceToDraw(state);
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

    private static JsonObject? PlayUnit(JsonObject state, int playerId, int? handIndex, string? battlefieldId)
    {
        if (handIndex is null)
        {
            return null;
        }

        var player = state["players"]!.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(candidate => candidate["id"]?.GetValue<int>() == playerId);
        if (player is null)
        {
            return null;
        }

        var hand = player["hand"]!.AsArray();
        if (handIndex.Value < 0 || handIndex.Value >= hand.Count)
        {
            return null;
        }

        var card = hand[handIndex.Value]!.AsObject();
        if (card["kind"]?.GetValue<string>() != "unit")
        {
            return null;
        }

        JsonObject? battlefield = null;
        if (!string.IsNullOrWhiteSpace(battlefieldId))
        {
            battlefield = state["battlefields"]!.AsArray()
                .Select(node => node!.AsObject())
                .FirstOrDefault(candidate => candidate["id"]?.GetValue<string>() == battlefieldId);
            if (battlefield is null || battlefield["controllerId"]?.GetValue<int>() != playerId)
            {
                return null;
            }
        }

        var cost = card["cost"]?.GetValue<int>() ?? 0;
        var energy = player["runePool"]!["energy"]?.GetValue<int>() ?? 0;
        var readyRunes = player["runes"]!["ready"]!.AsArray();
        var energyNeeded = Math.Max(0, cost - energy);
        if (readyRunes.Count < energyNeeded)
        {
            return null;
        }

        var unit = Clone(card);
        unit["uid"] = $"unit-{state["nextUid"]?.GetValue<int>() ?? 1}";
        unit["ownerId"] = playerId;
        unit["exhausted"] = true;
        unit["damage"] = 0;
        unit["attachedMight"] = 0;
        unit["attacker"] = false;
        unit["defender"] = false;

        state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;

        state = UpdatePlayer(state, playerId, p =>
        {
            p["hand"]!.AsArray().RemoveAt(handIndex.Value);

            var ready = p["runes"]!["ready"]!.AsArray();
            var exhausted = p["runes"]!["exhausted"]!.AsArray();
            for (var i = 0; i < energyNeeded && ready.Count > 0; i++)
            {
                exhausted.Add(ready[0]?.DeepClone());
                ready.RemoveAt(0);
            }

            p["runePool"]!["energy"] = energy + energyNeeded - cost;
            return p;
        });

        if (battlefield is null)
        {
            UpdatePlayer(state, playerId, p =>
            {
                p["base"]!.AsArray().Add(unit);
                return p;
            });
        }
        else
        {
            battlefield["units"]!.AsArray().Add(unit);
        }

        var destinationLabel = battlefield is null ? "their base" : battlefield["name"]?.GetValue<string>() ?? "a battlefield";
        return AddLog(state, $"{PlayerName(state, playerId)} played {card["name"]?.GetValue<string>() ?? "a unit"} to {destinationLabel}.");
    }

    private static JsonObject? MoveUnit(JsonObject state, int playerId, string? unitId, string? battlefieldId)
    {
        if (string.IsNullOrWhiteSpace(unitId) || string.IsNullOrWhiteSpace(battlefieldId))
        {
            return null;
        }

        var player = state["players"]!.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(candidate => candidate["id"]?.GetValue<int>() == playerId);
        if (player is null)
        {
            return null;
        }

        var base_ = player["base"]!.AsArray();
        var unitNode = base_.FirstOrDefault(node => node!["uid"]?.GetValue<string>() == unitId);
        if (unitNode is null)
        {
            return null;
        }

        var unit = unitNode.AsObject();
        if (unit["ownerId"]?.GetValue<int>() != playerId || unit["exhausted"]?.GetValue<bool>() != false)
        {
            return null;
        }

        var battlefield = state["battlefields"]!.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(candidate => candidate["id"]?.GetValue<string>() == battlefieldId);
        if (battlefield is null || battlefield["controllerId"]?.GetValue<int>() != playerId)
        {
            return null;
        }

        base_.Remove(unitNode);
        var moved = Clone(unit);
        moved["exhausted"] = true;
        battlefield["units"]!.AsArray().Add(moved);

        return AddLog(state, $"{PlayerName(state, playerId)} moved {unit["name"]?.GetValue<string>() ?? "a unit"} to {battlefield["name"]?.GetValue<string>() ?? "a battlefield"}.");
    }

    private static int? ReadInt(IReadOnlyDictionary<string, object?>? payload, string key)
    {
        if (payload is null || !payload.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            JsonElement element when element.TryGetInt32(out var intValue) => intValue,
            int intValue => intValue,
            _ => null
        };
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?>? payload, string key)
    {
        if (payload is null || !payload.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            string stringValue => stringValue,
            _ => null
        };
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

    private static JsonObject Card(string id, string instanceSuffix, IReadOnlyDictionary<string, CardDefinition>? catalog = null)
    {
        if (catalog is not null && catalog.TryGetValue(id, out var definition))
        {
            return new JsonObject
            {
                ["id"] = $"{id}-{instanceSuffix}",
                ["catalogId"] = id,
                ["name"] = definition.Name,
                ["kind"] = definition.Kind.ToString().ToLowerInvariant(),
                ["tags"] = ToArray(definition.Tags),
                ["domain"] = definition.Domain.ToString(),
                ["domains"] = ToArray(definition.Domains.Select(domain => domain.ToString())),
                ["cost"] = definition.Cost,
                ["might"] = definition.Might,
                ["text"] = definition.Text,
                ["image"] = definition.Image,
                ["cardType"] = definition.CardType,
                ["supertype"] = definition.Supertype,
                ["effect"] = new JsonObject { ["type"] = definition.Effect.Type.ToString().ToLowerInvariant(), ["amount"] = definition.Effect.Amount }
            };
        }

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

    private static JsonArray ToArray(IEnumerable<string> values)
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
