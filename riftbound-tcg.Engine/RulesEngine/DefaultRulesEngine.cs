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
                    ["runePool"] = EmptyRunePool(),
                    ["deck"] = ToArray(library),
                    ["hand"] = ToArray(hand),
                    ["trash"] = new JsonArray(),
                    ["base"] = new JsonArray(),
                    ["baseGear"] = new JsonArray(),
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
            ["rngState"] = seed,
            ["log"] = new JsonArray(new JsonObject { ["id"] = 0, ["text"] = $"{mode.Label} online match created. Players drew 4 and entered mulligan." }),
            ["passShield"] = true,
            ["effectStack"] = new JsonArray(),
            ["chainWindow"] = null,
            ["pendingBurnOut"] = null
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
        var chainOpen = state.State["chainWindow"] is not null;

        if (stage == "game-over")
        {
            return [];
        }

        var actions = new List<EngineLegalAction> { new("concede", "concede", "Concede", playerId) };

        if (stage == "playing" && PendingBurnOut(state.State) is { } pendingBurnOut)
        {
            var burnOutPlayerId = pendingBurnOut["playerId"]?.GetValue<int>() ?? -1;
            if (burnOutPlayerId == playerId)
            {
                foreach (var opponentId in OpponentPlayerIds(state.State, playerId))
                {
                    actions.Add(new(
                        $"choose-burn-out-opponent-{opponentId}",
                        "choose-burn-out-opponent",
                        $"Burn Out: {PlayerName(state.State, opponentId)} gains 1 point",
                        playerId,
                        new JsonObject { ["opponentPlayerId"] = opponentId }));
                }
            }

            return actions;
        }

        if (stage == "playing" && chainOpen)
        {
            var chainPriorityPlayerId = ChainPriorityPlayerId(state.State);
            if (chainPriorityPlayerId is null || chainPriorityPlayerId == playerId)
            {
                actions.Add(new("pass-chain-window", "pass-chain-window", "Pass priority", playerId));
                actions.AddRange(PlayableCardsFromHand(state.State, playerId, card => IsReactionCard(card)));
            }

            return actions;
        }

        if (stage == "mulligan" && !mulliganConfirmedPlayerIds.Contains(playerId))
        {
            actions.Add(new("confirm-mulligan", "confirm-mulligan", "Confirm mulligan", playerId));
        }

        if (stage == "playing" && turnPlayerId == playerId && !IsShowdownOpen(state.State))
        {
            actions.Add(new("advance-phase", "advance-phase", "Advance phase", playerId));
            actions.Add(new("end-turn", "end-turn", "End turn", playerId));

            if (turnPhase == "main")
            {
                actions.Add(new("play-unit", "play-unit", "Play unit", playerId));
                actions.Add(new("move-unit", "move-unit", "Move unit", playerId));
                actions.AddRange(PlayableCardsFromHand(state.State, playerId, IsSpellOrGear));

                if (CanSummonChampion(state.State, playerId))
                {
                    actions.Add(new("summon-champion", "summon-champion", "Summon champion", playerId));
                }
            }
        }

        if (stage == "playing" && IsShowdownOpen(state.State) && !CombatDamageRequired(state.State))
        {
            var focusPlayerId = state.State["focusPlayerId"]?.GetValue<int?>()
                ?? state.State["activePlayer"]?.GetValue<int?>()
                ?? turnPlayerId;
            if (focusPlayerId == playerId)
            {
                actions.Add(new("pass-focus", "pass-focus", "Pass focus", playerId));
                actions.AddRange(PlayableCardsFromHand(state.State, playerId, card => IsSpellOrGear(card) && (IsActionCard(card) || IsReactionCard(card))));
            }
        }

        var activeCombat = state.State["activeCombat"] as JsonObject;
        if (stage == "playing" && activeCombat is not null && CombatDamageRequired(state.State))
        {
            var attackerPlayerId = activeCombat["attackerPlayerId"]?.GetValue<int>();
            var defenderPlayerId = activeCombat["defenderPlayerId"]?.GetValue<int>();
            var assignmentKey = playerId == attackerPlayerId ? "attackerAssignments" : playerId == defenderPlayerId ? "defenderAssignments" : null;
            if (assignmentKey is not null && activeCombat[assignmentKey] is null)
            {
                actions.Add(new("resolve-combat", "resolve-combat", "Resolve combat", playerId));
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
            case "choose-burn-out-opponent":
                var burnOutResult = ChooseBurnOutOpponent(nextState, action.PlayerId, ReadInt(action.Payload, "opponentPlayerId"));
                if (burnOutResult is null)
                {
                    return Reject(state, "Invalid choose-burn-out-opponent action: choose a valid opponent.");
                }

                nextState = burnOutResult;
                break;
            case "end-turn":
                nextState = EndCurrentTurn(nextState, action.PlayerId);
                break;
            case "play-unit":
                var playUnitResult = PlayUnit(nextState, action.PlayerId, ReadInt(action.Payload, "handIndex"), ReadString(action.Payload, "battlefieldId"));
                if (playUnitResult is null)
                {
                    return Reject(state, "Invalid play-unit action: unit can only be played to your base or a battlefield you control.");
                }

                nextState = playUnitResult;
                break;
            case "summon-champion":
                var summonChampionResult = SummonChampion(nextState, action.PlayerId);
                if (summonChampionResult is null)
                {
                    return Reject(state, "Invalid summon-champion action: champion is unavailable or you lack the runes to summon it.");
                }

                nextState = summonChampionResult;
                break;
            case "move-unit":
                var moveUnitResult = MoveUnit(nextState, action.PlayerId, ReadString(action.Payload, "unitId"), ReadString(action.Payload, "battlefieldId"));
                if (moveUnitResult is null)
                {
                    return Reject(state, "Invalid move-unit action: only your own, unexhausted base units can move to a battlefield you control.");
                }

                nextState = moveUnitResult;
                break;
            case "play-card":
                var playCardResult = PlayCard(nextState, action.PlayerId, ReadInt(action.Payload, "handIndex"), ReadString(action.Payload, "targetUnitId"), ReadString(action.Payload, "targetLaneId"));
                if (playCardResult is null)
                {
                    return Reject(state, "Invalid play-card action: card timing, ownership, cost, or targets are not legal.");
                }

                nextState = playCardResult;
                break;
            case "pass-chain-window":
                var passResult = PassChainWindow(nextState, action.PlayerId);
                if (passResult is null)
                {
                    return Reject(state, "Invalid pass-chain-window action: no reaction window is open.");
                }

                nextState = passResult;
                break;
            case "pass-focus":
                var focusResult = PassFocus(nextState, action.PlayerId);
                if (focusResult is null)
                {
                    return Reject(state, "Invalid pass-focus action: no showdown focus is available.");
                }

                nextState = focusResult;
                break;
            case "resolve-combat":
                var combatResult = ResolveCombat(nextState, action.PlayerId, action.Payload);
                if (combatResult is null)
                {
                    return Reject(state, "Invalid resolve-combat action: combat must involve exactly two players and assign legal lethal damage.");
                }

                nextState = combatResult;
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

        if (!string.Equals(action.ActionType, "resolve-combat", StringComparison.OrdinalIgnoreCase))
        {
            nextState = RunFeprUntilChoiceRequired(nextState);
        }
        var resultPayload = BuildResultPayload(nextState);
        nextState.Remove("__scoreOutcomes");
        var next = ToEngineState(state.MatchId, state.Mode, state.SequenceNumber + 1, nextState, state.Players.Select(player => new EngineSeatConfig(player.PlayerId, player.UserId, PlayerName(nextState, player.PlayerId), null)).ToArray());
        return new EngineActionResult(true, "accepted", $"Accepted {action.ActionType}.", next, GetLegalActions(next, action.PlayerId), resultPayload);
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
        else if (currentPhase == "beginning")
        {
            foreach (var battlefieldId in ControlledBattlefieldIds(state, playerId))
            {
                state = ScoreBattlefield(state, new ScoreRequest(playerId, battlefieldId, ScoreSource.Hold));
            }

            if (state["stage"]?.GetValue<string>() == "game-over")
            {
                return state;
            }
        }
        else if (currentPhase == "draw")
        {
            state = DrawCards(state, playerId, 1);
            state = ClearRunePools(state);
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
        state["scoredBattlefieldIdsThisTurn"] = new JsonObject();
        state["turnPlayerId"] = next;
        state["activePlayer"] = next;
        state["turnPhase"] = "awaken";
        state["scoredBattlefieldIdsThisTurn"] = new JsonObject();
        if (next == order[0])
        {
            state["turnNumber"] = (state["turnNumber"]?.GetValue<int>() ?? 1) + 1;
        }

        state = AddLog(state, $"{PlayerName(state, next)} begins their turn.");
        return AutoAdvanceToDraw(state);
    }

    private static JsonObject ClearRunePools(JsonObject state)
    {
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            player["runePool"] = EmptyRunePool();
        }

        return state;
    }

    private static JsonObject ConquerBattlefield(JsonObject state, int playerId, string battlefieldId)
    {
        var battlefield = FindBattlefield(state, battlefieldId);
        if (battlefield is null)
        {
            return AppendScoreOutcome(state, new ScoreOutcome(playerId, battlefieldId, ScoreSource.Conquer, 0, "battlefield-not-found"));
        }

        var previousControllerId = battlefield["controllerId"]?.GetValue<int?>();
        if (previousControllerId == playerId)
        {
            return AppendScoreOutcome(state, new ScoreOutcome(playerId, battlefieldId, ScoreSource.Conquer, 0, "already-controlled"));
        }

        battlefield["controllerId"] = playerId;
        battlefield["contestedByPlayerId"] = null;
        battlefield["stagedShowdown"] = false;
        battlefield["stagedCombat"] = false;
        return ScoreBattlefield(state, new ScoreRequest(playerId, battlefieldId, ScoreSource.Conquer));
    }

    private static JsonObject ScoreBattlefield(JsonObject state, ScoreRequest request)
    {
        var battlefield = FindBattlefield(state, request.BattlefieldId);
        if (battlefield is null)
        {
            return AppendScoreOutcome(state, new ScoreOutcome(request.PlayerId, request.BattlefieldId, request.Source, 0, "battlefield-not-found"));
        }

        var alreadyScored = ScoredBattlefieldIds(state, request.PlayerId);
        if (alreadyScored.Contains(request.BattlefieldId, StringComparer.OrdinalIgnoreCase))
        {
            return AppendScoreOutcome(state, new ScoreOutcome(request.PlayerId, request.BattlefieldId, request.Source, 0, "already-scored-this-turn"));
        }

        var scoredNow = alreadyScored.Append(request.BattlefieldId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var scored = state["scoredBattlefieldIdsThisTurn"]!.AsObject();
        scored[request.PlayerId.ToString()] = ToArray(scoredNow);

        if (request.Source == ScoreSource.Conquer)
        {
            var victoryScore = ScoreRules.VictoryScore(state);
            var currentPoints = ReadPlayerPoints(state, request.PlayerId);
            var allBattlefieldIds = state["battlefields"]!.AsArray()
                .Select(node => node!["id"]?.GetValue<string>() ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();
            var hasScoredEveryBattlefield = allBattlefieldIds.All(id => scoredNow.Contains(id, StringComparer.OrdinalIgnoreCase));
            if (currentPoints >= victoryScore - 1 && !hasScoredEveryBattlefield)
            {
                state = DrawCards(state, request.PlayerId, 1);
                state = AppendScoreOutcome(state, new ScoreOutcome(request.PlayerId, request.BattlefieldId, request.Source, 0, "drew-instead"));
                return AddLog(state, $"{PlayerName(state, request.PlayerId)} conquered {battlefield["name"]?.GetValue<string>() ?? request.BattlefieldId} and drew instead of gaining the final point.");
            }
        }

        var awardedPoints = ScoreRules.AwardedPoints(state, request);
        if (awardedPoints <= 0)
        {
            return AppendScoreOutcome(state, new ScoreOutcome(request.PlayerId, request.BattlefieldId, request.Source, 0, "no-points-awarded"));
        }

        state = UpdatePlayer(state, request.PlayerId, player =>
        {
            player["points"] = Math.Max(0, (player["points"]?.GetValue<int>() ?? 0) + awardedPoints);
            return player;
        });

        var outcome = new ScoreOutcome(request.PlayerId, request.BattlefieldId, request.Source, awardedPoints, null);
        state = AppendScoreOutcome(state, outcome);
        var verb = request.Source == ScoreSource.Hold ? "held" : "conquered";
        state = AddLog(state, $"{PlayerName(state, request.PlayerId)} {verb} {battlefield["name"]?.GetValue<string>() ?? request.BattlefieldId} for {awardedPoints} point{(awardedPoints == 1 ? string.Empty : "s")}.");
        return CheckWinners(state);
    }

    private static string[] ControlledBattlefieldIds(JsonObject state, int playerId)
    {
        return state["battlefields"]!.AsArray()
            .Select(node => node!.AsObject())
            .Where(battlefield => battlefield["controllerId"]?.GetValue<int?>() == playerId)
            .Select(battlefield => battlefield["id"]?.GetValue<string>() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
    }

    private static JsonObject? FindBattlefield(JsonObject state, string battlefieldId)
    {
        return state["battlefields"]!.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(battlefield => string.Equals(battlefield["id"]?.GetValue<string>(), battlefieldId, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] ScoredBattlefieldIds(JsonObject state, int playerId)
    {
        return state["scoredBattlefieldIdsThisTurn"]?[playerId.ToString()]?.Deserialize<string[]>(JsonOptions) ?? [];
    }

    private static JsonObject AppendScoreOutcome(JsonObject state, ScoreOutcome outcome)
    {
        if (state["__scoreOutcomes"] is not JsonArray outcomes)
        {
            outcomes = new JsonArray();
            state["__scoreOutcomes"] = outcomes;
        }

        outcomes.Add(new JsonObject
        {
            ["playerId"] = outcome.PlayerId,
            ["battlefieldId"] = outcome.BattlefieldId,
            ["source"] = ScoreSourceValue(outcome.Source),
            ["pointsAwarded"] = outcome.PointsAwarded,
            ["skippedReason"] = outcome.SkippedReason
        });
        return state;
    }

    private static JsonObject? BuildResultPayload(JsonObject state)
    {
        if (state["__scoreOutcomes"] is not JsonArray outcomes || outcomes.Count == 0)
        {
            return null;
        }

        return new JsonObject { ["scoreOutcomes"] = outcomes.DeepClone() };
    }

    private static JsonObject CheckWinners(JsonObject state)
    {
        var victoryScore = ScoreRules.VictoryScore(state);
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            if ((player["points"]?.GetValue<int>() ?? 0) < victoryScore) continue;
            state["stage"] = "game-over";
            state["winner"] = player["id"]!.GetValue<int>();
            return AddLog(state, $"{player["name"]?.GetValue<string>() ?? "Player"} wins the match.");
        }

        return state;
    }

    private static JsonObject DrawAvailable(JsonObject player, int amount)
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

    private static JsonObject DrawCards(JsonObject state, int playerId, int amount)
    {
        if (amount <= 0 || PendingBurnOut(state) is not null)
        {
            return state;
        }

        var player = FindPlayer(state, playerId);
        if (player is null)
        {
            return state;
        }

        var deck = player["deck"]!.AsArray();
        if (deck.Count >= amount)
        {
            DrawAvailable(player, amount);
            return state;
        }

        var drawn = deck.Count;
        DrawAvailable(player, drawn);
        RecycleTrashIntoMainDeck(state, player);
        var remaining = amount - drawn;
        state["pendingBurnOut"] = new JsonObject
        {
            ["playerId"] = playerId,
            ["remainingDraws"] = remaining
        };
        return AddLog(state, $"{PlayerName(state, playerId)} burned out and must choose an opponent to gain 1 point.");
    }

    private static JsonObject? ChooseBurnOutOpponent(JsonObject state, int playerId, int? opponentPlayerId)
    {
        var pending = PendingBurnOut(state);
        if (pending is null || opponentPlayerId is null || pending["playerId"]?.GetValue<int>() != playerId)
        {
            return null;
        }

        if (!OpponentPlayerIds(state, playerId).Contains(opponentPlayerId.Value))
        {
            return null;
        }

        var remainingDraws = pending["remainingDraws"]?.GetValue<int>() ?? 0;
        state["pendingBurnOut"] = null;
        state = UpdatePlayer(state, opponentPlayerId.Value, player =>
        {
            player["points"] = Math.Max(0, (player["points"]?.GetValue<int>() ?? 0) + 1);
            return player;
        });
        state = AddLog(state, $"{PlayerName(state, opponentPlayerId.Value)} gained 1 point from {PlayerName(state, playerId)} burning out.");
        state = CheckWinners(state);
        if (state["stage"]?.GetValue<string>() == "game-over")
        {
            return state;
        }

        return DrawCards(state, playerId, remainingDraws);
    }

    private static void RecycleTrashIntoMainDeck(JsonObject state, JsonObject player)
    {
        var trash = player["trash"]!.AsArray();
        if (trash.Count == 0)
        {
            return;
        }

        var recycled = trash.Select(card => card?.DeepClone()).Where(card => card is not null).ToList();
        trash.Clear();
        ShuffleInPlace(state, recycled);
        var deck = player["deck"]!.AsArray();
        foreach (var card in recycled)
        {
            deck.Add(card);
        }
    }

    private static void ShuffleInPlace(JsonObject state, IList<JsonNode?> cards)
    {
        for (var index = cards.Count - 1; index > 0; index--)
        {
            var swapIndex = NextRandomIndex(state, index + 1);
            (cards[index], cards[swapIndex]) = (cards[swapIndex], cards[index]);
        }
    }

    private static int NextRandomIndex(JsonObject state, int maxExclusive)
    {
        var current = state["rngState"]?.GetValue<int>() ?? 1;
        var next = unchecked(current * 1664525 + 1013904223) & int.MaxValue;
        state["rngState"] = next;
        return maxExclusive <= 1 ? 0 : next % maxExclusive;
    }

    private static JsonObject? PendingBurnOut(JsonObject state)
    {
        return state["pendingBurnOut"] as JsonObject;
    }

    private static int[] OpponentPlayerIds(JsonObject state, int playerId)
    {
        var players = state["players"]!.AsArray()
            .Select(player => player!["id"]!.GetValue<int>())
            .Where(id => id != playerId)
            .ToArray();
        var teamIds = state["teamIds"]?.Deserialize<int[]>(JsonOptions) ?? [];
        if (teamIds.Length == 0 || playerId < 0 || playerId >= teamIds.Length)
        {
            return players;
        }

        var teamId = teamIds[playerId];
        return players.Where(id => id < 0 || id >= teamIds.Length || teamIds[id] != teamId).ToArray();
    }

    private static bool CanSummonChampion(JsonObject state, int playerId)
    {
        var player = state["players"]!.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(candidate => candidate["id"]?.GetValue<int>() == playerId);
        if (player is null)
        {
            return false;
        }

        var champion = player["champion"] as JsonObject;
        if (champion is null || player["championSummoned"]?.GetValue<bool>() == true)
        {
            return false;
        }

        return CanPay(player, ReadCost(champion));
    }

    private static JsonObject? SummonChampion(JsonObject state, int playerId)
    {
        var player = state["players"]!.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(candidate => candidate["id"]?.GetValue<int>() == playerId);
        if (player is null)
        {
            return null;
        }

        var champion = player["champion"] as JsonObject;
        if (champion is null || player["championSummoned"]?.GetValue<bool>() == true)
        {
            return null;
        }

        var cost = ReadCost(champion);
        if (!CanPay(player, cost))
        {
            return null;
        }

        var unit = Clone(champion);
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
            PayCost(p, cost);
            p["championSummoned"] = true;
            p["base"]!.AsArray().Add(unit);
            return p;
        });

        return AddLog(state, $"{PlayerName(state, playerId)} summoned {champion["name"]?.GetValue<string>() ?? "their champion"} to their base.");
    }

    private static JsonObject? PlayCard(JsonObject state, int playerId, int? handIndex, string? targetUnitId, string? targetLaneId)
    {
        if (handIndex is null)
        {
            return null;
        }

        var player = FindPlayer(state, playerId);
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
        if (!IsSpellOrGear(card) || !CanPlayCardNow(state, playerId, card))
        {
            return null;
        }

        if (!TargetsAreValid(state, playerId, card, targetUnitId, targetLaneId))
        {
            return null;
        }

        var cost = ReadCost(card);
        if (!CanPay(player, cost))
        {
            return null;
        }

        state = UpdatePlayer(state, playerId, p =>
        {
            p["hand"]!.AsArray().RemoveAt(handIndex.Value);
            PayCost(p, cost);
            return p;
        });

        return FinalizePendingCardPlay(state, playerId, card, targetUnitId, targetLaneId);
    }

    private static JsonObject FinalizePendingCardPlay(JsonObject state, int playerId, JsonObject card, string? targetUnitId, string? targetLaneId)
    {
        var kind = card["kind"]?.GetValue<string>() ?? string.Empty;
        if (kind == "gear")
        {
            state = PutResolvedGearIntoBase(state, playerId, card);
            if (state["effectStack"]!.AsArray().Count > 0)
            {
                OpenChainWindow(state, playerId, playerId);
            }
            else
            {
                CloseChainWindow(state, playerId);
            }

            return AddLog(state, $"{PlayerName(state, playerId)} played {card["name"]?.GetValue<string>() ?? "a gear"} to base.");
        }

        var stackItem = new JsonObject
        {
            ["id"] = $"stack-{state["nextUid"]?.GetValue<int>() ?? 1}",
            ["card"] = Clone(card),
            ["cardId"] = card["catalogId"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty,
            ["cardName"] = card["name"]?.GetValue<string>() ?? "a card",
            ["kind"] = kind,
            ["playerId"] = playerId,
            ["effect"] = card["effect"]?.DeepClone(),
            ["targetUnitId"] = targetUnitId,
            ["targetLaneId"] = targetLaneId
        };
        state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;
        state["effectStack"]!.AsArray().Insert(0, stackItem);
        OpenChainWindow(state, playerId, playerId);
        return AddLog(state, $"{PlayerName(state, playerId)} played {card["name"]?.GetValue<string>() ?? "a spell"} to the chain.");
    }

    private static JsonObject? PassChainWindow(JsonObject state, int playerId)
    {
        var chainWindow = state["chainWindow"]?.AsObject();
        if (chainWindow is null)
        {
            return null;
        }

        var order = state["turnOrder"]!.Deserialize<int[]>(JsonOptions) ?? [];
        var priorityPlayerId = ChainPriorityPlayerId(state);
        if (!order.Contains(playerId) || priorityPlayerId is not null && priorityPlayerId != playerId)
        {
            return null;
        }

        var passed = chainWindow["passedByPlayer"]?.AsObject() ?? new JsonObject();
        passed[playerId.ToString()] = true;
        chainWindow["passedByPlayer"] = passed;
        state = AddLog(state, $"{PlayerName(state, playerId)} passed reaction priority.");

        if (order.All(id => passed[id.ToString()]?.GetValue<bool>() == true))
        {
            state = ResolveTopStackItem(state);
            state = RunCleanup(state);
            var stack = state["effectStack"]!.AsArray();
            if (stack.Count > 0)
            {
                var nextPriority = stack[0]!["playerId"]?.GetValue<int>() ?? playerId;
                OpenChainWindow(state, nextPriority, nextPriority);
            }
            else
            {
                var startedBy = chainWindow["startedByPlayerId"]?.GetValue<int>() ?? playerId;
                CloseChainWindow(state, startedBy);
            }
        }
        else
        {
            var nextPriority = NextPlayerId(state, playerId);
            chainWindow["priorityPlayerId"] = nextPriority;
            state["priorityPlayerId"] = nextPriority;
            state["activePlayer"] = nextPriority;
            state["chainWindow"] = chainWindow;
        }

        return state;
    }

    private static JsonObject? PassFocus(JsonObject state, int playerId)
    {
        var activeShowdown = state["activeShowdown"] as JsonObject;
        if (activeShowdown is null || state["chainWindow"] is not null || CombatDamageRequired(state))
        {
            return null;
        }

        var focusPlayerId = state["focusPlayerId"]?.GetValue<int?>() ?? state["activePlayer"]?.GetValue<int?>() ?? -1;
        if (focusPlayerId != playerId)
        {
            return null;
        }

        var passed = state["hasPassedFocusByPlayer"]?.AsObject() ?? new JsonObject();
        passed[playerId.ToString()] = true;
        state["hasPassedFocusByPlayer"] = passed;
        state = AddLog(state, $"{PlayerName(state, playerId)} passed focus.");

        var order = state["turnOrder"]!.Deserialize<int[]>(JsonOptions) ?? [];
        if (order.All(id => passed[id.ToString()]?.GetValue<bool>() == true))
        {
            return CloseShowdown(state);
        }

        var nextFocus = NextPlayerId(state, playerId);
        state["focusPlayerId"] = nextFocus;
        state["priorityPlayerId"] = nextFocus;
        state["activePlayer"] = nextFocus;
        return state;
    }

    private static void OpenChainWindow(JsonObject state, int priorityPlayerId, int startedByPlayerId)
    {
        state["chainWindow"] = new JsonObject
        {
            ["priorityPlayerId"] = priorityPlayerId,
            ["startedByPlayerId"] = startedByPlayerId,
            ["passedByPlayer"] = new JsonObject()
        };
        state["priorityPlayerId"] = priorityPlayerId;
        state["activePlayer"] = priorityPlayerId;
    }

    private static void CloseChainWindow(JsonObject state, int startedByPlayerId)
    {
        state["chainWindow"] = null;
        state["priorityPlayerId"] = null;
        if (state["activeShowdown"] is not null && !CombatDamageRequired(state))
        {
            var nextFocus = NextPlayerId(state, startedByPlayerId);
            state["focusPlayerId"] = nextFocus;
            state["activePlayer"] = nextFocus;
            state["hasPassedFocusByPlayer"] = new JsonObject();
        }
        else
        {
            state["activePlayer"] = state["turnPlayerId"]?.GetValue<int>() ?? 0;
        }
    }

    private static int? ChainPriorityPlayerId(JsonObject state)
    {
        return state["chainWindow"]?["priorityPlayerId"]?.GetValue<int?>()
            ?? state["priorityPlayerId"]?.GetValue<int?>();
    }

    private static JsonObject ResolveTopStackItem(JsonObject state)
    {
        var stack = state["effectStack"]!.AsArray();
        if (stack.Count == 0)
        {
            return state;
        }

        var item = stack[0]!.AsObject();
        stack.RemoveAt(0);

        var playerId = item["playerId"]?.GetValue<int>() ?? 0;
        var effect = item["effect"]?.AsObject();
        var effectType = effect?["type"]?.GetValue<string>() ?? "rally";
        var amount = effect?["amount"]?.GetValue<int>() ?? 0;
        var targetUnitId = item["targetUnitId"]?.GetValue<string>();
        var targetLaneId = item["targetLaneId"]?.GetValue<string>();

        state = effectType switch
        {
            "draw" => DrawCards(state, playerId, amount),
            "damage" => ApplyDamage(state, playerId, amount, targetUnitId, targetLaneId),
            "buff" => ApplyUnitMod(state, targetUnitId, unit => { unit["attachedMight"] = (unit["attachedMight"]?.GetValue<int>() ?? 0) + amount; return unit; }),
            "rally" => ApplyUnitMod(state, targetUnitId, unit => { unit["exhausted"] = false; return unit; }),
            _ => state
        };

        var card = item["card"]!.AsObject();
        var kind = item["kind"]?.GetValue<string>() ?? card["kind"]?.GetValue<string>() ?? string.Empty;
        if (kind == "gear")
        {
            state = PutResolvedGearIntoBase(state, playerId, card);
        }
        else if (kind == "spell")
        {
            state = UpdatePlayer(state, playerId, player =>
            {
                player["trash"]!.AsArray().Add(card.DeepClone());
                return player;
            });
        }

        return AddLog(state, $"{item["cardName"]?.GetValue<string>() ?? "A card"} resolved.");
    }

    private static JsonObject PutResolvedGearIntoBase(JsonObject state, int playerId, JsonObject card)
    {
        var gear = Clone(card);
        gear["uid"] = $"gear-{state["nextUid"]?.GetValue<int>() ?? 1}";
        gear["ownerId"] = playerId;
        gear["location"] = new JsonObject { ["type"] = "base", ["battlefieldId"] = null };
        gear["exhausted"] = false;
        gear["attachedUnitId"] = null;
        state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;

        return UpdatePlayer(state, playerId, player =>
        {
            player["baseGear"]!.AsArray().Add(gear);
            return player;
        });
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

        var cost = ReadCost(card);
        if (!CanPay(player, cost))
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
            PayCost(p, cost);
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
        if (battlefield is null)
        {
            return null;
        }

        base_.Remove(unitNode);
        var moved = Clone(unit);
        moved["exhausted"] = true;
        battlefield["units"]!.AsArray().Add(moved);

        state = AddLog(state, $"{PlayerName(state, playerId)} moved {unit["name"]?.GetValue<string>() ?? "a unit"} to {battlefield["name"]?.GetValue<string>() ?? "a battlefield"}.");

        var owners = battlefield["units"]!.AsArray()
            .Select(node => node!.AsObject()["ownerId"]?.GetValue<int>())
            .Where(owner => owner is not null)
            .Select(owner => owner!.Value)
            .Distinct()
            .ToArray();

        if (owners.Length == 2)
        {
            battlefield["controllerId"] = null;
            battlefield["stagedCombat"] = true;
            battlefield["stagedShowdown"] = true;
            battlefield["contestedByPlayerId"] = playerId;
            return AddLog(state, $"{PlayerName(state, playerId)} contested {battlefield["name"]?.GetValue<string>() ?? "a battlefield"} and staged combat.");
        }

        if (owners.Length == 1)
        {
            battlefield["controllerId"] = playerId;
            battlefield["contestedByPlayerId"] = null;
            battlefield["stagedShowdown"] = false;
            battlefield["stagedCombat"] = false;
        }

        return state;
    }

    private static JsonObject RunFeprUntilChoiceRequired(JsonObject state)
    {
        return RunOutstandingTasks(state);
    }

    private static JsonObject RunOutstandingTasks(JsonObject state)
    {
        if (state["stage"]?.GetValue<string>() != "playing" || state["chainWindow"] is not null || PendingBurnOut(state) is not null)
        {
            return state;
        }

        return RunCleanup(state);
    }

    private static JsonObject RunCleanup(JsonObject state)
    {
        if (state["stage"]?.GetValue<string>() != "playing")
        {
            return state;
        }

        state = CheckWinners(state);
        if (state["stage"]?.GetValue<string>() == "game-over")
        {
            return state;
        }

        state = KillLethalUnits(state);
        if (state["activeShowdown"] is not null || state["activeCombat"] is not null || state["chainWindow"] is not null)
        {
            return state;
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var units = battlefield["units"]!.AsArray().Select(node => node!.AsObject()).ToArray();
            var owners = units
                .Select(unit => unit["ownerId"]?.GetValue<int>())
                .Where(owner => owner is not null)
                .Select(owner => owner!.Value)
                .Distinct()
                .ToArray();
            var contestedBy = battlefield["contestedByPlayerId"]?.GetValue<int?>();

            if (owners.Length == 0)
            {
                battlefield["controllerId"] = null;
                battlefield["contestedByPlayerId"] = null;
                battlefield["stagedShowdown"] = false;
                battlefield["stagedCombat"] = false;
                continue;
            }

            if (contestedBy is null || !owners.Contains(contestedBy.Value))
            {
                continue;
            }

            if (owners.Length == 2)
            {
                battlefield["controllerId"] = null;
                battlefield["stagedShowdown"] = true;
                battlefield["stagedCombat"] = true;
            }
        }

        return OpenNextStagedConflict(state);
    }

    private static JsonObject OpenNextStagedConflict(JsonObject state)
    {
        if (state["activeShowdown"] is not null || state["activeCombat"] is not null || state["chainWindow"] is not null)
        {
            return state;
        }

        var showdown = state["battlefields"]!.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(field => field["stagedShowdown"]?.GetValue<bool>() == true);
        if (showdown is not null)
        {
            return showdown["stagedCombat"]?.GetValue<bool>() == true
                ? OpenCombatFromCleanup(state, showdown)
                : OpenShowdownFromCleanup(state, showdown);
        }

        var combat = state["battlefields"]!.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(field => field["stagedCombat"]?.GetValue<bool>() == true);
        return combat is null ? state : OpenCombatFromCleanup(state, combat);
    }

    private static JsonObject OpenShowdownFromCleanup(JsonObject state, JsonObject battlefield)
    {
        var battlefieldId = battlefield["id"]?.GetValue<string>() ?? string.Empty;
        var focusPlayerId = battlefield["contestedByPlayerId"]?.GetValue<int?>()
            ?? state["turnPlayerId"]?.GetValue<int>()
            ?? 0;
        battlefield["stagedShowdown"] = false;
        state["activeShowdown"] = new JsonObject
        {
            ["battlefieldId"] = battlefieldId,
            ["kind"] = "non-combat"
        };
        state["focusPlayerId"] = focusPlayerId;
        state["priorityPlayerId"] = focusPlayerId;
        state["activePlayer"] = focusPlayerId;
        state["hasPassedFocusByPlayer"] = new JsonObject();
        return AddLog(state, $"Showdown opened at {battlefield["name"]?.GetValue<string>() ?? "a battlefield"}.");
    }

    private static JsonObject OpenCombatFromCleanup(JsonObject state, JsonObject battlefield)
    {
        var battlefieldId = battlefield["id"]?.GetValue<string>() ?? string.Empty;
        var attackerPlayerId = battlefield["contestedByPlayerId"]?.GetValue<int?>()
            ?? state["turnPlayerId"]?.GetValue<int>()
            ?? 0;
        var defenderPlayerId = battlefield["units"]!.AsArray()
            .Select(node => node!.AsObject()["ownerId"]?.GetValue<int>())
            .Where(owner => owner is not null && owner.Value != attackerPlayerId)
            .Select(owner => owner!.Value)
            .Distinct()
            .FirstOrDefault();

        battlefield["stagedShowdown"] = false;
        battlefield["stagedCombat"] = false;
        foreach (var unit in battlefield["units"]!.AsArray().Select(node => node!.AsObject()))
        {
            var ownerId = unit["ownerId"]?.GetValue<int>() ?? -1;
            unit["attacker"] = ownerId == attackerPlayerId;
            unit["defender"] = ownerId == defenderPlayerId;
        }

        state["activeShowdown"] = new JsonObject
        {
            ["battlefieldId"] = battlefieldId,
            ["kind"] = "combat"
        };
        state["activeCombat"] = new JsonObject
        {
            ["battlefieldId"] = battlefieldId,
            ["attackerPlayerId"] = attackerPlayerId,
            ["defenderPlayerId"] = defenderPlayerId,
            ["damageStep"] = false
        };
        state["focusPlayerId"] = attackerPlayerId;
        state["priorityPlayerId"] = attackerPlayerId;
        state["activePlayer"] = attackerPlayerId;
        state["hasPassedFocusByPlayer"] = new JsonObject();
        return AddLog(state, $"{PlayerName(state, attackerPlayerId)} challenges {PlayerName(state, defenderPlayerId)} to a combat showdown at {battlefield["name"]?.GetValue<string>() ?? "a battlefield"}.");
    }

    private static JsonObject CloseShowdown(JsonObject state)
    {
        var activeShowdown = state["activeShowdown"] as JsonObject;
        if (activeShowdown is null)
        {
            return state;
        }

        if (activeShowdown["kind"]?.GetValue<string>() == "combat" && state["activeCombat"] is JsonObject activeCombat)
        {
            activeCombat["damageStep"] = true;
            state["focusPlayerId"] = null;
            state["priorityPlayerId"] = null;
            state["activePlayer"] = state["turnPlayerId"]?.GetValue<int>() ?? 0;
            state["hasPassedFocusByPlayer"] = new JsonObject();
            return AddLog(state, "Combat showdown closed. Assign combat damage.");
        }

        var battlefieldId = activeShowdown["battlefieldId"]?.GetValue<string>() ?? string.Empty;
        var battlefield = FindBattlefield(state, battlefieldId);
        state["activeShowdown"] = null;
        state["focusPlayerId"] = null;
        state["priorityPlayerId"] = null;
        state["activePlayer"] = state["turnPlayerId"]?.GetValue<int>() ?? 0;
        state["hasPassedFocusByPlayer"] = new JsonObject();
        if (battlefield is null)
        {
            return state;
        }

        var owners = battlefield["units"]!.AsArray()
            .Select(node => node!.AsObject()["ownerId"]?.GetValue<int>())
            .Where(owner => owner is not null)
            .Select(owner => owner!.Value)
            .Distinct()
            .ToArray();
        if (owners.Length == 1 && battlefield["controllerId"]?.GetValue<int?>() != owners[0])
        {
            battlefield["controllerId"] = owners[0];
            battlefield["contestedByPlayerId"] = null;
            state = ScoreBattlefield(state, new ScoreRequest(owners[0], battlefieldId, ScoreSource.Conquer));
        }

        state = AddLog(state, $"Showdown at {battlefield["name"]?.GetValue<string>() ?? "a battlefield"} closed.");
        return RunCleanup(state);
    }

    private static JsonObject? ResolveCombat(JsonObject state, int playerId, IReadOnlyDictionary<string, object?>? payload)
    {
        var activeCombat = state["activeCombat"] as JsonObject;
        if (activeCombat is null)
        {
            return null;
        }

        if (!CombatDamageRequired(state))
        {
            return null;
        }

        var battlefieldId = ReadString(payload, "battlefieldId");
        var combatBattlefieldId = activeCombat["battlefieldId"]?.GetValue<string>();
        var attackerPlayerId = activeCombat["attackerPlayerId"]?.GetValue<int>();
        var defenderPlayerId = activeCombat["defenderPlayerId"]?.GetValue<int>();
        if (string.IsNullOrWhiteSpace(battlefieldId) ||
            battlefieldId != combatBattlefieldId ||
            attackerPlayerId is null ||
            defenderPlayerId is null ||
            playerId != attackerPlayerId.Value && playerId != defenderPlayerId.Value)
        {
            return null;
        }

        var battlefield = state["battlefields"]!.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(candidate => candidate["id"]?.GetValue<string>() == battlefieldId);
        if (battlefield is null)
        {
            return null;
        }

        var units = battlefield["units"]!.AsArray().Select(node => node!.AsObject()).ToArray();
        var unitOwners = units.Select(unit => unit["ownerId"]?.GetValue<int>())
            .Where(owner => owner is not null)
            .Select(owner => owner!.Value)
            .Distinct()
            .ToArray();
        if (unitOwners.Length != 2 ||
            !unitOwners.Contains(attackerPlayerId.Value) ||
            !unitOwners.Contains(defenderPlayerId.Value))
        {
            return null;
        }

        var attackers = units.Where(unit => unit["ownerId"]?.GetValue<int>() == attackerPlayerId.Value).ToArray();
        var defenders = units.Where(unit => unit["ownerId"]?.GetValue<int>() == defenderPlayerId.Value).ToArray();
        if (attackers.Length == 0 || defenders.Length == 0)
        {
            return null;
        }

        var isAttacker = playerId == attackerPlayerId.Value;
        var ownAssignments = ReadDamageAssignments(payload, "assignments");
        var submittedAttackerAssignments = ReadDamageAssignments(payload, "attackerAssignments");
        var submittedDefenderAssignments = ReadDamageAssignments(payload, "defenderAssignments");
        if (isAttacker && submittedDefenderAssignments is not null ||
            !isAttacker && submittedAttackerAssignments is not null)
        {
            return null;
        }

        ownAssignments ??= isAttacker ? submittedAttackerAssignments : submittedDefenderAssignments;
        if (ownAssignments is null)
        {
            return null;
        }

        var ownUnits = isAttacker ? attackers : defenders;
        var opposingUnits = isAttacker ? defenders : attackers;
        if (!ValidateDamageAssignments(ownUnits, opposingUnits, ownAssignments))
        {
            return null;
        }

        var assignmentKey = isAttacker ? "attackerAssignments" : "defenderAssignments";
        if (activeCombat[assignmentKey] is not null)
        {
            return null;
        }

        activeCombat[assignmentKey] = ToObject(ownAssignments);

        var attackerAssignments = ReadDamageAssignmentsFromNode(activeCombat["attackerAssignments"]);
        var defenderAssignments = ReadDamageAssignmentsFromNode(activeCombat["defenderAssignments"]);
        if (attackerAssignments is null || defenderAssignments is null)
        {
            return AddLog(state, $"{PlayerName(state, playerId)} assigned combat damage at {battlefield["name"]?.GetValue<string>() ?? "a battlefield"}.");
        }

        if (!ValidateDamageAssignments(attackers, defenders, attackerAssignments) ||
            !ValidateDamageAssignments(defenders, attackers, defenderAssignments))
        {
            return null;
        }

        foreach (var unit in defenders)
        {
            var uid = unit["uid"]?.GetValue<string>() ?? string.Empty;
            unit["damage"] = (unit["damage"]?.GetValue<int>() ?? 0) + attackerAssignments.GetValueOrDefault(uid);
        }

        foreach (var unit in attackers)
        {
            var uid = unit["uid"]?.GetValue<string>() ?? string.Empty;
            unit["damage"] = (unit["damage"]?.GetValue<int>() ?? 0) + defenderAssignments.GetValueOrDefault(uid);
        }

        state = RunCombatCleanup(state, battlefield);

        var remainingUnits = battlefield["units"]!.AsArray().Select(node => node!.AsObject()).ToArray();

        var attackerHasUnits = remainingUnits.Any(unit => unit["ownerId"]?.GetValue<int>() == attackerPlayerId.Value);
        var defenderHasUnits = remainingUnits.Any(unit => unit["ownerId"]?.GetValue<int>() == defenderPlayerId.Value);

        battlefield["stagedCombat"] = false;
        battlefield["stagedShowdown"] = false;
        battlefield["contestedByPlayerId"] = null;
        state["activeCombat"] = null;
        state["activeShowdown"] = null;
        state["focusPlayerId"] = null;
        state["priorityPlayerId"] = null;
        state["activePlayer"] = state["turnPlayerId"]?.GetValue<int>() ?? 0;
        state["hasPassedFocusByPlayer"] = new JsonObject();

        if (attackerHasUnits && defenderHasUnits)
        {
            battlefield["stagedCombat"] = true;
            battlefield["contestedByPlayerId"] = attackerPlayerId.Value;
            battlefield["controllerId"] = null;
            return AddLog(state, $"Combat at {battlefield["name"]?.GetValue<string>() ?? "a battlefield"} had no result.");
        }

        if (attackerHasUnits)
        {
            battlefield["controllerId"] = attackerPlayerId.Value;
            state = ScoreBattlefield(state, new ScoreRequest(attackerPlayerId.Value, battlefieldId, ScoreSource.Conquer));
        }
        else if (defenderHasUnits)
        {
            battlefield["controllerId"] = defenderPlayerId.Value;
            state = ScoreBattlefield(state, new ScoreRequest(defenderPlayerId.Value, battlefieldId, ScoreSource.Conquer));
        }
        else
        {
            battlefield["controllerId"] = null;
        }

        return AddLog(state, $"Combat at {battlefield["name"]?.GetValue<string>() ?? "a battlefield"} resolved.");
    }

    private static JsonObject RunCombatCleanup(JsonObject state, JsonObject battlefield)
    {
        KillLethalBattlefieldUnits(state, battlefield);
        foreach (var unit in battlefield["units"]!.AsArray().Select(node => node!.AsObject()))
        {
            unit["damage"] = 0;
            unit["attacker"] = false;
            unit["defender"] = false;
        }

        return state;
    }

    private static bool ValidateDamageAssignments(IReadOnlyList<JsonObject> assigningUnits, IReadOnlyList<JsonObject> opposingUnits, IReadOnlyDictionary<string, int> assignments)
    {
        if (assignments.Values.Any(amount => amount < 0))
        {
            return false;
        }

        var opposingById = opposingUnits
            .Select(unit => new { Unit = unit, Uid = unit["uid"]?.GetValue<string>() })
            .Where(item => !string.IsNullOrWhiteSpace(item.Uid))
            .ToDictionary(item => item.Uid!, item => item.Unit, StringComparer.Ordinal);
        if (assignments.Keys.Any(uid => !opposingById.ContainsKey(uid)))
        {
            return false;
        }

        var requiredTotal = assigningUnits.Sum(CurrentCombatMight);
        if (assignments.Values.Sum() != requiredTotal)
        {
            return false;
        }

        var positiveAssignments = assignments.Where(pair => pair.Value > 0).ToArray();
        var nonLethalPositiveCount = 0;
        var allOpposingUnitsAssignedLethal = opposingUnits.All(unit =>
        {
            var uid = unit["uid"]?.GetValue<string>() ?? string.Empty;
            return assignments.GetValueOrDefault(uid) >= LethalDamage(unit);
        });

        foreach (var (uid, amount) in positiveAssignments)
        {
            var lethal = LethalDamage(opposingById[uid]);
            if (amount < lethal)
            {
                nonLethalPositiveCount += 1;
                continue;
            }

            if (amount > lethal && !allOpposingUnitsAssignedLethal)
            {
                return false;
            }
        }

        return nonLethalPositiveCount <= 1;
    }

    private static Dictionary<string, int>? ReadDamageAssignments(IReadOnlyDictionary<string, object?>? payload, string key)
    {
        if (payload is null || !payload.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            return element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.TryGetInt32(out var amount) ? amount : -1, StringComparer.Ordinal);
        }

        if (value is JsonObject jsonObject)
        {
            return jsonObject.ToDictionary(pair => pair.Key, pair => pair.Value?.GetValue<int>() ?? -1, StringComparer.Ordinal);
        }

        if (value is IReadOnlyDictionary<string, int> intDictionary)
        {
            return new Dictionary<string, int>(intDictionary, StringComparer.Ordinal);
        }

        if (value is IReadOnlyDictionary<string, object?> objectDictionary)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (uid, amount) in objectDictionary)
            {
                result[uid] = amount switch
                {
                    int intValue => intValue,
                    JsonElement amountElement when amountElement.TryGetInt32(out var intValue) => intValue,
                    _ => -1
                };
            }

            return result;
        }

        return null;
    }

    private static Dictionary<string, int>? ReadDamageAssignmentsFromNode(JsonNode? node)
    {
        if (node is not JsonObject jsonObject)
        {
            return null;
        }

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (uid, amount) in jsonObject)
        {
            result[uid] = amount?.GetValue<int>() ?? -1;
        }

        return result;
    }

    private static void KillLethalBattlefieldUnits(JsonObject state, JsonObject battlefield)
    {
        var units = battlefield["units"]!.AsArray();
        for (var i = units.Count - 1; i >= 0; i--)
        {
            var unit = units[i]!.AsObject();
            if ((unit["damage"]?.GetValue<int>() ?? 0) < LethalDamage(unit))
            {
                continue;
            }

            var killed = Clone(unit);
            killed.Remove("uid");
            killed.Remove("ownerId");
            killed.Remove("exhausted");
            killed.Remove("damage");
            killed.Remove("attachedMight");
            killed.Remove("attacker");
            killed.Remove("defender");
            var ownerId = unit["ownerId"]?.GetValue<int>() ?? -1;
            state = UpdatePlayer(state, ownerId, player =>
            {
                player["trash"]!.AsArray().Add(killed);
                return player;
            });
            units.RemoveAt(i);
        }
    }

    private static JsonObject KillLethalUnits(JsonObject state)
    {
        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            KillLethalBattlefieldUnits(state, battlefield);
        }

        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            var units = player["base"]!.AsArray();
            for (var i = units.Count - 1; i >= 0; i--)
            {
                var unit = units[i]!.AsObject();
                if ((unit["damage"]?.GetValue<int>() ?? 0) < LethalDamage(unit))
                {
                    continue;
                }

                var killed = Clone(unit);
                killed.Remove("uid");
                killed.Remove("ownerId");
                killed.Remove("exhausted");
                killed.Remove("damage");
                killed.Remove("attachedMight");
                killed.Remove("attacker");
                killed.Remove("defender");
                player["trash"]!.AsArray().Add(killed);
                units.RemoveAt(i);
            }
        }

        return state;
    }

    private static int CurrentMight(JsonObject unit)
    {
        return (unit["might"]?.GetValue<int>() ?? 0) + (unit["attachedMight"]?.GetValue<int>() ?? 0);
    }

    private static int CurrentCombatMight(JsonObject unit)
    {
        return Math.Max(0, CurrentMight(unit));
    }

    private static int LethalDamage(JsonObject unit)
    {
        return Math.Max(1, CurrentMight(unit));
    }

    private static IReadOnlyList<EngineLegalAction> PlayableCardsFromHand(JsonObject state, int playerId, Func<JsonObject, bool> timingPredicate)
    {
        var player = FindPlayer(state, playerId);
        if (player is null)
        {
            return [];
        }

        return player["hand"]!.AsArray()
            .Select((node, index) => (Card: node!.AsObject(), Index: index))
            .Where(item => timingPredicate(item.Card) && CanPay(player, ReadCost(item.Card)))
            .Select(item => new EngineLegalAction(
                $"play-card-{playerId}-{item.Index}",
                "play-card",
                $"Play {item.Card["name"]?.GetValue<string>() ?? "card"}",
                playerId,
                new JsonObject { ["handIndex"] = item.Index }))
            .ToArray();
    }

    private static bool CanPlayCardNow(JsonObject state, int playerId, JsonObject card)
    {
        var stage = state["stage"]?.GetValue<string>() ?? "";
        if (stage != "playing")
        {
            return false;
        }

        if (state["chainWindow"] is not null)
        {
            return IsReactionCard(card);
        }

        if (IsShowdownOpen(state))
        {
            var focusPlayerId = state["priorityPlayerId"]?.GetValue<int?>()
                ?? state["focusPlayerId"]?.GetValue<int?>()
                ?? state["activePlayer"]?.GetValue<int?>()
                ?? state["turnPlayerId"]?.GetValue<int>()
                ?? 0;
            return playerId == focusPlayerId && (IsActionCard(card) || IsReactionCard(card));
        }

        var turnPlayerId = state["turnPlayerId"]?.GetValue<int>() ?? 0;
        var turnPhase = state["turnPhase"]?.GetValue<string>() ?? "";
        return playerId == turnPlayerId && turnPhase == "main";
    }

    private static bool IsShowdownOpen(JsonObject state)
    {
        return state["chainWindow"] is null
            && (state["activeShowdown"] is not null || state["activeCombat"] is not null);
    }

    private static bool CombatDamageRequired(JsonObject state)
    {
        if (state["activeCombat"] is not JsonObject activeCombat)
        {
            return false;
        }

        return activeCombat["damageStep"]?.GetValue<bool>() == true
            || state["focusPlayerId"] is null
            || state["activeShowdown"] is null
            || activeCombat["attackerAssignments"] is not null
            || activeCombat["defenderAssignments"] is not null;
    }

    private static bool IsSpellOrGear(JsonObject card)
    {
        var kind = card["kind"]?.GetValue<string>() ?? string.Empty;
        return kind is "spell" or "gear";
    }

    private static bool IsReactionCard(JsonObject card)
    {
        if (!IsSpellOrGear(card))
        {
            return false;
        }

        return (card["text"]?.GetValue<string>() ?? string.Empty).Contains("[Reaction]", StringComparison.Ordinal);
    }

    private static bool IsActionCard(JsonObject card)
    {
        if (!IsSpellOrGear(card))
        {
            return false;
        }

        var text = card["text"]?.GetValue<string>() ?? string.Empty;
        return text.Contains("[Action]", StringComparison.Ordinal)
            || !text.Contains("[Reaction]", StringComparison.Ordinal);
    }

    private static bool TargetsAreValid(JsonObject state, int playerId, JsonObject card, string? targetUnitId, string? targetLaneId)
    {
        var effect = card["effect"]?.AsObject();
        var effectType = effect?["type"]?.GetValue<string>() ?? "rally";
        if (effectType == "draw")
        {
            return string.IsNullOrWhiteSpace(targetUnitId) && string.IsNullOrWhiteSpace(targetLaneId);
        }

        if (effectType == "damage")
        {
            if (!string.IsNullOrWhiteSpace(targetLaneId))
            {
                return FindBattlefield(state, targetLaneId) is not null;
            }

            if (!string.IsNullOrWhiteSpace(targetUnitId))
            {
                return FindUnit(state, targetUnitId) is { } unit && unit["ownerId"]?.GetValue<int>() != playerId;
            }

            return false;
        }

        if (effectType is "buff" or "rally")
        {
            return !string.IsNullOrWhiteSpace(targetUnitId)
                && FindUnit(state, targetUnitId) is { } unit
                && unit["ownerId"]?.GetValue<int>() == playerId
                && string.IsNullOrWhiteSpace(targetLaneId);
        }

        return string.IsNullOrWhiteSpace(targetUnitId) && string.IsNullOrWhiteSpace(targetLaneId);
    }

    private static bool CanPay(JsonObject player, ResourceCost cost) => BuildPaymentPlan(player, cost) is not null;

    private static void PayCost(JsonObject player, ResourceCost cost)
    {
        var plan = BuildPaymentPlan(player, cost);
        if (plan is null)
        {
            return;
        }

        var ready = player["runes"]!["ready"]!.AsArray();
        var exhausted = player["runes"]!["exhausted"]!.AsArray();
        var runeDeck = player["runeDeck"]!.AsArray();
        for (var i = ready.Count - 1; i >= 0; i--)
        {
            var rune = ready[i]!.AsObject();
            var id = rune["id"]?.GetValue<string>() ?? string.Empty;
            if (plan.RecycledRuneIds.Contains(id, StringComparer.Ordinal))
            {
                runeDeck.Add(rune.DeepClone());
                ready.RemoveAt(i);
            }
        }

        for (var i = exhausted.Count - 1; i >= 0; i--)
        {
            var rune = exhausted[i]!.AsObject();
            var id = rune["id"]?.GetValue<string>() ?? string.Empty;
            if (plan.RecycledRuneIds.Contains(id, StringComparer.Ordinal))
            {
                runeDeck.Add(rune.DeepClone());
                exhausted.RemoveAt(i);
            }
        }

        for (var i = 0; i < plan.ReadyRunesToExhaust && ready.Count > 0; i++)
        {
            exhausted.Add(ready[0]?.DeepClone());
            ready.RemoveAt(0);
        }

        var pool = RunePool(player);
        pool["energy"] = plan.RemainingEnergy;
        pool["universalPower"] = plan.RemainingUniversalPower;
        pool["power"] = ToObject(plan.RemainingPower.ToDictionary(item => item.Key.ToString(), item => item.Value));
    }

    private static PaymentPlan? BuildPaymentPlan(JsonObject player, ResourceCost cost)
    {
        var pool = RunePool(player);
        var remainingPower = ReadPowerPool(pool);
        var universalPower = pool["universalPower"]?.GetValue<int>() ?? 0;
        var recycledRuneIds = new List<string>();
        var boardRunes = BoardRunes(player).ToList();
        var requiredPower = cost.Power.ToDictionary(item => item.Key, item => Math.Max(0, item.Value));

        foreach (var domain in DomainOrder())
        {
            var required = requiredPower.GetValueOrDefault(domain);
            if (required <= 0)
            {
                continue;
            }

            var fromDomain = Math.Min(remainingPower.GetValueOrDefault(domain), required);
            remainingPower[domain] = remainingPower.GetValueOrDefault(domain) - fromDomain;
            required -= fromDomain;

            var fromUniversal = Math.Min(universalPower, required);
            universalPower -= fromUniversal;
            required -= fromUniversal;

            while (required > 0)
            {
                var rune = boardRunes.FirstOrDefault(candidate => candidate.Domain == domain && !recycledRuneIds.Contains(candidate.Id, StringComparer.Ordinal));
                if (rune is null)
                {
                    return null;
                }

                recycledRuneIds.Add(rune.Id);
                required -= 1;
            }
        }

        var anyPowerRequired = cost.UniversalPower;
        var totalStoredPower = universalPower + remainingPower.Values.Sum();
        var fromStoredAny = Math.Min(totalStoredPower, anyPowerRequired);
        anyPowerRequired -= fromStoredAny;
        SpendAnyPower(remainingPower, ref universalPower, fromStoredAny);
        while (anyPowerRequired > 0)
        {
            var rune = boardRunes.FirstOrDefault(candidate => !recycledRuneIds.Contains(candidate.Id, StringComparer.Ordinal));
            if (rune is null)
            {
                return null;
            }

            recycledRuneIds.Add(rune.Id);
            anyPowerRequired -= 1;
        }

        var energy = pool["energy"]?.GetValue<int>() ?? 0;
        var energyFromPool = Math.Min(energy, cost.Energy);
        var energyNeeded = cost.Energy - energyFromPool;
        var readyRunesAvailableForEnergy = player["runes"]!["ready"]!.AsArray()
            .Select(node => node!.AsObject())
            .Count(rune => !recycledRuneIds.Contains(rune["id"]?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal));
        if (readyRunesAvailableForEnergy < energyNeeded)
        {
            return null;
        }

        return new PaymentPlan(
            energyNeeded,
            energy - energyFromPool,
            Math.Max(0, universalPower),
            remainingPower.ToDictionary(item => item.Key, item => Math.Max(0, item.Value)),
            recycledRuneIds);
    }

    private static void SpendAnyPower(Dictionary<Domain, int> power, ref int universalPower, int amount)
    {
        var fromUniversal = Math.Min(universalPower, amount);
        universalPower -= fromUniversal;
        amount -= fromUniversal;
        foreach (var domain in DomainOrder())
        {
            if (amount <= 0)
            {
                return;
            }

            var fromDomain = Math.Min(power.GetValueOrDefault(domain), amount);
            power[domain] = power.GetValueOrDefault(domain) - fromDomain;
            amount -= fromDomain;
        }
    }

    private static ResourceCost ReadCost(JsonObject card)
    {
        if (card["cost"] is JsonObject costObject)
        {
            return new ResourceCost(
                Math.Max(0, costObject["energy"]?.GetValue<int>() ?? 0),
                ReadPowerCost(costObject["power"]),
                Math.Max(0, costObject["universalPower"]?.GetValue<int>() ?? 0));
        }

        return new ResourceCost(Math.Max(0, card["cost"]?.GetValue<int>() ?? 0), new Dictionary<Domain, int>(), 0);
    }

    private static Dictionary<Domain, int> ReadPowerCost(JsonNode? node)
    {
        var result = new Dictionary<Domain, int>();
        if (node is JsonObject powerObject)
        {
            foreach (var (key, value) in powerObject)
            {
                if (TryReadDomain(key, out var domain))
                {
                    result[domain] = Math.Max(0, value?.GetValue<int>() ?? 0);
                }
            }
        }
        else if (node is JsonArray powerArray)
        {
            foreach (var item in powerArray)
            {
                if (TryReadDomain(item?.GetValue<string>(), out var domain))
                {
                    result[domain] = result.GetValueOrDefault(domain) + 1;
                }
            }
        }

        return result;
    }

    private static JsonObject RunePool(JsonObject player)
    {
        if (player["runePool"] is not JsonObject pool)
        {
            pool = EmptyRunePool();
            player["runePool"] = pool;
        }

        pool["energy"] ??= 0;
        pool["universalPower"] ??= 0;
        pool["power"] ??= new JsonObject();
        return pool;
    }

    private static Dictionary<Domain, int> ReadPowerPool(JsonObject pool)
    {
        var power = new Dictionary<Domain, int>();
        if (pool["power"] is JsonObject powerObject)
        {
            foreach (var (key, value) in powerObject)
            {
                if (TryReadDomain(key, out var domain))
                {
                    power[domain] = Math.Max(0, value?.GetValue<int>() ?? 0);
                }
            }
        }

        return power;
    }

    private static IReadOnlyList<RuneResource> BoardRunes(JsonObject player)
    {
        return player["runes"]!["ready"]!.AsArray()
            .Concat(player["runes"]!["exhausted"]!.AsArray())
            .Select(node => node!.AsObject())
            .Select(rune => new RuneResource(
                rune["id"]?.GetValue<string>() ?? string.Empty,
                TryReadDomain(rune["domain"]?.GetValue<string>(), out var domain) ? domain : Domain.Fury))
            .Where(rune => !string.IsNullOrWhiteSpace(rune.Id))
            .ToArray();
    }

    private static JsonObject EmptyRunePool() => new()
    {
        ["energy"] = 0,
        ["power"] = new JsonObject(),
        ["universalPower"] = 0
    };

    private static bool TryReadDomain(string? value, out Domain domain) =>
        Enum.TryParse(value, true, out domain);

    private static IReadOnlyList<Domain> DomainOrder() =>
        [Domain.Fury, Domain.Calm, Domain.Mind, Domain.Body, Domain.Chaos, Domain.Order];

    private static JsonObject ApplyDamage(JsonObject state, int playerId, int amount, string? targetUnitId, string? targetLaneId)
    {
        JsonObject? target = null;
        if (!string.IsNullOrWhiteSpace(targetLaneId))
        {
            target = FindBattlefield(state, targetLaneId)
                ?["units"]!.AsArray()
                .Select(node => node!.AsObject())
                .FirstOrDefault(unit => unit["ownerId"]?.GetValue<int>() != playerId);
        }
        else if (!string.IsNullOrWhiteSpace(targetUnitId))
        {
            var unit = FindUnit(state, targetUnitId);
            if (unit?["ownerId"]?.GetValue<int>() != playerId)
            {
                target = unit;
            }
        }

        if (target is not null)
        {
            target["damage"] = (target["damage"]?.GetValue<int>() ?? 0) + amount;
        }

        return state;
    }

    private static JsonObject ApplyUnitMod(JsonObject state, string? targetUnitId, Func<JsonObject, JsonObject> modify)
    {
        if (string.IsNullOrWhiteSpace(targetUnitId))
        {
            return state;
        }

        var unit = FindUnit(state, targetUnitId);
        if (unit is not null)
        {
            modify(unit);
        }

        return state;
    }

    private static JsonObject? FindPlayer(JsonObject state, int playerId)
    {
        return state["players"]!.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(candidate => candidate["id"]?.GetValue<int>() == playerId);
    }

    private static JsonObject? FindUnit(JsonObject state, string unitId)
    {
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            var unit = player["base"]!.AsArray()
                .Select(node => node!.AsObject())
                .FirstOrDefault(candidate => candidate["uid"]?.GetValue<string>() == unitId);
            if (unit is not null)
            {
                return unit;
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var unit = battlefield["units"]!.AsArray()
                .Select(node => node!.AsObject())
                .FirstOrDefault(candidate => candidate["uid"]?.GetValue<string>() == unitId);
            if (unit is not null)
            {
                return unit;
            }
        }

        return null;
    }

    private static int NextPlayerId(JsonObject state, int playerId)
    {
        var order = state["turnOrder"]!.Deserialize<int[]>(JsonOptions) ?? [];
        if (order.Length == 0)
        {
            return playerId;
        }

        var index = Array.IndexOf(order, playerId);
        return order[(index < 0 ? 0 : index + 1) % order.Length];
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

    private static JsonObject ScorePointPayloadSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["battlefieldId"] = new JsonObject { ["type"] = "string" },
                ["source"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("conquer", "hold") }
            }
        };
    }

    private static string FirstBattlefieldId(JsonObject state)
    {
        return state["battlefields"]!.AsArray()
            .Select(node => node!["id"]?.GetValue<string>())
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)) ?? string.Empty;
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
                ["cost"] = CostNode(definition.Cost, definition.PowerCost),
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

    private static JsonNode CostNode(int energy, IReadOnlyList<Domain> powerCost)
    {
        if (powerCost.Count == 0)
        {
            return JsonValue.Create(energy)!;
        }

        var power = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var domain in powerCost)
        {
            var key = domain.ToString();
            power[key] = power.GetValueOrDefault(key) + 1;
        }

        return new JsonObject
        {
            ["energy"] = Math.Max(0, energy),
            ["power"] = ToObject(power),
            ["universalPower"] = 0
        };
    }

    private static JsonObject ToObject(IReadOnlyDictionary<string, int> values)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in values)
        {
            obj[key] = value;
        }

        return obj;
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

    private static string? ReadString(IReadOnlyDictionary<string, object?>? payload, string key)
    {
        if (payload is null || !payload.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => null
        };
    }

    private static ScoreSource ScoreSourceFrom(string? source)
    {
        return string.Equals(source, "hold", StringComparison.OrdinalIgnoreCase) ? ScoreSource.Hold : ScoreSource.Conquer;
    }

    private static string ScoreSourceValue(ScoreSource source)
    {
        return source == ScoreSource.Hold ? "hold" : "conquer";
    }

    private enum ScoreSource
    {
        Conquer,
        Hold
    }

    private sealed record ScoreRequest(int PlayerId, string BattlefieldId, ScoreSource Source);

    private sealed record ScoreOutcome(int PlayerId, string BattlefieldId, ScoreSource Source, int PointsAwarded, string? SkippedReason);

    private sealed record ResourceCost(int Energy, IReadOnlyDictionary<Domain, int> Power, int UniversalPower);

    private sealed record PaymentPlan(
        int ReadyRunesToExhaust,
        int RemainingEnergy,
        int RemainingUniversalPower,
        IReadOnlyDictionary<Domain, int> RemainingPower,
        IReadOnlyList<string> RecycledRuneIds);

    private sealed record RuneResource(string Id, Domain Domain);

    private static class ScoreRules
    {
        public static int AwardedPoints(JsonObject state, ScoreRequest request)
        {
            _ = state;
            _ = request;
            return 1;
        }

        public static int VictoryScore(JsonObject state)
        {
            return state["victoryScore"]?.GetValue<int>() ?? 8;
        }
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
