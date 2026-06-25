import type { Card, Effect } from './cards'
import type { SavedDeck } from './decks'

export type CardStatusEffect = {
  id: string
  type: string
  amount: number
  sourceCard: Card
}

export type StackItem = {
  id: string
  cardId: string
  cardName: string
  playerId: number
  effect: Effect
  targetUnitId?: string
  targetLaneId?: string
}

export type Unit = Card & {
  uid: string
  owner: number
  location: { type: 'base' } | { type: 'battlefield'; battlefieldId: string }
  exhausted: boolean
  damage: number
  attachedMight: number
  attachedCards?: Card[]
  statusEffects?: CardStatusEffect[]
  attacker?: boolean
  defender?: boolean
}

export type Gear = Card & {
  uid: string
  ownerId: number
  location: { type: 'base' } | { type: 'battlefield'; battlefieldId: string }
  exhausted: boolean
  attachedUnitId: string | null
}

export type Player = {
  id: number
  name: string
  points: number
  xp?: number
  runes: {
    ready: Card[]
    exhausted: Card[]
  }
  runeDeck: Card[]
  runePool: {
    energy: number
  }
  deck: Card[]
  hand: Card[]
  trash: Card[]
  base: Unit[]
  baseGear: Gear[]
  champion: Card | null
  legend: Card | null
  championSummoned: boolean
  battlefieldId: string
}

export type Battlefield = {
  id: string
  catalogId?: string
  name: string
  claim: number
  chosenBy: number
  controllerId: number | null
  contestedByPlayerId?: number
  stagedShowdown?: boolean
  stagedCombat?: boolean
  units: Unit[]
}

export type DragPayload =
  | { type: 'card'; handIndex: number; playerId: number }
  | { type: 'champion' }
  | { type: 'unit'; unitId: string }

export type GameState = {
  id: string
  mode: GameMode
  victoryScore: number
  players: Player[]
  battlefields: Battlefield[]
  stage: 'setup' | 'mulligan' | 'playing' | 'game-over'
  turnPhase: 'awaken' | 'beginning' | 'channel' | 'draw' | 'main' | 'ending'
  turnNumber: number
  firstPlayerId: number
  turnPlayerId: number
  activePlayer: number
  priorityPlayerId: number | null
  focusPlayerId: number | null
  winner: number | null
  winningTeamId: number | null
  turnOrder: number[]
  teamIds: number[]
  hasPassedFocusByPlayer: Record<number, boolean>
  scoredBattlefieldIdsThisTurn: Record<number, string[]>
  firstTurnCompletedByPlayer: Record<number, boolean>
  mulliganPlayerIndex: number
  mulliganConfirmedPlayerIds?: number[]
  activeShowdown: { battlefieldId: string; kind: 'non-combat' | 'combat' } | null
  activeCombat: {
    battlefieldId: string
    attackerPlayerId: number
    defenderPlayerId: number
    damageStep?: boolean
    attackerAssignments?: Record<string, number>
    defenderAssignments?: Record<string, number>
  } | null
  selectedCard: { player: number; handIndex: number } | null
  selectedUnit: { player: number; unitId: string } | null
  nextUid: number
  nextLogId: number
  log: { id: number; text: string }[]
  passShield: boolean
  effectStack: StackItem[]
  chainWindow: { priorityPlayerId?: number; startedByPlayerId?: number; passedByPlayer: Record<number, boolean> } | null
}

export type GameMode = 'duel-1v1' | 'ffa-3' | 'ffa-4' | 'teams-2v2'

export type SetupState = {
  mode: GameMode
  playerCount: number
  names: string[]
  battlefieldIds: string[]
  userIds: string[]
  deckIds: string[]
  firstPlayerId: number
  turnOrder: number[]
  teamIds: number[]
  selectedBattlefieldIds: string[]
  setupStatus: 'configuring' | 'mulligan' | 'ready'
}

export type GameDeckAssignment = {
  userId: string
  deck: SavedDeck | null
}

export const victoryScore = 8
export const gameModes: Record<GameMode, { label: string; playerCount: number; victoryScore: number; battlefieldCount: number; firstPlayerSkipsBattlefield: boolean; teams: boolean }> = {
  'duel-1v1': { label: '1v1 Duel', playerCount: 2, victoryScore: 8, battlefieldCount: 2, firstPlayerSkipsBattlefield: false, teams: false },
  'ffa-3': { label: 'FFA3 Skirmish', playerCount: 3, victoryScore: 8, battlefieldCount: 3, firstPlayerSkipsBattlefield: false, teams: false },
  'ffa-4': { label: 'FFA4 War', playerCount: 4, victoryScore: 8, battlefieldCount: 3, firstPlayerSkipsBattlefield: true, teams: false },
  'teams-2v2': { label: '2v2 Magma Chamber', playerCount: 4, victoryScore: 11, battlefieldCount: 3, firstPlayerSkipsBattlefield: true, teams: true },
}
export const playerColors = ['Sun', 'Moon', 'Star', 'Storm']

export const defaultSetup: SetupState = {
  mode: 'duel-1v1',
  playerCount: 2,
  names: ['Sun Team', 'Moon Team', 'Star Team', 'Storm Team'],
  battlefieldIds: ['skybridge', 'emberfield', 'tidegate', 'rootmaze'],
  userIds: ['', '', '', ''],
  deckIds: ['', '', '', ''],
  firstPlayerId: 0,
  turnOrder: [0, 1, 2, 3],
  teamIds: [0, 1, 0, 1],
  selectedBattlefieldIds: ['skybridge', 'emberfield', 'tidegate', 'rootmaze'],
  setupStatus: 'configuring',
}
