using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.Effects;
using riftbound_tcg.Engine.GameActions;

namespace riftbound_tcg.Engine.RulesEngine;

public sealed class DefaultRulesEngine : IRulesEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] PhaseOrder = ["awaken", "beginning", "channel", "draw", "main", "ending"];

    public EngineMatchState CreateInitialState(EngineMatchConfig config, IReadOnlyList<EnginePlayerDeck> playerDecks, int seed, IReadOnlyDictionary<string, CardDefinition>? catalog = null)
    {
        var mode = ModeConfig.For(config.Mode);
        var orderedSeats = config.Seats.OrderBy(seat => seat.PlayerId).ToArray();
        var teamIdsByPlayer = orderedSeats.Select(seat => seat.TeamId ?? seat.PlayerId).ToArray();
        var turnOrder = OrderedPlayerIds(mode.PlayerCount, config.FirstPlayerId, teamIdsByPlayer, config.Mode);
        var battlefieldContributors = BattlefieldContributorPlayerIds(config.Mode, orderedSeats, config.FirstPlayerId, mode.BattlefieldCount);
        var battlefieldSelections = SelectBattlefieldsForSetup(config.BattlefieldIds, playerDecks, battlefieldContributors, mode.BattlefieldCount, seed);
        var selectedBattlefieldByPlayer = battlefieldSelections
            .Where(selection => selection.ChosenByPlayerId is not null)
            .ToDictionary(selection => selection.ChosenByPlayerId!.Value, selection => selection.CatalogId);
        var players = orderedSeats
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
                    ["xp"] = 0,
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
                    ["battlefieldId"] = selectedBattlefieldByPlayer.TryGetValue(seat.PlayerId, out var selectedBattlefieldId) ? selectedBattlefieldId : string.Empty
                };
            })
            .ToArray();

        var battlefields = battlefieldSelections.Select((selection, index) => new JsonObject
        {
            ["id"] = $"{selection.CatalogId}-{index}",
            ["catalogId"] = selection.CatalogId,
            ["name"] = DisplayName(selection.CatalogId),
            ["claim"] = 2,
            ["chosenBy"] = selection.ChosenByPlayerId is null ? null : JsonValue.Create(selection.ChosenByPlayerId.Value),
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
            ["teamIds"] = ToArray(teamIdsByPlayer),
            ["hasPassedFocusByPlayer"] = new JsonObject(),
            ["scoredBattlefieldIdsThisTurn"] = new JsonObject(),
            ["teamScoreBlockedBattlefieldIdsThisTurn"] = new JsonObject(),
            ["teamFinalPointExemptBattlefieldIdsThisTurn"] = new JsonObject(),
            ["playedCardsThisTurnByPlayer"] = new JsonObject(),
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
            ["pendingTriggerGroups"] = new JsonArray(),
            ["delayedAbilities"] = new JsonArray(),
            ["abilityEvents"] = new JsonArray(),
            ["chainWindow"] = null,
            ["pendingBurnOut"] = null,
            ["pendingVision"] = null
        };

        return ToEngineState(config.MatchId, config.Mode, 0, state, config.Seats);
    }

    public IReadOnlyList<EngineLegalAction> GetLegalActions(EngineMatchState state, int playerId)
    {
        if (state.Players.All(player => player.PlayerId != playerId) || !ActivePlayerIds(state.State).Contains(playerId))
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

        if (stage == "playing" && PendingVision(state.State) is { } pendingVision)
        {
            var visionPlayerId = pendingVision["playerId"]?.GetValue<int>() ?? -1;
            if (visionPlayerId == playerId)
            {
                actions.Add(new(
                    "choose-vision-keep",
                    "choose-vision",
                    "Vision: keep top card",
                    playerId,
                    new JsonObject { ["recycle"] = false }));
                actions.Add(new(
                    "choose-vision-recycle",
                    "choose-vision",
                    "Vision: recycle top card",
                    playerId,
                    new JsonObject { ["recycle"] = true }));
            }

            return actions;
        }

        if (stage == "playing" && CurrentPendingTriggerGroup(state.State) is { } pendingTriggerGroup)
        {
            var triggerPlayerId = pendingTriggerGroup["playerId"]?.GetValue<int>() ?? -1;
            if (triggerPlayerId == playerId)
            {
                actions.Add(new(
                    $"order-triggered-abilities-{pendingTriggerGroup["id"]?.GetValue<string>() ?? "group"}",
                    "order-triggered-abilities",
                    "Order simultaneous triggered abilities",
                    playerId,
                    new JsonObject
                    {
                        ["groupId"] = pendingTriggerGroup["id"]?.GetValue<string>() ?? string.Empty,
                        ["abilityIds"] = ToArray((pendingTriggerGroup["abilities"] as JsonArray ?? new JsonArray())
                            .Select(ability => ability?["id"]?.GetValue<string>() ?? string.Empty)
                            .Where(id => !string.IsNullOrWhiteSpace(id)))
                    }));
            }

            return actions;
        }

        if (stage == "playing" && chainOpen)
        {
            var chainPriorityPlayerId = ChainPriorityPlayerId(state.State);
            if (chainPriorityPlayerId is null || chainPriorityPlayerId == playerId)
            {
                actions.Add(new("pass-chain-window", "pass-chain-window", "Pass priority", playerId));
                actions.AddRange(PlayableCardsFromHand(state.State, playerId, IsSpellOrGear));
                actions.AddRange(PlayableUnitActions(state.State, playerId));
            }
            else if (CanUsePriority(state.State, playerId, chainPriorityPlayerId.Value))
            {
                actions.AddRange(PlayableCardsFromHand(state.State, playerId, IsSpellOrGear));
                actions.AddRange(PlayableUnitActions(state.State, playerId));
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
                actions.AddRange(PlayableUnitActions(state.State, playerId));
                actions.AddRange(MoveUnitActions(state.State, playerId));
                actions.Add(new("create-token", "create-token", "Create token", playerId));
                actions.AddRange(AttachCardActions(state.State, playerId));
                actions.AddRange(EquipActions(state.State, playerId));
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
        else if (stage == "playing" && !IsShowdownOpen(state.State) && IsTeammate(state.State, playerId, turnPlayerId) && turnPhase == "main")
        {
            actions.AddRange(PlayableCardsFromHand(state.State, playerId, IsSpellOrGear));
        }

        if (stage == "playing" && IsShowdownOpen(state.State) && !CombatDamageRequired(state.State))
        {
            var focusPlayerId = state.State["focusPlayerId"]?.GetValue<int?>()
                ?? state.State["activePlayer"]?.GetValue<int?>()
                ?? turnPlayerId;
            if (focusPlayerId == playerId)
            {
                actions.Add(new("pass-focus", "pass-focus", "Pass focus", playerId));
                actions.AddRange(PlayableCardsFromHand(state.State, playerId, IsSpellOrGear));
                actions.AddRange(PlayableUnitActions(state.State, playerId));
                actions.AddRange(ActivatedAbilityActions(state.State, playerId));
            }
            else if (CanUsePriority(state.State, playerId, focusPlayerId))
            {
                actions.AddRange(PlayableCardsFromHand(state.State, playerId, IsSpellOrGear));
                actions.AddRange(PlayableUnitActions(state.State, playerId));
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
            if (!CanAttemptPayloadValidatedMainAction(state, action))
            {
                return Reject(state, $"Action '{action.ActionType}' is not legal for player '{action.PlayerId}'.");
            }
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
            case "choose-vision":
                var visionResult = ChooseVision(nextState, action.PlayerId, ReadBool(action.Payload, "recycle"));
                if (visionResult is null)
                {
                    return Reject(state, "Invalid choose-vision action: choose keep or recycle for the pending Vision card.");
                }

                nextState = visionResult;
                break;
            case "order-triggered-abilities":
                var orderTriggeredResult = OrderTriggeredAbilities(
                    nextState,
                    action.PlayerId,
                    ReadString(action.Payload, "groupId"),
                    ReadStringArray(action.Payload, "abilityIds"));
                if (orderTriggeredResult is null)
                {
                    return Reject(state, "Invalid order-triggered-abilities action: choose all pending ability ids exactly once.");
                }

                nextState = orderTriggeredResult;
                break;
            case "end-turn":
                nextState = EndCurrentTurn(nextState, action.PlayerId);
                break;
            case "play-unit":
                var playUnitResult = PlayUnit(
                    nextState,
                    action.PlayerId,
                    ReadInt(action.Payload, "handIndex"),
                    ReadString(action.Payload, "battlefieldId"),
                    ReadBool(action.Payload, "accelerate") == true,
                    ReadString(action.Payload, "weaponmasterGearUid"));
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
            case "equip":
                var equipResult = EquipGear(nextState, action.PlayerId, ReadString(action.Payload, "gearUid"), ReadString(action.Payload, "targetUnitId"));
                if (equipResult is null)
                {
                    return Reject(state, "Invalid equip action: choose controlled gear with Equip, a controlled unit, and pay the Equip cost.");
                }

                nextState = equipResult;
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
                var playCardResult = PlayCard(
                    nextState,
                    action.PlayerId,
                    ReadInt(action.Payload, "handIndex"),
                    ReadString(action.Payload, "targetUnitId"),
                    ReadString(action.Payload, "targetLaneId"),
                    ReadStringArray(action.Payload, "targetUnitIds"),
                    ReadInt(action.Payload, "repeatCount") ?? 0);
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

        if (ShouldCollectActionAppliedTriggers(action.ActionType))
        {
            nextState = CollectTriggeredAbilities(nextState, "action-applied", new JsonObject
            {
                ["playerId"] = action.PlayerId,
                ["actionType"] = action.ActionType
            });
        }

        if (!string.Equals(action.ActionType, "resolve-combat", StringComparison.OrdinalIgnoreCase))
        {
            nextState = RunFeprUntilChoiceRequired(nextState);
        }
        var resultPayload = BuildResultPayload(nextState);
        nextState.Remove("__scoreOutcomes");
        var next = ToEngineState(state.MatchId, state.Mode, state.SequenceNumber + 1, nextState, ActiveSeats(nextState, state.Players));
        return new EngineActionResult(true, "accepted", $"Accepted {action.ActionType}.", next, GetLegalActions(next, action.PlayerId), resultPayload);
    }

    private static bool ShouldCollectActionAppliedTriggers(string actionType)
    {
        return !string.Equals(actionType, "resolve-combat", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(actionType, "confirm-mulligan", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(actionType, "concede", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(actionType, "order-triggered-abilities", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(actionType, "pass-chain-window", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(actionType, "pass-focus", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(actionType, "choose-vision", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(actionType, "choose-burn-out-opponent", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanAttemptPayloadValidatedMainAction(EngineMatchState state, EngineGameAction action)
    {
        if (!string.Equals(action.ActionType, "play-unit", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(action.ActionType, "move-unit", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(action.ActionType, "attach-card", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(action.ActionType, "equip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var stage = state.State["stage"]?.GetValue<string>() ?? state.Stage;
        var turnPlayerId = state.State["turnPlayerId"]?.GetValue<int>() ?? -1;
        var turnPhase = state.State["turnPhase"]?.GetValue<string>() ?? string.Empty;
        return ActivePlayerIds(state.State).Contains(action.PlayerId) &&
            string.Equals(stage, "playing", StringComparison.OrdinalIgnoreCase) &&
            turnPlayerId == action.PlayerId &&
            string.Equals(turnPhase, "main", StringComparison.OrdinalIgnoreCase) &&
            state.State["chainWindow"] is null &&
            PendingBurnOut(state.State) is null &&
            PendingVision(state.State) is null &&
            CurrentPendingTriggerGroup(state.State) is null &&
            !IsShowdownOpen(state.State);
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

        var activePlayerIds = oldOrder.Where(id => FindPlayer(state, id) is not null).ToArray();
        if (activePlayerIds.Length == 0)
        {
            activePlayerIds = state["players"]!.AsArray()
                .Select(node => node!.AsObject()["id"]!.GetValue<int>())
                .ToArray();
        }

        state["turnOrder"] = ToArray(activePlayerIds);
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
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            foreach (var zoneName in new[] { "base", "baseGear" })
            {
                RemovePlayerOwnedOrControlledObjects(state, player[zoneName]!.AsArray(), playerId);
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            RemovePlayerOwnedOrControlledObjects(state, battlefield["units"]!.AsArray(), playerId);
            RemovePlayerHiddenCards(battlefield, playerId);

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
                battlefield["text"] = string.Empty;
                battlefield["abilities"] = new JsonArray();
                battlefield["effect"] = new JsonObject { ["type"] = "rally", ["amount"] = 0 };
            }

            NormalizeBattlefieldAfterPlayerRemoval(state, battlefield);
        }

        var stack = state["effectStack"]!.AsArray();
        for (var i = stack.Count - 1; i >= 0; i--)
        {
            if (IsControlledByRemovedPlayer(stack[i]!.AsObject(), playerId))
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

        RemovePendingPlayerWork(state, playerId);

        if (stack.Count == 0 && state["chainWindow"] is JsonObject chainWindow)
        {
            CloseChainWindow(
                state,
                chainWindow["startedByPlayerId"]?.GetValue<int>() ?? state["turnPlayerId"]?.GetValue<int>() ?? 0,
                chainWindow["passesFocusOnClose"]?.GetValue<bool>() ?? true);
        }

        if (PendingBurnOut(state)?["playerId"]?.GetValue<int?>() == playerId)
        {
            state["pendingBurnOut"] = null;
        }

        if (PendingVision(state)?["playerId"]?.GetValue<int?>() == playerId)
        {
            state["pendingVision"] = null;
        }
    }

    private static void RemovePlayerOwnedOrControlledObjects(JsonObject state, JsonArray objects, int playerId)
    {
        for (var i = objects.Count - 1; i >= 0; i--)
        {
            var obj = objects[i]!.AsObject();
            if (obj["attachedCards"] is JsonArray attachedCards)
            {
                RemovePlayerOwnedOrControlledObjects(state, attachedCards, playerId);
                RecomputeTopCard(obj);
            }

            if (IsControlledByRemovedPlayer(obj, playerId))
            {
                objects.RemoveAt(i);
            }
        }
    }

    private static bool IsControlledByRemovedPlayer(JsonObject obj, int playerId)
    {
        if (obj["playerId"]?.GetValue<int?>() == playerId ||
            obj["ownerId"]?.GetValue<int?>() == playerId ||
            obj["controllerId"]?.GetValue<int?>() == playerId)
        {
            return true;
        }

        if (obj["card"] is JsonObject card)
        {
            return IsControlledByRemovedPlayer(card, playerId);
        }

        if (obj["source"] is JsonObject source)
        {
            return IsControlledByRemovedPlayer(source, playerId);
        }

        return false;
    }

    private static void RemovePlayerHiddenCards(JsonObject battlefield, int playerId)
    {
        var hiddenCards = battlefield["hiddenCards"] as JsonArray;
        if (hiddenCards is null)
        {
            return;
        }

        for (var i = hiddenCards.Count - 1; i >= 0; i--)
        {
            var hidden = hiddenCards[i]!.AsObject();
            if (IsControlledByRemovedPlayer(hidden, playerId))
            {
                hiddenCards.RemoveAt(i);
            }
        }
    }

    private static void NormalizeBattlefieldAfterPlayerRemoval(JsonObject state, JsonObject battlefield)
    {
        var units = battlefield["units"]!.AsArray().Select(node => node!.AsObject()).ToArray();
        var ownerIds = units
            .Select(unit => unit["ownerId"]?.GetValue<int>())
            .Where(owner => owner is not null)
            .Select(owner => owner!.Value)
            .Distinct()
            .ToArray();
        var teamIds = ownerIds.Select(ownerId => TeamIdForPlayer(state, ownerId)).Distinct().ToArray();

        if (ownerIds.Length == 0)
        {
            battlefield["controllerId"] = null;
            battlefield["contestedByPlayerId"] = null;
            battlefield["stagedShowdown"] = false;
            battlefield["stagedCombat"] = false;
            return;
        }

        var controllerId = battlefield["controllerId"]?.GetValue<int?>();
        if (controllerId is not null && FindPlayer(state, controllerId.Value) is null)
        {
            battlefield["controllerId"] = null;
        }

        var contestedBy = battlefield["contestedByPlayerId"]?.GetValue<int?>();
        if (contestedBy is not null && !ownerIds.Contains(contestedBy.Value))
        {
            battlefield["contestedByPlayerId"] = null;
        }

        if (teamIds.Length == 1)
        {
            var controller = ownerIds.FirstOrDefault(ownerId => battlefield["controllerId"]?.GetValue<int?>() == ownerId, ownerIds[0]);
            battlefield["controllerId"] = controller;
            battlefield["contestedByPlayerId"] = null;
            battlefield["stagedShowdown"] = false;
            battlefield["stagedCombat"] = false;
            return;
        }

        if (teamIds.Length == 2 && battlefield["contestedByPlayerId"] is not null)
        {
            battlefield["controllerId"] = null;
            battlefield["stagedShowdown"] = true;
            battlefield["stagedCombat"] = true;
        }
    }

    private static void RemovePendingPlayerWork(JsonObject state, int playerId)
    {
        RemovePendingItemsByPlayer(state["pendingTriggeredAbilities"] as JsonArray, playerId);
        RemovePendingItemsByPlayer(state["pendingTriggerGroups"] as JsonArray, playerId);
        RemovePendingItemsByPlayer(state["delayedAbilities"] as JsonArray, playerId);
        RemovePendingItemsByPlayer(state["abilityEvents"] as JsonArray, playerId);
        RemoveObjectProperty(state["chainWindow"]?["passedByPlayer"], playerId);
    }

    private static void RemovePendingItemsByPlayer(JsonArray? items, int playerId)
    {
        if (items is null)
        {
            return;
        }

        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (IsControlledByRemovedPlayer(items[i]!.AsObject(), playerId))
            {
                items.RemoveAt(i);
            }
        }
    }

    private static void RemovePlayerState(JsonObject state, int playerId)
    {
        var teamId = TeamIdForPlayer(state, playerId);
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
        RemoveObjectProperty(state["teamScoreBlockedBattlefieldIdsThisTurn"], teamId);
        RemoveObjectProperty(state["teamFinalPointExemptBattlefieldIdsThisTurn"], teamId);
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
            MarkTeamBeginningPhaseRestrictions(state, playerId);
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
        state["teamScoreBlockedBattlefieldIdsThisTurn"] = new JsonObject();
        state["teamFinalPointExemptBattlefieldIdsThisTurn"] = new JsonObject();
        state["playedCardsThisTurnByPlayer"] = new JsonObject();
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

        var scoringTeamId = TeamIdForPlayer(state, request.PlayerId);
        if (BlockedBattlefieldIdsForTeam(state, scoringTeamId).Contains(request.BattlefieldId, StringComparer.OrdinalIgnoreCase))
        {
            return AppendScoreOutcome(state, new ScoreOutcome(request.PlayerId, request.BattlefieldId, request.Source, 0, "teammate-controlled-at-beginning"));
        }

        var scoredNow = alreadyScored.Append(request.BattlefieldId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var scored = state["scoredBattlefieldIdsThisTurn"]!.AsObject();
        scored[request.PlayerId.ToString()] = ToArray(scoredNow);
        state = AwardHuntXpForScoring(state, request, battlefield);

        if (request.Source == ScoreSource.Conquer)
        {
            var victoryScore = ScoreRules.VictoryScore(state);
            var currentPoints = TeamPoints(state, scoringTeamId);
            var allBattlefieldIds = state["battlefields"]!.AsArray()
                .Select(node => node!["id"]?.GetValue<string>() ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Where(id => !FinalPointExemptBattlefieldIdsForTeam(state, scoringTeamId).Contains(id, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var teamScoredNow = ScoredBattlefieldIdsForTeam(state, scoringTeamId).Append(request.BattlefieldId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var hasScoredEveryBattlefield = allBattlefieldIds.All(id => teamScoredNow.Contains(id, StringComparer.OrdinalIgnoreCase));
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

    private static void MarkTeamBeginningPhaseRestrictions(JsonObject state, int playerId)
    {
        if (!IsTeamMode(state))
        {
            return;
        }

        var teamId = TeamIdForPlayer(state, playerId);
        var teammateIds = TeammatePlayerIds(state, playerId);
        if (teammateIds.Length == 0)
        {
            return;
        }

        var blocked = BlockedBattlefieldIdsForTeam(state, teamId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exempt = FinalPointExemptBattlefieldIdsForTeam(state, teamId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var battlefieldId = battlefield["id"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(battlefieldId))
            {
                continue;
            }

            if (battlefield["controllerId"]?.GetValue<int?>() is { } controllerId && teammateIds.Contains(controllerId))
            {
                blocked.Add(battlefieldId);
            }

            if (battlefield["units"]!.AsArray().Any(unit => teammateIds.Contains(unit!["ownerId"]?.GetValue<int>() ?? -1)))
            {
                exempt.Add(battlefieldId);
            }
        }

        var blockedByTeam = state["teamScoreBlockedBattlefieldIdsThisTurn"]!.AsObject();
        blockedByTeam[teamId.ToString()] = ToArray(blocked);
        var exemptByTeam = state["teamFinalPointExemptBattlefieldIdsThisTurn"]!.AsObject();
        exemptByTeam[teamId.ToString()] = ToArray(exempt);
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
        foreach (var teamId in ActivePlayerIds(state).Select(id => TeamIdForPlayer(state, id)).Distinct())
        {
            if (TeamPoints(state, teamId) < victoryScore) continue;
            state["stage"] = "game-over";
            state["winningTeamId"] = teamId;
            state["winner"] = ActivePlayerIds(state).First(id => TeamIdForPlayer(state, id) == teamId);
            return AddLog(state, IsTeamMode(state) ? $"Team {teamId} wins the match." : $"{PlayerName(state, state["winner"]!.GetValue<int>())} wins the match.");
        }

        return state;
    }

    private static JsonObject AwardHuntXpForScoring(JsonObject state, ScoreRequest request, JsonObject battlefield)
    {
        var huntXp = battlefield["units"]!.AsArray()
            .Select(node => node!.AsObject())
            .Where(unit => UnitControllerId(unit) == request.PlayerId)
            .Sum(unit => KeywordValue(state, unit, KeywordKind.Hunt));
        if (huntXp <= 0)
        {
            return state;
        }

        state = AddXp(state, request.PlayerId, huntXp);
        return AddLog(state, $"{PlayerName(state, request.PlayerId)} gained {huntXp} XP from Hunt.");
    }

    private static JsonObject AddXp(JsonObject state, int playerId, int amount)
    {
        if (amount <= 0)
        {
            return state;
        }

        return UpdatePlayer(state, playerId, player =>
        {
            player["xp"] = Math.Max(0, (player["xp"]?.GetValue<int>() ?? 0) + amount);
            return player;
        });
    }

    private static JsonObject DrawAvailable(JsonObject state, int playerId, int amount) =>
        amount <= 0
            ? state
            : ExecuteInternalGameAction(state, new InternalGameAction(
                InternalGameActionType.Draw,
                playerId,
                new Dictionary<string, object?> { ["amount"] = amount }));

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

            state = DrawAvailable(state, playerId, draw.Amount);
            state = CollectTriggeredAbilities(state, "cards-drawn", new JsonObject
            {
                ["playerId"] = playerId,
                ["amount"] = draw.Amount
            });
            return state;
        }

        var drawn = deck.Count;
        state = DrawAvailable(state, playerId, drawn);
        if (drawn > 0)
        {
            state = CollectTriggeredAbilities(state, "cards-drawn", new JsonObject
            {
                ["playerId"] = playerId,
                ["amount"] = drawn
            });
        }
        state = RecycleTrashIntoMainDeck(state, playerId);
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

    private static JsonObject QueueVisionChoice(JsonObject state, int playerId, JsonObject permanent)
    {
        var visionCount = KeywordValue(permanent, KeywordKind.Vision, countPresence: true);
        if (visionCount <= 0 || PendingVision(state) is not null)
        {
            return state;
        }

        return StartVisionChoice(state, playerId, permanent, visionCount);
    }

    private static JsonObject StartVisionChoice(JsonObject state, int playerId, JsonObject permanent, int remainingChoices)
    {
        var player = FindPlayer(state, playerId);
        var deck = player?["deck"]?.AsArray();
        if (player is null || deck is null || deck.Count == 0 || remainingChoices <= 0)
        {
            state["pendingVision"] = null;
            return state;
        }

        var sourceName = permanent["name"]?.GetValue<string>() ?? "a permanent";
        state["pendingVision"] = new JsonObject
        {
            ["playerId"] = playerId,
            ["sourceUid"] = permanent["uid"]?.GetValue<string>(),
            ["sourceCardId"] = permanent["catalogId"]?.GetValue<string>() ?? permanent["id"]?.GetValue<string>(),
            ["sourceName"] = sourceName,
            ["remainingChoices"] = remainingChoices,
            ["card"] = deck[0]?.DeepClone()
        };

        return AddLog(state, $"{PlayerName(state, playerId)} is resolving Vision from {sourceName}.");
    }

    private static JsonObject? ChooseVision(JsonObject state, int playerId, bool? recycle)
    {
        var pending = PendingVision(state);
        if (pending is null || recycle is null || pending["playerId"]?.GetValue<int>() != playerId)
        {
            return null;
        }

        var remainingChoices = pending["remainingChoices"]?.GetValue<int>() ?? 1;
        var sourceUid = pending["sourceUid"]?.GetValue<string>();
        var sourceName = pending["sourceName"]?.GetValue<string>() ?? "Vision";
        var source = string.IsNullOrWhiteSpace(sourceUid) ? null : FindObjectByUid(state, sourceUid);

        state["pendingVision"] = null;
        var player = FindPlayer(state, playerId);
        var deck = player?["deck"]?.AsArray();
        if (player is null || deck is null)
        {
            return state;
        }

        if (recycle.Value && deck.Count > 0)
        {
            var top = deck[0]?.DeepClone();
            deck.RemoveAt(0);
            deck.Add(top);
            state = AddLog(state, $"{PlayerName(state, playerId)} recycled the top card with Vision.");
        }
        else
        {
            state = AddLog(state, $"{PlayerName(state, playerId)} kept the top card with Vision.");
        }

        remainingChoices -= 1;
        if (remainingChoices > 0 && source is not null)
        {
            state = StartVisionChoice(state, playerId, source, remainingChoices);
        }

        return state;
    }

    private static JsonObject RecycleTrashIntoMainDeck(JsonObject state, int playerId)
    {
        var player = FindPlayer(state, playerId);
        if (player is null)
        {
            return state;
        }

        var trash = player["trash"]!.AsArray();
        if (trash.Count == 0)
        {
            return state;
        }

        return ExecuteInternalGameAction(state, new InternalGameAction(
            InternalGameActionType.Recycle,
            playerId,
            new Dictionary<string, object?>()));
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

    private static JsonObject? PendingVision(JsonObject state)
    {
        return state["pendingVision"] as JsonObject;
    }

    private static JsonObject MarkCardPlayedThisTurn(JsonObject state, int playerId, JsonObject card, string? uid)
    {
        var playedByPlayer = state["playedCardsThisTurnByPlayer"] as JsonObject ?? new JsonObject();
        state["playedCardsThisTurnByPlayer"] = playedByPlayer;
        var key = playerId.ToString();
        var played = playedByPlayer[key] as JsonArray ?? new JsonArray();
        playedByPlayer[key] = played;
        played.Add(new JsonObject
        {
            ["uid"] = uid,
            ["cardId"] = card["catalogId"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty
        });
        return state;
    }

    private static bool HasPlayedAnotherCardThisTurn(JsonObject state, int playerId, string? sourceUid)
    {
        if (state["playedCardsThisTurnByPlayer"]?[playerId.ToString()] is not JsonArray played)
        {
            return false;
        }

        return played
            .Select(node => node!.AsObject())
            .Any(entry => string.IsNullOrWhiteSpace(sourceUid) || !string.Equals(entry["uid"]?.GetValue<string>(), sourceUid, StringComparison.Ordinal));
    }

    private static int[] OpponentPlayerIds(JsonObject state, int playerId)
    {
        return ActivePlayerIds(state).Where(id => id != playerId && !IsFriendlyPlayer(state, playerId, id)).ToArray();
    }

    private static int[] TeammatePlayerIds(JsonObject state, int playerId)
    {
        return ActivePlayerIds(state).Where(id => id != playerId && IsFriendlyPlayer(state, playerId, id)).ToArray();
    }

    private static int[] ActivePlayerIds(JsonObject state)
    {
        return state["turnOrder"]?.Deserialize<int[]>(JsonOptions)
            ?? state["players"]!.AsArray().Select(player => player!["id"]!.GetValue<int>()).ToArray();
    }

    private static bool IsTeamMode(JsonObject state)
    {
        var activeTeams = ActivePlayerIds(state).Select(id => TeamIdForPlayer(state, id)).Distinct().Count();
        return ActivePlayerIds(state).Length > activeTeams;
    }

    private static bool CanUsePriority(JsonObject state, int playerId, int priorityPlayerId)
    {
        return playerId == priorityPlayerId || IsTeammate(state, playerId, priorityPlayerId);
    }

    private static bool IsTeammate(JsonObject state, int playerId, int otherPlayerId)
    {
        return playerId != otherPlayerId && IsFriendlyPlayer(state, playerId, otherPlayerId);
    }

    private static bool IsFriendlyPlayer(JsonObject state, int playerId, int otherPlayerId)
    {
        return TeamIdForPlayer(state, playerId) == TeamIdForPlayer(state, otherPlayerId);
    }

    private static bool IsFriendlyUnit(JsonObject state, int playerId, JsonObject unit)
    {
        var controllerId = UnitControllerId(unit);
        return controllerId >= 0 && IsFriendlyPlayer(state, playerId, controllerId);
    }

    private static bool IsEnemyUnit(JsonObject state, int playerId, JsonObject unit)
    {
        var controllerId = UnitControllerId(unit);
        return controllerId >= 0 && !IsFriendlyPlayer(state, playerId, controllerId);
    }

    private static int TeamIdForPlayer(JsonObject state, int playerId)
    {
        var teamIds = state["teamIds"]?.Deserialize<int[]>(JsonOptions) ?? [];
        return playerId >= 0 && playerId < teamIds.Length ? teamIds[playerId] : playerId;
    }

    private static int TeamPoints(JsonObject state, int teamId)
    {
        return state["players"]!.AsArray()
            .Select(node => node!.AsObject())
            .Where(player => TeamIdForPlayer(state, player["id"]!.GetValue<int>()) == teamId)
            .Sum(player => player["points"]?.GetValue<int>() ?? 0);
    }

    private static string[] ScoredBattlefieldIdsForTeam(JsonObject state, int teamId)
    {
        return ActivePlayerIds(state)
            .Where(id => TeamIdForPlayer(state, id) == teamId)
            .SelectMany(id => ScoredBattlefieldIds(state, id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] BlockedBattlefieldIdsForTeam(JsonObject state, int teamId)
    {
        return state["teamScoreBlockedBattlefieldIdsThisTurn"]?[teamId.ToString()]?.Deserialize<string[]>(JsonOptions) ?? [];
    }

    private static string[] FinalPointExemptBattlefieldIdsForTeam(JsonObject state, int teamId)
    {
        return state["teamFinalPointExemptBattlefieldIdsThisTurn"]?[teamId.ToString()]?.Deserialize<string[]>(JsonOptions) ?? [];
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

    private static IReadOnlyList<int> BattlefieldContributorPlayerIds(string mode, IReadOnlyList<EngineSeatConfig> seats, int firstPlayerId, int battlefieldCount)
    {
        var contributorIds = seats
            .OrderBy(seat => seat.PlayerId)
            .Select(seat => seat.PlayerId);

        if (mode is "ffa-4" or "teams-2v2")
        {
            contributorIds = contributorIds.Where(playerId => playerId != firstPlayerId);
        }

        return contributorIds.Take(battlefieldCount).ToArray();
    }

    private static IReadOnlyList<BattlefieldSetupSelection> SelectBattlefieldsForSetup(
        IReadOnlyList<string> explicitBattlefieldIds,
        IReadOnlyList<EnginePlayerDeck> playerDecks,
        IReadOnlyList<int> contributorPlayerIds,
        int battlefieldCount,
        int seed)
    {
        var selections = explicitBattlefieldIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Take(battlefieldCount)
            .Select((id, index) => new BattlefieldSetupSelection(id, ContributorAt(contributorPlayerIds, index)))
            .ToList();

        foreach (var contributorId in contributorPlayerIds.Skip(selections.Count))
        {
            if (selections.Count >= battlefieldCount)
            {
                break;
            }

            var deck = contributorId >= 0 && contributorId < playerDecks.Count ? playerDecks[contributorId] : null;
            var options = deck?.BattlefieldDeckIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
            if (options.Length == 0)
            {
                continue;
            }

            var chosen = Shuffle(options.ToList(), seed + contributorId + 83)[0];
            selections.Add(new BattlefieldSetupSelection(chosen, contributorId));
        }

        if (selections.Count < battlefieldCount)
        {
            foreach (var id in playerDecks
                .SelectMany(deck => deck.BattlefieldDeckIds)
                .Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                if (selections.Count >= battlefieldCount)
                {
                    break;
                }

                selections.Add(new BattlefieldSetupSelection(id, null));
            }
        }

        return selections.Take(battlefieldCount).ToArray();
    }

    private static int? ContributorAt(IReadOnlyList<int> contributorPlayerIds, int index) =>
        index >= 0 && index < contributorPlayerIds.Count ? contributorPlayerIds[index] : null;

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

        var nextUid = state["nextUid"]?.GetValue<int>() ?? 1;
        var unit = Clone(champion);
        unit["uid"] = $"unit-{nextUid}";
        unit["layerTimestamp"] = nextUid;
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
            p["champion"] = null;
            p["base"]!.AsArray().Add(unit);
            return p;
        });

        state = AddLog(state, $"{PlayerName(state, playerId)} summoned {champion["name"]?.GetValue<string>() ?? "their champion"} to their base.");
        state = QueueVisionChoice(state, playerId, unit);
        return CollectTriggeredAbilities(state, "unit-entered", new JsonObject
        {
            ["playerId"] = playerId,
            ["sourceUid"] = unit["uid"]?.GetValue<string>(),
            ["zone"] = "base"
        });
    }

    private static JsonObject? PlayCard(JsonObject state, int playerId, int? handIndex, string? targetUnitId, string? targetLaneId, IReadOnlyList<string>? targetUnitIds, int repeatCount)
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

        var targetSelection = ValidateTargetSelection(state, playerId, card, targetUnitId, targetLaneId, targetUnitIds);
        if (!targetSelection.IsValid)
        {
            return null;
        }

        if (!ValidateRepeatChoice(card, repeatCount))
        {
            return null;
        }

        var cost = AdjustedCardCost(state, playerId, card, ReadCost(card));
        cost = AddCosts(cost, RepeatCost(card, repeatCount));
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

        return FinalizePendingCardPlay(state, playerId, card, targetSelection, repeatCount);
    }

    private static JsonObject FinalizePendingCardPlay(JsonObject state, int playerId, JsonObject card, TargetSelection targetSelection, int repeatCount)
    {
        var kind = card["kind"]?.GetValue<string>() ?? string.Empty;
        if (kind == "gear")
        {
            state = PutResolvedGearIntoBase(state, playerId, card);
            state = MarkCardPlayedThisTurn(state, playerId, card, null);
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
            ["card"] = OwnedZoneCard(card, playerId),
            ["cardId"] = card["catalogId"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty,
            ["cardName"] = card["name"]?.GetValue<string>() ?? "a card",
            ["kind"] = kind,
            ["playerId"] = playerId,
            ["effect"] = card["effect"]?.DeepClone(),
            ["targetUnitId"] = targetSelection.LegacyTargetUnitId,
            ["targetLaneId"] = targetSelection.LegacyTargetLaneId,
            ["targets"] = ToArray(targetSelection.Targets.Select(TargetToJson)),
            ["repeatCount"] = repeatCount,
            ["status"] = ChainItemStatusValue(ChainItemStatus.Pending),
            ["source"] = ChainItemSourceValue(ChainItemSource.PlayedCard)
        };
        state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;
        state["effectStack"]!.AsArray().Insert(0, stackItem);
        state = MarkCardPlayedThisTurn(state, playerId, card, stackItem["id"]?.GetValue<string>());
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
            var startedBy = chainWindow["startedByPlayerId"]?.GetValue<int>() ?? playerId;
            var passesFocusOnClose = chainWindow["passesFocusOnClose"]?.GetValue<bool>() ?? true;
            state["chainWindow"] = null;
            state["priorityPlayerId"] = null;
            state = ResolveTopStackItem(state);
            state = RunOutstandingTasks(state);
            if (state["chainWindow"] is not null ||
                PendingBurnOut(state) is not null ||
                PendingVision(state) is not null ||
                CurrentPendingTriggerGroup(state) is not null)
            {
                return state;
            }

            var stack = state["effectStack"]!.AsArray();
            if (stack.Count > 0)
            {
                var nextPriority = stack[0]!["playerId"]?.GetValue<int>() ?? playerId;
                var nextSource = stack[0]!["source"]?.GetValue<string>() ?? ChainItemSourceValue(ChainItemSource.PlayedCard);
                OpenChainWindow(state, nextPriority, nextPriority, nextSource);
            }
            else
            {
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
        var targetUnitId = item["targetUnitId"]?.GetValue<string>();
        var targetLaneId = item["targetLaneId"]?.GetValue<string>();
        var targets = ReadStackTargets(item, targetUnitId, targetLaneId);
        var executionCount = 1 + Math.Max(0, item["repeatCount"]?.GetValue<int>() ?? 0);
        var card = item["card"]!.AsObject();

        for (var execution = 0; execution < executionCount; execution++)
        {
            if (effect?["steps"] is JsonArray steps && steps.Count > 0)
            {
                // Each target-requiring step consumes its own target(s) in order, so
                // "Deal 4 to a unit. Draw 1." doesn't apply the draw to the damage target.
                // [Repeat] re-runs this whole sequence against the same originally-chosen targets.
                var remainingTargets = targets.ToList();
                foreach (var stepNode in steps)
                {
                    var step = stepNode!.AsObject();
                    var stepType = step["type"]?.GetValue<string>() ?? "rally";
                    var stepAmount = step["amount"]?.GetValue<int>() ?? 0;
                    state = ResolveEffectThroughInternalActions(state, playerId, stepType, stepAmount, TakeStepTargets(remainingTargets, stepType), card);
                }
            }
            else
            {
                // Legacy single-instruction cards apply to every chosen/legal target (e.g. lane-wide damage).
                var effectType = effect?["type"]?.GetValue<string>() ?? "rally";
                var amount = effect?["amount"]?.GetValue<int>() ?? 0;
                state = IsDelayedCreationEffect(effectType)
                    ? CreateDelayedAbility(state, item, playerId, effect!)
                    : ResolveEffectThroughInternalActions(state, playerId, effectType, amount, targets, card);
            }

            if (PendingBurnOut(state) is not null || state["stage"]?.GetValue<string>() == "game-over")
            {
                break;
            }
        }

        var kind = item["kind"]?.GetValue<string>() ?? card["kind"]?.GetValue<string>() ?? string.Empty;
        if (kind == "gear")
        {
            state = PutResolvedGearIntoBase(state, playerId, card);
        }
        else if (kind == "spell")
        {
            state = PutObjectInOwnerZone(state, card.DeepClone().AsObject(), "trash");
        }

        return AddLog(state, $"{item["cardName"]?.GetValue<string>() ?? "A card"} resolved.");
    }

    private static JsonObject PutResolvedGearIntoBase(JsonObject state, int playerId, JsonObject card)
    {
        var nextUid = state["nextUid"]?.GetValue<int>() ?? 1;
        var gear = Clone(card);
        gear["uid"] = $"gear-{nextUid}";
        gear["layerTimestamp"] = nextUid;
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
        state["nextUid"] = nextUid + 1;

        state = UpdatePlayer(state, playerId, player =>
        {
            player["baseGear"]!.AsArray().Add(gear);
            return player;
        });

        return QueueVisionChoice(state, playerId, gear);
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

        var nextUid = state["nextUid"]?.GetValue<int>() ?? 1;
        var hiddenCard = Clone(card);
        hiddenCard["uid"] = $"hidden-{nextUid}";
        hiddenCard["layerTimestamp"] = nextUid;
        InformationModel.MarkHiddenFacedown(hiddenCard, playerId, state["turnNumber"]?.GetValue<int>() ?? 1, battlefieldId);
        state["nextUid"] = nextUid + 1;
        hiddenCards.Add(hiddenCard);

        return AddLog(state, $"{PlayerName(state, playerId)} hid a card at {battlefield["name"]?.GetValue<string>() ?? "a battlefield"}.");
    }

    private static JsonObject? PlayUnit(JsonObject state, int playerId, int? handIndex, string? battlefieldId, bool accelerate, string? weaponmasterGearUid)
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
        if (!IsPlayableUnitCard(card))
        {
            return null;
        }

        JsonObject? battlefield = null;
        if (!string.IsNullOrWhiteSpace(battlefieldId))
        {
            battlefield = state["battlefields"]!.AsArray()
                .Select(node => node!.AsObject())
                .FirstOrDefault(candidate => candidate["id"]?.GetValue<string>() == battlefieldId);
            if (battlefield is null || !CanPlayUnitToBattlefield(state, playerId, card, battlefield))
            {
                return null;
            }
        }
        else if (!CanPlayUnitToBase(state, playerId, card))
        {
            return null;
        }

        var unitCost = AdjustedCardCost(state, playerId, card, ReadCost(card));
        if (accelerate && !HasKeyword(card, KeywordKind.Accelerate))
        {
            return null;
        }

        if (accelerate)
        {
            unitCost = unitCost with { Energy = unitCost.Energy + 2 };
        }

        var totalCost = unitCost;
        if (!string.IsNullOrWhiteSpace(weaponmasterGearUid))
        {
            if (!HasKeyword(card, KeywordKind.Weaponmaster))
            {
                return null;
            }

            var gear = FindBaseGear(state, weaponmasterGearUid);
            if (gear is null || !CanWeaponmasterChooseGear(playerId, gear.Gear))
            {
                return null;
            }

            totalCost = AddCosts(totalCost, DiscountCost(EquipCost(gear.Gear), energyDiscount: 1));
        }

        if (!CanPay(player, totalCost))
        {
            return null;
        }

        var nextUid = state["nextUid"]?.GetValue<int>() ?? 1;
        var unit = Clone(card);
        unit["uid"] = $"unit-{nextUid}";
        unit["layerTimestamp"] = nextUid;
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

        state["nextUid"] = nextUid + 1;

        state = UpdatePlayer(state, playerId, p =>
        {
            p["hand"]!.AsArray().RemoveAt(handIndex.Value);
            PayCost(p, unitCost);
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
            state = UpdateBattlefieldAfterUnitPlay(state, playerId, battlefield);
        }

        var destinationLabel = battlefield is null ? "their base" : battlefield["name"]?.GetValue<string>() ?? "a battlefield";
        state = AddLog(state, $"{PlayerName(state, playerId)} played {card["name"]?.GetValue<string>() ?? "a unit"} to {destinationLabel}.");
        state = MarkCardPlayedThisTurn(state, playerId, card, unit["uid"]?.GetValue<string>());
        if (!string.IsNullOrWhiteSpace(weaponmasterGearUid))
        {
            var equipped = EquipGear(state, playerId, weaponmasterGearUid, unit["uid"]?.GetValue<string>(), energyDiscount: 1, requireEquipmentTag: true);
            if (equipped is null)
            {
                return null;
            }

            state = AddLog(equipped, $"{PlayerName(equipped, playerId)} equipped {unit["name"]?.GetValue<string>() ?? "a unit"} with Weaponmaster.");
        }

        state = QueueVisionChoice(state, playerId, unit);
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
            if (battlefield is null || !CanAddUnitToBattlefield(state, playerId, battlefield))
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
            ["layerTimestamp"] = nextUid,
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
        if (!IsAttachableCard(card))
        {
            return null;
        }

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

    private static JsonObject? EquipGear(JsonObject state, int playerId, string? gearUid, string? targetUnitId, int energyDiscount = 0, bool requireEquipmentTag = false)
    {
        if (string.IsNullOrWhiteSpace(gearUid) || string.IsNullOrWhiteSpace(targetUnitId))
        {
            return null;
        }

        var player = FindPlayer(state, playerId);
        var gear = FindBaseGear(state, gearUid);
        var target = FindUnit(state, targetUnitId);
        if (player is null ||
            gear is null ||
            target is null ||
            UnitControllerId(target) != playerId ||
            gear.Gear["controllerId"]?.GetValue<int?>() != playerId && gear.Gear["ownerId"]?.GetValue<int?>() != playerId ||
            !HasKeyword(gear.Gear, KeywordKind.Equip) ||
            requireEquipmentTag && !HasEquipmentTag(gear.Gear))
        {
            return null;
        }

        var cost = DiscountCost(EquipCost(gear.Gear), energyDiscount);
        if (!CanPay(player, cost))
        {
            return null;
        }

        PayCost(player, cost);
        var attached = Clone(gear.Gear);
        gear.Container.Remove(gear.Gear);
        attached["location"] = new JsonObject { ["type"] = "attached", ["battlefieldId"] = null, ["attachedToUid"] = targetUnitId };
        attached["attachedUnitId"] = targetUnitId;
        attached["controllerId"] = playerId;
        attached["rulesTextActive"] = true;
        attached["attachedCards"] ??= new JsonArray();
        attached["topCardId"] = attached["topCardId"]?.GetValue<string>() ?? attached["id"]?.GetValue<string>() ?? string.Empty;

        var attachedCards = target["attachedCards"] as JsonArray ?? new JsonArray();
        attachedCards.Add(attached);
        target["attachedCards"] = attachedCards;
        RecomputeTopCard(target);
        return AddLog(state, $"{PlayerName(state, playerId)} equipped {target["name"]?.GetValue<string>() ?? "a unit"} with {attached["name"]?.GetValue<string>() ?? "gear"}.");
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

        if (faceDown)
        {
            InformationModel.MarkFacedown(obj, playerId, state["turnNumber"]?.GetValue<int>() ?? 1);
        }
        else
        {
            InformationModel.MarkFaceup(obj, state["turnNumber"]?.GetValue<int>() ?? 1);
        }

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
        if (moves.Any(move => UnitControllerId(move.Unit) != playerId || move.Unit["exhausted"]?.GetValue<bool>() != false))
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

        if (string.IsNullOrWhiteSpace(battlefieldId) || moves.Any(move => move.Origin == MoveOrigin.Battlefield && !HasKeyword(state, move.Unit, KeywordKind.Ganking)))
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
            .FirstOrDefault(unit =>
                string.Equals(unit["uid"]?.GetValue<string>(), unitId, StringComparison.Ordinal) &&
                UnitControllerId(unit) == playerId);
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
                    UnitControllerId(candidate) == playerId);
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
            battlefield["units"]!.AsArray().Any(unit => UnitControllerId(unit!.AsObject()) == playerId);
    }

    private static bool CanAddUnitToBattlefield(JsonObject state, int playerId, JsonObject battlefield)
    {
        if (PlayerCount(state) <= 2 || !BattlefieldHasStagedOrActiveCombat(state, battlefield))
        {
            return true;
        }

        return IsCombatParticipantAtBattlefield(state, playerId, battlefield) ||
            battlefield["units"]!.AsArray().Any(unit => UnitControllerId(unit!.AsObject()) == playerId);
    }

    private static JsonObject UpdateBattlefieldAfterMovement(JsonObject state, int playerId, JsonObject battlefield)
    {
        var teams = battlefield["units"]!.AsArray()
            .Select(node => UnitControllerId(node!.AsObject()))
            .Where(controller => controller >= 0)
            .Select(controller => TeamIdForPlayer(state, controller))
            .Distinct()
            .ToArray();
        var movingTeamId = TeamIdForPlayer(state, playerId);
        var opposingTeams = teams.Where(teamId => teamId != movingTeamId).ToArray();

        if (opposingTeams.Length == 1)
        {
            battlefield["controllerId"] = null;
            battlefield["stagedCombat"] = true;
            battlefield["stagedShowdown"] = true;
            battlefield["contestedByPlayerId"] = playerId;
            return AddLog(state, $"{PlayerName(state, playerId)} contested {battlefield["name"]?.GetValue<string>() ?? "a battlefield"} and staged combat.");
        }

        if (opposingTeams.Length == 0)
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
            .Select(node => UnitControllerId(node!.AsObject()))
            .Where(controller => controller >= 0)
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
        if (state["stage"]?.GetValue<string>() != "playing" ||
            state["chainWindow"] is not null ||
            PendingBurnOut(state) is not null ||
            PendingVision(state) is not null)
        {
            return state;
        }

        state = RunTriggerOrderGroups(state);
        if (CurrentPendingTriggerGroup(state) is not null)
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
        state = RemoveInvalidFacedownZoneCards(state);
        if (state["activeShowdown"] is not null || state["activeCombat"] is not null || state["chainWindow"] is not null)
        {
            return state;
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var units = battlefield["units"]!.AsArray().Select(node => node!.AsObject()).ToArray();
            var owners = units
                .Select(UnitControllerId)
                .Where(controller => controller >= 0)
                .Distinct()
                .ToArray();
            var teams = owners.Select(owner => TeamIdForPlayer(state, owner)).Distinct().ToArray();
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

            if (teams.Length == 2)
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
            .Select(node => UnitControllerId(node!.AsObject()))
            .Where(controller => controller >= 0 && !IsFriendlyPlayer(state, attackerPlayerId, controller))
            .Distinct()
            .Cast<int?>()
            .FirstOrDefault();
        if (defenderPlayerId is null)
        {
            battlefield["stagedCombat"] = false;
            battlefield["stagedShowdown"] = false;
            return state;
        }

        battlefield["stagedShowdown"] = false;
        battlefield["stagedCombat"] = false;
        foreach (var unit in battlefield["units"]!.AsArray().Select(node => node!.AsObject()))
        {
            var controllerId = UnitControllerId(unit);
            unit["attacker"] = IsFriendlyPlayer(state, attackerPlayerId, controllerId);
            unit["defender"] = IsFriendlyPlayer(state, defenderPlayerId.Value, controllerId);
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
            ["defenderPlayerId"] = defenderPlayerId.Value,
            ["damageStep"] = false
        };
        state["focusPlayerId"] = attackerPlayerId;
        state["priorityPlayerId"] = attackerPlayerId;
        state["activePlayer"] = attackerPlayerId;
        state["hasPassedFocusByPlayer"] = new JsonObject();
        state = QueueCombatDesignationTriggers(state, battlefield, attackerPlayerId, defenderPlayerId.Value);
        return AddLog(state, $"{PlayerName(state, attackerPlayerId)} challenges {PlayerName(state, defenderPlayerId.Value)} to a combat showdown at {battlefield["name"]?.GetValue<string>() ?? "a battlefield"}.");
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
        var unitTeams = units.Select(unit => unit["ownerId"]?.GetValue<int>())
            .Where(owner => owner is not null)
            .Select(owner => TeamIdForPlayer(state, owner!.Value))
            .Distinct()
            .ToArray();
        if (unitTeams.Length != 2 ||
            !unitTeams.Contains(TeamIdForPlayer(state, attackerPlayerId.Value)) ||
            !unitTeams.Contains(TeamIdForPlayer(state, defenderPlayerId.Value)))
        {
            return null;
        }

        var attackers = units.Where(unit => IsFriendlyUnit(state, attackerPlayerId.Value, unit)).ToArray();
        var defenders = units.Where(unit => IsFriendlyUnit(state, defenderPlayerId.Value, unit)).ToArray();
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

        var attackerHasUnits = remainingUnits.Any(unit => IsFriendlyUnit(state, attackerPlayerId.Value, unit));
        var defenderHasUnits = remainingUnits.Any(unit => IsFriendlyUnit(state, defenderPlayerId.Value, unit));
        if (attackerHasUnits && defenderHasUnits)
        {
            state = RecallAttackers(state, battlefield, attackerPlayerId.Value);
            remainingUnits = battlefield["units"]!.AsArray().Select(node => node!.AsObject()).ToArray();
            attackerHasUnits = remainingUnits.Any(unit => IsFriendlyUnit(state, attackerPlayerId.Value, unit));
            defenderHasUnits = remainingUnits.Any(unit => IsFriendlyUnit(state, defenderPlayerId.Value, unit));
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
            if (UnitControllerId(unit) != attackerPlayerId)
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
            var assignedPriority = DamageAssignmentPriority(state, assignedUnit);
            foreach (var candidate in opposingUnits)
            {
                var candidateUid = candidate["uid"]?.GetValue<string>() ?? string.Empty;
                if (candidateUid == uid || !CanTakeDamage(candidate))
                {
                    continue;
                }

                var candidatePriority = DamageAssignmentPriority(state, candidate);
                if (candidatePriority < assignedPriority && assignments.GetValueOrDefault(candidateUid) < LethalDamage(state, candidate))
                {
                    return false;
                }
            }
        }

        var tankUnits = opposingUnits.Where(unit => HasKeyword(state, unit, KeywordKind.Tank)).ToArray();
        if (tankUnits.Length > 0)
        {
            var allTanksLethal = tankUnits.All(unit =>
            {
                var uid = unit["uid"]?.GetValue<string>() ?? string.Empty;
                return assignments.GetValueOrDefault(uid) >= LethalDamage(state, unit);
            });
            if (!allTanksLethal && opposingUnits.Any(unit => !HasKeyword(state, unit, KeywordKind.Tank) && assignments.GetValueOrDefault(unit["uid"]?.GetValue<string>() ?? string.Empty) > 0))
            {
                return false;
            }
        }

        var backlineUnits = opposingUnits.Where(unit => HasKeyword(state, unit, KeywordKind.Backline)).ToArray();
        if (backlineUnits.Length > 0)
        {
            var frontlineUnits = opposingUnits.Where(unit => !HasKeyword(state, unit, KeywordKind.Backline)).ToArray();
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
                if (UnitControllerId(unit) != playerId || !HasKeyword(state, unit, KeywordKind.Temporary))
                {
                    continue;
                }

                baseUnits.RemoveAt(i);
                state = MoveKilledPermanentToTrash(state, playerId, unit);
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var units = battlefield["units"]!.AsArray();
            for (var i = units.Count - 1; i >= 0; i--)
            {
                var unit = units[i]!.AsObject();
                if (UnitControllerId(unit) != playerId || !HasKeyword(state, unit, KeywordKind.Temporary))
                {
                    continue;
                }

                units.RemoveAt(i);
                state = MoveKilledPermanentToTrash(state, playerId, unit);
            }
        }

        return state;
    }

    private static JsonObject MoveKilledPermanentToTrash(JsonObject state, int ownerId, JsonObject permanent)
    {
        state = ResolveDeathknell(state, ownerId, permanent);
        return MoveObjectAndAttachmentsToOwnerZone(state, permanent, "trash");
    }

    private static JsonObject RemoveInvalidFacedownZoneCards(JsonObject state)
    {
        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var controllerId = battlefield["controllerId"]?.GetValue<int?>();
            if (battlefield["hiddenCards"] is not JsonArray hiddenCards)
            {
                continue;
            }

            for (var i = hiddenCards.Count - 1; i >= 0; i--)
            {
                var hidden = hiddenCards[i]!.AsObject();
                if (hidden["controllerId"]?.GetValue<int?>() == controllerId)
                {
                    continue;
                }

                hiddenCards.RemoveAt(i);
                InformationModel.MarkFaceup(hidden, state["turnNumber"]?.GetValue<int>() ?? 1);
                hidden.Remove("hiddenAtBattlefieldId");
                hidden.Remove("hiddenTurnNumber");
                state = MoveObjectAndAttachmentsToOwnerZone(state, hidden, "trash");
            }
        }

        return state;
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

    private static LayeredCharacteristics LayeredUnitCharacteristics(JsonObject state, JsonObject unit)
    {
        var effects = BoardUnits(state)
            .SelectMany((source, sourceIndex) => ContinuousEffectLayerResolver.EffectsFromSource(source, sourceIndex * 1000)
                .Concat(DependentContinuousEffects(state, source, sourceIndex * 1000 + 500)))
            .ToArray();
        return ContinuousEffectLayerResolver.EvaluateUnit(unit, effects);
    }

    private static IEnumerable<ContinuousEffect> DependentContinuousEffects(JsonObject state, JsonObject source, int sourceOrderOffset)
    {
        if (!HasActiveRulesText(source))
        {
            yield break;
        }

        var controllerId = UnitControllerId(source);
        if (controllerId < 0)
        {
            yield break;
        }

        var sourceUid = source["uid"]?.GetValue<string>() ?? string.Empty;
        var dependentIndex = 0;
        foreach (var text in ActiveDependentTexts(state, controllerId, source))
        {
            var mightBonus = ReadMightBonus(text);
            if (mightBonus != 0)
            {
                yield return new ContinuousEffect(
                    Id: $"{sourceUid}:dependent-might:{dependentIndex}",
                    Layer: ContinuousEffectLayer.Arithmetic,
                    Operation: ContinuousEffectOperation.Add,
                    Property: "might",
                    Timestamp: source["layerTimestamp"]?.GetValue<int?>() ?? sourceOrderOffset + dependentIndex,
                    SourceOrder: sourceOrderOffset + dependentIndex,
                    Amount: mightBonus,
                    TargetUnitId: sourceUid,
                    AppliesTo: "self",
                    SourceUnitId: sourceUid,
                    SourceControllerId: controllerId);
            }

            foreach (var grantedKeyword in KeywordCatalog.Parse(text).Where(keyword => keyword.Kind is not (KeywordKind.Legion or KeywordKind.Level or KeywordKind.Deathknell)))
            {
                yield return new ContinuousEffect(
                    Id: $"{sourceUid}:dependent-keyword:{dependentIndex}:{grantedKeyword.Kind}",
                    Layer: ContinuousEffectLayer.Ability,
                    Operation: ContinuousEffectOperation.Add,
                    Property: "abilities",
                    Timestamp: source["layerTimestamp"]?.GetValue<int?>() ?? sourceOrderOffset + dependentIndex,
                    SourceOrder: sourceOrderOffset + dependentIndex,
                    TextValue: KeywordAbilityText(grantedKeyword),
                    TargetUnitId: sourceUid,
                    AppliesTo: "self",
                    SourceUnitId: sourceUid,
                    SourceControllerId: controllerId);
            }

            dependentIndex++;
        }
    }

    private static IEnumerable<JsonObject> BoardUnits(JsonObject state)
    {
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            foreach (var unit in player["base"]!.AsArray().Select(node => node!.AsObject()))
            {
                yield return unit;
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            foreach (var unit in battlefield["units"]!.AsArray().Select(node => node!.AsObject()))
            {
                yield return unit;
            }
        }
    }

    private static int CurrentMight(JsonObject state, JsonObject unit)
    {
        var might = LayeredUnitCharacteristics(state, unit).Might + PassiveMightBonus(state, unit);
        if (unit["defender"]?.GetValue<bool>() == true)
        {
            might += KeywordValue(state, unit, KeywordKind.Shield);
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
            might += KeywordValue(state, unit, KeywordKind.Assault);
        }

        return Math.Max(0, might);
    }

    private static int LethalDamage(JsonObject state, JsonObject unit)
    {
        return Math.Max(1, CurrentMight(state, unit));
    }

    private static IReadOnlyList<EngineLegalAction> ActivatedAbilityActions(JsonObject state, int playerId)
    {
        if (state["stage"]?.GetValue<string>() != "playing" ||
            state["chainWindow"] is not null ||
            PendingBurnOut(state) is not null ||
            PendingVision(state) is not null ||
            CurrentPendingTriggerGroup(state) is not null)
        {
            return [];
        }

        return AbilitySources(state)
            .Where(source => source.ControllerId == playerId)
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

        var source = AbilitySources(state).FirstOrDefault(candidate => candidate.Uid == sourceUid && candidate.ControllerId == playerId);
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

        var targetSelection = ValidateEffectTargetSelection(state, playerId, effect, targetUnitId, targetLaneId, AbilityAllowsZeroTargets(ability, effect), source.Card);
        if (!targetSelection.IsValid)
        {
            return null;
        }

        state = PayAbilityCost(state, playerId, source.Uid, ability);
        state = EnqueueAbilityEffect(state, source, ability, effect, targetSelection);
        OpenChainWindow(state, playerId, playerId, ChainItemSourceValue(ChainItemSource.PlayedCard));
        return AddLog(state, $"{PlayerName(state, playerId)} activated {ability["label"]?.GetValue<string>() ?? source.Name}.");
    }

    private static JsonObject CollectTriggeredAbilities(JsonObject state, string eventName, JsonObject eventPayload)
    {
        state = FireDelayedAbilities(state, eventName, eventPayload, delayedKind: "delayed-triggered");
        var groups = state["pendingTriggerGroups"] as JsonArray ?? new JsonArray();
        state["pendingTriggerGroups"] = groups;

        var collected = new List<JsonObject>();
        var sourceOrder = 0;

        foreach (var source in AbilitySources(state))
        {
            foreach (var ability in source.Abilities.Where(ability => AbilityKind(ability) == "triggered" && AbilityEvent(ability) == eventName))
            {
                collected.Add(PendingAbility(source, ability, eventPayload, $"{source.Uid}:{ability["id"]?.GetValue<string>() ?? "ability"}:{sourceOrder}"));
                AppendAbilityEvent(state, "trigger-collected", source, ability, eventName);
                sourceOrder++;
            }
        }

        foreach (var playerGroup in collected
            .GroupBy(ability => ability["playerId"]?.GetValue<int>() ?? 0)
            .OrderBy(group => TurnOrderIndexFromCurrentPlayer(state, group.Key)))
        {
            groups.Add(new JsonObject
            {
                ["id"] = $"trigger-group-{state["nextUid"]?.GetValue<int>() ?? 1}",
                ["playerId"] = playerGroup.Key,
                ["event"] = eventName,
                ["abilities"] = ToArray(playerGroup.Select(Clone))
            });
            state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;
        }

        return state;
    }

    private static JsonObject RunTriggerOrderGroups(JsonObject state)
    {
        var groups = state["pendingTriggerGroups"] as JsonArray;
        if (groups is null || groups.Count == 0)
        {
            return state;
        }

        var queue = state["pendingTriggeredAbilities"] as JsonArray ?? new JsonArray();
        state["pendingTriggeredAbilities"] = queue;

        while (groups.Count > 0)
        {
            var group = groups[0]!.AsObject();
            var abilities = group["abilities"] as JsonArray ?? new JsonArray();
            if (abilities.Count == 0)
            {
                groups.RemoveAt(0);
                continue;
            }

            if (abilities.Count > 1)
            {
                return state;
            }

            queue.Add(abilities[0]!.DeepClone());
            groups.RemoveAt(0);
        }

        return state;
    }

    private static JsonObject? CurrentPendingTriggerGroup(JsonObject state)
    {
        var groups = state["pendingTriggerGroups"] as JsonArray;
        if (groups is null || groups.Count == 0)
        {
            return null;
        }

        var group = groups[0]?.AsObject();
        var abilities = group?["abilities"] as JsonArray;
        return abilities is not null && abilities.Count > 1 ? group : null;
    }

    private static JsonObject? OrderTriggeredAbilities(JsonObject state, int playerId, string? groupId, IReadOnlyList<string> abilityIds)
    {
        if (CurrentPendingTriggerGroup(state) is not { } group ||
            group["playerId"]?.GetValue<int>() != playerId ||
            !string.IsNullOrWhiteSpace(groupId) && group["id"]?.GetValue<string>() != groupId)
        {
            return null;
        }

        var abilities = group["abilities"]!.AsArray().Select(node => node!.AsObject()).ToArray();
        var pendingIds = abilities.Select(ability => ability["id"]?.GetValue<string>() ?? string.Empty).ToArray();
        if (abilityIds.Count != pendingIds.Length ||
            abilityIds.Distinct(StringComparer.Ordinal).Count() != abilityIds.Count ||
            pendingIds.Except(abilityIds, StringComparer.Ordinal).Any())
        {
            return null;
        }

        var queue = state["pendingTriggeredAbilities"] as JsonArray ?? new JsonArray();
        state["pendingTriggeredAbilities"] = queue;
        foreach (var abilityId in abilityIds)
        {
            queue.Add(Clone(abilities.Single(ability => ability["id"]?.GetValue<string>() == abilityId)));
        }

        state["pendingTriggerGroups"]!.AsArray().RemoveAt(0);
        return AddLog(state, $"{PlayerName(state, playerId)} ordered simultaneous triggered abilities.");
    }

    private static int TurnOrderIndexFromCurrentPlayer(JsonObject state, int playerId)
    {
        var order = state["turnOrder"]?.Deserialize<int[]>(JsonOptions) ?? ActivePlayerIds(state);
        if (order.Length == 0)
        {
            return int.MaxValue;
        }

        var currentPlayerId = state["turnPlayerId"]?.GetValue<int>() ?? order[0];
        var currentIndex = Array.IndexOf(order, currentPlayerId);
        var playerIndex = Array.IndexOf(order, playerId);
        if (playerIndex < 0)
        {
            return int.MaxValue;
        }

        currentIndex = currentIndex < 0 ? 0 : currentIndex;
        return (playerIndex - currentIndex + order.Length) % order.Length;
    }

    private static JsonObject RunAbilityQueues(JsonObject state)
    {
        var queue = state["pendingTriggeredAbilities"] as JsonArray;
        if (queue is null || queue.Count == 0 || state["chainWindow"] is not null || CurrentPendingTriggerGroup(state) is not null)
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
                ["abilityId"] = pending["abilityId"]?.GetValue<string>() ?? string.Empty,
                ["effect"] = effect.DeepClone(),
                ["targetUnitId"] = pending["targetUnitId"]?.GetValue<string>(),
                ["targetLaneId"] = pending["targetLaneId"]?.GetValue<string>(),
                ["targets"] = pending["targets"]?.DeepClone() ?? new JsonArray(),
                ["source"] = ChainItemSourceValue(ChainItemSource.TriggeredAbility)
            };
            state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;
            stack.Insert(0, item);
        }

        if (stack.Count > 0)
        {
            var priorityPlayerId = stack[0]!["playerId"]?.GetValue<int>() ?? state["turnPlayerId"]?.GetValue<int>() ?? 0;
            OpenChainWindow(state, priorityPlayerId, priorityPlayerId, ChainItemSourceValue(ChainItemSource.TriggeredAbility));
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
            if (AbilityKind(ability) != "delayed-replacement" ||
                AbilityEvent(ability) != eventName ||
                !DelayedAbilityMatchesEventPayload(ability, eventPayload))
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
            if (ConsumeDelayedAbility(ability))
            {
                delayed.RemoveAt(i);
            }
        }
    }

    private static bool DelayedAbilityMatchesEventPayload(JsonObject ability, JsonObject eventPayload)
    {
        if (ability["targetPlayerId"]?.GetValue<int?>() is { } targetPlayerId &&
            eventPayload["playerId"]?.GetValue<int?>() != targetPlayerId)
        {
            return false;
        }

        if (ability["targetBattlefieldId"]?.GetValue<string>() is { } targetBattlefieldId &&
            !string.Equals(eventPayload["battlefieldId"]?.GetValue<string>(), targetBattlefieldId, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool ConsumeDelayedAbility(JsonObject ability)
    {
        if (ability["consume"]?.GetValue<bool>() == false)
        {
            return false;
        }

        var uses = ability["uses"]?.GetValue<int?>() ?? ability["remainingUses"]?.GetValue<int?>() ?? 1;
        if (uses > 1)
        {
            ability["uses"] = uses - 1;
            ability["remainingUses"] = uses - 1;
            return false;
        }

        return true;
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
            if (AbilityKind(ability) != delayedKind ||
                AbilityEvent(ability) != eventName ||
                !DelayedAbilityMatchesEventPayload(ability, eventPayload))
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
            if (ConsumeDelayedAbility(ability))
            {
                delayed.RemoveAt(i);
            }
        }

        return state;
    }

    private static bool IsDelayedCreationEffect(string effectType)
    {
        return effectType is "create-delayed-trigger" or "create-delayed-replacement";
    }

    private static JsonObject CreateDelayedAbility(JsonObject state, JsonObject stackItem, int playerId, JsonObject effect)
    {
        var effectType = effect["type"]?.GetValue<string>() ?? string.Empty;
        if (!IsDelayedCreationEffect(effectType))
        {
            return state;
        }

        var delayed = state["delayedAbilities"] as JsonArray ?? new JsonArray();
        state["delayedAbilities"] = delayed;
        var sourceCard = stackItem["card"]?.DeepClone()?.AsObject() ?? new JsonObject();
        var abilityId = stackItem["abilityId"]?.GetValue<string>() ?? "ability";
        var entry = new JsonObject
        {
            ["id"] = $"{abilityId}-delayed-{state["nextUid"]?.GetValue<int>() ?? 1}",
            ["kind"] = effectType == "create-delayed-trigger" ? "delayed-triggered" : "delayed-replacement",
            ["event"] = effect["event"]?.GetValue<string>() ?? "action-applied",
            ["playerId"] = playerId,
            ["sourceUid"] = sourceCard["uid"]?.GetValue<string>() ?? stackItem["cardId"]?.GetValue<string>() ?? abilityId,
            ["sourceCardId"] = stackItem["cardId"]?.GetValue<string>() ?? sourceCard["catalogId"]?.GetValue<string>() ?? string.Empty,
            ["sourceName"] = stackItem["cardName"]?.GetValue<string>() ?? sourceCard["name"]?.GetValue<string>() ?? "Delayed ability",
            ["consume"] = effect["consume"]?.GetValue<bool>() != false,
            ["effect"] = effect["effect"]?.DeepClone() ?? new JsonObject { ["type"] = "rally", ["amount"] = 0 }
        };

        if (effect["uses"]?.GetValue<int?>() is { } uses)
        {
            entry["uses"] = uses;
            entry["remainingUses"] = uses;
        }

        if (effect["targetPlayerId"] is not null)
        {
            entry["targetPlayerId"] = effect["targetPlayerId"]!.DeepClone();
        }

        if (effect["targetBattlefieldId"] is not null)
        {
            entry["targetBattlefieldId"] = effect["targetBattlefieldId"]!.DeepClone();
        }

        delayed.Add(entry);
        state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;

        var source = new AbilitySource(
            entry["sourceUid"]?.GetValue<string>() ?? abilityId,
            entry["sourceCardId"]?.GetValue<string>() ?? string.Empty,
            entry["sourceName"]?.GetValue<string>() ?? "Delayed ability",
            playerId,
            sourceCard,
            [new JsonObject { ["id"] = abilityId }]);
        AppendAbilityEvent(state, "delayed-created", source, source.Abilities[0], entry["event"]?.GetValue<string>() ?? "action-applied");
        return state;
    }

    private static JsonObject EnqueueAbilityEffect(JsonObject state, AbilitySource source, JsonObject ability, JsonObject effect, TargetSelection targetSelection)
    {
        state["effectStack"]!.AsArray().Insert(0, new JsonObject
        {
            ["id"] = $"ability-stack-{state["nextUid"]?.GetValue<int>() ?? 1}",
            ["card"] = Clone(source.Card),
            ["cardId"] = source.CardId,
            ["cardName"] = source.Name,
            ["kind"] = "ability",
            ["playerId"] = source.ControllerId,
            ["abilityId"] = ability["id"]?.GetValue<string>() ?? string.Empty,
            ["effect"] = effect.DeepClone(),
            ["targetUnitId"] = targetSelection.LegacyTargetUnitId,
            ["targetLaneId"] = targetSelection.LegacyTargetLaneId,
            ["targets"] = ToArray(targetSelection.Targets.Select(TargetToJson)),
            ["source"] = ChainItemSourceValue(ChainItemSource.PlayedCard)
        });
        state["nextUid"] = (state["nextUid"]?.GetValue<int>() ?? 1) + 1;
        return state;
    }

    private static JsonObject PendingAbility(AbilitySource source, JsonObject ability, JsonObject eventPayload, string? id = null)
    {
        var pending = new JsonObject
        {
            ["id"] = id ?? $"{source.Uid}:{ability["id"]?.GetValue<string>() ?? "ability"}",
            ["source"] = Clone(source.Card),
            ["sourceUid"] = source.Uid,
            ["sourceCardId"] = source.CardId,
            ["sourceName"] = source.Name,
            ["abilityId"] = ability["id"]?.GetValue<string>() ?? string.Empty,
            ["playerId"] = source.ControllerId,
            ["effect"] = ability["effect"]?.DeepClone() ?? new JsonObject { ["type"] = "rally", ["amount"] = 0 },
            ["targetUnitId"] = eventPayload["targetUnitId"]?.GetValue<string>(),
            ["targetLaneId"] = eventPayload["targetLaneId"]?.GetValue<string>()
        };

        if (eventPayload["targets"] is JsonArray targets)
        {
            pending["targets"] = targets.DeepClone();
        }

        return pending;
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
                if (AbilitySourceFromCard(card, ownerId) is { } source)
                {
                    yield return source;
                }
            }

            foreach (var card in player["baseGear"]!.AsArray().Select(node => node!.AsObject()))
            {
                if (AbilitySourceFromCard(card, ownerId) is { } source)
                {
                    yield return source;
                }
            }

            foreach (var zone in new[] { "champion", "legend" })
            {
                if (player[zone] is JsonObject card && AbilitySourceFromCard(card, ownerId) is { } source)
                {
                    yield return source;
                }
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            foreach (var card in battlefield["units"]!.AsArray().Select(node => node!.AsObject()))
            {
                var fallbackControllerId = card["ownerId"]?.GetValue<int>() ?? 0;
                if (AbilitySourceFromCard(card, fallbackControllerId) is { } source)
                {
                    yield return source;
                }
            }
        }
    }

    private static AbilitySource? AbilitySourceFromCard(JsonObject card, int fallbackControllerId)
    {
        var abilities = ReadAbilities(card);
        if (abilities.Length == 0)
        {
            return null;
        }

        return new AbilitySource(
            card["uid"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty,
            card["catalogId"]?.GetValue<string>() ?? card["id"]?.GetValue<string>() ?? string.Empty,
            card["name"]?.GetValue<string>() ?? "a card",
            card["controllerId"]?.GetValue<int?>() ?? fallbackControllerId,
            card,
            abilities);
    }

    private static JsonObject[] ReadAbilities(JsonObject card)
    {
        return HasActiveRulesText(card) && card["abilities"] is JsonArray abilities
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
            ["playerId"] = source.ControllerId
        });
    }

    private static int PassiveMightBonus(JsonObject state, JsonObject unit)
    {
        var controllerId = UnitControllerId(unit);
        var bonus = 0;
        foreach (var source in AbilitySources(state))
        {
            foreach (var ability in source.Abilities.Where(ability => AbilityKind(ability) is "passive" or "continuous"))
            {
                var effect = ability["effect"]?.AsObject();
                if (effect?["type"]?.GetValue<string>() != "modify-own-units-might" || source.ControllerId != controllerId)
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

    private static int DamageAssignmentPriority(JsonObject state, JsonObject unit)
    {
        if (HasKeyword(state, unit, KeywordKind.Tank) || HasKeyword(unit, "Tank"))
        {
            return -1;
        }

        return HasKeyword(state, unit, KeywordKind.Backline) || HasKeyword(unit, "Backline") || HasKeyword(unit, "LastDamage") || unit["assignDamageLast"]?.GetValue<bool>() == true
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
            return target is null || IsFriendlyUnit(state, playerId, target) ? 0 : KeywordValue(state, target, KeywordKind.Deflect);
        }

        var battlefield = FindBattlefield(state, targetLaneId!);
        var targetInLane = battlefield?["units"]!.AsArray()
            .Select(node => node!.AsObject())
            .FirstOrDefault(unit => IsEnemyUnit(state, playerId, unit));
        return targetInLane is null ? 0 : KeywordValue(state, targetInLane, KeywordKind.Deflect);
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
            OpenChainWindow(state, attackerPlayerId, attackerPlayerId, ChainItemSourceValue(ChainItemSource.TriggeredAbility));
        }

        return state;
    }

    private static bool HasKeyword(JsonObject unit, string keyword)
    {
        if (!HasActiveRulesText(unit))
        {
            return false;
        }

        if (unit[keyword] is JsonValue value && value.TryGetValue<bool>(out var hasKeyword) && hasKeyword)
        {
            return true;
        }

        if (unit["keywords"] is JsonArray keywords && keywords.Any(item => KeywordNodeMatches(item, keyword)))
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

        if (unit["keywords"] is JsonArray keywords)
        {
            var keywordObject = keywords
                .Select(KeywordObjectFromNode)
                .FirstOrDefault(item => string.Equals(item["kind"]?.GetValue<string>(), keyword, StringComparison.OrdinalIgnoreCase));
            if (keywordObject is not null)
            {
                return keywordObject["value"]?.GetValue<int?>() ?? defaultValue;
            }
        }

        return HasKeyword(unit, keyword) ? defaultValue : 0;
    }

    private static IReadOnlyList<EngineLegalAction> PlayableCardsFromHand(JsonObject state, int playerId, Func<JsonObject, bool> timingPredicate)
    {
        var player = FindPlayer(state, playerId);
        if (player is null || PendingVision(state) is not null || CurrentPendingTriggerGroup(state) is not null)
        {
            return [];
        }

        return player["hand"]!.AsArray()
            .Select((node, index) => (Card: node!.AsObject(), Index: index))
            .Where(item => timingPredicate(item.Card) && CanPlayCardNow(state, playerId, item.Card))
            .SelectMany(item => PlayableCardActionsForHandCard(state, player, playerId, item.Card, item.Index))
            .ToArray();
    }

    private static IEnumerable<EngineLegalAction> PlayableCardActionsForHandCard(JsonObject state, JsonObject player, int playerId, JsonObject card, int handIndex)
    {
        var baseCost = AdjustedCardCost(state, playerId, card, ReadCost(card));
        if (CanPay(player, baseCost))
        {
            yield return new EngineLegalAction(
                $"play-card-{playerId}-{handIndex}",
                "play-card",
                $"Play {card["name"]?.GetValue<string>() ?? "card"}",
                playerId,
                new JsonObject { ["handIndex"] = handIndex });
        }

        if (!string.Equals(card["kind"]?.GetValue<string>(), "spell", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var maxRepeat = KeywordValue(card, KeywordKind.Repeat, countPresence: true);
        for (var repeatCount = 1; repeatCount <= maxRepeat; repeatCount++)
        {
            var cost = AddCosts(baseCost, RepeatCost(card, repeatCount));
            if (!CanPay(player, cost))
            {
                continue;
            }

            yield return new EngineLegalAction(
                $"play-card-{playerId}-{handIndex}-repeat-{repeatCount}",
                "play-card",
                $"Play {card["name"]?.GetValue<string>() ?? "card"} with Repeat {repeatCount}",
                playerId,
                new JsonObject
                {
                    ["handIndex"] = handIndex,
                    ["repeatCount"] = repeatCount
                });
        }
    }

    private static IReadOnlyList<EngineLegalAction> PlayableUnitActions(JsonObject state, int playerId)
    {
        var player = FindPlayer(state, playerId);
        if (player is null || PendingVision(state) is not null || CurrentPendingTriggerGroup(state) is not null)
        {
            return [];
        }

        var actions = new List<EngineLegalAction>();
        foreach (var (card, handIndex) in player["hand"]!.AsArray()
            .Select((node, index) => (Card: node!.AsObject(), Index: index))
            .Where(item => IsPlayableUnitCard(item.Card)))
        {
            var cost = AdjustedCardCost(state, playerId, card, ReadCost(card));
            if (CanPay(player, cost))
            {
                if (CanPlayUnitToBase(state, playerId, card))
                {
                    AddUnitPlayActionWithWeaponmasterOptions(actions, state, player, playerId, handIndex, card, "base", null, false, cost);
                }

                AddUnitPlayBattlefieldActions(actions, state, player, playerId, handIndex, card, false, cost);
            }

            if (!HasKeyword(card, KeywordKind.Accelerate))
            {
                continue;
            }

            var accelerateCost = cost with { Energy = cost.Energy + 2 };
            if (!CanPay(player, accelerateCost))
            {
                continue;
            }

            if (CanPlayUnitToBase(state, playerId, card))
            {
                AddUnitPlayActionWithWeaponmasterOptions(actions, state, player, playerId, handIndex, card, "base", null, true, accelerateCost);
            }

            AddUnitPlayBattlefieldActions(actions, state, player, playerId, handIndex, card, true, accelerateCost);
        }

        return actions;
    }

    private static void AddUnitPlayBattlefieldActions(List<EngineLegalAction> actions, JsonObject state, JsonObject player, int playerId, int handIndex, JsonObject card, bool accelerate, ResourceCost cost)
    {
        foreach (var battlefield in LegalUnitPlayBattlefields(state, playerId, card))
        {
            var battlefieldId = battlefield["id"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(battlefieldId))
            {
                continue;
            }

            AddUnitPlayActionWithWeaponmasterOptions(actions, state, player, playerId, handIndex, card, battlefieldId, battlefield["name"]?.GetValue<string>() ?? "battlefield", accelerate, cost);
        }
    }

    private static void AddUnitPlayActionWithWeaponmasterOptions(
        List<EngineLegalAction> actions,
        JsonObject state,
        JsonObject player,
        int playerId,
        int handIndex,
        JsonObject card,
        string destinationId,
        string? destinationLabel,
        bool accelerate,
        ResourceCost cost)
    {
        var payload = new JsonObject { ["handIndex"] = handIndex };
        if (!string.Equals(destinationId, "base", StringComparison.OrdinalIgnoreCase))
        {
            payload["battlefieldId"] = destinationId;
        }

        if (accelerate)
        {
            payload["accelerate"] = true;
        }

        var labelDestination = string.Equals(destinationId, "base", StringComparison.OrdinalIgnoreCase)
            ? "base"
            : destinationLabel ?? "battlefield";
        actions.Add(new EngineLegalAction(
            accelerate
                ? $"play-unit-{playerId}-{handIndex}-{destinationId}-accelerate"
                : $"play-unit-{playerId}-{handIndex}-{destinationId}",
            "play-unit",
            $"{(accelerate ? "Accelerate" : "Play")} {card["name"]?.GetValue<string>() ?? "unit"} to {labelDestination}",
            playerId,
            payload));

        if (!HasKeyword(card, KeywordKind.Weaponmaster))
        {
            return;
        }

        foreach (var gear in ControlledBaseGear(state, playerId).Where(item => CanWeaponmasterChooseGear(playerId, item.Gear)))
        {
            var discountedEquipCost = DiscountCost(EquipCost(gear.Gear), energyDiscount: 1);
            if (!CanPay(player, AddCosts(cost, discountedEquipCost)))
            {
                continue;
            }

            var gearUid = gear.Gear["uid"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(gearUid))
            {
                continue;
            }

            var weaponmasterPayload = payload.DeepClone().AsObject();
            weaponmasterPayload["weaponmasterGearUid"] = gearUid;
            actions.Add(new EngineLegalAction(
                $"{(accelerate ? $"play-unit-{playerId}-{handIndex}-{destinationId}-accelerate" : $"play-unit-{playerId}-{handIndex}-{destinationId}")}-weaponmaster-{gearUid}",
                "play-unit",
                $"{(accelerate ? "Accelerate" : "Play")} {card["name"]?.GetValue<string>() ?? "unit"} and equip {gear.Gear["name"]?.GetValue<string>() ?? "gear"}",
                playerId,
                weaponmasterPayload));
        }
    }

    private static IReadOnlyList<JsonObject> LegalUnitPlayBattlefields(JsonObject state, int playerId, JsonObject card)
    {
        return state["battlefields"]!.AsArray()
            .Select(node => node!.AsObject())
            .Where(battlefield => CanPlayUnitToBattlefield(state, playerId, card, battlefield))
            .ToArray();
    }

    private static bool CanPlayUnitToBase(JsonObject state, int playerId, JsonObject card)
    {
        var baseAllowed = IsPlayableUnitCard(card) &&
            state["chainWindow"] is null &&
            !IsShowdownOpen(state) &&
            state["stage"]?.GetValue<string>() == "playing" &&
            state["turnPlayerId"]?.GetValue<int>() == playerId &&
            state["turnPhase"]?.GetValue<string>() == "main";

        return IsPlayableUnitCard(card) && ApplyRuleModifiers(
            state,
            new RulePermissionQuery(playerId, "play-unit", CurrentTiming(state), SourceCard: card, Destination: "base"),
            baseAllowed);
    }

    private static bool CanPlayUnitToBattlefield(JsonObject state, int playerId, JsonObject card, JsonObject battlefield)
    {
        if (!IsPlayableUnitCard(card) ||
            state["stage"]?.GetValue<string>() != "playing")
        {
            return false;
        }

        var baseAllowed = false;
        var controllerId = battlefield["controllerId"]?.GetValue<int?>();
        if (controllerId == playerId && CanPlayUnitToBase(state, playerId, card) && CanAddUnitToBattlefield(state, playerId, battlefield))
        {
            baseAllowed = true;
        }
        else if (HasKeyword(card, KeywordKind.Ambush) && BattlefieldHasControlledUnit(playerId, battlefield) && CanAddUnitToBattlefield(state, playerId, battlefield))
        {
            if (state["chainWindow"] is not null)
            {
                var priorityPlayerId = ChainPriorityPlayerId(state);
                baseAllowed = priorityPlayerId is null || CanUsePriority(state, playerId, priorityPlayerId.Value);
            }
            else if (IsShowdownOpen(state))
            {
                var focusPlayerId = state["priorityPlayerId"]?.GetValue<int?>()
                    ?? state["focusPlayerId"]?.GetValue<int?>()
                    ?? state["activePlayer"]?.GetValue<int?>()
                    ?? state["turnPlayerId"]?.GetValue<int>()
                    ?? 0;
                baseAllowed = CanUsePriority(state, playerId, focusPlayerId);
            }
            else
            {
                baseAllowed = state["turnPlayerId"]?.GetValue<int>() == playerId &&
                    state["turnPhase"]?.GetValue<string>() == "main";
            }

        }

        var battlefieldId = battlefield["id"]?.GetValue<string>();
        return ApplyRuleModifiers(
            state,
            new RulePermissionQuery(playerId, "play-unit", CurrentTiming(state), SourceCard: card, Destination: "battlefield", TargetBattlefieldId: battlefieldId),
            baseAllowed);
    }

    private static bool BattlefieldHasControlledUnit(int playerId, JsonObject battlefield)
    {
        return battlefield["units"]!.AsArray()
            .Select(node => node!.AsObject())
            .Any(unit => UnitControllerId(unit) == playerId);
    }

    private static JsonObject UpdateBattlefieldAfterUnitPlay(JsonObject state, int playerId, JsonObject battlefield)
    {
        if (state["activeShowdown"] is not null || state["activeCombat"] is not null)
        {
            return state;
        }

        return UpdateBattlefieldAfterMovement(state, playerId, battlefield);
    }

    private static int UnitControllerId(JsonObject unit)
    {
        return unit["controllerId"]?.GetValue<int?>()
            ?? unit["ownerId"]?.GetValue<int>()
            ?? -1;
    }

    private static bool HasActiveRulesText(JsonObject card)
    {
        return card["rulesTextActive"]?.GetValue<bool?>() != false
            && card["isFaceDown"]?.GetValue<bool?>() != true
            && card["facedown"]?.GetValue<bool?>() != true
            && card["faceDown"]?.GetValue<bool?>() != true;
    }

    private static IReadOnlyList<EngineLegalAction> MoveUnitActions(JsonObject state, int playerId)
    {
        var player = FindPlayer(state, playerId);
        if (player is null)
        {
            return [];
        }

        var actions = new List<EngineLegalAction>();
        foreach (var unit in player["base"]!.AsArray().Select(node => node!.AsObject()))
        {
            if (!IsUnitMovableByPlayer(unit, playerId))
            {
                continue;
            }

            var unitId = unit["uid"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(unitId))
            {
                continue;
            }

            foreach (var battlefield in LegalBattlefieldMoveDestinations(state, playerId))
            {
                AddMoveUnitAction(actions, playerId, unit, unitId, battlefield);
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            var currentBattlefieldId = battlefield["id"]?.GetValue<string>() ?? string.Empty;
            foreach (var unit in battlefield["units"]!.AsArray().Select(node => node!.AsObject()))
            {
                if (!IsUnitMovableByPlayer(unit, playerId))
                {
                    continue;
                }

                var unitId = unit["uid"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(unitId))
                {
                    continue;
                }

                actions.Add(new EngineLegalAction(
                    $"move-unit-{playerId}-{unitId}-base",
                    "move-unit",
                    $"Move {unit["name"]?.GetValue<string>() ?? "unit"} to base",
                    playerId,
                    new JsonObject
                    {
                        ["unitId"] = unitId,
                        ["battlefieldId"] = "base"
                    }));

                if (!HasKeyword(state, unit, KeywordKind.Ganking))
                {
                    continue;
                }

                foreach (var destination in LegalBattlefieldMoveDestinations(state, playerId)
                    .Where(destination => !string.Equals(destination["id"]?.GetValue<string>(), currentBattlefieldId, StringComparison.OrdinalIgnoreCase)))
                {
                    AddMoveUnitAction(actions, playerId, unit, unitId, destination);
                }
            }
        }

        return actions;
    }

    private static IEnumerable<JsonObject> LegalBattlefieldMoveDestinations(JsonObject state, int playerId)
    {
        return state["battlefields"]!.AsArray()
            .Select(node => node!.AsObject())
            .Where(battlefield => CanMoveToBattlefield(state, playerId, battlefield));
    }

    private static void AddMoveUnitAction(List<EngineLegalAction> actions, int playerId, JsonObject unit, string unitId, JsonObject battlefield)
    {
        var battlefieldId = battlefield["id"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(battlefieldId))
        {
            return;
        }

        actions.Add(new EngineLegalAction(
            $"move-unit-{playerId}-{unitId}-{battlefieldId}",
            "move-unit",
            $"Move {unit["name"]?.GetValue<string>() ?? "unit"} to {battlefield["name"]?.GetValue<string>() ?? "battlefield"}",
            playerId,
            new JsonObject
            {
                ["unitId"] = unitId,
                ["battlefieldId"] = battlefieldId
            }));
    }

    private static bool IsUnitMovableByPlayer(JsonObject unit, int playerId)
    {
        return UnitControllerId(unit) == playerId &&
            unit["exhausted"]?.GetValue<bool>() == false;
    }

    private static IReadOnlyList<EngineLegalAction> AttachCardActions(JsonObject state, int playerId)
    {
        var player = FindPlayer(state, playerId);
        if (player is null)
        {
            return [];
        }

        var units = AllUnits(state).ToArray();
        if (units.Length == 0)
        {
            return [];
        }

        return player["hand"]!.AsArray()
            .Select((node, index) => (Card: node!.AsObject(), Index: index))
            .Where(item => IsAttachableCard(item.Card))
            .SelectMany(item => units.Select(unit =>
            {
                var unitId = unit["uid"]?.GetValue<string>() ?? string.Empty;
                return string.IsNullOrWhiteSpace(unitId)
                    ? null
                    : new EngineLegalAction(
                        $"attach-card-{playerId}-{item.Index}-{unitId}",
                        "attach-card",
                        $"Attach {item.Card["name"]?.GetValue<string>() ?? "card"} to {unit["name"]?.GetValue<string>() ?? "unit"}",
                        playerId,
                        new JsonObject
                        {
                            ["handIndex"] = item.Index,
                            ["targetUnitId"] = unitId
                        });
            }))
            .Where(action => action is not null)
            .Select(action => action!)
            .ToArray();
    }

    private static IReadOnlyList<EngineLegalAction> EquipActions(JsonObject state, int playerId)
    {
        var player = FindPlayer(state, playerId);
        if (player is null)
        {
            return [];
        }

        var units = ControlledUnits(state, playerId).ToArray();
        if (units.Length == 0)
        {
            return [];
        }

        return ControlledBaseGear(state, playerId)
            .Where(item => HasKeyword(item.Gear, KeywordKind.Equip))
            .SelectMany(item =>
            {
                var gearUid = item.Gear["uid"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(gearUid) || !CanPay(player, EquipCost(item.Gear)))
                {
                    return [];
                }

                return units.Select(unit =>
                {
                    var unitId = unit["uid"]?.GetValue<string>() ?? string.Empty;
                    return string.IsNullOrWhiteSpace(unitId)
                        ? null
                        : new EngineLegalAction(
                            $"equip-{playerId}-{gearUid}-{unitId}",
                            "equip",
                            $"Equip {item.Gear["name"]?.GetValue<string>() ?? "gear"} to {unit["name"]?.GetValue<string>() ?? "unit"}",
                            playerId,
                            new JsonObject
                            {
                                ["gearUid"] = gearUid,
                                ["targetUnitId"] = unitId
                            });
                });
            })
            .Where(action => action is not null)
            .Select(action => action!)
            .ToArray();
    }

    private static bool IsPlayableUnitCard(JsonObject card)
    {
        return card["kind"]?.GetValue<string>() is "unit" or "champion";
    }

    private static bool IsAttachableCard(JsonObject card)
    {
        var kind = card["kind"]?.GetValue<string>() ?? string.Empty;
        var cardType = card["cardType"]?.GetValue<string>() ?? string.Empty;
        return string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase) ||
            cardType.Contains("gear", StringComparison.OrdinalIgnoreCase) ||
            cardType.Contains("equipment", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanWeaponmasterChooseGear(int playerId, JsonObject gear)
    {
        return (gear["controllerId"]?.GetValue<int?>() ?? gear["ownerId"]?.GetValue<int?>()) == playerId &&
            HasEquipmentTag(gear) &&
            HasKeyword(gear, KeywordKind.Equip);
    }

    private static bool HasEquipmentTag(JsonObject card)
    {
        var cardType = card["cardType"]?.GetValue<string>() ?? string.Empty;
        if (cardType.Contains("equipment", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return card["tags"] is JsonArray tags &&
            tags.Any(tag => string.Equals(tag?.GetValue<string>(), "Equipment", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<JsonObject> AllUnits(JsonObject state)
    {
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            foreach (var unit in player["base"]!.AsArray().Select(node => node!.AsObject()))
            {
                yield return unit;
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            foreach (var unit in battlefield["units"]!.AsArray().Select(node => node!.AsObject()))
            {
                yield return unit;
            }
        }
    }

    private static IEnumerable<JsonObject> ControlledUnits(JsonObject state, int playerId) =>
        AllUnits(state).Where(unit => UnitControllerId(unit) == playerId);

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

        var baseAllowed = false;
        if (state["chainWindow"] is not null)
        {
            var priorityPlayerId = ChainPriorityPlayerId(state);
            baseAllowed = IsReactionCard(card) && (priorityPlayerId is null || CanUsePriority(state, playerId, priorityPlayerId.Value));
            return ApplyRuleModifiers(
                state,
                new RulePermissionQuery(playerId, "play-card", CurrentTiming(state), SourceCard: card),
                baseAllowed);
        }

        if (IsShowdownOpen(state))
        {
            var focusPlayerId = state["priorityPlayerId"]?.GetValue<int?>()
                ?? state["focusPlayerId"]?.GetValue<int?>()
                ?? state["activePlayer"]?.GetValue<int?>()
                ?? state["turnPlayerId"]?.GetValue<int>()
                ?? 0;
            baseAllowed = CanUsePriority(state, playerId, focusPlayerId) && (IsActionCard(card) || IsReactionCard(card));
            return ApplyRuleModifiers(
                state,
                new RulePermissionQuery(playerId, "play-card", CurrentTiming(state), SourceCard: card),
                baseAllowed);
        }

        var turnPlayerId = state["turnPlayerId"]?.GetValue<int>() ?? 0;
        var turnPhase = state["turnPhase"]?.GetValue<string>() ?? "";
        baseAllowed = (playerId == turnPlayerId || IsTeammate(state, playerId, turnPlayerId)) && turnPhase == "main";
        return ApplyRuleModifiers(
            state,
            new RulePermissionQuery(playerId, "play-card", CurrentTiming(state), SourceCard: card),
            baseAllowed);
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

    private static TargetSelection ValidateTargetSelection(JsonObject state, int playerId, JsonObject card, string? targetUnitId, string? targetLaneId, IReadOnlyList<string>? targetUnitIds = null)
    {
        var effect = card["effect"]?.AsObject();
        var effectType = FirstTargetRequiringStepType(effect) ?? effect?["type"]?.GetValue<string>() ?? "rally";
        var targetRequirement = RequiredTargetRequirement(card);
        var requiredCount = targetRequirement.Count;

        if (requiredCount > 1 && effectType is "buff" or "rally" or "kill" or "banish" or "stun")
        {
            return ValidateMultiUnitTargetSelection(state, playerId, effectType, targetRequirement, targetUnitIds ?? [], card);
        }

        return ValidateEffectTargetSelection(state, playerId, effect, targetUnitId, targetLaneId, AllowsZeroTargets(card), card);
    }

    private static TargetSelection ValidateEffectTargetSelection(JsonObject state, int playerId, JsonObject? effect, string? targetUnitId, string? targetLaneId, bool allowsZeroTargets, JsonObject? sourceCard = null)
    {
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
                    .Where(unit => UnitCanBeTargetedByEffect(state, playerId, "damage", unit, sourceCard))
                    .Select(unit => UnitTargetFrom(state, unit, "damage", targetLaneId!))
                    .ToArray();

                return targets.Length == 0 && !allowsZeroTargets
                    ? TargetSelection.Invalid
                    : new TargetSelection(true, null, targetLaneId, targets);
            }

            if (hasUnitTarget && FindUnit(state, targetUnitId!) is { } unit && UnitCanBeTargetedByEffect(state, playerId, "damage", unit, sourceCard))
            {
                return new TargetSelection(true, targetUnitId, null, [UnitTargetFrom(state, unit, "damage", null)]);
            }

            return !hasUnitTarget && allowsZeroTargets
                ? new TargetSelection(true, null, null, [])
                : TargetSelection.Invalid;
        }

        if (effectType is "buff" or "rally" or "kill" or "banish" or "stun")
        {
            if (hasUnitTarget && FindUnit(state, targetUnitId!) is { } unit && UnitCanBeTargetedByEffect(state, playerId, effectType, unit, sourceCard))
            {
                return new TargetSelection(true, targetUnitId, null, [UnitTargetFrom(state, unit, effectType, null)]);
            }

            return !hasUnitTarget && !hasLaneTarget && allowsZeroTargets
                ? new TargetSelection(true, null, null, [])
                : TargetSelection.Invalid;
        }

        return hasUnitTarget || hasLaneTarget
            ? TargetSelection.Invalid
            : new TargetSelection(true, null, null, []);
    }

    // Reads "two friendly units"/"three units" etc. out of card text so cards like "Give two
    // friendly units each +2 Might this turn." can require that many distinct unit targets.
    // Defaults to 1 (the existing single-target behavior) when no such phrase is present.
    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["two"] = 2,
        ["three"] = 3,
        ["four"] = 4,
    };

    private static MultiUnitTargetRequirement RequiredTargetRequirement(JsonObject card)
    {
        var text = card["text"]?.GetValue<string>() ?? string.Empty;
        var pairMatch = Regex.Match(text, @"\ba\s+(friendly|enemy)?\s*unit\s+and\s+an?\s+(friendly|enemy)?\s*unit\b", RegexOptions.IgnoreCase);
        if (pairMatch.Success)
        {
            return new MultiUnitTargetRequirement(2,
            [
                ParseTargetQualifier(pairMatch.Groups[1].Value),
                ParseTargetQualifier(pairMatch.Groups[2].Value)
            ]);
        }

        var match = Regex.Match(text, @"\b(two|three|four|\d+)\b\s+((friendly|enemy)\s+)?units\b", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return new MultiUnitTargetRequirement(1, [TargetQualifier.Any]);
        }

        var word = match.Groups[1].Value;
        var count = NumberWords.TryGetValue(word, out var value) ? value : int.TryParse(word, out var parsed) ? parsed : 1;
        var qualifier = ParseTargetQualifier(match.Groups[3].Value);
        return new MultiUnitTargetRequirement(count, Enumerable.Repeat(qualifier, count).ToArray());
    }

    private static TargetSelection ValidateMultiUnitTargetSelection(JsonObject state, int playerId, string effectType, MultiUnitTargetRequirement requirement, IReadOnlyList<string> targetUnitIds, JsonObject? sourceCard = null)
    {
        var requiredCount = requirement.Count;
        var distinctIds = targetUnitIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (distinctIds.Length != requiredCount)
        {
            return TargetSelection.Invalid;
        }

        var targetUnits = new List<JsonObject>(requiredCount);
        foreach (var unitId in distinctIds)
        {
            if (FindUnit(state, unitId) is not { } unit || !UnitCanBeTargetedByEffect(state, playerId, effectType, unit, sourceCard))
            {
                return TargetSelection.Invalid;
            }

            targetUnits.Add(unit);
        }

        if (!SelectedUnitsMatchQualifiers(state, playerId, targetUnits, requirement.Qualifiers))
        {
            return TargetSelection.Invalid;
        }

        var targets = targetUnits.Select(unit => UnitTargetFrom(state, unit, effectType, null)).ToList();
        return new TargetSelection(true, distinctIds[0], null, targets);
    }

    private static TargetQualifier ParseTargetQualifier(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "friendly" => TargetQualifier.Friendly,
            "enemy" => TargetQualifier.Enemy,
            _ => TargetQualifier.Any
        };
    }

    private static bool SelectedUnitsMatchQualifiers(JsonObject state, int playerId, IReadOnlyList<JsonObject> targetUnits, IReadOnlyList<TargetQualifier> qualifiers)
    {
        if (qualifiers.Count != targetUnits.Count)
        {
            return false;
        }

        var used = new bool[qualifiers.Count];
        return MatchAt(0);

        bool MatchAt(int unitIndex)
        {
            if (unitIndex >= targetUnits.Count)
            {
                return true;
            }

            var unit = targetUnits[unitIndex];
            for (var qualifierIndex = 0; qualifierIndex < qualifiers.Count; qualifierIndex++)
            {
                if (used[qualifierIndex] || !UnitMatchesQualifier(state, playerId, unit, qualifiers[qualifierIndex]))
                {
                    continue;
                }

                used[qualifierIndex] = true;
                if (MatchAt(unitIndex + 1))
                {
                    return true;
                }

                used[qualifierIndex] = false;
            }

            return false;
        }
    }

    private static bool UnitMatchesQualifier(JsonObject state, int playerId, JsonObject unit, TargetQualifier qualifier)
    {
        return qualifier switch
        {
            TargetQualifier.Any => true,
            TargetQualifier.Friendly => IsFriendlyUnit(state, playerId, unit),
            TargetQualifier.Enemy => IsEnemyUnit(state, playerId, unit),
            _ => false
        };
    }

    // For a multi-step card, target selection is validated/tagged against the first step that
    // needs a target, so the player picks one target up front for the whole resolved sequence.
    private static string? FirstTargetRequiringStepType(JsonObject? effect)
    {
        if (effect?["steps"] is not JsonArray steps)
        {
            return null;
        }

        foreach (var stepNode in steps)
        {
            var stepType = stepNode?["type"]?.GetValue<string>();
            if (stepType is "damage" or "buff" or "rally" or "kill" or "banish" or "stun")
            {
                return stepType;
            }
        }

        return null;
    }

    private static bool AbilityAllowsZeroTargets(JsonObject ability, JsonObject effect)
    {
        return ability["allowZeroTargets"]?.GetValue<bool?>() == true
            || ability["optional"]?.GetValue<bool?>() == true
            || effect["allowZeroTargets"]?.GetValue<bool?>() == true
            || effect["optional"]?.GetValue<bool?>() == true
            || effect["upTo"]?.GetValue<int?>() == 0;
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
            var id = RuneIdFromNode(ready[i]);
            if (plan.RecycledRuneIds.Contains(id, StringComparer.Ordinal))
            {
                runeDeck.Add(ready[i]?.DeepClone());
                ready.RemoveAt(i);
            }
        }

        for (var i = exhausted.Count - 1; i >= 0; i--)
        {
            var id = RuneIdFromNode(exhausted[i]);
            if (plan.RecycledRuneIds.Contains(id, StringComparer.Ordinal))
            {
                runeDeck.Add(exhausted[i]?.DeepClone());
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
            .Select(RuneResourceFromNode)
            .Where(rune => rune is not null)
            .Select(rune => rune!)
            .Count(rune => !recycledRuneIds.Contains(rune.Id, StringComparer.Ordinal));
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

    private static ResourceCost AdjustedCardCost(JsonObject state, int playerId, JsonObject card, ResourceCost baseCost)
    {
        var reduction = ActiveDependentTexts(state, playerId, card)
            .Select(ReadCostReduction)
            .Sum();
        return reduction <= 0
            ? baseCost
            : baseCost with { Energy = Math.Max(0, baseCost.Energy - reduction) };
    }

    private static ResourceCost RepeatCost(JsonObject card, int repeatCount)
    {
        if (repeatCount <= 0)
        {
            return new ResourceCost(0, new Dictionary<Domain, int>(), 0);
        }

        var costs = Keywords(card)
            .Where(keyword => KeywordKindName(keyword) == KeywordKind.Repeat)
            .Take(repeatCount)
            .Select(keyword => ReadKeywordCost(keyword["cost"]?.GetValue<string>()))
            .ToArray();

        return costs.Aggregate(new ResourceCost(0, new Dictionary<Domain, int>(), 0), AddCosts);
    }

    private static ResourceCost EquipCost(JsonObject gear)
    {
        var keyword = Keywords(gear).FirstOrDefault(keyword => KeywordKindName(keyword) == KeywordKind.Equip);
        return ReadKeywordCost(keyword?["cost"]?.GetValue<string>());
    }

    private static bool ValidateRepeatChoice(JsonObject card, int repeatCount)
    {
        if (repeatCount < 0)
        {
            return false;
        }

        if (repeatCount == 0)
        {
            return true;
        }

        return string.Equals(card["kind"]?.GetValue<string>(), "spell", StringComparison.OrdinalIgnoreCase) &&
            repeatCount <= KeywordValue(card, KeywordKind.Repeat, countPresence: true);
    }

    private static ResourceCost ReadKeywordCost(string? cost)
    {
        if (string.IsNullOrWhiteSpace(cost))
        {
            return new ResourceCost(0, new Dictionary<Domain, int>(), 0);
        }

        var energy = 0;
        var universalPower = 0;
        var power = new Dictionary<Domain, int>();
        foreach (Match match in Regex.Matches(cost, @"\[(?<symbol>[^\]]+)\]|(?<symbol>\d+)", RegexOptions.CultureInvariant))
        {
            var symbol = match.Groups["symbol"].Value.Trim();
            if (int.TryParse(symbol, out var amount))
            {
                energy += amount;
                continue;
            }

            if (string.Equals(symbol, "A", StringComparison.OrdinalIgnoreCase))
            {
                energy += 1;
                continue;
            }

            if (string.Equals(symbol, "P", StringComparison.OrdinalIgnoreCase))
            {
                universalPower += 1;
                continue;
            }

            if (TryReadDomainSymbol(symbol, out var domain))
            {
                power[domain] = power.GetValueOrDefault(domain) + 1;
            }
        }

        return new ResourceCost(energy, power, universalPower);
    }

    private static ResourceCost AddCosts(ResourceCost first, ResourceCost second)
    {
        var power = first.Power.ToDictionary(item => item.Key, item => item.Value);
        foreach (var (domain, amount) in second.Power)
        {
            power[domain] = power.GetValueOrDefault(domain) + amount;
        }

        return new ResourceCost(
            Math.Max(0, first.Energy + second.Energy),
            power,
            Math.Max(0, first.UniversalPower + second.UniversalPower));
    }

    private static ResourceCost DiscountCost(ResourceCost cost, int energyDiscount)
    {
        return energyDiscount <= 0
            ? cost
            : cost with { Energy = Math.Max(0, cost.Energy - energyDiscount) };
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
            .Select(RuneResourceFromNode)
            .Where(rune => rune is not null)
            .Select(rune => rune!)
            .Where(rune => !string.IsNullOrWhiteSpace(rune.Id))
            .ToArray();
    }

    private static RuneResource? RuneResourceFromNode(JsonNode? node)
    {
        if (node is JsonObject rune)
        {
            return new RuneResource(
                rune["id"]?.GetValue<string>() ?? string.Empty,
                TryReadDomain(rune["domain"]?.GetValue<string>(), out var domain) ? domain : Domain.Fury);
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var id))
        {
            return new RuneResource(id, Domain.Fury);
        }

        return null;
    }

    private static string RuneIdFromNode(JsonNode? node)
    {
        if (node is JsonObject rune)
        {
            return rune["id"]?.GetValue<string>() ?? string.Empty;
        }

        return node is JsonValue value && value.TryGetValue<string>(out var id) ? id : string.Empty;
    }

    private static JsonObject EmptyRunePool() => new()
    {
        ["energy"] = 0,
        ["power"] = new JsonObject(),
        ["universalPower"] = 0
    };

    private static bool TryReadDomain(string? value, out Domain domain) =>
        Enum.TryParse(value, true, out domain);

    private static bool TryReadDomainSymbol(string? value, out Domain domain)
    {
        if (TryReadDomain(value, out domain))
        {
            return true;
        }

        domain = value?.ToUpperInvariant() switch
        {
            "F" => Domain.Fury,
            "C" => Domain.Calm,
            "M" => Domain.Mind,
            "B" => Domain.Body,
            "CH" => Domain.Chaos,
            "O" => Domain.Order,
            _ => default
        };

        return value?.ToUpperInvariant() is "F" or "C" or "M" or "B" or "CH" or "O";
    }

    private static IReadOnlyList<Domain> DomainOrder() =>
        [Domain.Fury, Domain.Calm, Domain.Mind, Domain.Body, Domain.Chaos, Domain.Order];

    // A multi-step card's target-requiring step consumes the next not-yet-consumed target tagged
    // for this step's effect type (one target per step), falling back to the next untagged target.
    private static List<EffectTarget> TakeStepTargets(List<EffectTarget> remainingTargets, string stepType)
    {
        var matched = remainingTargets.Where(t => string.Equals(t.EffectType, stepType, StringComparison.Ordinal)).Take(1).ToList();
        if (matched.Count == 0)
        {
            matched = remainingTargets.Take(1).ToList();
        }

        foreach (var target in matched)
        {
            remainingTargets.Remove(target);
        }

        return matched;
    }

    private static JsonObject ResolveEffectThroughInternalActions(
        JsonObject state,
        int playerId,
        string effectType,
        int amount,
        IReadOnlyList<EffectTarget> targets,
        JsonObject sourceCard)
    {
        if (effectType == "draw")
        {
            return DrawCards(state, playerId, amount);
        }

        var actions = new List<InternalGameAction>();
        foreach (var target in targets)
        {
            var unit = FindLegalResolvedTarget(state, playerId, effectType, target, sourceCard);
            var unitId = unit?["uid"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(unitId))
            {
                continue;
            }

            var action = effectType switch
            {
                "damage" => new InternalGameAction(
                    InternalGameActionType.Deal,
                    playerId,
                    new Dictionary<string, object?> { ["unitId"] = unitId, ["amount"] = amount }),
                "buff" => new InternalGameAction(
                    InternalGameActionType.ModifyMight,
                    playerId,
                    new Dictionary<string, object?>
                    {
                        ["unitId"] = unitId,
                        ["amount"] = amount,
                        ["effectType"] = effectType,
                        ["sourceCardId"] = sourceCard["catalogId"]?.GetValue<string>() ?? sourceCard["id"]?.GetValue<string>() ?? string.Empty,
                        ["sourceName"] = sourceCard["name"]?.GetValue<string>() ?? "Effect",
                        ["sourceCard"] = sourceCard.DeepClone().AsObject()
                    }),
                "rally" => new InternalGameAction(
                    InternalGameActionType.Ready,
                    playerId,
                    new Dictionary<string, object?> { ["unitId"] = unitId }),
                "kill" => new InternalGameAction(
                    InternalGameActionType.Kill,
                    playerId,
                    new Dictionary<string, object?> { ["unitId"] = unitId }),
                "banish" => new InternalGameAction(
                    InternalGameActionType.Banish,
                    playerId,
                    new Dictionary<string, object?> { ["unitId"] = unitId }),
                "stun" => new InternalGameAction(
                    InternalGameActionType.Stun,
                    playerId,
                    new Dictionary<string, object?> { ["unitId"] = unitId }),
                _ => null
            };
            if (action is not null)
            {
                actions.Add(action);
            }
        }

        return InternalGameActionExecutor.ApplyAll(state, actions, InternalGameActionResolutionMode.DoAsMuchAsPossible).State;
    }

    private static JsonObject ExecuteInternalGameAction(JsonObject state, InternalGameAction action)
    {
        var result = InternalGameActionExecutor.Apply(state, action);
        return result.Accepted ? result.State : state;
    }

    private static IEnumerable<LocatedGear> ControlledBaseGear(JsonObject state, int playerId)
    {
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            var baseGear = player["baseGear"]!.AsArray();
            foreach (var gear in baseGear.Select(node => node!.AsObject()))
            {
                if ((gear["controllerId"]?.GetValue<int?>() ?? gear["ownerId"]?.GetValue<int?>()) == playerId)
                {
                    yield return new LocatedGear(gear, baseGear);
                }
            }
        }
    }

    private static LocatedGear? FindBaseGear(JsonObject state, string gearUid)
    {
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            var baseGear = player["baseGear"]!.AsArray();
            foreach (var gear in baseGear.Select(node => node!.AsObject()))
            {
                if (string.Equals(gear["uid"]?.GetValue<string>(), gearUid, StringComparison.Ordinal))
                {
                    return new LocatedGear(gear, baseGear);
                }
            }
        }

        return null;
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

            if (battlefield["hiddenCards"] is JsonArray hiddenCards)
            {
                found = FindObjectInArray(hiddenCards, uid);
                if (found is not null)
                {
                    return found;
                }
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

            if (battlefield["hiddenCards"] is JsonArray hiddenCards)
            {
                removed = RemoveObjectFromArray(hiddenCards, uid);
                if (removed is not null)
                {
                    return removed;
                }
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
        ClearFacedownStateForOwnerZoneMove(state, obj);
        obj["location"] = new JsonObject { ["type"] = zoneName == "banished" ? "banished" : "trash", ["battlefieldId"] = null, ["attachedToUid"] = null };
        obj["attachedUnitId"] = null;
        return PutObjectInOwnerZone(state, obj, zoneName);
    }

    private static void ClearFacedownStateForOwnerZoneMove(JsonObject state, JsonObject obj)
    {
        if (obj["isFaceDown"]?.GetValue<bool?>() == true ||
            obj["facedown"]?.GetValue<bool?>() == true ||
            obj["faceDown"]?.GetValue<bool?>() == true ||
            obj["hiddenAtBattlefieldId"] is not null)
        {
            InformationModel.MarkFaceup(obj, state["turnNumber"]?.GetValue<int>() ?? 1);
        }

        obj.Remove("hiddenAtBattlefieldId");
        obj.Remove("hiddenTurnNumber");
        obj.Remove("hidden");
        obj.Remove("statusEffects");
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

    private static JsonObject OwnedZoneCard(JsonObject card, int ownerId)
    {
        var owned = Clone(card);
        owned["ownerId"] = ownerId;
        owned["controllerId"] = ownerId;
        owned["isToken"] = false;
        return owned;
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

    private static JsonObject? FindLegalResolvedTarget(JsonObject state, int playerId, string effectType, EffectTarget target, JsonObject sourceCard)
    {
        var resolved = FindUnitWithPublicZone(state, target.UnitId);
        return resolved is not null
            && UnitCanBeTargetedByEffect(state, playerId, effectType, resolved.Value.Unit, sourceCard)
            && UnitIsStillInTargetedPublicZone(resolved.Value.ZoneType, resolved.Value.BattlefieldId, target)
                ? resolved.Value.Unit
                : null;
    }

    private static bool UnitCanBeTargetedByEffect(JsonObject state, int playerId, string effectType, JsonObject unit, JsonObject? sourceCard = null)
    {
        var baseAllowed = effectType switch
        {
            "damage" => IsEnemyUnit(state, playerId, unit),
            // "Give a unit +/-N Might" cards have no friendly/enemy qualifier in their text, so any
            // unit is a legal target (unlike "rally"/"ready", which only ever targets a friendly unit).
            "buff" => true,
            "rally" => IsFriendlyUnit(state, playerId, unit),
            "kill" or "banish" or "stun" => true,
            _ => false
        };

        if (effectType == "damage" && !CanTakeDamage(unit))
        {
            return false;
        }

        return ApplyRuleModifiers(
            state,
            new RulePermissionQuery(playerId, "effect-target", CurrentTiming(state), SourceCard: sourceCard, EffectType: effectType, TargetUnit: unit),
            baseAllowed);
    }

    private static string CurrentTiming(JsonObject state)
    {
        if (state["chainWindow"] is not null)
        {
            return "chain";
        }

        if (IsShowdownOpen(state))
        {
            return "showdown";
        }

        return state["turnPhase"]?.GetValue<string>() ?? string.Empty;
    }

    private static bool ApplyRuleModifiers(JsonObject state, RulePermissionQuery query, bool baseAllowed)
    {
        var matchingModifiers = RuleModifierSources(state, query)
            .SelectMany(source => RuleModifiers(source.Card).Select(modifier => (source, modifier)))
            .Where(item => RuleModifierMatches(state, query, item.source, item.modifier))
            .ToArray();

        if (matchingModifiers.Any(item => RuleModifierPolarity(item.modifier) == "cannot"))
        {
            return false;
        }

        return baseAllowed || matchingModifiers.Any(item => RuleModifierPolarity(item.modifier) == "can");
    }

    private static IEnumerable<RuleModifierSource> RuleModifierSources(JsonObject state, RulePermissionQuery query)
    {
        if (query.SourceCard is not null)
        {
            yield return new RuleModifierSource(query.SourceCard, CardControllerId(query.SourceCard, query.PlayerId), true);
        }

        foreach (var source in ActiveRuleModifierCards(state))
        {
            if (query.SourceCard is not null && SameCardObject(source, query.SourceCard))
            {
                continue;
            }

            yield return new RuleModifierSource(source, CardControllerId(source, query.PlayerId), false);
        }
    }

    private static IEnumerable<JsonObject> ActiveRuleModifierCards(JsonObject state)
    {
        foreach (var player in state["players"]!.AsArray().Select(node => node!.AsObject()))
        {
            foreach (var zoneName in new[] { "base", "baseGear" })
            {
                foreach (var card in player[zoneName]!.AsArray().Select(node => node!.AsObject()))
                {
                    if (RulesTextIsActive(card))
                    {
                        yield return card;
                    }
                }
            }

            foreach (var zoneName in new[] { "champion", "legend" })
            {
                if (player[zoneName] is JsonObject card && RulesTextIsActive(card))
                {
                    yield return card;
                }
            }
        }

        foreach (var battlefield in state["battlefields"]!.AsArray().Select(node => node!.AsObject()))
        {
            foreach (var unit in battlefield["units"]!.AsArray().Select(node => node!.AsObject()))
            {
                if (RulesTextIsActive(unit))
                {
                    yield return unit;
                }
            }
        }
    }

    private static bool RuleModifierMatches(JsonObject state, RulePermissionQuery query, RuleModifierSource source, JsonObject modifier)
    {
        return MatchesRuleValue(RuleModifierText(modifier, "actionType") ?? RuleModifierText(modifier, "action"), query.ActionType)
            && MatchesRuleValue(RuleModifierText(modifier, "timing"), query.Timing)
            && MatchesRuleValue(RuleModifierText(modifier, "destination"), query.Destination)
            && MatchesRuleValue(RuleModifierText(modifier, "effectType"), query.EffectType ?? query.SourceCard?["effect"]?["type"]?.GetValue<string>())
            && MatchesRuleValue(RuleModifierText(modifier, "cardKind"), query.SourceCard?["kind"]?.GetValue<string>())
            && RuleModifierScopeApplies(state, query, source, RuleModifierText(modifier, "appliesTo") ?? "self")
            && RuleModifierTargetMatches(state, query, RuleModifierText(modifier, "target"));
    }

    private static bool RuleModifierScopeApplies(JsonObject state, RulePermissionQuery query, RuleModifierSource source, string appliesTo)
    {
        return appliesTo.ToLowerInvariant() switch
        {
            "self" => source.IsQuerySource || SameCardObject(source.Card, query.TargetUnit),
            "controller" or "owner" => source.ControllerId == query.PlayerId,
            "friendly" or "friendly-player" or "friendly-players" => IsFriendlyPlayer(state, query.PlayerId, source.ControllerId),
            "opponent" or "opponents" or "enemy" or "enemy-player" or "enemy-players" => !IsFriendlyPlayer(state, query.PlayerId, source.ControllerId),
            "all" or "any" => true,
            _ => false
        };
    }

    private static bool RuleModifierTargetMatches(JsonObject state, RulePermissionQuery query, string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || target == "*")
        {
            return true;
        }

        if (query.TargetUnit is null)
        {
            return false;
        }

        return target.ToLowerInvariant() switch
        {
            "unit" or "any-unit" => true,
            "friendly" or "friendly-unit" => IsFriendlyUnit(state, query.PlayerId, query.TargetUnit),
            "enemy" or "enemy-unit" => IsEnemyUnit(state, query.PlayerId, query.TargetUnit),
            "self" => query.SourceCard is not null && SameCardObject(query.SourceCard, query.TargetUnit),
            _ => false
        };
    }

    private static string RuleModifierPolarity(JsonObject modifier)
    {
        var value = RuleModifierText(modifier, "polarity")
            ?? RuleModifierText(modifier, "permission")
            ?? RuleModifierText(modifier, "type")
            ?? "can";
        return value.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "cannot" or "cant" or "deny" or "restriction" or "forbid" => "cannot",
            _ => "can"
        };
    }

    private static IEnumerable<JsonObject> RuleModifiers(JsonObject card)
    {
        if (card["ruleModifiers"] is not JsonArray modifiers)
        {
            yield break;
        }

        foreach (var node in modifiers)
        {
            if (node is JsonObject modifier)
            {
                yield return modifier;
            }
        }
    }

    private static bool RulesTextIsActive(JsonObject card)
    {
        return HasActiveRulesText(card);
    }

    private static int CardControllerId(JsonObject card, int fallbackPlayerId)
    {
        return card["controllerId"]?.GetValue<int?>()
            ?? card["ownerId"]?.GetValue<int?>()
            ?? fallbackPlayerId;
    }

    private static bool MatchesRuleValue(string? expected, string? actual)
    {
        return string.IsNullOrWhiteSpace(expected)
            || expected == "*"
            || string.Equals(expected, "any", StringComparison.OrdinalIgnoreCase)
            || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    private static string? RuleModifierText(JsonObject modifier, string key)
    {
        return modifier[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
    }

    private static bool SameCardObject(JsonObject? first, JsonObject? second)
    {
        if (first is null || second is null)
        {
            return false;
        }

        if (ReferenceEquals(first, second))
        {
            return true;
        }

        var firstUid = first["uid"]?.GetValue<string>();
        var secondUid = second["uid"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(firstUid) || !string.IsNullOrWhiteSpace(secondUid))
        {
            return string.Equals(firstUid, secondUid, StringComparison.Ordinal);
        }

        return string.Equals(first["id"]?.GetValue<string>(), second["id"]?.GetValue<string>(), StringComparison.Ordinal);
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
                ["continuousEffects"] = ToArray(definition.ContinuousEffects.Select(ToContinuousEffectObject)),
                ["ruleModifiers"] = ToArray(definition.RuleModifiers.Select(ToRuleModifierObject)),
                ["effect"] = EffectNode(definition.Effect)
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
            ["continuousEffects"] = new JsonArray(),
            ["ruleModifiers"] = new JsonArray(),
            ["effect"] = new JsonObject { ["type"] = "rally", ["amount"] = 0 }
        };
    }

    private static JsonObject EffectNode(CardEffectDefinition effect)
    {
        var node = new JsonObject { ["type"] = effect.Type.ToString().ToLowerInvariant(), ["amount"] = effect.Amount };
        if (effect.Steps.Count > 0)
        {
            node["steps"] = ToArray(effect.Steps.Select(step => new JsonObject
            {
                ["type"] = step.Type.ToString().ToLowerInvariant(),
                ["amount"] = step.Amount
            }));
        }

        return node;
    }

    private static JsonObject ToContinuousEffectObject(CardContinuousEffectDefinition effect)
    {
        var node = new JsonObject
        {
            ["id"] = effect.Id,
            ["layer"] = effect.Layer,
            ["operation"] = effect.Operation,
            ["property"] = effect.Property,
            ["appliesTo"] = effect.AppliesTo
        };

        if (effect.Amount is not null) node["amount"] = effect.Amount.Value;
        if (!string.IsNullOrWhiteSpace(effect.TextValue)) node["textValue"] = effect.TextValue;
        if (effect.TextValues is { Count: > 0 }) node["values"] = ToArray(effect.TextValues);
        if (effect.DependsOn is { Count: > 0 }) node["dependsOn"] = ToArray(effect.DependsOn);
        if (effect.RequiresTraits is { Count: > 0 }) node["requiresTraits"] = ToArray(effect.RequiresTraits);
        if (effect.RequiresAbilities is { Count: > 0 }) node["requiresAbilities"] = ToArray(effect.RequiresAbilities);
        if (effect.RequiresMinimumMight is not null) node["requiresMinimumMight"] = effect.RequiresMinimumMight.Value;
        if (effect.Timestamp is not null) node["timestamp"] = effect.Timestamp.Value;
        return node;
    }

    private static JsonObject ToRuleModifierObject(CardRuleModifierDefinition modifier)
    {
        var node = new JsonObject
        {
            ["id"] = modifier.Id,
            ["polarity"] = modifier.Polarity.ToString().ToLowerInvariant(),
            ["actionType"] = modifier.ActionType,
            ["appliesTo"] = modifier.AppliesTo
        };

        if (!string.IsNullOrWhiteSpace(modifier.Timing)) node["timing"] = modifier.Timing;
        if (!string.IsNullOrWhiteSpace(modifier.Destination)) node["destination"] = modifier.Destination;
        if (!string.IsNullOrWhiteSpace(modifier.EffectType)) node["effectType"] = modifier.EffectType;
        if (!string.IsNullOrWhiteSpace(modifier.Target)) node["target"] = modifier.Target;
        if (!string.IsNullOrWhiteSpace(modifier.CardKind)) node["cardKind"] = modifier.CardKind;
        return node;
    }

    private static string DisplayName(string id)
    {
        return string.Join(' ', id.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static int[] OrderedPlayerIds(int playerCount, int firstPlayerId, IReadOnlyList<int>? teamIds = null, string? mode = null)
    {
        var ids = Enumerable.Range(0, playerCount).ToArray();
        var first = firstPlayerId >= 0 && firstPlayerId < playerCount ? firstPlayerId : 0;
        var clockwise = ids.Skip(first).Concat(ids.Take(first)).ToArray();
        if (mode != "teams-2v2" || teamIds is null || teamIds.Count < playerCount)
        {
            return clockwise;
        }

        var firstTeam = teamIds[first];
        var opponent = clockwise.FirstOrDefault(id => teamIds[id] != firstTeam, -1);
        var teammate = clockwise.FirstOrDefault(id => id != first && teamIds[id] == firstTeam, -1);
        var opponentTeammate = opponent >= 0
            ? clockwise.FirstOrDefault(id => id != opponent && teamIds[id] == teamIds[opponent], -1)
            : -1;
        var alternated = new[] { first, opponent, teammate, opponentTeammate }.Where(id => id >= 0).Distinct().ToArray();
        return alternated.Length == playerCount ? alternated : clockwise;
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

    private static bool HasKeyword(JsonObject state, JsonObject card, KeywordKind kind) =>
        KeywordValue(state, card, kind, countPresence: true) > 0;

    private static int KeywordValue(JsonObject card, KeywordKind kind, bool countPresence = false)
    {
        if (!HasActiveRulesText(card))
        {
            return 0;
        }

        var propertyName = kind.ToString();
        var explicitValue = card[$"{char.ToLowerInvariant(propertyName[0])}{propertyName[1..]}Value"]?.GetValue<int?>();
        var matching = Keywords(card).Where(keyword => KeywordKindName(keyword) == kind).ToArray();
        if (matching.Length == 0)
        {
            return explicitValue is null ? 0 : countPresence ? 1 : explicitValue.Value;
        }

        if (countPresence)
        {
            return matching.Length;
        }

        if (explicitValue is not null)
        {
            return explicitValue.Value;
        }

        return kind switch
        {
            KeywordKind.Assault or KeywordKind.Deflect or KeywordKind.Shield or KeywordKind.Hunt => matching.Sum(keyword => keyword["value"]?.GetValue<int>() ?? 1),
            _ => matching.Length
        };
    }

    private static int KeywordValue(JsonObject state, JsonObject? card, KeywordKind kind, bool countPresence = false)
    {
        if (card is null)
        {
            return 0;
        }

        var rawValue = KeywordValue(card, kind, countPresence);
        if (string.IsNullOrWhiteSpace(card["uid"]?.GetValue<string>()))
        {
            return rawValue;
        }

        var layeredValue = LayeredKeywordValue(LayeredUnitCharacteristics(state, card).Abilities, kind, countPresence);
        layeredValue = Math.Max(0, layeredValue - StringKeywordValue(card, kind, countPresence));
        if (layeredValue <= 0)
        {
            return rawValue;
        }

        return rawValue + layeredValue;
    }

    private static int LayeredKeywordValue(IReadOnlySet<string> layeredAbilities, KeywordKind kind, bool countPresence)
    {
        var matching = layeredAbilities
            .SelectMany(KeywordCatalog.Parse)
            .Where(keyword => keyword.Kind == kind)
            .ToArray();
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
            KeywordKind.Assault or KeywordKind.Deflect or KeywordKind.Shield or KeywordKind.Hunt => matching.Sum(keyword => keyword.Value ?? 1),
            _ => matching.Length
        };
    }

    private static int StringKeywordValue(JsonObject card, KeywordKind kind, bool countPresence)
    {
        if (card["keywords"] is not JsonArray keywords)
        {
            return 0;
        }

        var matching = keywords
            .OfType<JsonValue>()
            .Select(value => value.TryGetValue<string>(out var text) ? text : null)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .SelectMany(text => KeywordCatalog.Parse(text))
            .Where(keyword => keyword.Kind == kind)
            .ToArray();
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
            KeywordKind.Assault or KeywordKind.Deflect or KeywordKind.Shield or KeywordKind.Hunt => matching.Sum(keyword => keyword.Value ?? 1),
            _ => matching.Length
        };
    }

    private static IReadOnlyList<JsonObject> Keywords(JsonObject card)
    {
        if (!HasActiveRulesText(card))
        {
            return [];
        }

        if (card["keywords"] is JsonArray keywords && keywords.Count > 0)
        {
            return keywords.Select(KeywordObjectFromNode).ToArray();
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

    private static bool KeywordNodeMatches(JsonNode? node, string keyword)
    {
        var keywordObject = KeywordObjectFromNode(node);
        return string.Equals(keywordObject["kind"]?.GetValue<string>(), keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject KeywordObjectFromNode(JsonNode? node)
    {
        if (node is JsonObject keyword)
        {
            return keyword;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            var parsed = KeywordCatalog.Parse(text).FirstOrDefault();
            return parsed is null
                ? new JsonObject { ["kind"] = text, ["behavior"] = KeywordBehavior.Passive.ToString() }
                : ToKeywordObject(parsed);
        }

        return new JsonObject { ["kind"] = string.Empty, ["behavior"] = KeywordBehavior.Passive.ToString() };
    }

    private static IEnumerable<string> ActiveDependentTexts(JsonObject state, int playerId, JsonObject card)
    {
        foreach (var keyword in Keywords(card))
        {
            var kind = KeywordKindName(keyword);
            if (kind is not (KeywordKind.Legion or KeywordKind.Level))
            {
                continue;
            }

            var text = keyword["text"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(text) || !DependentConditionSatisfied(state, playerId, card, keyword, kind.Value))
            {
                continue;
            }

            yield return text;
        }
    }

    private static bool DependentConditionSatisfied(JsonObject state, int playerId, JsonObject card, JsonObject keyword, KeywordKind kind)
    {
        return kind switch
        {
            KeywordKind.Legion => HasPlayedAnotherCardThisTurn(state, playerId, card["uid"]?.GetValue<string>()),
            KeywordKind.Level => PlayerXp(state, playerId) >= (keyword["value"]?.GetValue<int?>() ?? 0),
            _ => false
        };
    }

    private static int PlayerXp(JsonObject state, int playerId) =>
        FindPlayer(state, playerId)?["xp"]?.GetValue<int>() ?? 0;

    private static int ReadMightBonus(string text)
    {
        var match = Regex.Match(text, @"\+\s*(?<amount>\d+)\s*(?:\[?M\]?|might)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["amount"].Value, out var amount) ? amount : 0;
    }

    private static int ReadCostReduction(string text)
    {
        var match = Regex.Match(text, @"cost\s*\[?(?<amount>\d+)\]?\s*less", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["amount"].Value, out var amount) ? amount : 0;
    }

    private static string KeywordAbilityText(CardKeywordDefinition keyword)
    {
        var name = keyword.Kind == KeywordKind.QuickDraw ? "Quick-Draw" : keyword.Kind.ToString();
        return keyword.Value is null ? name : $"{name} {keyword.Value.Value}";
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

    private sealed record LocatedGear(JsonObject Gear, JsonArray Container);

    private sealed record AbilitySource(string Uid, string CardId, string Name, int ControllerId, JsonObject Card, IReadOnlyList<JsonObject> Abilities);

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

    private sealed record BattlefieldSetupSelection(string CatalogId, int? ChosenByPlayerId);

    private enum TargetQualifier
    {
        Any,
        Friendly,
        Enemy
    }

    private sealed record MultiUnitTargetRequirement(int Count, IReadOnlyList<TargetQualifier> Qualifiers);

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

    private sealed record RulePermissionQuery(
        int PlayerId,
        string ActionType,
        string Timing,
        JsonObject? SourceCard = null,
        string? Destination = null,
        string? EffectType = null,
        JsonObject? TargetUnit = null,
        string? TargetBattlefieldId = null);

    private sealed record RuleModifierSource(JsonObject Card, int ControllerId, bool IsQuerySource);

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
