import type { Card } from '../../models'
import type { ApiClient } from '../http'
import { unwrapData } from '../http'
import type {
  AdminUpdateDeckRequest,
  AdminUpdateUserRequest,
  ApiAdminDeck,
  ApiResult,
  ApiUserProfile,
  CardUpsertResult,
  RiftCodexImportResult,
} from '../types'

export function createAdminApi(client: ApiClient) {
  return {
    async listUsers(): Promise<ApiUserProfile[]> {
      return unwrapData(await client.request<ApiResult<ApiUserProfile[]>>('/admin/users'))
    },

    async updateUser(userId: string, request: AdminUpdateUserRequest): Promise<ApiUserProfile> {
      return unwrapData(await client.request<ApiResult<ApiUserProfile>>(`/admin/users/${encodeURIComponent(userId)}`, {
        method: 'PATCH',
        body: JSON.stringify(request),
      }))
    },

    async listDecks(): Promise<ApiAdminDeck[]> {
      return unwrapData(await client.request<ApiResult<ApiAdminDeck[]>>('/admin/decks'))
    },

    async updateDeck(deckId: string, request: AdminUpdateDeckRequest): Promise<ApiAdminDeck> {
      return unwrapData(await client.request<ApiResult<ApiAdminDeck>>(`/admin/decks/${encodeURIComponent(deckId)}`, {
        method: 'PATCH',
        body: JSON.stringify(request),
      }))
    },

    async deleteDeck(deckId: string): Promise<void> {
      await client.request<void>(`/admin/decks/${encodeURIComponent(deckId)}`, {
        method: 'DELETE',
      })
    },

    async upsertCard(card: Card): Promise<CardUpsertResult> {
      return unwrapData(await client.request<ApiResult<CardUpsertResult>>('/admin/cards', {
        method: 'POST',
        body: JSON.stringify(card),
      }))
    },

    async deleteCard(cardId: string): Promise<void> {
      await client.request<void>(`/admin/cards/${encodeURIComponent(cardId)}`, {
        method: 'DELETE',
      })
    },

    async importRiftCodex(): Promise<RiftCodexImportResult> {
      return unwrapData(await client.request<ApiResult<RiftCodexImportResult>>('/admin/cards/import/riftcodex', {
        method: 'POST',
      }))
    },
  }
}
