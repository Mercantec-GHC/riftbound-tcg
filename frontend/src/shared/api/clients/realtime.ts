import type { GameState } from '../../models'
import type { ApiErrorPayload, LegalAction, Lobby, MatchEvent, MatchSnapshot, MatchmakingTicket, SubmitActionRequest } from '../types'

export type RealtimeClientMessage =
  | { type: 'match.join'; matchId: string; userId: string }
  | { type: 'match.leave'; matchId: string }
  | { type: 'match.actionSubmit'; matchId: string; action: SubmitActionRequest }
  | { type: 'match.legalActionsRequest'; matchId: string; playerId: number }
  | { type: 'matchmaking.subscribe'; ticketId: string }
  | { type: 'lobby.subscribe'; lobbyId: string }
  | { type: 'ping'; sentAt: string }

export type RealtimeServerMessage =
  | { type: 'match.joined'; match: MatchSnapshot }
  | { type: 'match.state'; matchId: string; state: GameState; sequenceNumber: number }
  | { type: 'match.legalActions'; matchId: string; playerId: number; legalActions: LegalAction[] }
  | { type: 'match.actionSubmitted'; matchId: string; actionType: string; playerId: number }
  | { type: 'match.actionRejected'; matchId: string; playerId: number; error: ApiErrorPayload }
  | { type: 'match.eventAppended'; matchId: string; event: MatchEvent }
  | { type: 'match.completed'; matchId: string; state: GameState; winnerPlayerId: number | null; winningTeamId: number | null }
  | { type: 'matchmaking.ticketUpdated'; ticket: MatchmakingTicket }
  | { type: 'lobby.updated'; lobby: Lobby }
  | { type: 'lobby.playerJoined'; lobby: Lobby }
  | { type: 'lobby.playerLeft'; lobby: Lobby }
  | { type: 'lobby.loadoutUpdated'; lobby: Lobby }
  | { type: 'lobby.readyChanged'; lobby: Lobby }
  | { type: 'lobby.matched'; lobby: Lobby }
  | { type: 'lobby.cancelled'; lobby: Lobby }
  | { type: 'error'; error: ApiErrorPayload }
  | { type: 'pong'; sentAt: string; receivedAt: string }

export type RealtimeMessage = RealtimeClientMessage | RealtimeServerMessage

export type RealtimeConnectionOptions = {
  url: string
  protocols?: string | string[]
}

export function createRealtimeUrl(baseUrl: string, matchId?: string) {
  const url = new URL(baseUrl, window.location.origin)
  if (matchId) url.searchParams.set('matchId', matchId)
  return url.toString()
}
