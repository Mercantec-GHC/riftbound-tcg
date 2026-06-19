export type Domain = 'Fury' | 'Calm' | 'Mind' | 'Body' | 'Chaos' | 'Order'
export type CardKind = 'unit' | 'spell' | 'gear' | 'champion' | 'legend' | 'battlefield' | 'token' | 'rune'
export type EffectType = 'damage' | 'draw' | 'buff' | 'rally'
export type Page = 'home' | 'game' | 'cards' | 'decks' | 'deck-list'

export type Effect = {
  type: EffectType
  amount: number
}

export type Card = {
  id: string
  name: string
  kind: CardKind
  tags: string[]
  domain: Domain
  domains: Domain[]
  cost: number
  might: number
  text: string
  image: string
  cardType: string
  supertype: string | null
  effect: Effect
}

export type Unit = Card & {
  uid: string
  owner: number
  location: { type: 'base' } | { type: 'battlefield'; battlefieldId: string }
  exhausted: boolean
  damage: number
  attachedMight: number
  attacker?: boolean
  defender?: boolean
}

export type Player = {
  id: number
  name: string
  points: number
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
  champion: Card | null
  legend: Card | null
  championSummoned: boolean
  battlefieldId: string
}

export type Battlefield = {
  id: string
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
  | { type: 'card'; handIndex: number }
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
  activeShowdown: { battlefieldId: string; kind: 'non-combat' | 'combat' } | null
  activeCombat: { battlefieldId: string; attackerId: number; defenderId: number } | null
  selectedCard: { player: number; handIndex: number } | null
  selectedUnit: { player: number; unitId: string } | null
  nextUid: number
  nextLogId: number
  log: { id: number; text: string }[]
  passShield: boolean
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

export type SavedDeck = {
  id: string
  name: string
  ownerUserId: string
  visibility: 'private' | 'public'
  legendId: string
  championId: string
  battlefieldDeckIds: string[]
  runeDeckIds: string[]
  mainDeckIds: string[]
}

export type SharedDeck = SavedDeck & {
  author: string
  visibility: 'private' | 'public'
  tags: string[]
  domains: Domain[]
  legendName: string
  championName: string
  cardCounts: {
    main: number
    runes: number
    battlefields: number
  }
  description?: string
  updatedAt?: string
}

export type UserStats = {
  gamesPlayed: number
  wins: number
  losses: number
  pointsScored: number
  lastPlayedAt?: string
}

export type UserProfile = {
  id: string
  displayName: string
  createdAt: string
  stats: UserStats
}

export type RiftCodexCard = {
  id: string
  name: string
  riftbound_id: string | null
  tcgplayer_id: string | null
  collector_number: number | null
  attributes: {
    energy: number | null
    might: number | null
    power: number | null
  }
  classification: {
    type: string | null
    supertype: string | null
    rarity: string | null
    domain: string[] | null
  }
  text: {
    rich: string | null
    plain: string | null
    flavour: string | null
  }
  set: {
    set_id: string | null
    label: string | null
  }
  media: {
    image_url: string | null
    artist: string | null
    accessibility_text: string | null
  }
  tags: string[]
  orientation: string | null
  metadata: {
    clean_name: string | null
    updated_on: string | null
    alternate_art: boolean | null
    overnumbered: boolean | null
    signature: boolean | null
  }
}

export const victoryScore = 8
export const gameModes: Record<GameMode, { label: string; playerCount: number; victoryScore: number; battlefieldCount: number; firstPlayerSkipsBattlefield: boolean; teams: boolean }> = {
  'duel-1v1': { label: '1v1 Duel', playerCount: 2, victoryScore: 8, battlefieldCount: 2, firstPlayerSkipsBattlefield: false, teams: false },
  'ffa-3': { label: 'FFA3 Skirmish', playerCount: 3, victoryScore: 8, battlefieldCount: 3, firstPlayerSkipsBattlefield: false, teams: false },
  'ffa-4': { label: 'FFA4 War', playerCount: 4, victoryScore: 8, battlefieldCount: 3, firstPlayerSkipsBattlefield: true, teams: false },
  'teams-2v2': { label: '2v2 Magma Chamber', playerCount: 4, victoryScore: 11, battlefieldCount: 3, firstPlayerSkipsBattlefield: true, teams: true },
}
export const schemaVersion = 2
export const customCardsKey = 'rift-prototype-custom-cards-v2'
export const savedDecksKey = 'rift-prototype-saved-decks-v1'
export const userProfilesKey = 'rift-prototype-user-profiles-v1'
export const activeUserKey = 'rift-prototype-active-user-v1'
export const localCardsEndpoint = '/api/local-cards'
export const localDecksEndpoint = '/api/local-decks'

export const domains: Domain[] = ['Fury', 'Calm', 'Mind', 'Body', 'Chaos', 'Order']
export const kinds: CardKind[] = ['unit', 'spell', 'gear']
export const playerColors = ['Sun', 'Moon', 'Star', 'Storm']

export const fallbackBattlefieldOptions = [
  { id: 'skybridge', name: 'Skybridge Spire', claim: 2 },
  { id: 'emberfield', name: 'Emberfield Crossing', claim: 3 },
  { id: 'tidegate', name: 'Tidegate Ruins', claim: 2 },
  { id: 'rootmaze', name: 'Rootmaze Vault', claim: 2 },
  { id: 'glassmarket', name: 'Glassmarket Gate', claim: 3 },
  { id: 'thunderdock', name: 'Thunder Dock', claim: 2 },
]

export const blankCard: Card = {
  id: '',
  name: '',
  kind: 'unit',
  tags: [],
  domain: 'Fury',
  domains: ['Fury'],
  cost: 1,
  might: 1,
  image: '✨',
  text: '',
  cardType: 'Unit',
  supertype: null,
  effect: { type: 'rally', amount: 0 },
}

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
