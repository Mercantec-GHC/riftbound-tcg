import type { ApiClient } from './http'
import { unwrapData } from './http'
import type { ApiResult, JoinMatchmakingRequest, MatchmakingTicket } from './types'

export function createMatchmakingApi(client: ApiClient) {
  return {
    async joinQueue(request: JoinMatchmakingRequest): Promise<MatchmakingTicket> {
      return unwrapData(await client.request<ApiResult<MatchmakingTicket>>('/matchmaking/tickets', {
        method: 'POST',
        body: JSON.stringify(request),
      }))
    },

    async getTicket(ticketId: string): Promise<MatchmakingTicket> {
      return unwrapData(await client.request<ApiResult<MatchmakingTicket>>(`/matchmaking/tickets/${encodeURIComponent(ticketId)}`))
    },

    async cancelTicket(ticketId: string): Promise<MatchmakingTicket> {
      return unwrapData(await client.request<ApiResult<MatchmakingTicket>>(`/matchmaking/tickets/${encodeURIComponent(ticketId)}`, {
        method: 'DELETE',
      }))
    },
  }
}
