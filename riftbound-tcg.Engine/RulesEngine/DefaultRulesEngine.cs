using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.Effects;

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
                    ["banished"] = new JsonArray(),
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
            ["units"] = new JsonArray(),
            ["hiddenCards"] = new JsonArray()
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
            ["pendingTriggeredAbilities"] = new JsonArray(),
            ["delayedAbilities"] = new JsonArray(),
            ["abilityEvents"] = new JsonArray(),
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

        if (stage == "mulligan" && CurrentMulliganPlayerId(state.State, mulliganConfirmedPlayerIds) == playerId)
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
                actions.Add(new("create-token", "create-token", "Create token", playerId));
                actions.Add(new("attach-card", "attach-card", "Attach card", playerId));
                actions.Add(new("detach-card", "detach-card", "Detach card", playerId));
                actions.Add(new("banish-object", "banish-object", "Banish object", playerId));
                actions.Add(new("set-facedown", "set-facedown", "Set facedown", playerId));
                actions.AddRange(PlayableCardsFromHand(state.State, playerId, IsSpellOrGear));
                actions.AddRange(ActivatedAbilityActions(state.State, playerId));
                actions.AddRange(HideCardsFromHand(state.State, playerId));

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
                actions.AddRange(ActivatedAbilityActions(state.State, playerId));
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
                var playUnitResult = PlayUnit(nextState, action.PlayerId, ReadInt(action.Payload, "handIndex"), ReadString(action.Payload, "battlefieldId"), ReadBool(action.Payload, "accelerate"));
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
                var moveUnitResult = MoveUnits(
                    nextState,
                    action.PlayerId,
                    ReadUnitIds(action.Payload),
                    ReadString(action.Payload, "battlefieldId"),
                    ReadString(action.Payload, "destination"));
                if (moveUnitResult is null)
                {
                    return Reject(state, "Invalid move-unit action: choose one or more of your own unexhausted units and a legal shared destination.");
                }

                nextState = moveUnitResult;
                break;
            case "create-token":
                var createTokenResult = CreateToken(nextState, action.PlayerId, ReadString(action.Payload, "cardId"), ReadString(action.Payload, "name"), ReadString(action.Payload, "battlefieldId"));
                if (createTokenResult is null)
                {
                    return Reject(state, "Invalid create-token action: target base or battlefield must exist.");
                }

                nextState = createTokenResult;
                break;
            case "attach-card":
                var attachCardResult = AttachCard(nextState, action.PlayerId, ReadInt(action.Payload, "handIndex"), ReadString(action.Payload, "targetUnitId"));
                if (attachCardResult is null)
                {
                    return Reject(state, "Invalid attach-card action: choose one of your cards in hand and an existing unit.");
                }

                nextState = attachCardResult;
                break;
            case "detach-card":
                var detachCardResult = DetachCard(nextState, action.PlayerId, ReadString(action.Payload, "attachedCardUid"));
                if (detachCardResult is null)
                {
                    return Reject(state, "Invalid detach-card action: only attached cards you own can be detached.");
                }

                nextState = detachCardResult;
                break;
            case "banish-object":
                var banishObjectResult = BanishObject(nextState, action.PlayerId, ReadString(action.Payload, "objectUid"));
                if (banishObjectResult is null)
                {
                    return Reject(state, "Invalid banish-object action: object must exist and be controlled by you.");
                }

                nextState = banishObjectResult;
                break;
            case "set-facedown":
                var setFaceDownResult = SetFaceDown(nextState, action.PlayerId, ReadString(action.Payload, "objectUid"), ReadBool(action.Payload, "faceDown") ?? true);
                if (setFaceDownResult is null)
                {
                    return Reject(state, "Invalid set-facedown action: object must exist and be controlled by you.");
                }

                nextState = setFaceDownResult;
                break;
            case "play-card":
                var playCardResult = PlayCard(nextState, action.PlayerId, ReadInt(action.Payload, "handIndex"), ReadString(action.Payload, "targetUnitId"), ReadString(action.Payload, "targetLaneId"));
                if (playCardResult is null)
                {
                    return Reject(state, "Invalid play-card action: card timing, ownership, cost, or targets are not legal.");
                }

                nextState = playCardResult;
                break;
            case "activate-ability":
                var activateAbilityResult = ActivateAbility(
                    nextState,
                    action.PlayerId,
                    ReadString(action.Payload, "sourceUid"),
                    ReadString(action.Payload, "abilityId"),
                    ReadString(action.Payload, "modeId"),
                    ReadString(action.Payload, "targetUnitId"),
                    ReadString(action.Payload, "targetLaneId"));
                if (activateAbilityResult is null)
                {
                    return Reject(state, "Invalid activate-ability action: source, ability, mode, cost, or targets are not legal.");
                }

                nextState = activateAbilityResult;
                break;
            case "hide-card":
                var hideCardResult = HideCard(nextState, action.PlayerId, ReadInt(action.Payload, "handIndex"), ReadString(action.Payload, "battlefieldId"));
                if (hideCardResult is null)
                {
                    return Reject(state, "Invalid hide-card action: only cards with Hidden can be hidden at a battlefield you control with no hidden card there.");
                }

                nextState = hideCardResult;
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
                nextState = Concede(nextState, action.PlayerId);
                break;
            default:
                return Reject(state, $"Action '{action.ActionType}' is not supported.");
        }

        if (!string.Equals(action.ActionType, "resolve-combat", StringComparison.OrdinalIgnoreCase))
        {
            nextState = CollectTriggeredAbilities(nextState, "action-applied", new JsonObject
            {
                ["playerId"] = action.PlayerId,
                ["actionType"] = action.ActionType
            });
            nextState = RunFeprUntilChoiceRequired(nextState);
        }
        var resultPayload = BuildResultPayload(nextState);
        nextState.Remove("__scoreOutcomes");
        var next = ToEngineState(state.MatchId, state.Mode, state.SequenceNumber + 1, nextState, ActiveSeats(nextState, state.Players));
        return new EngineActionResult(true, "accepted", $"Accepted {action.ActionType}.", next, GetLegalActions(next, action.PlayerId), resultPayload);
    }

    private static JsonObject Concede(JsonObject state, int playerId)
    {
        var concedingPlayerName = PlayerName(state, playerId);
        var mode = state["mode"]?.GetValue<string>() ?? "duel-1v1";
        var activePlayerIds = ActivePlayerIds(state);

        if (mode == "teams-2v2")
        {
            var teamIds = state["teamIds"]?.Deserialize<int[]>(JsonOptions) ?? [];
            var losingTeamId = TeamIdForPlayer(teamIds, playerId);
            var winningTeamId = activePlayerIds
                .Where(id => TeamIdForPlayer(teamIds, id) != losingTeamId)
                .Select(id => TeamIdForPlayer(teamIds, id))
                .FirstOrDefault();
            var winningPlayerId = activePlayerIds.FirstOrDefault(id => TeamIdForPlayer(teamIds, id) == winningTeamId);

            foreach (var removedPlayerId in activePlayerIds.Where(id => TeamIdForPlayer(teamIds, id) == losingTeamId).ToArray())
            {
                state = RemovePlayerFromContinuingGame(state, removedPlayerId);
            }

            state["stage"] = "game-over";
            state["winner"] = winningPlayerId;
            state["winningTeamId"] = winningTeamId;
            return AddLog(state, $"{concedingPlayerName} conceded. Team {losingTeamId + 1} loses the match.");
        }

        var remainingAfterConcede = activePlayerIds.Where(id => id != playerId).ToArray();
        if (mode == "duel-1v1" || remainingAfterConcede.Length == 1)
        {
            var winner = remainingAfterConcede.FirstOrDefault();
            state = RemovePlayerFromContinuingGame(state, playerId);
            state["stage"] = "game-over";
            state["winner"] = winner;
            state["winningTeamId"] = null;
            return AddLog(state, $"{concedingPlayerName} conceded.");
        }

        state = RemovePlayerFromContinuingGame(state, playerId);
        return AddLog(state, $"{concedingPlayerName} conceded and was removed from the game.");
    }

    private static JsonObject RemovePlayerFromContinuingGame(JsonObject state, int playerId)
    {
        var oldOrder = state["turnOrder"]!.Deserialize<int[]>(JsonOptions) ?? [];
        var nextAfterRemoved = NextAvailablePlayerId(oldOrder, playerId, id => id != playerId && FindPlayer(state, id) is not null);

        RemovePlayerObjects(state, playerId);
        RemovePlayerState(state, playerId);

        var activePlayerIds = ActivePlayerIds(state);
        state["turnOrder"] = ToArray(oldOrder.Where(activePlayerIds.Contains));
        if (activePlayerIds.Length == 0)
        {
            state["turnPlayerId"] = null;
            state["activePlayer"] = null;
            state["priorityPlayerId"] = null;
            state["focusPlayerId"] = null;
            return state;
        }

        nextAfterRemoved = nextAfterRemoved is not null && activePlayerIds.Contains(nextAfterRemoved.Value)
            ? nextAfterRemoved
            : activePlayerIds[0];

        if (state["firstPlayerId"]?.GetValue<int?>() == playerId)
        {
            state["firstPlayerId"] = activePlayerIds[0];
        }

        if (state["turnPlayerId"]?.GetValue<int?>() == playerId)
        {
            state["turnPlayerId"] = nextAfterRemoved;
            state["turnPhase"] = "awaken";
            state["scoredBattlefieldIdsThisTurn"] = new JsonObject();
        }

        if (state["activePlayer"]?.GetValue<int?>() == playerId)
        {
            state["activePlayer"] = state["turnPlayerId"]?.GetValue<int?>() ?? nextAfterRemoved;
        }

        ReassignPriorityAndFocus(state, playerId, activePlayerIds);
        NormalizeMulliganAfterPlayerRemoval(state, activePlayerIds);
        return state;
    }

    private static void RemovePlayerObjects(JsonObject state, int playerId)
    {
        var removedBattlefieldId = FindPlayer(state, playerId)?["battlefieldId"]?.GetValue<string>();
        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var units = battlefield["units"]!.AsArray();
            for (var i = units.Count - 1; i >= 0; i--)
            {
                if (units[i]!["ownerId"]?.GetValue<int?>() == playerId)
                {
                    units.RemoveAt(i);
                }
            }

            if (battlefield["controllerId"]?.GetValue<int?>() == playerId)
            {
                battlefield["controllerId"] = null;
            }

            if (battlefield["contestedByPlayerId"]?.GetValue<int?>() == playerId)
            {
                battlefield["contestedByPlayerId"] = null;
                battlefield["stagedShowdown"] = false;
                battlefield["stagedCombat"] = false;
            }

            var chosenBy = battlefield["chosenBy"]?.GetValue<int?>();
            var catalogId = battlefield["catalogId"]?.GetValue<string>();
            if (chosenBy == playerId || !string.IsNullOrWhiteSpace(removedBattlefieldId) && string.Equals(catalogId, removedBattlefieldId, StringComparison.OrdinalIgnoreCase))
            {
                battlefield["catalogId"] = "token-battlefield";
                battlefield["name"] = "Token Battlefield";
                battlefield["claim"] = 2;
                battlefield["chosenBy"] = null;
            }
        }

        var stack = state["effectStack"]!.AsArray();
        for (var i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i]!["playerId"]?.GetValue<int?>() == playerId)
            {
                stack.RemoveAt(i);
            }
        }

        if (state["activeCombat"] is JsonObject activeCombat
            && (activeCombat["attackerPlayerId"]?.GetValue<int?>() == playerId || activeCombat["defenderPlayerId"]?.GetValue<int?>() == playerId))
        {
            state["activeCombat"] = null;
            state["activeShowdown"] = null;
            state["focusPlayerId"] = null;
            state["priorityPlayerId"] = null;
            state["hasPassedFocusByPlayer"] = new JsonObject();
        }

        if (PendingBurnOut(state)?["playerId"]?.GetValue<int?>() == playerId)
        {
            state["pendingBurnOut"] = null;
        }
    }

    private static void RemovePlayerState(JsonObject state, int playerId)
    {
        var players = state["players"]!.AsArray();
        for (var i = players.Count - 1; i >= 0; i--)
        {
            if (players[i]!["id"]?.GetValue<int?>() == playerId)
            {
                players.RemoveAt(i);
            }
        }

        RemoveObjectProperty(state["hasPassedFocusByPlayer"], playerId);
        RemoveObjectProperty(state["scoredBattlefieldIdsThisTurn"], playerId);
        RemoveObjectProperty(state["firstTurnCompletedByPlayer"], playerId);
        RemoveArrayValue(state["mulliganConfirmedPlayerIds"], playerId);
        RemoveObjectProperty(state["chainWindow"]?["passedByPlayer"], playerId);
    }

    private static void ReassignPriorityAndFocus(JsonObject state, int removedPlayerId, int[] activePlayerIds)
    {
        if (state["chainWindow"] is JsonObject chainWindow)
        {
            var currentPriority = chainWindow["priorityPlayerId"]?.GetValue<int?>()
                ?? state["priorityPlayerId"]?.GetValue<int?>();
            if (currentPriority == removedPlayerId || currentPriority is null || !activePlayerIds.Contains(currentPriority.Value))
            {
                var nextPriority = NextPlayerId(state, removedPlayerId);
                chainWindow["priorityPlayerId"] = nextPriority;
                state["priorityPlayerId"] = nextPriority;
                state["activePlayer"] = nextPriority;
            }
        }

        var focusPlayerId = state["focusPlayerId"]?.GetValue<int?>();
        if (state["activeShowdown"] is not null && (focusPlayerId == removedPlayerId || focusPlayerId is null || !activePlayerIds.Contains(focusPlayerId.Value)))
        {
            var nextFocus = NextPlayerId(state, removedPlayerId);
            state["focusPlayerId"] = nextFocus;
            state["priorityPlayerId"] = nextFocus;
            state["activePlayer"] = nextFocus;
        }
    }

    private static void NormalizeMulliganAfterPlayerRemoval(JsonObject state, int[] activePlayerIds)
    {
        if (state["stage"]?.GetValue<string>() != "mulligan")
        {
            return;
        }

        var confirmed = state["mulliganConfirmedPlayerIds"]?.Deserialize<int[]>(JsonOptions) ?? [];
        if (activePlayerIds.Length > 0 && activePlayerIds.All(confirmed.Contains))
        {
            state["stage"] = "playing";
            state["activePlayer"] = state["turnPlayerId"]?.GetValue<int?>() ?? activePlayerIds[0];
        }
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
            var selected = handIndexes.Distinct().Where(i => i >= 0 && i < hand.Count).Order().ToArray();
            var redrawCount = selected.Length;
            var returned = selected.Select(handIndex => hand[handIndex]?.DeepClone()).ToList();
            foreach (var handIndex in selected.OrderDescending())
            {
                hand.RemoveAt(handIndex);
            }

            for (var i = 0; i < redrawCount; i++)
            {
                if (deck.Count == 0) break;
                hand.Add(deck[0]?.DeepClone());
                deck.RemoveAt(0);
            }

            RecycleCardsToMainDeck(state, player, returned);

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

    private static int? CurrentMulliganPlayerId(JsonObject state, IReadOnlyCollection<int> confirmedPlayerIds)
    {
        var order = state["turnOrder"]?.Deserialize<int[]>(JsonOptions) ?? [];
        foreach (var playerId in order)
        {
            if (!confirmedPlayerIds.Contains(playerId))
            {
                return playerId;
            }
        }

        return null;
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
            state = KillTemporaryPermanents(state, playerId);

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
            if (!ShouldSkipFirstDraw(state, playerId))
            {
                state = DrawCards(state, playerId, 1);
            }
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

    private static bool ShouldSkipFirstDraw(JsonObject state, int playerId)
    {
        var mode = state["mode"]?.GetValue<string>() ?? "";
        if (mode is not ("ffa-3" or "ffa-4" or "teams-2v2"))
        {
            return false;
        }

        var firstPlayerId = state["firstPlayerId"]?.GetValue<int>() ?? 0;
        var firstTurnCompleted = state["firstTurnCompletedByPlayer"]?[playerId.ToString()]?.GetValue<bool>() ?? false;
        return playerId == firstPlayerId && !firstTurnCompleted;
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

        var award = ApplyReplacementAbilities(state, "score-point", new JsonObject
        {
            ["playerId"] = request.PlayerId,
            ["battlefieldId"] = request.BattlefieldId,
            ["source"] = ScoreSourceValue(request.Source),
            ["amount"] = ScoreRules.AwardedPoints(state, request)
        });
        state = award.State;
        if (award.Prevented)
        {
            return AppendScoreOutcome(state, new ScoreOutcome(request.PlayerId, request.BattlefieldId, request.Source, 0, "replaced"));
        }

        var awardedPoints = award.Amount;
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
        state = CollectTriggeredAbilities(state, "score-point", new JsonObject
        {
            ["playerId"] = request.PlayerId,
            ["battlefieldId"] = request.BattlefieldId,
            ["source"] = ScoreSourceValue(request.Source),
            ["amount"] = awardedPoints
        });
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
            var draw = ApplyReplacementAbilities(state, "draw-cards", new JsonObject
            {
                ["playerId"] = playerId,
                ["amount"] = amount
            });
            state = draw.State;
            if (draw.Prevented)
            {
                return state;
            }

            DrawAvailable(player, draw.Amount);
            state = CollectTriggeredAbilities(state, "cards-drawn", new JsonObject
            {
                ["playerId"] = playerId,
                ["amount"] = draw.Amount
            });
            return state;
        }

        var drawn = deck.Count;
        DrawAvailable(player, drawn);
        if (drawn > 0)
        {
            state = CollectTriggeredAbilities(state, "cards-drawn", new JsonObject
            {
                ["playerId"] = playerId,
                ["amount"] = drawn
            });
        }
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
        RecycleCardsToMainDeck(state, player, recycled);
    }

    private static void RecycleCardsToMainDeck(JsonObject state, JsonObject player, IList<JsonNode?> recycled)
    {
        if (recycled.Count == 0)
        {
            return;
        }

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

    private static bool AreTeammates(JsonObject state, int firstPlayerId, int secondPlayerId)
    {
        if (firstPlayerId == secondPlayerId)
        {
            return true;
        }

        var teamIds = state["teamIds"]?.Deserialize<int[]>(JsonOptions) ?? [];
        return firstPlayerId >= 0 &&
            firstPlayerId < teamIds.Length &&
            secondPlayerId >= 0 &&
            secondPlayerId < teamIds.Length &&
            teamIds[firstPlayerId] == teamIds[secondPlayerId];
    }

    private static int PlayerCount(JsonObject state)
    {
        return state["players"]!.AsArray().Count;
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
        unit["controllerId"] = playerId;
        unit["location"] = new JsonObject { ["type"] = "base", ["battlefieldId"] = null, ["attachedToUid"] = null };
        unit["exhausted"] = true;
        unit["damage"] = 0;
        unit["attachedMight"] = 0;
        unit["attacker"] = false;
        unit["defender"] = false;
        unit["isToken"] = false;
        unit["isFaceDown"] = false;
        unit["rulesTextActive"] = true;
        unit["attachedCards"] = new JsonArray();
        unit["topCardId"] = unit["id"]?.GetValue<string>() ?? champion["id"]?.GetValue<string>() ?? string.Empty;

        state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;

        state = UpdatePlayer(state, playerId, p =>
        {
            PayCost(p, cost);
            p["championSummoned"] = true;
            p["base"]!.AsArray().Add(unit);
            return p;
        });

        state = AddLog(state, $"{PlayerName(state, playerId)} summoned {champion["name"]?.GetValue<string>() ?? "their champion"} to their base.");
        return CollectTriggeredAbilities(state, "unit-entered", new JsonObject
        {
            ["playerId"] = playerId,
            ["sourceUid"] = unit["uid"]?.GetValue<string>(),
            ["zone"] = "base"
        });
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

        var targetSelection = ValidateTargetSelection(state, playerId, card, targetUnitId, targetLaneId);
        if (!targetSelection.IsValid)
        {
            return null;
        }

        var cost = ReadCost(card);
        var additionalEnergyCost = AdditionalTargetingCost(state, playerId, card, targetUnitId, targetLaneId);
        if (additionalEnergyCost > 0)
        {
            cost = cost with { Energy = cost.Energy + additionalEnergyCost };
        }
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

        return FinalizePendingCardPlay(state, playerId, card, targetSelection);
    }

    private static JsonObject FinalizePendingCardPlay(JsonObject state, int playerId, JsonObject card, TargetSelection targetSelection)
    {
        var kind = card["kind"]?.GetValue<string>() ?? string.Empty;
        if (kind == "gear")
        {
            state = PutResolvedGearIntoBase(state, playerId, card);
            if (state["effectStack"]!.AsArray().Count > 0)
            {
                OpenChainWindow(state, playerId, playerId, ChainItemSourceValue(ChainItemSource.AddCreated));
            }
            else
            {
                CloseChainWindow(state, playerId, passesFocusOnClose: false);
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
            ["targetUnitId"] = targetSelection.LegacyTargetUnitId,
            ["targetLaneId"] = targetSelection.LegacyTargetLaneId,
            ["targets"] = ToArray(targetSelection.Targets.Select(TargetToJson)),
            ["status"] = ChainItemStatusValue(ChainItemStatus.Pending),
            ["source"] = ChainItemSourceValue(ChainItemSource.PlayedCard)
        };
        state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;
        state["effectStack"]!.AsArray().Insert(0, stackItem);
        state = CollectTriggeredAbilities(state, "card-played", new JsonObject
        {
            ["playerId"] = playerId,
            ["cardId"] = stackItem["cardId"]?.GetValue<string>(),
            ["kind"] = kind
        });
        OpenChainWindow(state, playerId, playerId, ChainItemSourceValue(ChainItemSource.PlayedCard));
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
                var nextSource = stack[0]!["source"]?.GetValue<string>() ?? ChainItemSourceValue(ChainItemSource.PlayedCard);
                OpenChainWindow(state, nextPriority, nextPriority, nextSource);
            }
            else
            {
                var startedBy = chainWindow["startedByPlayerId"]?.GetValue<int>() ?? playerId;
                var passesFocusOnClose = chainWindow["passesFocusOnClose"]?.GetValue<bool>() ?? true;
                CloseChainWindow(state, startedBy, passesFocusOnClose);
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

    private static void OpenChainWindow(JsonObject state, int priorityPlayerId, int startedByPlayerId, string source)
    {
        state["chainWindow"] = new JsonObject
        {
            ["priorityPlayerId"] = priorityPlayerId,
            ["startedByPlayerId"] = startedByPlayerId,
            ["source"] = source,
            ["passesFocusOnClose"] = source == ChainItemSourceValue(ChainItemSource.PlayedCard),
            ["passedByPlayer"] = new JsonObject()
        };
        state["priorityPlayerId"] = priorityPlayerId;
        state["activePlayer"] = priorityPlayerId;
    }

    private static void CloseChainWindow(JsonObject state, int startedByPlayerId, bool passesFocusOnClose)
    {
        state["chainWindow"] = null;
        state["priorityPlayerId"] = null;
        if (passesFocusOnClose && state["activeShowdown"] is not null && !CombatDamageRequired(state))
        {
            if (state["activeShowdown"]?["kind"]?.GetValue<string>() == "combat" && state["activeCombat"] is JsonObject activeCombat)
            {
                var attackerPlayerId = activeCombat["attackerPlayerId"]?.GetValue<int>() ?? startedByPlayerId;
                state["focusPlayerId"] = attackerPlayerId;
                state["activePlayer"] = attackerPlayerId;
                state["hasPassedFocusByPlayer"] = new JsonObject();
                return;
            }

            var nextFocus = NextPlayerId(state, startedByPlayerId);
            state["focusPlayerId"] = nextFocus;
            state["activePlayer"] = nextFocus;
            state["hasPassedFocusByPlayer"] = new JsonObject();
        }
        else if (state["activeShowdown"] is not null && !CombatDamageRequired(state))
        {
            var focusPlayerId = state["focusPlayerId"]?.GetValue<int?>()
                ?? state["activePlayer"]?.GetValue<int?>()
                ?? state["turnPlayerId"]?.GetValue<int>()
                ?? 0;
            state["focusPlayerId"] = focusPlayerId;
            state["activePlayer"] = focusPlayerId;
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
        item["status"] = ChainItemStatusValue(ChainItemStatus.Finalized);
        stack.RemoveAt(0);

        var playerId = item["playerId"]?.GetValue<int>() ?? 0;
        var effect = item["effect"]?.AsObject();
        var effectType = effect?["type"]?.GetValue<string>() ?? "rally";
        var amount = effect?["amount"]?.GetValue<int>() ?? 0;
        var targetUnitId = item["targetUnitId"]?.GetValue<string>();
        var targetLaneId = item["targetLaneId"]?.GetValue<string>();
        var targets = ReadStackTargets(item, targetUnitId, targetLaneId);

        state = effectType switch
        {
            "draw" => DrawCards(state, playerId, amount),
            "damage" => ApplyDamage(state, playerId, amount, targets),
            "buff" => ApplyUnitMod(state, playerId, targets, unit => { unit["attachedMight"] = (unit["attachedMight"]?.GetValue<int>() ?? 0) + amount; return unit; }),
            "rally" => ApplyUnitMod(state, playerId, targets, unit => { unit["exhausted"] = false; return unit; }),
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
        gear["controllerId"] = playerId;
        gear["location"] = new JsonObject { ["type"] = "base", ["battlefieldId"] = null, ["attachedToUid"] = null };
        gear["exhausted"] = false;
        gear["attachedUnitId"] = null;
        gear["isToken"] = false;
        gear["isFaceDown"] = false;
        gear["rulesTextActive"] = true;
        gear["attachedCards"] = new JsonArray();
        gear["topCardId"] = gear["id"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty;
        state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;

        return UpdatePlayer(state, playerId, player =>
        {
            player["baseGear"]!.AsArray().Add(gear);
            return player;
        });
    }

    private static JsonObject? HideCard(JsonObject state, int playerId, int? handIndex, string? battlefieldId)
    {
        if (handIndex is null || string.IsNullOrWhiteSpace(battlefieldId))
        {
            return null;
        }

        var player = FindPlayer(state, playerId);
        var battlefield = FindBattlefield(state, battlefieldId);
        if (player is null || battlefield is null || battlefield["controllerId"]?.GetValue<int>() != playerId)
        {
            return null;
        }

        var hiddenCards = battlefield["hiddenCards"] as JsonArray ?? new JsonArray();
        battlefield["hiddenCards"] = hiddenCards;
        if (hiddenCards.Any(node => node?["ownerId"]?.GetValue<int>() == playerId))
        {
            return null;
        }

        var hand = player["hand"]!.AsArray();
        if (handIndex.Value < 0 || handIndex.Value >= hand.Count)
        {
            return null;
        }

        var card = hand[handIndex.Value]!.AsObject();
        if (!HasKeyword(card, KeywordKind.Hidden) || !CanPay(player, 1))
        {
            return null;
        }

        state = UpdatePlayer(state, playerId, p =>
        {
            p["hand"]!.AsArray().RemoveAt(handIndex.Value);
            PayCost(p, 1);
            return p;
        });

        var hiddenCard = Clone(card);
        hiddenCard["uid"] = $"hidden-{state["nextUid"]?.GetValue<int>() ?? 1}";
        hiddenCard["ownerId"] = playerId;
        hiddenCard["hiddenAtBattlefieldId"] = battlefieldId;
        hiddenCard["hiddenTurnNumber"] = state["turnNumber"]?.GetValue<int>() ?? 1;
        hiddenCard["facedown"] = true;
        state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;
        hiddenCards.Add(hiddenCard);

        return AddLog(state, $"{PlayerName(state, playerId)} hid a card at {battlefield["name"]?.GetValue<string>() ?? "a battlefield"}.");
    }

    private static JsonObject? PlayUnit(JsonObject state, int playerId, int? handIndex, string? battlefieldId, bool accelerate)
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
        if (accelerate && !HasKeyword(card, KeywordKind.Accelerate))
        {
            return null;
        }

        var cost = ReadCost(card);
        if (accelerate)
        {
            cost = cost with { Energy = cost.Energy + 2 };
        }

        if (!CanPay(player, cost))
        {
            return null;
        }

        var unit = Clone(card);
        unit["uid"] = $"unit-{state["nextUid"]?.GetValue<int>() ?? 1}";
        unit["ownerId"] = playerId;
        unit["controllerId"] = playerId;
        unit["location"] = new JsonObject { ["type"] = battlefield is null ? "base" : "battlefield", ["battlefieldId"] = battlefield?["id"]?.GetValue<string>(), ["attachedToUid"] = null };
        unit["exhausted"] = !accelerate;
        unit["damage"] = 0;
        unit["attachedMight"] = 0;
        unit["attacker"] = false;
        unit["defender"] = false;
        unit["isToken"] = false;
        unit["isFaceDown"] = false;
        unit["rulesTextActive"] = true;
        unit["attachedCards"] = new JsonArray();
        unit["topCardId"] = unit["id"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty;

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
        state = AddLog(state, $"{PlayerName(state, playerId)} played {card["name"]?.GetValue<string>() ?? "a unit"} to {destinationLabel}.");
        return CollectTriggeredAbilities(state, "unit-entered", new JsonObject
        {
            ["playerId"] = playerId,
            ["sourceUid"] = unit["uid"]?.GetValue<string>(),
            ["zone"] = battlefield is null ? "base" : "battlefield",
            ["battlefieldId"] = battlefield?["id"]?.GetValue<string>()
        });
    }

    private static JsonObject? CreateToken(JsonObject state, int playerId, string? cardId, string? name, string? battlefieldId)
    {
        if (FindPlayer(state, playerId) is null)
        {
            return null;
        }

        JsonObject? battlefield = null;
        if (!string.IsNullOrWhiteSpace(battlefieldId))
        {
            battlefield = FindBattlefield(state, battlefieldId);
            if (battlefield is null)
            {
                return null;
            }
        }

        var nextUid = state["nextUid"]?.GetValue<int>() ?? 1;
        var tokenId = string.IsNullOrWhiteSpace(cardId) ? "token-unit" : cardId;
        var token = new JsonObject
        {
            ["id"] = $"{tokenId}-token-{nextUid}",
            ["catalogId"] = tokenId,
            ["name"] = string.IsNullOrWhiteSpace(name) ? DisplayName(tokenId) : name,
            ["kind"] = "unit",
            ["tags"] = new JsonArray("token"),
            ["domain"] = "Fury",
            ["domains"] = new JsonArray("Fury"),
            ["cost"] = 0,
            ["might"] = 1,
            ["text"] = string.Empty,
            ["image"] = "*",
            ["cardType"] = "Unit",
            ["supertype"] = "Token",
            ["effect"] = new JsonObject { ["type"] = "rally", ["amount"] = 0 },
            ["uid"] = $"token-{nextUid}",
            ["ownerId"] = playerId,
            ["controllerId"] = playerId,
            ["location"] = new JsonObject { ["type"] = battlefield is null ? "base" : "battlefield", ["battlefieldId"] = battlefield?["id"]?.GetValue<string>(), ["attachedToUid"] = null },
            ["exhausted"] = false,
            ["damage"] = 0,
            ["attachedMight"] = 0,
            ["attacker"] = false,
            ["defender"] = false,
            ["isToken"] = true,
            ["isFaceDown"] = false,
            ["rulesTextActive"] = true,
            ["attachedCards"] = new JsonArray(),
            ["topCardId"] = null
        };
        state["nextUid"] = nextUid + 1;

        if (battlefield is null)
        {
            state = UpdatePlayer(state, playerId, player =>
            {
                player["base"]!.AsArray().Add(token);
                return player;
            });
        }
        else
        {
            battlefield["units"]!.AsArray().Add(token);
        }

        return AddLog(state, $"{PlayerName(state, playerId)} created a token.");
    }

    private static JsonObject? AttachCard(JsonObject state, int playerId, int? handIndex, string? targetUnitId)
    {
        if (handIndex is null || string.IsNullOrWhiteSpace(targetUnitId))
        {
            return null;
        }

        var player = FindPlayer(state, playerId);
        var target = FindUnit(state, targetUnitId);
        if (player is null || target is null)
        {
            return null;
        }

        var hand = player["hand"]!.AsArray();
        if (handIndex.Value < 0 || handIndex.Value >= hand.Count)
        {
            return null;
        }

        var card = hand[handIndex.Value]!.AsObject();
        var attached = Clone(card);
        var uid = $"attached-{state["nextUid"]?.GetValue<int>() ?? 1}";
        attached["uid"] = uid;
        attached["ownerId"] = playerId;
        attached["controllerId"] = playerId;
        attached["location"] = new JsonObject { ["type"] = "attached", ["battlefieldId"] = null, ["attachedToUid"] = targetUnitId };
        attached["exhausted"] = false;
        attached["attachedUnitId"] = targetUnitId;
        attached["isToken"] = false;
        attached["isFaceDown"] = false;
        attached["rulesTextActive"] = true;
        attached["attachedCards"] = new JsonArray();
        attached["topCardId"] = attached["id"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty;
        state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;

        hand.RemoveAt(handIndex.Value);

        var attachedCards = target["attachedCards"] as JsonArray ?? new JsonArray();
        attachedCards.Add(attached);
        target["attachedCards"] = attachedCards;
        RecomputeTopCard(target);
        return AddLog(state, $"{PlayerName(state, playerId)} attached {card["name"]?.GetValue<string>() ?? "a card"}.");
    }

    private static JsonObject? DetachCard(JsonObject state, int playerId, string? attachedCardUid)
    {
        if (string.IsNullOrWhiteSpace(attachedCardUid))
        {
            return null;
        }

        var detached = RemoveAttachedCard(state, attachedCardUid);
        if (detached is null || detached["ownerId"]?.GetValue<int>() != playerId)
        {
            return null;
        }

        detached["location"] = new JsonObject { ["type"] = "base", ["battlefieldId"] = null, ["attachedToUid"] = null };
        detached["attachedUnitId"] = null;
        state = PutObjectInOwnerZone(state, detached, "baseGear");
        return AddLog(state, $"{PlayerName(state, playerId)} detached a card.");
    }

    private static JsonObject? BanishObject(JsonObject state, int playerId, string? objectUid)
    {
        if (string.IsNullOrWhiteSpace(objectUid))
        {
            return null;
        }

        var removed = RemoveObjectByUid(state, objectUid);
        if (removed is null || removed["controllerId"]?.GetValue<int>() != playerId)
        {
            return null;
        }

        state = MoveObjectAndAttachmentsToOwnerZone(state, removed, "banished");
        return AddLog(state, $"{PlayerName(state, playerId)} banished an object.");
    }

    private static JsonObject? SetFaceDown(JsonObject state, int playerId, string? objectUid, bool faceDown)
    {
        if (string.IsNullOrWhiteSpace(objectUid))
        {
            return null;
        }

        var obj = FindObjectByUid(state, objectUid);
        if (obj is null || obj["controllerId"]?.GetValue<int>() != playerId)
        {
            return null;
        }

        obj["isFaceDown"] = faceDown;
        obj["rulesTextActive"] = !faceDown;
        return AddLog(state, $"{PlayerName(state, playerId)} {(faceDown ? "turned an object facedown" : "turned an object faceup")}.");
    }

    private static JsonObject? MoveUnits(JsonObject state, int playerId, IReadOnlyList<string> unitIds, string? battlefieldId, string? destination)
    {
        if (unitIds.Count == 0 || unitIds.Distinct(StringComparer.Ordinal).Count() != unitIds.Count)
        {
            return null;
        }

        var player = FindPlayer(state, playerId);
        if (player is null)
        {
            return null;
        }

        var movingUnits = unitIds.Select(unitId => FindMovableUnit(state, playerId, unitId)).ToArray();
        if (movingUnits.Any(unit => unit is null))
        {
            return null;
        }

        var moves = movingUnits.Select(unit => unit!).ToArray();
        if (moves.Any(move => move.Unit["ownerId"]?.GetValue<int>() != playerId || move.Unit["exhausted"]?.GetValue<bool>() != false))
        {
            return null;
        }

        if (IsBaseDestination(destination, battlefieldId))
        {
            if (moves.Any(move => move.Origin == MoveOrigin.Base))
            {
                return null;
            }

            foreach (var move in moves)
            {
                RemoveUnit(move.Source, move.UnitId);
                var moved = Clone(move.Unit);
                moved["exhausted"] = true;
                player["base"]!.AsArray().Add(moved);
                state = CollectTriggeredAbilities(state, "unit-moved", new JsonObject
                {
                    ["playerId"] = playerId,
                    ["sourceUid"] = moved["uid"]?.GetValue<string>(),
                    ["zone"] = "base",
                    ["battlefieldId"] = null
                });
            }

            var unitLabel = moves.Length == 1 ? moves[0].Unit["name"]?.GetValue<string>() ?? "a unit" : $"{moves.Length} units";
            return AddLog(state, $"{PlayerName(state, playerId)} moved {unitLabel} to their base.");
        }

        if (string.IsNullOrWhiteSpace(battlefieldId) || moves.Any(move => move.Origin == MoveOrigin.Battlefield && !HasKeyword(move.Unit, KeywordKind.Ganking)))
        {
            return null;
        }

        var battlefield = FindBattlefield(state, battlefieldId);
        if (battlefield is null || !CanMoveToBattlefield(state, playerId, battlefield))
        {
            return null;
        }

        foreach (var move in moves)
        {
            RemoveUnit(move.Source, move.UnitId);
            var moved = Clone(move.Unit);
            moved["exhausted"] = true;
            battlefield["units"]!.AsArray().Add(moved);
            state = CollectTriggeredAbilities(state, "unit-moved", new JsonObject
            {
                ["playerId"] = playerId,
                ["sourceUid"] = moved["uid"]?.GetValue<string>(),
                ["zone"] = "battlefield",
                ["battlefieldId"] = battlefieldId
            });
        }

        var movedLabel = moves.Length == 1 ? moves[0].Unit["name"]?.GetValue<string>() ?? "a unit" : $"{moves.Length} units";
        state = AddLog(state, $"{PlayerName(state, playerId)} moved {movedLabel} to {battlefield["name"]?.GetValue<string>() ?? "a battlefield"}.");
        return UpdateBattlefieldAfterMovement(state, playerId, battlefield);
    }

    private static bool IsBaseDestination(string? destination, string? battlefieldId)
    {
        return string.Equals(destination, "base", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(battlefieldId, "base", StringComparison.OrdinalIgnoreCase);
    }

    private static MovableUnit? FindMovableUnit(JsonObject state, int playerId, string unitId)
    {
        var player = FindPlayer(state, playerId);
        if (player is null)
        {
            return null;
        }

        var baseUnits = player["base"]!.AsArray();
        var baseUnit = baseUnits
            .Select(node => node!.AsObject())
            .FirstOrDefault(unit => string.Equals(unit["uid"]?.GetValue<string>(), unitId, StringComparison.Ordinal));
        if (baseUnit is not null)
        {
            return new MovableUnit(unitId, baseUnit, baseUnits, MoveOrigin.Base);
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var units = battlefield["units"]!.AsArray();
            var unit = units
                .Select(node => node!.AsObject())
                .FirstOrDefault(candidate =>
                    string.Equals(candidate["uid"]?.GetValue<string>(), unitId, StringComparison.Ordinal) &&
                    candidate["ownerId"]?.GetValue<int>() == playerId);
            if (unit is not null)
            {
                return new MovableUnit(unitId, unit, units, MoveOrigin.Battlefield);
            }
        }

        return null;
    }

    private static void RemoveUnit(JsonArray source, string unitId)
    {
        for (var index = 0; index < source.Count; index++)
        {
            if (string.Equals(source[index]?["uid"]?.GetValue<string>(), unitId, StringComparison.Ordinal))
            {
                source.RemoveAt(index);
                return;
            }
        }
    }

    private static bool CanMoveToBattlefield(JsonObject state, int playerId, JsonObject battlefield)
    {
        var controllerId = battlefield["controllerId"]?.GetValue<int?>();
        if (controllerId is not null && controllerId.Value != playerId && AreTeammates(state, playerId, controllerId.Value))
        {
            return false;
        }

        var otherOwners = UnitOwnerIds(battlefield)
            .Where(ownerId => ownerId != playerId)
            .Distinct()
            .ToArray();
        if (otherOwners.Length >= 2)
        {
            return false;
        }

        if (PlayerCount(state) <= 2 || !BattlefieldHasStagedOrActiveCombat(state, battlefield))
        {
            return true;
        }

        return IsCombatParticipantAtBattlefield(state, playerId, battlefield) ||
            battlefield["units"]!.AsArray().Any(unit => unit!["ownerId"]?.GetValue<int>() == playerId);
    }

    private static JsonObject UpdateBattlefieldAfterMovement(JsonObject state, int playerId, JsonObject battlefield)
    {
        var opposingOwners = UnitOwnerIds(battlefield)
            .Where(ownerId => ownerId != playerId && !AreTeammates(state, playerId, ownerId))
            .Distinct()
            .ToArray();

        if (opposingOwners.Length == 1)
        {
            battlefield["controllerId"] = null;
            battlefield["stagedCombat"] = true;
            battlefield["stagedShowdown"] = true;
            battlefield["contestedByPlayerId"] = playerId;
            return AddLog(state, $"{PlayerName(state, playerId)} contested {battlefield["name"]?.GetValue<string>() ?? "a battlefield"} and staged combat.");
        }

        if (opposingOwners.Length == 0)
        {
            battlefield["controllerId"] = playerId;
            battlefield["contestedByPlayerId"] = null;
            battlefield["stagedShowdown"] = false;
            battlefield["stagedCombat"] = false;
        }

        return state;
    }

    private static int[] UnitOwnerIds(JsonObject battlefield)
    {
        return battlefield["units"]!.AsArray()
            .Select(node => node!.AsObject()["ownerId"]?.GetValue<int>())
            .Where(owner => owner is not null)
            .Select(owner => owner!.Value)
            .Distinct()
            .ToArray();
    }

    private static bool BattlefieldHasStagedOrActiveCombat(JsonObject state, JsonObject battlefield)
    {
        var battlefieldId = battlefield["id"]?.GetValue<string>() ?? string.Empty;
        if (battlefield["stagedCombat"]?.GetValue<bool>() == true)
        {
            return true;
        }

        return state["activeCombat"] is JsonObject activeCombat &&
            string.Equals(activeCombat["battlefieldId"]?.GetValue<string>(), battlefieldId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCombatParticipantAtBattlefield(JsonObject state, int playerId, JsonObject battlefield)
    {
        var battlefieldId = battlefield["id"]?.GetValue<string>() ?? string.Empty;
        if (state["activeCombat"] is JsonObject activeCombat &&
            string.Equals(activeCombat["battlefieldId"]?.GetValue<string>(), battlefieldId, StringComparison.OrdinalIgnoreCase) &&
            (activeCombat["attackerPlayerId"]?.GetValue<int>() == playerId || activeCombat["defenderPlayerId"]?.GetValue<int>() == playerId))
        {
            return true;
        }

        return battlefield["contestedByPlayerId"]?.GetValue<int?>() == playerId ||
            UnitOwnerIds(battlefield).Contains(playerId);
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

        state = RunAbilityQueues(state);
        if (state["chainWindow"] is not null)
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
        state = QueueCombatDesignationTriggers(state, battlefield, attackerPlayerId, defenderPlayerId);
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
        if (!ValidateDamageAssignments(state, ownUnits, opposingUnits, ownAssignments))
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

        if (!ValidateDamageAssignments(state, attackers, defenders, attackerAssignments) ||
            !ValidateDamageAssignments(state, defenders, attackers, defenderAssignments))
        {
            return null;
        }

        var sourceMap = new JsonObject();
        foreach (var unit in defenders)
        {
            var uid = unit["uid"]?.GetValue<string>() ?? string.Empty;
            var assignedDamage = attackerAssignments.GetValueOrDefault(uid);
            if (assignedDamage > 0)
            {
                sourceMap[uid] = ToArray(attackers.Select(unit => unit["uid"]?.GetValue<string>() ?? string.Empty).Where(uid => !string.IsNullOrWhiteSpace(uid)));
                DealDamage(unit, assignedDamage);
            }
        }

        foreach (var unit in attackers)
        {
            var uid = unit["uid"]?.GetValue<string>() ?? string.Empty;
            var assignedDamage = defenderAssignments.GetValueOrDefault(uid);
            if (assignedDamage > 0)
            {
                sourceMap[uid] = ToArray(defenders.Select(unit => unit["uid"]?.GetValue<string>() ?? string.Empty).Where(uid => !string.IsNullOrWhiteSpace(uid)));
                DealDamage(unit, assignedDamage);
            }
        }

        activeCombat["damageSourcesByUnitId"] = sourceMap;

        state = RunCombatCleanup(state, battlefield);

        var remainingUnits = battlefield["units"]!.AsArray().Select(node => node!.AsObject()).ToArray();

        var attackerHasUnits = remainingUnits.Any(unit => unit["ownerId"]?.GetValue<int>() == attackerPlayerId.Value);
        var defenderHasUnits = remainingUnits.Any(unit => unit["ownerId"]?.GetValue<int>() == defenderPlayerId.Value);
        if (attackerHasUnits && defenderHasUnits)
        {
            state = RecallAttackers(state, battlefield, attackerPlayerId.Value);
            remainingUnits = battlefield["units"]!.AsArray().Select(node => node!.AsObject()).ToArray();
            attackerHasUnits = remainingUnits.Any(unit => unit["ownerId"]?.GetValue<int>() == attackerPlayerId.Value);
            defenderHasUnits = remainingUnits.Any(unit => unit["ownerId"]?.GetValue<int>() == defenderPlayerId.Value);
        }

        state["lastCombat"] = new JsonObject
        {
            ["battlefieldId"] = battlefieldId,
            ["attackerPlayerId"] = attackerPlayerId.Value,
            ["defenderPlayerId"] = defenderPlayerId.Value,
            ["damageSourcesByUnitId"] = sourceMap.DeepClone()
        };

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
            state["lastCombat"]!["result"] = "no-result";
            battlefield["stagedCombat"] = true;
            battlefield["contestedByPlayerId"] = attackerPlayerId.Value;
            battlefield["controllerId"] = null;
            return AddLog(state, $"Combat at {battlefield["name"]?.GetValue<string>() ?? "a battlefield"} had no result.");
        }

        if (attackerHasUnits)
        {
            state["lastCombat"]!["result"] = "attacker-won";
            state["lastCombat"]!["winningPlayerId"] = attackerPlayerId.Value;
            state["lastCombat"]!["losingPlayerId"] = defenderPlayerId.Value;
            battlefield["controllerId"] = attackerPlayerId.Value;
            state = ScoreBattlefield(state, new ScoreRequest(attackerPlayerId.Value, battlefieldId, ScoreSource.Conquer));
        }
        else if (defenderHasUnits)
        {
            state["lastCombat"]!["result"] = "defender-won";
            state["lastCombat"]!["winningPlayerId"] = defenderPlayerId.Value;
            state["lastCombat"]!["losingPlayerId"] = attackerPlayerId.Value;
            battlefield["controllerId"] = defenderPlayerId.Value;
            state = ScoreBattlefield(state, new ScoreRequest(defenderPlayerId.Value, battlefieldId, ScoreSource.Conquer));
        }
        else
        {
            state["lastCombat"]!["result"] = "no-result";
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

    private static JsonObject RecallAttackers(JsonObject state, JsonObject battlefield, int attackerPlayerId)
    {
        var units = battlefield["units"]!.AsArray();
        for (var index = units.Count - 1; index >= 0; index--)
        {
            var unit = units[index]!.AsObject();
            if (unit["ownerId"]?.GetValue<int>() != attackerPlayerId)
            {
                continue;
            }

            var recalled = Clone(unit);
            units.RemoveAt(index);
            state = UpdatePlayer(state, attackerPlayerId, player =>
            {
                player["base"]!.AsArray().Add(recalled);
                return player;
            });
        }

        return state;
    }

    private static bool ValidateDamageAssignments(JsonObject state, IReadOnlyList<JsonObject> assigningUnits, IReadOnlyList<JsonObject> opposingUnits, IReadOnlyDictionary<string, int> assignments)
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

        var requiredTotal = assigningUnits.Sum(unit => CurrentCombatMight(state, unit));
        if (assignments.Values.Sum() != requiredTotal)
        {
            return false;
        }

        var positiveAssignments = assignments.Where(pair => pair.Value > 0).ToArray();
        foreach (var (uid, amount) in positiveAssignments)
        {
            var assignedUnit = opposingById[uid];
            var assignedPriority = DamageAssignmentPriority(assignedUnit);
            foreach (var candidate in opposingUnits)
            {
                var candidateUid = candidate["uid"]?.GetValue<string>() ?? string.Empty;
                if (candidateUid == uid || !CanTakeDamage(candidate))
                {
                    continue;
                }

                var candidatePriority = DamageAssignmentPriority(candidate);
                if (candidatePriority < assignedPriority && assignments.GetValueOrDefault(candidateUid) < LethalDamage(state, candidate))
                {
                    return false;
                }
            }
        }

        var tankUnits = opposingUnits.Where(unit => HasKeyword(unit, KeywordKind.Tank)).ToArray();
        if (tankUnits.Length > 0)
        {
            var allTanksLethal = tankUnits.All(unit =>
            {
                var uid = unit["uid"]?.GetValue<string>() ?? string.Empty;
                return assignments.GetValueOrDefault(uid) >= LethalDamage(state, unit);
            });
            if (!allTanksLethal && opposingUnits.Any(unit => !HasKeyword(unit, KeywordKind.Tank) && assignments.GetValueOrDefault(unit["uid"]?.GetValue<string>() ?? string.Empty) > 0))
            {
                return false;
            }
        }

        var backlineUnits = opposingUnits.Where(unit => HasKeyword(unit, KeywordKind.Backline)).ToArray();
        if (backlineUnits.Length > 0)
        {
            var frontlineUnits = opposingUnits.Where(unit => !HasKeyword(unit, KeywordKind.Backline)).ToArray();
            var allFrontlineLethal = frontlineUnits.All(unit =>
            {
                var uid = unit["uid"]?.GetValue<string>() ?? string.Empty;
                return assignments.GetValueOrDefault(uid) >= LethalDamage(state, unit);
            });
            if (!allFrontlineLethal && backlineUnits.Any(unit => assignments.GetValueOrDefault(unit["uid"]?.GetValue<string>() ?? string.Empty) > 0))
            {
                return false;
            }
        }

        var nonLethalPositiveCount = 0;
        var damageableOpposingUnits = opposingUnits.Where(CanTakeDamage).ToArray();
        var allOpposingUnitsAssignedLethal = damageableOpposingUnits.All(unit =>
        {
            var uid = unit["uid"]?.GetValue<string>() ?? string.Empty;
            return assignments.GetValueOrDefault(uid) >= LethalDamage(state, unit);
        });

        foreach (var (uid, amount) in positiveAssignments)
        {
            var lethal = LethalDamage(state, opposingById[uid]);
            if (!CanTakeDamage(opposingById[uid]))
            {
                nonLethalPositiveCount += 1;
                continue;
            }
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
            if ((unit["damage"]?.GetValue<int>() ?? 0) < LethalDamage(state, unit))
            {
                continue;
            }

            var ownerId = unit["ownerId"]?.GetValue<int>() ?? -1;
            state = ResolveDeathknell(state, ownerId, unit);
            units.RemoveAt(i);
            state = MoveObjectAndAttachmentsToOwnerZone(state, unit, "trash");
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
                if ((unit["damage"]?.GetValue<int>() ?? 0) < LethalDamage(state, unit))
                {
                    continue;
                }

                var ownerId = unit["ownerId"]?.GetValue<int>() ?? player["id"]?.GetValue<int>() ?? -1;
                state = ResolveDeathknell(state, ownerId, unit);
                units.RemoveAt(i);
                state = MoveObjectAndAttachmentsToOwnerZone(state, unit, "trash");
            }
        }

        return state;
    }

    private static JsonObject KillTemporaryPermanents(JsonObject state, int playerId)
    {
        var player = FindPlayer(state, playerId);
        if (player is not null)
        {
            var baseUnits = player["base"]!.AsArray();
            for (var i = baseUnits.Count - 1; i >= 0; i--)
            {
                var unit = baseUnits[i]!.AsObject();
                if (!HasKeyword(unit, KeywordKind.Temporary))
                {
                    continue;
                }

                player["trash"]!.AsArray().Add(TrashSnapshot(unit));
                state = ResolveDeathknell(state, playerId, unit);
                baseUnits.RemoveAt(i);
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var units = battlefield["units"]!.AsArray();
            for (var i = units.Count - 1; i >= 0; i--)
            {
                var unit = units[i]!.AsObject();
                if (unit["ownerId"]?.GetValue<int>() != playerId || !HasKeyword(unit, KeywordKind.Temporary))
                {
                    continue;
                }

                state = MoveKilledPermanentToTrash(state, playerId, unit);
                units.RemoveAt(i);
            }
        }

        return state;
    }

    private static JsonObject TrashSnapshot(JsonObject permanent)
    {
        var killed = Clone(permanent);
        killed.Remove("uid");
        killed.Remove("ownerId");
        killed.Remove("exhausted");
        killed.Remove("damage");
        killed.Remove("attachedMight");
        killed.Remove("attacker");
        killed.Remove("defender");
        return killed;
    }

    private static JsonObject ResolveDeathknell(JsonObject state, int ownerId, JsonObject permanent)
    {
        foreach (var keyword in Keywords(permanent).Where(keyword => KeywordKindName(keyword) == KeywordKind.Deathknell))
        {
            var text = keyword["text"]?.GetValue<string>() ?? permanent["text"]?.GetValue<string>() ?? string.Empty;
            if (TryReadInstructionAmount(text, "draw") is { } drawAmount)
            {
                state = DrawCards(state, ownerId, drawAmount);
            }
        }

        return state;
    }

    private static int CurrentMight(JsonObject state, JsonObject unit)
    {
        var might = ContinuousEffectLayerResolver.EvaluateUnit(unit).Might + PassiveMightBonus(state, unit);
        if (unit["defender"]?.GetValue<bool>() == true)
        {
            might += KeywordValue(unit, KeywordKind.Shield);
        }

        return might;
    }

    private static int CurrentCombatMight(JsonObject state, JsonObject unit)
    {
        if (unit["stunned"]?.GetValue<bool>() == true)
        {
            return 0;
        }

        var might = CurrentMight(state, unit);
        if (unit["attacker"]?.GetValue<bool>() == true)
        {
            might += KeywordValue(unit, KeywordKind.Assault);
        }

        if (unit["defender"]?.GetValue<bool>() == true)
        {
            might += KeywordValue(unit, KeywordKind.Shield);
        }

        return Math.Max(0, might);
    }

    private static int LethalDamage(JsonObject state, JsonObject unit)
    {
        return Math.Max(1, CurrentMight(state, unit));
    }

    private static IReadOnlyList<EngineLegalAction> ActivatedAbilityActions(JsonObject state, int playerId)
    {
        if (state["stage"]?.GetValue<string>() != "playing" || state["chainWindow"] is not null || PendingBurnOut(state) is not null)
        {
            return [];
        }

        return AbilitySources(state)
            .Where(source => source.OwnerId == playerId)
            .SelectMany(source => source.Abilities
                .Where(ability => AbilityKind(ability) is "activated" or "modal")
                .Where(ability => CanPayAbilityCost(state, playerId, source.Card, ability))
                .Select(ability =>
                {
                    var abilityId = ability["id"]?.GetValue<string>() ?? string.Empty;
                    return new EngineLegalAction(
                        $"activate-ability-{source.Uid}-{abilityId}",
                        "activate-ability",
                        ability["label"]?.GetValue<string>() ?? $"Activate {source.Name}",
                        playerId,
                        new JsonObject
                        {
                            ["sourceUid"] = source.Uid,
                            ["abilityId"] = abilityId
                        });
                }))
            .ToArray();
    }

    private static JsonObject? ActivateAbility(JsonObject state, int playerId, string? sourceUid, string? abilityId, string? modeId, string? targetUnitId, string? targetLaneId)
    {
        if (string.IsNullOrWhiteSpace(sourceUid) || string.IsNullOrWhiteSpace(abilityId))
        {
            return null;
        }

        var source = AbilitySources(state).FirstOrDefault(candidate => candidate.Uid == sourceUid && candidate.OwnerId == playerId);
        if (source is null)
        {
            return null;
        }

        var ability = source.Abilities.FirstOrDefault(candidate => string.Equals(candidate["id"]?.GetValue<string>(), abilityId, StringComparison.Ordinal));
        if (ability is null || AbilityKind(ability) is not ("activated" or "modal") || !CanPayAbilityCost(state, playerId, source.Card, ability))
        {
            return null;
        }

        var effect = SelectAbilityEffect(ability, modeId);
        if (effect is null)
        {
            return null;
        }

        state = PayAbilityCost(state, playerId, source.Uid, ability);
        state = EnqueueAbilityEffect(state, source, ability, effect, targetUnitId, targetLaneId);
        OpenChainWindow(state, playerId, playerId);
        return AddLog(state, $"{PlayerName(state, playerId)} activated {ability["label"]?.GetValue<string>() ?? source.Name}.");
    }

    private static JsonObject CollectTriggeredAbilities(JsonObject state, string eventName, JsonObject eventPayload)
    {
        state = FireDelayedAbilities(state, eventName, eventPayload, delayedKind: "delayed-triggered");
        var queue = state["pendingTriggeredAbilities"] as JsonArray ?? new JsonArray();
        state["pendingTriggeredAbilities"] = queue;

        foreach (var source in AbilitySources(state))
        {
            foreach (var ability in source.Abilities.Where(ability => AbilityKind(ability) == "triggered" && AbilityEvent(ability) == eventName))
            {
                queue.Add(PendingAbility(source, ability, eventPayload));
                AppendAbilityEvent(state, "trigger-collected", source, ability, eventName);
            }
        }

        return state;
    }

    private static JsonObject RunAbilityQueues(JsonObject state)
    {
        var queue = state["pendingTriggeredAbilities"] as JsonArray;
        if (queue is null || queue.Count == 0 || state["chainWindow"] is not null)
        {
            return state;
        }

        var stack = state["effectStack"]!.AsArray();
        while (queue.Count > 0)
        {
            var pending = queue[0]!.AsObject();
            queue.RemoveAt(0);
            var effect = pending["effect"]?.AsObject();
            if (effect is null)
            {
                continue;
            }

            var item = new JsonObject
            {
                ["id"] = $"ability-stack-{state["nextUid"]?.GetValue<int>() ?? 1}",
                ["card"] = pending["source"]?.DeepClone() ?? new JsonObject(),
                ["cardId"] = pending["sourceCardId"]?.GetValue<string>() ?? string.Empty,
                ["cardName"] = pending["sourceName"]?.GetValue<string>() ?? "an ability",
                ["kind"] = "ability",
                ["playerId"] = pending["playerId"]?.GetValue<int>() ?? 0,
                ["effect"] = effect.DeepClone(),
                ["targetUnitId"] = pending["targetUnitId"]?.GetValue<string>(),
                ["targetLaneId"] = pending["targetLaneId"]?.GetValue<string>()
            };
            state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;
            stack.Insert(0, item);
        }

        if (stack.Count > 0)
        {
            var priorityPlayerId = stack[0]!["playerId"]?.GetValue<int>() ?? state["turnPlayerId"]?.GetValue<int>() ?? 0;
            OpenChainWindow(state, priorityPlayerId, priorityPlayerId);
        }

        return state;
    }

    private static ReplacementResult ApplyReplacementAbilities(JsonObject state, string eventName, JsonObject eventPayload)
    {
        var amount = eventPayload["amount"]?.GetValue<int>() ?? 0;
        var prevented = false;
        ApplyDelayedReplacementAbilities(state, eventName, eventPayload, ref amount, ref prevented);
        foreach (var source in AbilitySources(state))
        {
            foreach (var ability in source.Abilities.Where(ability => AbilityKind(ability) is "replacement" or "delayed-replacement" && AbilityEvent(ability) == eventName))
            {
                var effect = ability["effect"]?.AsObject();
                var effectType = effect?["type"]?.GetValue<string>() ?? "";
                if (effectType == "prevent")
                {
                    prevented = true;
                }
                else if (effectType == "modify-amount")
                {
                    amount += effect?["amount"]?.GetValue<int>() ?? 0;
                }

                AppendAbilityEvent(state, "replacement-applied", source, ability, eventName);
            }
        }

        return new ReplacementResult(state, Math.Max(0, amount), prevented);
    }

    private static void ApplyDelayedReplacementAbilities(JsonObject state, string eventName, JsonObject eventPayload, ref int amount, ref bool prevented)
    {
        var delayed = state["delayedAbilities"] as JsonArray;
        if (delayed is null || delayed.Count == 0)
        {
            return;
        }

        for (var i = delayed.Count - 1; i >= 0; i--)
        {
            var ability = delayed[i]!.AsObject();
            if (AbilityKind(ability) != "delayed-replacement" || AbilityEvent(ability) != eventName)
            {
                continue;
            }

            var effect = ability["effect"]?.AsObject();
            var effectType = effect?["type"]?.GetValue<string>() ?? "";
            if (effectType == "prevent")
            {
                prevented = true;
            }
            else if (effectType == "modify-amount")
            {
                amount += effect?["amount"]?.GetValue<int>() ?? 0;
            }

            var source = new AbilitySource(
                ability["sourceUid"]?.GetValue<string>() ?? $"delayed-{i}",
                ability["sourceCardId"]?.GetValue<string>() ?? string.Empty,
                ability["sourceName"]?.GetValue<string>() ?? "Delayed ability",
                ability["playerId"]?.GetValue<int>() ?? eventPayload["playerId"]?.GetValue<int>() ?? 0,
                ability,
                [ability]);
            AppendAbilityEvent(state, "delayed-fired", source, ability, eventName);
            if (ability["consume"]?.GetValue<bool>() != false)
            {
                delayed.RemoveAt(i);
            }
        }
    }

    private static JsonObject FireDelayedAbilities(JsonObject state, string eventName, JsonObject eventPayload, string delayedKind)
    {
        var delayed = state["delayedAbilities"] as JsonArray;
        if (delayed is null || delayed.Count == 0)
        {
            return state;
        }

        var pendingTriggers = state["pendingTriggeredAbilities"] as JsonArray ?? new JsonArray();
        state["pendingTriggeredAbilities"] = pendingTriggers;
        for (var i = delayed.Count - 1; i >= 0; i--)
        {
            var ability = delayed[i]!.AsObject();
            if (AbilityKind(ability) != delayedKind || AbilityEvent(ability) != eventName)
            {
                continue;
            }

            var source = new AbilitySource(
                ability["sourceUid"]?.GetValue<string>() ?? $"delayed-{i}",
                ability["sourceCardId"]?.GetValue<string>() ?? string.Empty,
                ability["sourceName"]?.GetValue<string>() ?? "Delayed ability",
                ability["playerId"]?.GetValue<int>() ?? eventPayload["playerId"]?.GetValue<int>() ?? 0,
                ability,
                [ability]);
            if (delayedKind == "delayed-triggered")
            {
                pendingTriggers.Add(PendingAbility(source, ability, eventPayload));
            }

            AppendAbilityEvent(state, "delayed-fired", source, ability, eventName);
            if (ability["consume"]?.GetValue<bool>() != false)
            {
                delayed.RemoveAt(i);
            }
        }

        return state;
    }

    private static JsonObject EnqueueAbilityEffect(JsonObject state, AbilitySource source, JsonObject ability, JsonObject effect, string? targetUnitId, string? targetLaneId)
    {
        var effectType = effect["type"]?.GetValue<string>() ?? "";
        if (effectType == "create-delayed-trigger" || effectType == "create-delayed-replacement")
        {
            var delayed = state["delayedAbilities"] as JsonArray ?? new JsonArray();
            state["delayedAbilities"] = delayed;
            delayed.Add(new JsonObject
            {
                ["id"] = $"{ability["id"]?.GetValue<string>() ?? "ability"}-delayed-{state["nextUid"]?.GetValue<int>() ?? 1}",
                ["kind"] = effectType == "create-delayed-trigger" ? "delayed-triggered" : "delayed-replacement",
                ["event"] = effect["event"]?.GetValue<string>() ?? "action-applied",
                ["playerId"] = source.OwnerId,
                ["sourceUid"] = source.Uid,
                ["sourceCardId"] = source.CardId,
                ["sourceName"] = source.Name,
                ["consume"] = effect["consume"]?.GetValue<bool>() != false,
                ["effect"] = effect["effect"]?.DeepClone() ?? new JsonObject { ["type"] = "rally", ["amount"] = 0 }
            });
            state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;
            AppendAbilityEvent(state, "delayed-created", source, ability, effect["event"]?.GetValue<string>() ?? "action-applied");
            return state;
        }

        state["effectStack"]!.AsArray().Insert(0, new JsonObject
        {
            ["id"] = $"ability-stack-{state["nextUid"]?.GetValue<int>() ?? 1}",
            ["card"] = Clone(source.Card),
            ["cardId"] = source.CardId,
            ["cardName"] = source.Name,
            ["kind"] = "ability",
            ["playerId"] = source.OwnerId,
            ["effect"] = effect.DeepClone(),
            ["targetUnitId"] = targetUnitId,
            ["targetLaneId"] = targetLaneId
        });
        state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;
        return state;
    }

    private static JsonObject PendingAbility(AbilitySource source, JsonObject ability, JsonObject eventPayload)
    {
        return new JsonObject
        {
            ["source"] = Clone(source.Card),
            ["sourceUid"] = source.Uid,
            ["sourceCardId"] = source.CardId,
            ["sourceName"] = source.Name,
            ["abilityId"] = ability["id"]?.GetValue<string>() ?? string.Empty,
            ["playerId"] = source.OwnerId,
            ["effect"] = ability["effect"]?.DeepClone() ?? new JsonObject { ["type"] = "rally", ["amount"] = 0 },
            ["targetUnitId"] = eventPayload["targetUnitId"]?.GetValue<string>(),
            ["targetLaneId"] = eventPayload["targetLaneId"]?.GetValue<string>()
        };
    }

    private static JsonObject? SelectAbilityEffect(JsonObject ability, string? modeId)
    {
        if (ability["modes"] is JsonArray modes)
        {
            var selectedMode = modes
                .Select(node => node!.AsObject())
                .FirstOrDefault(mode => string.Equals(mode["id"]?.GetValue<string>(), modeId, StringComparison.Ordinal))
                ?? (string.IsNullOrWhiteSpace(modeId) ? modes.FirstOrDefault()?.AsObject() : null);
            return selectedMode?["effect"]?.AsObject();
        }

        return ability["effect"]?.AsObject();
    }

    private static bool CanPayAbilityCost(JsonObject state, int playerId, JsonObject source, JsonObject ability)
    {
        var cost = ability["cost"]?.AsObject();
        if (cost is null)
        {
            return true;
        }

        if (cost["exhaust"]?.GetValue<bool>() == true && source["exhausted"]?.GetValue<bool>() == true)
        {
            return false;
        }

        var runeCost = cost["runes"]?.GetValue<int>() ?? 0;
        return runeCost <= 0 || FindPlayer(state, playerId) is { } player && CanPay(player, runeCost);
    }

    private static JsonObject PayAbilityCost(JsonObject state, int playerId, string sourceUid, JsonObject ability)
    {
        var cost = ability["cost"]?.AsObject();
        if (cost is null)
        {
            return state;
        }

        if (cost["exhaust"]?.GetValue<bool>() == true && FindUnit(state, sourceUid) is { } unit)
        {
            unit["exhausted"] = true;
        }

        var runeCost = cost["runes"]?.GetValue<int>() ?? 0;
        if (runeCost > 0)
        {
            state = UpdatePlayer(state, playerId, player =>
            {
                PayCost(player, runeCost);
                return player;
            });
        }

        return state;
    }

    private static IEnumerable<AbilitySource> AbilitySources(JsonObject state)
    {
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            var ownerId = player["id"]?.GetValue<int>() ?? 0;
            foreach (var card in player["base"]!.AsArray().Select(node => node!.AsObject()))
            {
                if (ReadAbilities(card).Length > 0)
                {
                    yield return new AbilitySource(card["uid"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty, card["catalogId"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty, card["name"]?.GetValue<string>() ?? "a card", ownerId, card, ReadAbilities(card));
                }
            }

            foreach (var card in player["baseGear"]!.AsArray().Select(node => node!.AsObject()))
            {
                if (ReadAbilities(card).Length > 0)
                {
                    yield return new AbilitySource(card["uid"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty, card["catalogId"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty, card["name"]?.GetValue<string>() ?? "a card", ownerId, card, ReadAbilities(card));
                }
            }

            foreach (var zone in new[] { "champion", "legend" })
            {
                if (player[zone] is JsonObject card && ReadAbilities(card).Length > 0)
                {
                    yield return new AbilitySource(card["uid"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty, card["catalogId"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty, card["name"]?.GetValue<string>() ?? "a card", ownerId, card, ReadAbilities(card));
                }
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            foreach (var card in battlefield["units"]!.AsArray().Select(node => node!.AsObject()))
            {
                if (ReadAbilities(card).Length > 0)
                {
                    yield return new AbilitySource(card["uid"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty, card["catalogId"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty, card["name"]?.GetValue<string>() ?? "a card", card["ownerId"]?.GetValue<int>() ?? 0, card, ReadAbilities(card));
                }
            }
        }
    }

    private static JsonObject[] ReadAbilities(JsonObject card)
    {
        return card["abilities"] is JsonArray abilities
            ? abilities.Select(node => node!.AsObject()).ToArray()
            : [];
    }

    private static string AbilityKind(JsonObject ability)
    {
        return ability["kind"]?.GetValue<string>() ?? ability["type"]?.GetValue<string>() ?? string.Empty;
    }

    private static string AbilityEvent(JsonObject ability)
    {
        return ability["event"]?.GetValue<string>() ?? ability["trigger"]?.GetValue<string>() ?? string.Empty;
    }

    private static void AppendAbilityEvent(JsonObject state, string type, AbilitySource source, JsonObject ability, string eventName)
    {
        var events = state["abilityEvents"] as JsonArray ?? new JsonArray();
        state["abilityEvents"] = events;
        events.Add(new JsonObject
        {
            ["type"] = type,
            ["event"] = eventName,
            ["sourceUid"] = source.Uid,
            ["abilityId"] = ability["id"]?.GetValue<string>() ?? string.Empty,
            ["playerId"] = source.OwnerId
        });
    }

    private static int PassiveMightBonus(JsonObject state, JsonObject unit)
    {
        var ownerId = unit["ownerId"]?.GetValue<int>() ?? -1;
        var bonus = 0;
        foreach (var source in AbilitySources(state))
        {
            foreach (var ability in source.Abilities.Where(ability => AbilityKind(ability) is "passive" or "continuous"))
            {
                var effect = ability["effect"]?.AsObject();
                if (effect?["type"]?.GetValue<string>() != "modify-own-units-might" || source.OwnerId != ownerId)
                {
                    continue;
                }

                bonus += effect["amount"]?.GetValue<int>() ?? 0;
            }
        }

        return bonus;
    }

    private static JsonObject BuildAbilityContributions(JsonObject state)
    {
        var contributions = new JsonObject();
        foreach (var unit in state["players"]!.AsArray().Select(node => node!.AsObject()).SelectMany(player => player["base"]!.AsArray().Select(node => node!.AsObject()))
            .Concat(state["battlefields"]!.AsArray().Select(node => node!.AsObject()).SelectMany(battlefield => battlefield["units"]!.AsArray().Select(node => node!.AsObject()))))
        {
            var uid = unit["uid"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(uid))
            {
                continue;
            }

            contributions[uid] = new JsonObject
            {
                ["might"] = PassiveMightBonus(state, unit)
            };
        }

        return contributions;
    }

    private static bool CanTakeDamage(JsonObject unit)
    {
        return unit["preventDamage"]?.GetValue<bool>() != true
            && unit["cannotTakeDamage"]?.GetValue<bool>() != true
            && !HasKeyword(unit, "PreventDamage");
    }

    private static void DealDamage(JsonObject unit, int amount)
    {
        if (amount <= 0 || !CanTakeDamage(unit))
        {
            return;
        }

        unit["damage"] = (unit["damage"]?.GetValue<int>() ?? 0) + amount;
    }

    private static int DamageAssignmentPriority(JsonObject unit)
    {
        if (HasKeyword(unit, "Tank"))
        {
            return -1;
        }

        return HasKeyword(unit, "Backline") || HasKeyword(unit, "LastDamage") || unit["assignDamageLast"]?.GetValue<bool>() == true
            ? 1
            : 0;
    }

    private static int AdditionalTargetingCost(JsonObject state, int playerId, JsonObject card, string? targetUnitId, string? targetLaneId)
    {
        if (string.IsNullOrWhiteSpace(targetUnitId) && string.IsNullOrWhiteSpace(targetLaneId))
        {
            return 0;
        }

        var effect = card["effect"]?.AsObject();
        var effectType = effect?["type"]?.GetValue<string>() ?? "rally";
        if (effectType is "draw" or "rally" or "buff")
        {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(targetUnitId))
        {
            var target = FindUnit(state, targetUnitId);
            return target?["ownerId"]?.GetValue<int>() == playerId ? 0 : KeywordValue(target, "Deflect");
        }

        var battlefield = FindBattlefield(state, targetLaneId!);
        var targetInLane = battlefield?["units"]!.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(unit => unit["ownerId"]?.GetValue<int>() != playerId);
        return targetInLane is null ? 0 : KeywordValue(targetInLane, "Deflect");
    }

    private static JsonObject QueueCombatDesignationTriggers(JsonObject state, JsonObject battlefield, int attackerPlayerId, int defenderPlayerId)
    {
        foreach (var unit in battlefield["units"]!.AsArray().Select(node => node!.AsObject()))
        {
            var ownerId = unit["ownerId"]?.GetValue<int>() ?? -1;
            var trigger = ownerId == attackerPlayerId ? unit["attackTrigger"] : ownerId == defenderPlayerId ? unit["defendTrigger"] : null;
            if (trigger is not JsonObject effect)
            {
                continue;
            }

            var unitId = unit["uid"]?.GetValue<string>() ?? string.Empty;
            state["effectStack"]!.AsArray().Add(new JsonObject
            {
                ["id"] = $"stack-{state["nextUid"]?.GetValue<int>() ?? 1}",
                ["card"] = Clone(unit),
                ["cardId"] = unit["catalogId"]?.GetValue<string>() ?? unit["id"]?.GetValue<string>() ?? string.Empty,
                ["cardName"] = unit["name"]?.GetValue<string>() ?? "a unit",
                ["kind"] = "ability",
                ["playerId"] = ownerId,
                ["effect"] = effect.DeepClone(),
                ["targetUnitId"] = unitId,
                ["targetLaneId"] = battlefield["id"]?.GetValue<string>() ?? string.Empty
            });
            state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;
        }

        if (state["effectStack"]!.AsArray().Count > 0)
        {
            OpenChainWindow(state, attackerPlayerId, attackerPlayerId);
        }

        return state;
    }

    private static bool HasKeyword(JsonObject unit, string keyword)
    {
        if (unit[keyword] is JsonValue value && value.TryGetValue<bool>(out var hasKeyword) && hasKeyword)
        {
            return true;
        }

        if (unit["keywords"] is JsonArray keywords && keywords.Any(item => string.Equals(item?.GetValue<string>(), keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var text = unit["text"]?.GetValue<string>() ?? string.Empty;
        return text.Contains($"[{keyword}", StringComparison.OrdinalIgnoreCase)
            || text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static int KeywordValue(JsonObject? unit, string keyword, int defaultValue = 0)
    {
        if (unit is null)
        {
            return 0;
        }

        if (unit[$"{char.ToLowerInvariant(keyword[0])}{keyword[1..]}Value"]?.GetValue<int?>() is { } explicitValue)
        {
            return explicitValue;
        }

        var text = unit["text"]?.GetValue<string>() ?? string.Empty;
        var match = Regex.Match(text, $@"\b{Regex.Escape(keyword)}\s*(\d+)?", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : defaultValue;
        }

        return HasKeyword(unit, keyword) ? defaultValue : 0;
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

    private static IReadOnlyList<EngineLegalAction> HideCardsFromHand(JsonObject state, int playerId)
    {
        var player = FindPlayer(state, playerId);
        if (player is null || !CanPay(player, 1))
        {
            return [];
        }

        var controlledBattlefields = state["battlefields"]!.AsArray()
            .Select(node => node!.AsObject())
            .Where(battlefield => battlefield["controllerId"]?.GetValue<int>() == playerId)
            .Where(battlefield => !(battlefield["hiddenCards"] as JsonArray ?? []).Any(card => card?["ownerId"]?.GetValue<int>() == playerId))
            .ToArray();
        if (controlledBattlefields.Length == 0)
        {
            return [];
        }

        return player["hand"]!.AsArray()
            .Select((node, index) => (Card: node!.AsObject(), Index: index))
            .Where(item => HasKeyword(item.Card, KeywordKind.Hidden))
            .Select(item => new EngineLegalAction(
                $"hide-card-{playerId}-{item.Index}",
                "hide-card",
                $"Hide {item.Card["name"]?.GetValue<string>() ?? "card"}",
                playerId,
                new JsonObject
                {
                    ["handIndex"] = item.Index,
                    ["battlefieldIds"] = ToArray(controlledBattlefields.Select(battlefield => battlefield["id"]?.GetValue<string>() ?? string.Empty))
                }))
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

        return HasKeyword(card, KeywordKind.Reaction) || HasKeyword(card, KeywordKind.QuickDraw);
    }

    private static bool IsActionCard(JsonObject card)
    {
        if (!IsSpellOrGear(card))
        {
            return false;
        }

        var text = card["text"]?.GetValue<string>() ?? string.Empty;
        return HasKeyword(card, KeywordKind.Action)
            || !HasKeyword(card, KeywordKind.Reaction);
    }

    private static TargetSelection ValidateTargetSelection(JsonObject state, int playerId, JsonObject card, string? targetUnitId, string? targetLaneId)
    {
        var effect = card["effect"]?.AsObject();
        var effectType = effect?["type"]?.GetValue<string>() ?? "rally";
        var hasUnitTarget = !string.IsNullOrWhiteSpace(targetUnitId);
        var hasLaneTarget = !string.IsNullOrWhiteSpace(targetLaneId);

        if (hasUnitTarget && hasLaneTarget)
        {
            return TargetSelection.Invalid;
        }

        if (effectType == "draw")
        {
            return hasUnitTarget || hasLaneTarget
                ? TargetSelection.Invalid
                : new TargetSelection(true, null, null, []);
        }

        if (effectType == "damage")
        {
            if (hasLaneTarget)
            {
                var battlefield = FindBattlefield(state, targetLaneId!);
                if (battlefield is null)
                {
                    return TargetSelection.Invalid;
                }

                var targets = battlefield["units"]!.AsArray()
                    .Select(node => node!.AsObject())
                    .Where(unit => UnitCanBeTargetedByEffect(state, playerId, "damage", unit))
                    .Select(unit => UnitTargetFrom(state, unit, "damage", targetLaneId!))
                    .ToArray();

                return targets.Length == 0 && !AllowsZeroTargets(card)
                    ? TargetSelection.Invalid
                    : new TargetSelection(true, null, targetLaneId, targets);
            }

            if (hasUnitTarget && FindUnit(state, targetUnitId!) is { } unit && UnitCanBeTargetedByEffect(state, playerId, "damage", unit))
            {
                return new TargetSelection(true, targetUnitId, null, [UnitTargetFrom(state, unit, "damage", null)]);
            }

            return !hasUnitTarget && AllowsZeroTargets(card)
                ? new TargetSelection(true, null, null, [])
                : TargetSelection.Invalid;
        }

        if (effectType is "buff" or "rally")
        {
            if (hasUnitTarget && FindUnit(state, targetUnitId!) is { } unit && UnitCanBeTargetedByEffect(state, playerId, effectType, unit))
            {
                return new TargetSelection(true, targetUnitId, null, [UnitTargetFrom(state, unit, effectType, null)]);
            }

            return !hasUnitTarget && !hasLaneTarget && AllowsZeroTargets(card)
                ? new TargetSelection(true, null, null, [])
                : TargetSelection.Invalid;
        }

        return hasUnitTarget || hasLaneTarget
            ? TargetSelection.Invalid
            : new TargetSelection(true, null, null, []);
    }

    private static bool AllowsZeroTargets(JsonObject card)
    {
        var text = card["text"]?.GetValue<string>() ?? string.Empty;
        return text.Contains("up to", StringComparison.OrdinalIgnoreCase)
            || text.Contains("any number", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanPay(JsonObject player, ResourceCost cost) => BuildPaymentPlan(player, cost) is not null;

    private static bool CanPay(JsonObject player, int energy) => CanPay(player, new ResourceCost(Math.Max(0, energy), new Dictionary<Domain, int>(), 0));

    private static void PayCost(JsonObject player, int energy) => PayCost(player, new ResourceCost(Math.Max(0, energy), new Dictionary<Domain, int>(), 0));

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

    private static JsonObject ApplyDamage(JsonObject state, int playerId, int amount, IReadOnlyList<EffectTarget> targets)
    {
        foreach (var target in targets)
        {
            var unit = FindLegalResolvedTarget(state, playerId, "damage", target);
            if (unit is null)
            {
                continue;
            }

            unit["damage"] = (unit["damage"]?.GetValue<int>() ?? 0) + amount;
        }

        return state;
    }

    private static JsonObject ApplyUnitMod(JsonObject state, int playerId, IReadOnlyList<EffectTarget> targets, Func<JsonObject, JsonObject> modify)
    {
        foreach (var target in targets)
        {
            var unit = FindLegalResolvedTarget(state, playerId, target.EffectType, target);
            if (unit is not null)
            {
                modify(unit);
            }
        }

        return state;
    }

    private static JsonObject? FindObjectByUid(JsonObject state, string uid)
    {
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            foreach (var zoneName in new[] { "base", "baseGear" })
            {
                var found = FindObjectInArray(player[zoneName]!.AsArray(), uid);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var found = FindObjectInArray(battlefield["units"]!.AsArray(), uid);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static JsonObject? FindObjectInArray(JsonArray objects, string uid)
    {
        foreach (var node in objects)
        {
            var obj = node!.AsObject();
            if (obj["uid"]?.GetValue<string>() == uid)
            {
                return obj;
            }

            if (obj["attachedCards"] is JsonArray attachedCards)
            {
                var found = FindObjectInArray(attachedCards, uid);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static JsonObject? RemoveObjectByUid(JsonObject state, string uid)
    {
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            foreach (var zoneName in new[] { "base", "baseGear" })
            {
                var removed = RemoveObjectFromArray(player[zoneName]!.AsArray(), uid);
                if (removed is not null)
                {
                    return removed;
                }
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var removed = RemoveObjectFromArray(battlefield["units"]!.AsArray(), uid);
            if (removed is not null)
            {
                return removed;
            }
        }

        return null;
    }

    private static JsonObject? RemoveAttachedCard(JsonObject state, string uid)
    {
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            foreach (var zoneName in new[] { "base", "baseGear" })
            {
                var removed = RemoveAttachedCardFromArray(player[zoneName]!.AsArray(), uid);
                if (removed is not null)
                {
                    return removed;
                }
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var removed = RemoveAttachedCardFromArray(battlefield["units"]!.AsArray(), uid);
            if (removed is not null)
            {
                return removed;
            }
        }

        return null;
    }

    private static JsonObject? RemoveAttachedCardFromArray(JsonArray objects, string uid)
    {
        foreach (var node in objects)
        {
            var obj = node!.AsObject();
            if (obj["attachedCards"] is not JsonArray attachedCards)
            {
                continue;
            }

            for (var i = 0; i < attachedCards.Count; i++)
            {
                var attached = attachedCards[i]!.AsObject();
                if (attached["uid"]?.GetValue<string>() == uid)
                {
                    attachedCards.RemoveAt(i);
                    RecomputeTopCard(obj);
                    return attached;
                }

                if (attached["attachedCards"] is JsonArray nestedCards &&
                    RemoveObjectFromArray(nestedCards, uid) is { } nested)
                {
                    RecomputeTopCard(attached);
                    RecomputeTopCard(obj);
                    return nested;
                }
            }
        }

        return null;
    }

    private static JsonObject? RemoveObjectFromArray(JsonArray objects, string uid)
    {
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects[i]!.AsObject();
            if (obj["uid"]?.GetValue<string>() == uid)
            {
                objects.RemoveAt(i);
                return obj;
            }

            if (obj["attachedCards"] is JsonArray attachedCards)
            {
                var removed = RemoveObjectFromArray(attachedCards, uid);
                if (removed is not null)
                {
                    RecomputeTopCard(obj);
                    return removed;
                }
            }
        }

        return null;
    }

    private static JsonObject MoveObjectAndAttachmentsToOwnerZone(JsonObject state, JsonObject obj, string zoneName)
    {
        if (obj["attachedCards"] is JsonArray attachedCards)
        {
            while (attachedCards.Count > 0)
            {
                var attached = attachedCards[0]!.AsObject();
                attachedCards.RemoveAt(0);
                state = MoveObjectAndAttachmentsToOwnerZone(state, attached, zoneName);
            }
        }

        obj["attachedCards"] = new JsonArray();
        RecomputeTopCard(obj);
        obj["location"] = new JsonObject { ["type"] = zoneName == "banished" ? "banished" : "trash", ["battlefieldId"] = null, ["attachedToUid"] = null };
        obj["attachedUnitId"] = null;
        return PutObjectInOwnerZone(state, obj, zoneName);
    }

    private static JsonObject PutObjectInOwnerZone(JsonObject state, JsonObject obj, string zoneName)
    {
        if (obj["isToken"]?.GetValue<bool>() == true)
        {
            return state;
        }

        var ownerId = obj["ownerId"]?.GetValue<int>() ?? -1;
        return UpdatePlayer(state, ownerId, player =>
        {
            if (player[zoneName] is not JsonArray zone)
            {
                zone = new JsonArray();
                player[zoneName] = zone;
            }

            zone.Add(obj);
            return player;
        });
    }

    private static void RecomputeTopCard(JsonObject obj)
    {
        if (obj["attachedCards"] is JsonArray attachedCards && attachedCards.Count > 0)
        {
            var top = attachedCards[attachedCards.Count - 1]!.AsObject();
            obj["topCardId"] = top["topCardId"]?.GetValue<string>() ?? top["id"]?.GetValue<string>();
            return;
        }

        obj["topCardId"] = obj["isToken"]?.GetValue<bool>() == true
            ? null
            : obj["id"]?.GetValue<string>();
    }

    private static JsonObject? FindLegalResolvedTarget(JsonObject state, int playerId, string effectType, EffectTarget target)
    {
        var resolved = FindUnitWithPublicZone(state, target.UnitId);
        return resolved is not null
            && UnitCanBeTargetedByEffect(state, playerId, effectType, resolved.Value.Unit)
            && UnitIsStillInTargetedPublicZone(resolved.Value.ZoneType, resolved.Value.BattlefieldId, target)
                ? resolved.Value.Unit
                : null;
    }

    private static bool UnitCanBeTargetedByEffect(JsonObject state, int playerId, string effectType, JsonObject unit)
    {
        _ = state;
        var ownerId = unit["ownerId"]?.GetValue<int>();
        return effectType switch
        {
            "damage" => ownerId is not null && ownerId.Value != playerId,
            "buff" or "rally" => ownerId == playerId,
            _ => false
        };
    }

    private static bool UnitIsStillInTargetedPublicZone(string zoneType, string? battlefieldId, EffectTarget target)
    {
        return string.Equals(zoneType, target.ZoneType, StringComparison.Ordinal)
            && string.Equals(battlefieldId, target.BattlefieldId, StringComparison.Ordinal);
    }

    private static EffectTarget UnitTargetFrom(JsonObject state, JsonObject unit, string effectType, string? selectedBattlefieldId)
    {
        var unitId = unit["uid"]?.GetValue<string>() ?? string.Empty;
        var zone = FindUnitWithPublicZone(state, unitId);
        return new EffectTarget(
            UnitId: unitId,
            EffectType: effectType,
            ZoneType: selectedBattlefieldId is not null ? "battlefield" : zone?.ZoneType ?? "base",
            BattlefieldId: selectedBattlefieldId ?? zone?.BattlefieldId);
    }

    private static (JsonObject Unit, string ZoneType, string? BattlefieldId)? FindUnitWithPublicZone(JsonObject state, string unitId)
    {
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            var unit = player["base"]!.AsArray()
                .Select(node => node!.AsObject())
                .FirstOrDefault(candidate => candidate["uid"]?.GetValue<string>() == unitId);
            if (unit is not null)
            {
                return (unit, "base", null);
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var unit = battlefield["units"]!.AsArray()
                .Select(node => node!.AsObject())
                .FirstOrDefault(candidate => candidate["uid"]?.GetValue<string>() == unitId);
            if (unit is not null)
            {
                return (unit, "battlefield", battlefield["id"]?.GetValue<string>());
            }
        }

        return null;
    }

    private static JsonObject TargetToJson(EffectTarget target)
    {
        return new JsonObject
        {
            ["type"] = "unit",
            ["unitId"] = target.UnitId,
            ["effectType"] = target.EffectType,
            ["zoneType"] = target.ZoneType,
            ["battlefieldId"] = target.BattlefieldId
        };
    }

    private static IReadOnlyList<EffectTarget> ReadStackTargets(JsonObject item, string? legacyTargetUnitId, string? legacyTargetLaneId)
    {
        var effectType = item["effect"]?["type"]?.GetValue<string>() ?? "rally";
        if (item["targets"] is JsonArray targets)
        {
            return targets
                .Select(node => node?.AsObject())
                .Where(target => target is not null && target["type"]?.GetValue<string>() == "unit")
                .Select(target => new EffectTarget(
                    UnitId: target!["unitId"]?.GetValue<string>() ?? string.Empty,
                    EffectType: string.IsNullOrWhiteSpace(target["effectType"]?.GetValue<string>()) ? effectType : target["effectType"]!.GetValue<string>(),
                    ZoneType: target["zoneType"]?.GetValue<string>() ?? "base",
                    BattlefieldId: target["battlefieldId"]?.GetValue<string>()))
                .Where(target => !string.IsNullOrWhiteSpace(target.UnitId))
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(legacyTargetUnitId))
        {
            return [new EffectTarget(legacyTargetUnitId, effectType, "base", null)];
        }

        if (!string.IsNullOrWhiteSpace(legacyTargetLaneId))
        {
            return [];
        }

        return [];
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
        state["abilityContributions"] = BuildAbilityContributions(state);
        var players = seats.Select(seat => new EnginePlayerState(seat.PlayerId, seat.UserId, ReadPlayerPoints(state, seat.PlayerId), false)).ToArray();
        return new EngineMatchState(matchId, mode, state["stage"]?.GetValue<string>() ?? "mulligan", sequenceNumber, state, players);
    }

    private static EngineSeatConfig[] ActiveSeats(JsonObject state, IReadOnlyList<EnginePlayerState> previousPlayers)
    {
        var teamIds = state["teamIds"]?.Deserialize<int[]>(JsonOptions) ?? [];
        return state["players"]!.AsArray()
            .Select(node => node!.AsObject())
            .Select(player =>
            {
                var playerId = player["id"]!.GetValue<int>();
                var previous = previousPlayers.FirstOrDefault(candidate => candidate.PlayerId == playerId);
                return new EngineSeatConfig(
                    playerId,
                    previous?.UserId ?? $"player-{playerId}",
                    player["name"]?.GetValue<string>() ?? $"Player {playerId + 1}",
                    playerId >= 0 && playerId < teamIds.Length ? teamIds[playerId] : null);
            })
            .ToArray();
    }

    private static int[] ActivePlayerIds(JsonObject state)
    {
        return state["players"]!.AsArray()
            .Select(node => node!.AsObject()["id"]!.GetValue<int>())
            .ToArray();
    }

    private static int TeamIdForPlayer(int[] teamIds, int playerId)
    {
        return playerId >= 0 && playerId < teamIds.Length ? teamIds[playerId] : playerId;
    }

    private static int? NextAvailablePlayerId(int[] order, int playerId, Func<int, bool> isAvailable)
    {
        if (order.Length == 0)
        {
            return null;
        }

        var index = Array.IndexOf(order, playerId);
        var start = index < 0 ? 0 : index + 1;
        for (var offset = 0; offset < order.Length; offset++)
        {
            var candidate = order[(start + offset) % order.Length];
            if (isAvailable(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void RemoveObjectProperty(JsonNode? node, int playerId)
    {
        if (node is JsonObject obj)
        {
            obj.Remove(playerId.ToString());
        }
    }

    private static void RemoveArrayValue(JsonNode? node, int playerId)
    {
        if (node is not JsonArray array)
        {
            return;
        }

        for (var i = array.Count - 1; i >= 0; i--)
        {
            if (array[i]?.GetValue<int>() == playerId)
            {
                array.RemoveAt(i);
            }
        }
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
                ["keywords"] = ToArray(KeywordCatalog.For(definition).Select(ToKeywordObject)),
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
            ["keywords"] = new JsonArray(),
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

    private static JsonObject ToKeywordObject(CardKeywordDefinition keyword)
    {
        var obj = new JsonObject
        {
            ["kind"] = keyword.Kind.ToString(),
            ["behavior"] = keyword.Behavior.ToString()
        };
        if (keyword.Value is not null)
        {
            obj["value"] = keyword.Value.Value;
        }

        if (!string.IsNullOrWhiteSpace(keyword.Cost))
        {
            obj["cost"] = keyword.Cost;
        }

        if (!string.IsNullOrWhiteSpace(keyword.Text))
        {
            obj["text"] = keyword.Text;
        }

        return obj;
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

    private static string[] ReadUnitIds(IReadOnlyDictionary<string, object?>? payload)
    {
        var unitIds = ReadStringArray(payload, "unitIds");
        if (unitIds.Length > 0)
        {
            return unitIds;
        }

        var unitId = ReadString(payload, "unitId");
        return string.IsNullOrWhiteSpace(unitId) ? [] : [unitId];
    }

    private static string[] ReadStringArray(IReadOnlyDictionary<string, object?>? payload, string key)
    {
        if (payload is null || !payload.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        return value switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.Array => element.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!)
                .ToArray(),
            IEnumerable<string> values => values.Where(text => !string.IsNullOrWhiteSpace(text)).ToArray(),
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

    private static bool? ReadBool(IReadOnlyDictionary<string, object?>? payload, string key)
    {
        if (payload is null || !payload.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            bool boolValue => boolValue,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }

    private static bool HasKeyword(JsonObject card, KeywordKind kind) =>
        KeywordValue(card, kind, countPresence: true) > 0;

    private static int KeywordValue(JsonObject card, KeywordKind kind, bool countPresence = false)
    {
        var matching = Keywords(card).Where(keyword => KeywordKindName(keyword) == kind).ToArray();
        if (matching.Length == 0)
        {
            return 0;
        }

        if (countPresence)
        {
            return matching.Length;
        }

        return kind switch
        {
            KeywordKind.Assault or KeywordKind.Deflect or KeywordKind.Shield or KeywordKind.Hunt => matching.Sum(keyword => keyword["value"]?.GetValue<int>() ?? 1),
            _ => matching.Length
        };
    }

    private static IReadOnlyList<JsonObject> Keywords(JsonObject card)
    {
        if (card["keywords"] is JsonArray keywords && keywords.Count > 0)
        {
            return keywords.Select(node => node!.AsObject()).ToArray();
        }

        return KeywordCatalog.Parse(card["text"]?.GetValue<string>())
            .Select(ToKeywordObject)
            .ToArray();
    }

    private static KeywordKind? KeywordKindName(JsonObject keyword)
    {
        var value = keyword["kind"]?.GetValue<string>() ?? string.Empty;
        return Enum.TryParse<KeywordKind>(value, ignoreCase: true, out var kind) ? kind : null;
    }

    private static int? TryReadInstructionAmount(string text, string instruction)
    {
        var words = text.Split([' ', '.', ',', '[', ']'], StringSplitOptions.RemoveEmptyEntries);
        var instructionIndex = Array.FindIndex(words, word => string.Equals(word, instruction, StringComparison.OrdinalIgnoreCase));
        if (instructionIndex < 0)
        {
            return null;
        }

        for (var i = instructionIndex + 1; i < words.Length; i++)
        {
            if (int.TryParse(words[i], out var amount))
            {
                return amount;
            }
        }

        return 1;
    }

    private static ScoreSource ScoreSourceFrom(string? source)
    {
        return string.Equals(source, "hold", StringComparison.OrdinalIgnoreCase) ? ScoreSource.Hold : ScoreSource.Conquer;
    }

    private static string ScoreSourceValue(ScoreSource source)
    {
        return source == ScoreSource.Hold ? "hold" : "conquer";
    }

    private static string ChainItemStatusValue(ChainItemStatus status)
    {
        return status == ChainItemStatus.Finalized ? "finalized" : "pending";
    }

    private static string ChainItemSourceValue(ChainItemSource source)
    {
        return source switch
        {
            ChainItemSource.TriggeredAbility => "triggered",
            ChainItemSource.AddCreated => "add-created",
            _ => "played-card"
        };
    }

    private enum ScoreSource
    {
        Conquer,
        Hold
    }

    private enum MoveOrigin
    {
        Base,
        Battlefield
    }

    private sealed record MovableUnit(string UnitId, JsonObject Unit, JsonArray Source, MoveOrigin Origin);

    private sealed record AbilitySource(string Uid, string CardId, string Name, int OwnerId, JsonObject Card, IReadOnlyList<JsonObject> Abilities);

    private sealed record ReplacementResult(JsonObject State, int Amount, bool Prevented);

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

    private sealed record TargetSelection(
        bool IsValid,
        string? LegacyTargetUnitId,
        string? LegacyTargetLaneId,
        IReadOnlyList<EffectTarget> Targets)
    {
        public static TargetSelection Invalid { get; } = new(false, null, null, []);
    }

    private sealed record EffectTarget(
        string UnitId,
        string EffectType,
        string ZoneType,
        string? BattlefieldId);

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
