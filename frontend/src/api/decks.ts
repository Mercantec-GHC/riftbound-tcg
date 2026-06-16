import type { ApiClient } from './http'
import { queryString, unwrapData } from './http'
import type { ApiDeck, ApiResult, ApiSharedDeck, CreateDeckRequest, DeckListQuery, UpdateDeckRequest } from './types'

export function createDecksApi(client: ApiClient) {
  return {
    async listDecks(query?: DeckListQuery): Promise<ApiDeck[]> {
      return unwrapData(await client.request<ApiResult<ApiDeck[]>>(`/decks${queryString(query)}`))
    },

    async listPublicDecks(query?: Omit<DeckListQuery, 'visibility'>): Promise<ApiSharedDeck[]> {
      return unwrapData(await client.request<ApiResult<ApiSharedDeck[]>>(`/decks/public${queryString(query)}`))
    },

    async getDeck(deckId: string): Promise<ApiDeck> {
      return unwrapData(await client.request<ApiResult<ApiDeck>>(`/decks/${encodeURIComponent(deckId)}`))
    },

    async createDeck(deck: CreateDeckRequest): Promise<ApiDeck> {
      return unwrapData(await client.request<ApiResult<ApiDeck>>('/decks', {
        method: 'POST',
        body: JSON.stringify(deck),
      }))
    },

    async updateDeck(deckId: string, deck: UpdateDeckRequest): Promise<ApiDeck> {
      return unwrapData(await client.request<ApiResult<ApiDeck>>(`/decks/${encodeURIComponent(deckId)}`, {
        method: 'PATCH',
        body: JSON.stringify(deck),
      }))
    },

    async deleteDeck(deckId: string): Promise<void> {
      await client.request<void>(`/decks/${encodeURIComponent(deckId)}`, { method: 'DELETE' })
    },
  }
}
