import type { ApiClient } from '../http'
import { queryString, unwrapData } from '../http'
import type {
  ApiResult,
  CreateMatchRequest,
  LegalAction,
  ListQuery,
  MatchEvent,
  MatchSnapshot,
  MatchSummary,
  SubmitActionRequest,
  SubmitActionResponse,
} from '../types'

export type MatchListQuery = ListQuery & {
  userId?: string
  status?: MatchSummary['status']
}

export function createMatchesApi(client: ApiClient) {
  return {
    async listMatches(query?: MatchListQuery): Promise<MatchSummary[]> {
      return unwrapData(await client.request<ApiResult<MatchSummary[]>>(`/matches${queryString(query)}`))
    },

    async createMatch(match: CreateMatchRequest): Promise<MatchSnapshot> {
      return unwrapData(await client.request<ApiResult<MatchSnapshot>>('/matches', {
        method: 'POST',
        body: JSON.stringify(match),
      }))
    },

    async getMatch(matchId: string): Promise<MatchSnapshot> {
      return unwrapData(await client.request<ApiResult<MatchSnapshot>>(`/matches/${encodeURIComponent(matchId)}`))
    },

    async listLegalActions(matchId: string, playerId: number): Promise<LegalAction[]> {
      return unwrapData(await client.request<ApiResult<LegalAction[]>>(
        `/matches/${encodeURIComponent(matchId)}/legal-actions${queryString({ playerId })}`,
      ))
    },

    async submitAction(matchId: string, action: SubmitActionRequest): Promise<SubmitActionResponse> {
      return unwrapData(await client.request<ApiResult<SubmitActionResponse>>(`/matches/${encodeURIComponent(matchId)}/actions`, {
        method: 'POST',
        body: JSON.stringify(action),
      }))
    },

    async listEvents(matchId: string, query?: ListQuery): Promise<MatchEvent[]> {
      return unwrapData(await client.request<ApiResult<MatchEvent[]>>(
        `/matches/${encodeURIComponent(matchId)}/events${queryString(query)}`,
      ))
    },
  }
}
