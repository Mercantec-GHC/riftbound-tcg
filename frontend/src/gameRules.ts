import { battlefieldOptionsFromCards, cloneCard, makeDeck, makeRuneDeck } from './cardUtils'
import {
  defaultSetup,
  gameModes,
  playerColors,
  type Battlefield,
  type Card,
  type CardKind,
  type DragPayload,
  type GameDeckAssignment,
  type GameMode,
  type GameState,
  type Player,
  type SavedDeck,
  type SetupState,
  type Unit,
} from './models'

export type ManualAction =
  | { type: 'adjust-points'; playerId: number; amount: number }
  | { type: 'draw'; playerId: number; amount: number }
  | { type: 'ready-unit' | 'exhaust-unit' | 'kill-unit' | 'recall-unit'; unitId: string }
  | { type: 'damage-unit'; unitId: string; amount: number }
  | { type: 'set-controller'; battlefieldId: string; controllerId: number | null }
  | { type: 'stage-showdown' | 'clear-showdown' | 'stage-combat' | 'clear-combat'; battlefieldId: string }

function addLog(state: GameState, text: string): GameState {
  return { ...state, nextLogId: state.nextLogId + 1, log: [{ id: state.nextLogId, text }, ...state.log].slice(0, 14) }
}

function draw(player: Player, amount: number): Player {
  const deck = [...player.deck]
  const hand = [...player.hand]
  for (let i = 0; i < amount; i += 1) {
    const card = deck.shift()
    if (card) hand.push(card)
  }
  return { ...player, deck, hand }
}

function chooseCard(cards: Card[], kind: CardKind, playerId: number) {
  const options = cards.filter((card) => card.kind === kind)
  return options.length > 0 ? cloneCard(options[playerId % options.length]) : null
}

function cardCopiesFromIds(ids: string[], cards: Card[], prefix: string) {
  return ids
    .map((cardId, index) => {
      const card = cards.find((candidate) => candidate.id === cardId)
      return card ? { ...cloneCard(card), id: `${card.id}-${prefix}-${index}` } : null
    })
    .filter((card): card is Card => Boolean(card))
}

function shuffleCards<T>(cards: T[], seed: number) {
  const shuffled = [...cards]
  let state = seed || 1
  for (let index = shuffled.length - 1; index > 0; index -= 1) {
    state = (state * 1664525 + 1013904223) % 4294967296
    const swapIndex = state % (index + 1)
    ;[shuffled[index], shuffled[swapIndex]] = [shuffled[swapIndex], shuffled[index]]
  }
  return shuffled
}

function makePlayerDeckCards(id: number, cards: Card[], deck: SavedDeck | null | undefined) {
  if (!deck) {
    const legend = chooseCard(cards, 'legend', id)
    return {
      champion: chooseCard(cards, 'champion', id),
      deck: shuffleCards(makeDeck(cards, id * 100), id + 31),
      legend,
      runeDeck: shuffleCards(makeRuneDeck(cards, legend, id), id + 47),
    }
  }
  const legend = cards.find((card) => card.id === deck.legendId) ?? null
  const champion = cards.find((card) => card.id === deck.championId) ?? null
  return {
    champion: champion ? cloneCard(champion) : null,
    deck: shuffleCards(cardCopiesFromIds(deck.mainDeckIds, cards, `main-${id}`), id + 31),
    legend: legend ? cloneCard(legend) : null,
    runeDeck: shuffleCards(cardCopiesFromIds(deck.runeDeckIds, cards, `rune-${id}`), id + 47),
  }
}

function makePlayer(id: number, name: string, battlefieldId: string, cards: Card[], deck?: SavedDeck | null): Player {
  const playerDeck = makePlayerDeckCards(id, cards, deck)
  return draw(
    {
      id,
      name: name.trim() || `${playerColors[id]} Team`,
      battlefieldId,
      points: 0,
      runes: { ready: [], exhausted: [] },
      runeDeck: playerDeck.runeDeck,
      runePool: { energy: 0 },
      deck: playerDeck.deck,
      hand: [],
      trash: [],
      base: [],
      champion: playerDeck.champion,
      legend: playerDeck.legend,
      championSummoned: false,
    },
    4,
  )
}

export function modeConfig(mode: GameMode) {
  return gameModes[mode]
}

export function orderedPlayerIds(setup: SetupState) {
  const ids = Array.from({ length: modeConfig(setup.mode).playerCount }, (_, id) => id)
  const first = setup.firstPlayerId < ids.length ? setup.firstPlayerId : 0
  return [...ids.slice(first), ...ids.slice(0, first)]
}

export function selectedBattlefieldIdsForSetup(setup: SetupState, assignments: (GameDeckAssignment | null)[]) {
  const config = modeConfig(setup.mode)
  return orderedPlayerIds(setup)
    .filter((playerId) => !(config.firstPlayerSkipsBattlefield && playerId === setup.firstPlayerId))
    .map((playerId) => setup.selectedBattlefieldIds[playerId] || setup.battlefieldIds[playerId] || assignments[playerId]?.deck?.battlefieldDeckIds[0] || '')
    .filter(Boolean)
    .slice(0, config.battlefieldCount)
}

export function validateSetup(setup: SetupState, assignments: (GameDeckAssignment | null)[], cards: Card[]) {
  const config = modeConfig(setup.mode)
  const messages: string[] = []
  if (setup.playerCount !== config.playerCount) messages.push(`${config.label} needs ${config.playerCount} players.`)
  for (let index = 0; index < config.playerCount; index += 1) {
    if (!setup.names[index]?.trim()) messages.push(`Player ${index + 1} needs a name.`)
    const deck = assignments[index]?.deck
    if (deck) {
      if (!cards.some((card) => card.id === deck.legendId)) messages.push(`Player ${index + 1} deck has no valid legend.`)
      if (!cards.some((card) => card.id === deck.championId)) messages.push(`Player ${index + 1} deck has no valid champion.`)
      if (deck.battlefieldDeckIds.length === 0) messages.push(`Player ${index + 1} deck has no battlefields.`)
    }
  }
  if (selectedBattlefieldIdsForSetup(setup, assignments).length !== config.battlefieldCount) messages.push(`${config.label} needs ${config.battlefieldCount} selected battlefields.`)
  if (config.teams) {
    const teamCounts = setup.teamIds.slice(0, 4).reduce<Record<number, number>>((counts, teamId) => ({ ...counts, [teamId]: (counts[teamId] ?? 0) + 1 }), {})
    if (Object.values(teamCounts).filter((count) => count === 2).length !== 2) messages.push('2v2 needs two teams of two players.')
  }
  return messages
}

export function createGameFromSetup(cards: Card[] = [], setup: SetupState = defaultSetup, assignments: (GameDeckAssignment | null)[] = []): GameState {
  const config = modeConfig(setup.mode)
  const playerCount = config.playerCount
  const players = Array.from({ length: playerCount }, (_, id) => makePlayer(id, setup.names[id], setup.battlefieldIds[id], cards, assignments[id]?.deck))
  const battlefieldOptions = battlefieldOptionsFromCards(cards)
  const selectedIds = selectedBattlefieldIdsForSetup(setup, assignments)
  const battlefields = selectedIds.map((fieldId, index): Battlefield => {
    const option = battlefieldOptions.find((field) => field.id === fieldId) ?? battlefieldOptions[index % battlefieldOptions.length]
    const chosenBy = setup.selectedBattlefieldIds.findIndex((id) => id === fieldId)
    return { ...option, id: `${option.id}-${index}`, chosenBy: chosenBy >= 0 ? chosenBy : index, controllerId: null, units: [] }
  })
  const turnOrder = setup.turnOrder.length >= playerCount ? setup.turnOrder.slice(0, playerCount) : orderedPlayerIds({ ...setup, playerCount })
  const first = turnOrder[0] ?? 0
  return {
    id: `game-${crypto.randomUUID()}`,
    mode: setup.mode,
    victoryScore: config.victoryScore,
    players,
    battlefields,
    stage: 'mulligan',
    turnPhase: 'awaken',
    turnNumber: 1,
    firstPlayerId: first,
    turnPlayerId: first,
    activePlayer: first,
    priorityPlayerId: null,
    focusPlayerId: null,
    winner: null,
    winningTeamId: null,
    turnOrder,
    teamIds: setup.teamIds.slice(0, playerCount),
    hasPassedFocusByPlayer: {},
    scoredBattlefieldIdsThisTurn: {},
    firstTurnCompletedByPlayer: {},
    mulliganPlayerIndex: 0,
    activeShowdown: null,
    activeCombat: null,
    selectedCard: null,
    selectedUnit: null,
    nextUid: 1,
    nextLogId: 1,
    passShield: true,
    log: [{ id: 0, text: `${config.label} setup complete. Players drew 4 and entered mulligan.` }],
  }
}

export const createInitialGame = createGameFromSetup

export function confirmMulligan(state: GameState, playerId: number, handIndexes: number[]) {
  if (state.stage !== 'mulligan' || state.turnOrder[state.mulliganPlayerIndex] !== playerId) return state
  const indexes = [...new Set(handIndexes)].slice(0, 2).sort((a, b) => b - a)
  const nextPlayers = state.players.map((player) => {
    if (player.id !== playerId) return player
    const selected = player.hand.filter((_, index) => indexes.includes(index))
    const kept = player.hand.filter((_, index) => !indexes.includes(index))
    const redrawn = draw({ ...player, hand: kept }, selected.length)
    return { ...redrawn, deck: [...redrawn.deck, ...selected] }
  })
  const nextIndex = state.mulliganPlayerIndex + 1
  const done = nextIndex >= state.turnOrder.length
  return addLog(
    {
      ...state,
      players: nextPlayers,
      stage: done ? 'playing' : 'mulligan',
      turnPhase: done ? 'awaken' : state.turnPhase,
      mulliganPlayerIndex: done ? state.mulliganPlayerIndex : nextIndex,
      activePlayer: done ? state.turnPlayerId : state.turnOrder[nextIndex],
      passShield: true,
    },
    `${state.players[playerId]?.name} mulliganed ${indexes.length} card${indexes.length === 1 ? '' : 's'}.`,
  )
}

export function beginGameAfterMulligans(game: GameState) {
  return { ...game, stage: 'playing', activePlayer: game.turnPlayerId, turnPhase: 'awaken', passShield: true }
}

function updatePlayer(state: GameState, playerId: number, mapper: (player: Player) => Player): GameState {
  return { ...state, players: state.players.map((player) => (player.id === playerId ? mapper(player) : player)) }
}

function allUnits(state: GameState) {
  return [...state.players.flatMap((player) => player.base), ...state.battlefields.flatMap((field) => field.units)]
}

function mapUnit(state: GameState, unitId: string, mapper: (unit: Unit) => Unit | null): GameState {
  const mapUnits = (units: Unit[]) => units.flatMap((unit) => unit.uid === unitId ? (mapper(unit) ? [mapper(unit) as Unit] : []) : [unit])
  return {
    ...state,
    players: state.players.map((player) => ({ ...player, base: mapUnits(player.base) })),
    battlefields: state.battlefields.map((field) => ({ ...field, units: mapUnits(field.units) })),
  }
}

function removeUnit(state: GameState, unitId: string): [GameState, Unit | null] {
  const unit = allUnits(state).find((candidate) => candidate.uid === unitId) ?? null
  return [
    {
      ...state,
      players: state.players.map((player) => ({ ...player, base: player.base.filter((candidate) => candidate.uid !== unitId) })),
      battlefields: state.battlefields.map((field) => ({ ...field, units: field.units.filter((candidate) => candidate.uid !== unitId) })),
    },
    unit,
  ]
}

export function totalMight(units: Unit[], playerId?: number) {
  return units
    .filter((unit) => playerId === undefined || unit.owner === playerId)
    .reduce((sum, unit) => sum + Math.max(0, unit.might + unit.attachedMight - unit.damage), 0)
}

export function readyRuneCount(player: Player) {
  return player.runes.ready.length
}

export function totalRuneCount(player: Player) {
  return player.runes.ready.length + player.runes.exhausted.length
}

function channelRunes(player: Player, amount: number) {
  const channeled = player.runeDeck.slice(0, amount)
  return { ...player, runes: { ...player.runes, ready: [...player.runes.ready, ...channeled] }, runeDeck: player.runeDeck.slice(channeled.length) }
}

function emptyRunePools(state: GameState) {
  return { ...state, players: state.players.map((player) => ({ ...player, runePool: { energy: 0 } })) }
}

export function scoreBattlefield(state: GameState, playerId: number, battlefieldId: string, method: 'hold' | 'conquer') {
  const scored = state.scoredBattlefieldIdsThisTurn[playerId] ?? []
  if (scored.includes(battlefieldId)) return state
  const field = state.battlefields.find((candidate) => candidate.id === battlefieldId)
  if (!field) return state
  const allBattlefieldIds = state.battlefields.map((candidate) => candidate.id)
  const wouldWin = state.players[playerId].points >= state.victoryScore - 1
  const hasAllForFinalConquer = [...new Set([...scored, battlefieldId])].length === allBattlefieldIds.length
  const shouldDraw = method === 'conquer' && wouldWin && !hasAllForFinalConquer
  let updated = updatePlayer(state, playerId, (player) => shouldDraw ? draw(player, 1) : { ...player, points: player.points + 1 })
  updated = { ...updated, scoredBattlefieldIdsThisTurn: { ...updated.scoredBattlefieldIdsThisTurn, [playerId]: [...scored, battlefieldId] } }
  return checkWinners(addLog(updated, `${state.players[playerId].name} ${method === 'hold' ? 'held' : 'conquered'} ${field.name}${shouldDraw ? ' and drew instead of gaining the final point' : ' for 1 point'}.`))
}

function checkWinners(state: GameState) {
  const candidates = state.players.filter((player) => player.points >= state.victoryScore)
  if (candidates.length === 0) return state
  const leader = [...state.players].sort((a, b) => b.points - a.points)[0]
  if (!leader || candidates.every((player) => player.points !== leader.points)) return state
  if (state.mode === 'teams-2v2') {
    const teamId = state.teamIds[leader.id]
    const opponentMax = Math.max(...state.players.filter((player) => state.teamIds[player.id] !== teamId).map((player) => player.points))
    if (leader.points <= opponentMax) return state
    return addLog({ ...state, stage: 'game-over', winner: leader.id, winningTeamId: teamId }, `Team ${teamId + 1} wins the match.`)
  }
  const opponentMax = Math.max(...state.players.filter((player) => player.id !== leader.id).map((player) => player.points))
  if (leader.points <= opponentMax) return state
  return addLog({ ...state, stage: 'game-over', winner: leader.id }, `${leader.name} wins the match.`)
}

export function advancePhase(state: GameState): GameState {
  if (state.stage !== 'playing') return state
  const playerId = state.turnPlayerId
  if (state.turnPhase === 'awaken') {
    const updated = updatePlayer(state, playerId, (player) => ({
      ...player,
      runes: { ready: [...player.runes.ready, ...player.runes.exhausted], exhausted: [] },
      base: player.base.map((unit) => ({ ...unit, exhausted: false, damage: 0 })),
    }))
    return addLog({ ...updated, turnPhase: 'beginning' }, `${state.players[playerId].name} awakened their board.`)
  }
  if (state.turnPhase === 'beginning') {
    let updated = state
    state.battlefields.filter((field) => field.controllerId === playerId).forEach((field) => {
      updated = scoreBattlefield(updated, playerId, field.id, 'hold')
    })
    return addLog({ ...updated, turnPhase: 'channel' }, `${state.players[playerId].name} finished Beginning.`)
  }
  if (state.turnPhase === 'channel') {
    const isFirstTurn = !state.firstTurnCompletedByPlayer[playerId]
    const lastPlayer = state.turnOrder[state.turnOrder.length - 1]
    const extra = isFirstTurn && (state.mode === 'duel-1v1' ? playerId !== state.firstPlayerId : playerId === lastPlayer) ? 1 : 0
    const updated = updatePlayer(state, playerId, (player) => channelRunes(player, 2 + extra))
    return addLog({ ...updated, turnPhase: 'draw' }, `${state.players[playerId].name} channeled ${2 + extra} rune${2 + extra === 1 ? '' : 's'}.`)
  }
  if (state.turnPhase === 'draw') {
    const skip = !state.firstTurnCompletedByPlayer[playerId] && playerId === state.turnOrder[0] && ['ffa-3', 'ffa-4', 'teams-2v2'].includes(state.mode)
    const updated = emptyRunePools(skip ? state : updatePlayer(state, playerId, (player) => draw(player, 1)))
    return addLog({ ...updated, turnPhase: 'main', priorityPlayerId: playerId }, skip ? `${state.players[playerId].name} skipped their first draw.` : `${state.players[playerId].name} drew 1.`)
  }
  if (state.turnPhase === 'main') return endTurn(state)
  return endTurn(state)
}

export function endTurn(state: GameState): GameState {
  const orderIndex = state.turnOrder.indexOf(state.turnPlayerId)
  const nextPlayer = state.turnOrder[(orderIndex + 1) % state.turnOrder.length] ?? 0
  const nextTurn = nextPlayer === state.turnOrder[0] ? state.turnNumber + 1 : state.turnNumber
  return addLog(
    {
      ...emptyRunePools(state),
      turnPhase: 'awaken',
      turnPlayerId: nextPlayer,
      activePlayer: nextPlayer,
      priorityPlayerId: null,
      focusPlayerId: null,
      hasPassedFocusByPlayer: {},
      scoredBattlefieldIdsThisTurn: {},
      firstTurnCompletedByPlayer: { ...state.firstTurnCompletedByPlayer, [state.turnPlayerId]: true },
      turnNumber: nextTurn,
      selectedCard: null,
      selectedUnit: null,
      passShield: true,
    },
    `${state.players[nextPlayer].name} begins their turn.`,
  )
}

function canMoveToBattlefield(unit: Unit, field: Battlefield) {
  const owners = new Set(field.units.map((candidate) => candidate.owner))
  owners.add(unit.owner)
  return owners.size <= 2
}

export function moveUnit(state: GameState, unitId: string, destination: { type: 'base' } | { type: 'battlefield'; battlefieldId: string }): GameState {
  const [withoutUnit, unit] = removeUnit(state, unitId)
  if (!unit || unit.owner !== state.turnPlayerId || unit.exhausted) return state
  if (destination.type === 'base') {
    return addLog(updatePlayer(withoutUnit, unit.owner, (player) => ({ ...player, base: [...player.base, { ...unit, location: { type: 'base' }, exhausted: true }] })), `${unit.name} moved to base.`)
  }
  const field = withoutUnit.battlefields.find((candidate) => candidate.id === destination.battlefieldId)
  if (!field || !canMoveToBattlefield(unit, field)) return addLog(state, `${unit.name} cannot move there.`)
  const moved = { ...unit, location: destination, exhausted: true }
  const ownersBefore = new Set(field.units.map((candidate) => candidate.owner))
  const contests = field.controllerId !== unit.owner
  const stagedCombat = contests && ownersBefore.size > 0 && !ownersBefore.has(unit.owner)
  const stagedShowdown = contests && !stagedCombat
  return addLog(
    {
      ...withoutUnit,
      battlefields: withoutUnit.battlefields.map((candidate) =>
        candidate.id === field.id
          ? { ...candidate, units: [...candidate.units, moved], contestedByPlayerId: unit.owner, stagedCombat, stagedShowdown }
          : candidate,
      ),
      selectedUnit: null,
    },
    `${unit.name} moved to ${field.name}${stagedCombat ? ' and staged combat' : stagedShowdown ? ' and staged a showdown' : ''}.`,
  )
}

export function passFocus(state: GameState, playerId: number) {
  if (!state.activeShowdown || state.focusPlayerId !== playerId) return state
  const passed = { ...state.hasPassedFocusByPlayer, [playerId]: true }
  if (state.turnOrder.every((id) => passed[id])) return resolveShowdown({ ...state, hasPassedFocusByPlayer: passed })
  const currentIndex = state.turnOrder.indexOf(playerId)
  const next = state.turnOrder.slice(currentIndex + 1).concat(state.turnOrder.slice(0, currentIndex)).find((id) => !passed[id]) ?? playerId
  return { ...state, hasPassedFocusByPlayer: passed, focusPlayerId: next, priorityPlayerId: next }
}

export function openNextStagedConflict(state: GameState) {
  const combat = state.battlefields.find((field) => field.stagedCombat)
  if (combat && combat.contestedByPlayerId !== undefined) {
    const defender = combat.units.find((unit) => unit.owner !== combat.contestedByPlayerId)?.owner ?? combat.controllerId ?? 0
    return addLog({
      ...state,
      activeShowdown: { battlefieldId: combat.id, kind: 'combat' },
      activeCombat: { battlefieldId: combat.id, attackerId: combat.contestedByPlayerId, defenderId: defender },
      focusPlayerId: combat.contestedByPlayerId,
      priorityPlayerId: combat.contestedByPlayerId,
      hasPassedFocusByPlayer: {},
      battlefields: state.battlefields.map((field) => field.id === combat.id ? { ...field, stagedCombat: false } : field),
    }, `Combat showdown opened at ${combat.name}.`)
  }
  const showdown = state.battlefields.find((field) => field.stagedShowdown)
  if (showdown && showdown.contestedByPlayerId !== undefined) {
    return addLog({
      ...state,
      activeShowdown: { battlefieldId: showdown.id, kind: 'non-combat' },
      focusPlayerId: showdown.contestedByPlayerId,
      priorityPlayerId: showdown.contestedByPlayerId,
      hasPassedFocusByPlayer: {},
      battlefields: state.battlefields.map((field) => field.id === showdown.id ? { ...field, stagedShowdown: false } : field),
    }, `Showdown opened at ${showdown.name}.`)
  }
  return state
}

export function resolveShowdown(state: GameState): GameState {
  if (!state.activeShowdown) return openNextStagedConflict(state)
  if (state.activeShowdown.kind === 'combat') return resolveCombat(state)
  const field = state.battlefields.find((candidate) => candidate.id === state.activeShowdown?.battlefieldId)
  if (!field) return { ...state, activeShowdown: null, focusPlayerId: null }
  const owners = [...new Set(field.units.map((unit) => unit.owner))]
  let updated: GameState = { ...state, activeShowdown: null, focusPlayerId: null, priorityPlayerId: null, hasPassedFocusByPlayer: {} }
  if (owners.length === 1 && field.controllerId !== owners[0]) {
    updated = { ...updated, battlefields: updated.battlefields.map((candidate) => candidate.id === field.id ? { ...candidate, controllerId: owners[0], contestedByPlayerId: undefined } : candidate) }
    updated = scoreBattlefield(updated, owners[0], field.id, 'conquer')
  }
  return addLog(updated, `Showdown at ${field.name} closed.`)
}

export function resolveCombat(state: GameState): GameState {
  const combat = state.activeCombat
  if (!combat) return { ...state, activeShowdown: null }
  const field = state.battlefields.find((candidate) => candidate.id === combat.battlefieldId)
  if (!field) return { ...state, activeCombat: null, activeShowdown: null }
  const attackers = field.units.filter((unit) => unit.owner === combat.attackerId)
  const defenders = field.units.filter((unit) => unit.owner === combat.defenderId)
  const attackerMight = totalMight(attackers)
  const defenderMight = totalMight(defenders)
  let damageToAttackers = defenderMight
  let damageToDefenders = attackerMight
  const damagedUnits = field.units.map((unit) => {
    if (unit.owner === combat.attackerId && damageToAttackers > 0) {
      const dealt = Math.min(unit.might + unit.attachedMight, damageToAttackers)
      damageToAttackers -= dealt
      return { ...unit, damage: unit.damage + dealt }
    }
    if (unit.owner === combat.defenderId && damageToDefenders > 0) {
      const dealt = Math.min(unit.might + unit.attachedMight, damageToDefenders)
      damageToDefenders -= dealt
      return { ...unit, damage: unit.damage + dealt }
    }
    return unit
  })
  const survivors = damagedUnits.filter((unit) => unit.damage < unit.might + unit.attachedMight).map((unit) => ({ ...unit, damage: 0, attacker: false, defender: false }))
  const killed = damagedUnits.filter((unit) => unit.damage >= unit.might + unit.attachedMight)
  let updated: GameState = {
    ...state,
    activeCombat: null,
    activeShowdown: null,
    focusPlayerId: null,
    priorityPlayerId: null,
    hasPassedFocusByPlayer: {},
    battlefields: state.battlefields.map((candidate) => candidate.id === field.id ? { ...candidate, units: survivors, contestedByPlayerId: undefined } : candidate),
    players: state.players.map((player) => ({ ...player, trash: [...player.trash, ...killed.filter((unit) => unit.owner === player.id)] })),
  }
  const survivorOwners = [...new Set(survivors.map((unit) => unit.owner))]
  if (survivorOwners.length === 1) {
    updated = { ...updated, battlefields: updated.battlefields.map((candidate) => candidate.id === field.id ? { ...candidate, controllerId: survivorOwners[0] } : candidate) }
    updated = scoreBattlefield(updated, survivorOwners[0], field.id, 'conquer')
  }
  return addLog(updated, `Combat at ${field.name} resolved.`)
}

export function playCard(state: GameState, laneId?: string, targetUnitId?: string): GameState {
  const selection = state.selectedCard
  if (!selection || state.stage !== 'playing' || selection.player !== state.turnPlayerId) return state
  const player = state.players[selection.player]
  const card = player.hand[selection.handIndex]
  if (!card || ['legend', 'battlefield', 'token', 'rune'].includes(card.kind)) return state
  const energyNeeded = Math.max(0, card.cost - player.runePool.energy)
  if (readyRuneCount(player) < energyNeeded) return addLog({ ...state, selectedCard: null }, `${player.name} needs ${card.cost} Energy to play ${card.name}.`)
  let updated = updatePlayer({ ...state, selectedCard: null }, player.id, (current) => ({
    ...current,
    runes: { ready: current.runes.ready.slice(energyNeeded), exhausted: [...current.runes.exhausted, ...current.runes.ready.slice(0, energyNeeded)] },
    runePool: { energy: current.runePool.energy + energyNeeded - card.cost },
    hand: current.hand.filter((_, index) => index !== selection.handIndex),
    trash: card.kind === 'unit' || card.kind === 'champion' || card.kind === 'gear' ? current.trash : [card, ...current.trash],
  }))
  if (card.kind === 'unit' || card.kind === 'champion' || card.kind === 'gear') {
    const unit: Unit = { ...card, uid: `u-${state.nextUid}`, owner: player.id, location: { type: 'base' }, exhausted: card.kind !== 'gear', damage: 0, attachedMight: 0 }
    updated = updatePlayer({ ...updated, nextUid: state.nextUid + 1 }, player.id, (current) => ({ ...current, base: [...current.base, unit] }))
    return addLog(updated, `${player.name} played ${card.name} to base.`)
  }
  if (card.effect.type === 'draw') return addLog(updatePlayer(updated, player.id, (current) => draw(current, card.effect.amount)), `${player.name} drew ${card.effect.amount}.`)
  if ((card.effect.type === 'buff' || card.effect.type === 'rally') && targetUnitId) return addLog(mapUnit(updated, targetUnitId, (unit) => ({ ...unit, attachedMight: card.effect.type === 'buff' ? unit.attachedMight + card.effect.amount : unit.attachedMight, exhausted: card.effect.type === 'rally' ? false : unit.exhausted })), `${player.name} used ${card.name}.`)
  if (card.effect.type === 'damage' && laneId) {
    const target = updated.battlefields.find((field) => field.id === laneId)?.units.find((unit) => unit.owner !== player.id)
    return target ? addLog(mapUnit(updated, target.uid, (unit) => ({ ...unit, damage: unit.damage + card.effect.amount })), `${card.name} dealt ${card.effect.amount} damage.`) : updated
  }
  return addLog(updated, `${player.name} played ${card.name}.`)
}

export function summonChampion(state: GameState): GameState {
  const player = state.players[state.turnPlayerId]
  if (!player?.champion || player.championSummoned) return state
  const energyNeeded = Math.max(0, player.champion.cost - player.runePool.energy)
  if (readyRuneCount(player) < energyNeeded) return addLog(state, `${player.name} needs ${player.champion.cost} Energy to summon ${player.champion.name}.`)
  return playCard({
    ...state,
    players: state.players.map((candidate) => candidate.id === player.id ? { ...candidate, hand: [player.champion as Card, ...candidate.hand], champion: null, championSummoned: true } : candidate),
    selectedCard: { player: player.id, handIndex: 0 },
  })
}

export function handleDrop(state: GameState, payload: DragPayload, laneId?: string, unitId?: string): GameState {
  if (payload.type === 'champion') return laneId || unitId ? state : summonChampion(state)
  if (payload.type === 'card') {
    const withSelection = { ...state, selectedCard: { player: state.turnPlayerId, handIndex: payload.handIndex }, selectedUnit: null }
    if (unitId) return playCard(withSelection, undefined, unitId)
    return playCard(withSelection, laneId)
  }
  if (payload.type === 'unit' && laneId) return moveUnit(state, payload.unitId, { type: 'battlefield', battlefieldId: laneId })
  if (payload.type === 'unit' && !laneId) return moveUnit(state, payload.unitId, { type: 'base' })
  return state
}

export function applyManualAction(state: GameState, action: ManualAction): GameState {
  if (action.type === 'adjust-points') return checkWinners(addLog(updatePlayer(state, action.playerId, (player) => ({ ...player, points: Math.max(0, player.points + action.amount) })), `Adjusted ${state.players[action.playerId]?.name} points by ${action.amount}.`))
  if (action.type === 'draw') return addLog(updatePlayer(state, action.playerId, (player) => draw(player, action.amount)), `${state.players[action.playerId]?.name} manually drew ${action.amount}.`)
  if (action.type === 'ready-unit') return addLog(mapUnit(state, action.unitId, (unit) => ({ ...unit, exhausted: false })), 'Manual ready applied.')
  if (action.type === 'exhaust-unit') return addLog(mapUnit(state, action.unitId, (unit) => ({ ...unit, exhausted: true })), 'Manual exhaust applied.')
  if (action.type === 'damage-unit') return addLog(mapUnit(state, action.unitId, (unit) => ({ ...unit, damage: Math.max(0, unit.damage + action.amount) })), `Manual damage adjusted by ${action.amount}.`)
  if (action.type === 'kill-unit') {
    const [without, unit] = removeUnit(state, action.unitId)
    return unit ? addLog(updatePlayer(without, unit.owner, (player) => ({ ...player, trash: [unit, ...player.trash] })), `Manually killed ${unit.name}.`) : state
  }
  if (action.type === 'recall-unit') {
    const [without, unit] = removeUnit(state, action.unitId)
    return unit ? addLog(updatePlayer(without, unit.owner, (player) => ({ ...player, base: [...player.base, { ...unit, location: { type: 'base' } }] })), `Recalled ${unit.name}.`) : state
  }
  if (action.type === 'set-controller') return addLog({ ...state, battlefields: state.battlefields.map((field) => field.id === action.battlefieldId ? { ...field, controllerId: action.controllerId } : field) }, 'Battlefield controller adjusted.')
  if (action.type === 'stage-showdown' || action.type === 'clear-showdown' || action.type === 'stage-combat' || action.type === 'clear-combat') {
    return addLog({
      ...state,
      battlefields: state.battlefields.map((field) =>
        field.id === action.battlefieldId
          ? { ...field, stagedShowdown: action.type === 'stage-showdown' ? true : action.type === 'clear-showdown' ? false : field.stagedShowdown, stagedCombat: action.type === 'stage-combat' ? true : action.type === 'clear-combat' ? false : field.stagedCombat, contestedByPlayerId: field.contestedByPlayerId ?? state.turnPlayerId }
          : field,
      ),
    }, 'Manual conflict staging adjusted.')
  }
  return state
}
