import type { Card, GameMode, GameState, SavedDeck, SharedDeck, UserProfile } from '../models'

export type ApiId = string
export type IsoDateTime = string

export type ApiErrorPayload = {
  code: string
  message: string
  details?: Record<string, unknown>
}

export type ApiResult<T> = {
  data: T
  meta?: {
    requestId?: string
    nextCursor?: string | null
    totalCount?: number
  }
}

export type ListQuery = {
  cursor?: string
  limit?: number
}

export type CardListQuery = ListQuery & {
  domain?: string
  kind?: string
  search?: string
  tags?: string[]
}

export type DeckListQuery = ListQuery & {
  ownerUserId?: ApiId
  visibility?: SavedDeck['visibility']
}

export type CreateDeckRequest = Omit<SavedDeck, 'id'>
export type UpdateDeckRequest = Partial<CreateDeckRequest>

export type CreateUserRequest = {
  displayName: string
}

export type UpdateUserRequest = {
  displayName?: string
}

export type MatchStatus = 'configuring' | 'mulligan' | 'active' | 'completed' | 'abandoned'

export type MatchPlayer = {
  playerId: number
  userId: ApiId
  displayName: string
  deckId: ApiId | null
  teamId: number | null
}

export type MatchSummary = {
  id: ApiId
  mode: GameMode
  status: MatchStatus
  players: MatchPlayer[]
  createdAt: IsoDateTime
  updatedAt: IsoDateTime
  completedAt?: IsoDateTime | null
  winnerPlayerId?: number | null
  winningTeamId?: number | null
}

export type CreateMatchRequest = {
  mode: GameMode
  players: Array<{
    userId: ApiId
    deckId: ApiId
    teamId?: number | null
  }>
  battlefieldIds?: ApiId[]
  firstPlayerId?: number
}

export type MatchSnapshot = MatchSummary & {
  state: GameState
  sequenceNumber: number
}

export type LegalActionTarget =
  | { type: 'none' }
  | { type: 'card'; cardId: ApiId; zone?: string }
  | { type: 'unit'; unitId: ApiId }
  | { type: 'battlefield'; battlefieldId: ApiId }
  | { type: 'player'; playerId: number }

export type LegalAction = {
  id: ApiId
  type: string
  playerId: number
  label: string
  source?: LegalActionTarget
  targets: LegalActionTarget[]
  payloadSchema?: Record<string, unknown>
}

export type SubmitActionRequest = {
  actionId?: ApiId
  type: string
  playerId: number
  payload?: Record<string, unknown>
  expectedSequenceNumber?: number
}

export type MatchEvent = {
  id: ApiId
  matchId: ApiId
  sequenceNumber: number
  playerId: number | null
  actionType: string
  actionPayload: Record<string, unknown>
  resultPayload: Record<string, unknown>
  createdAt: IsoDateTime
}

export type SubmitActionResponse = {
  accepted: true
  event: MatchEvent
  state: GameState
  legalActions?: LegalAction[]
}

export type MatchmakingTicketStatus = 'queued' | 'matched' | 'cancelled' | 'expired'

export type MatchmakingTicket = {
  id: ApiId
  userId: ApiId
  deckId: ApiId
  mode: GameMode
  status: MatchmakingTicketStatus
  createdAt: IsoDateTime
  matchId?: ApiId | null
}

export type JoinMatchmakingRequest = {
  userId: ApiId
  deckId: ApiId
  mode: GameMode
}

export type ApiCard = Card
export type ApiDeck = SavedDeck
export type ApiSharedDeck = SharedDeck
export type ApiUserProfile = UserProfile
