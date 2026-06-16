import type { ApiClient } from './http'
import { queryString, unwrapData } from './http'
import type { ApiCard, ApiResult, CardListQuery } from './types'

export function createCardsApi(client: ApiClient) {
  return {
    async listCards(query?: CardListQuery): Promise<ApiCard[]> {
      return unwrapData(await client.request<ApiResult<ApiCard[]>>(`/cards${queryString(query)}`))
    },

    async getCard(cardId: string): Promise<ApiCard> {
      return unwrapData(await client.request<ApiResult<ApiCard>>(`/cards/${encodeURIComponent(cardId)}`))
    },
  }
}
