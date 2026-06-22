import type { ApiClient } from '../http'
import { unwrapData } from '../http'
import type { ApiResult, CreateLobbyRequest, Lobby, UpdateLobbyLoadoutRequest, UpdateLobbySettingsRequest } from '../types'

export function createLobbiesApi(client: ApiClient) {
  return {
    async listLobbies(): Promise<Lobby[]> {
      return unwrapData(await client.request<ApiResult<Lobby[]>>('/lobbies'))
    },

    async createLobby(request: CreateLobbyRequest): Promise<Lobby> {
      return unwrapData(await client.request<ApiResult<Lobby>>('/lobbies', {
        method: 'POST',
        body: JSON.stringify(request),
      }))
    },

    async getLobby(lobbyId: string): Promise<Lobby> {
      return unwrapData(await client.request<ApiResult<Lobby>>(`/lobbies/${encodeURIComponent(lobbyId)}`))
    },

    async joinLobby(lobbyId: string): Promise<Lobby> {
      return unwrapData(await client.request<ApiResult<Lobby>>(`/lobbies/${encodeURIComponent(lobbyId)}/join`, {
        method: 'POST',
      }))
    },

    async leaveLobby(lobbyId: string): Promise<Lobby> {
      return unwrapData(await client.request<ApiResult<Lobby>>(`/lobbies/${encodeURIComponent(lobbyId)}/leave`, {
        method: 'POST',
      }))
    },

    async updateSettings(lobbyId: string, request: UpdateLobbySettingsRequest): Promise<Lobby> {
      return unwrapData(await client.request<ApiResult<Lobby>>(`/lobbies/${encodeURIComponent(lobbyId)}/settings`, {
        method: 'PATCH',
        body: JSON.stringify(request),
      }))
    },

    async updateLoadout(lobbyId: string, request: UpdateLobbyLoadoutRequest): Promise<Lobby> {
      return unwrapData(await client.request<ApiResult<Lobby>>(`/lobbies/${encodeURIComponent(lobbyId)}/loadout`, {
        method: 'PATCH',
        body: JSON.stringify(request),
      }))
    },

    async ready(lobbyId: string): Promise<Lobby> {
      return unwrapData(await client.request<ApiResult<Lobby>>(`/lobbies/${encodeURIComponent(lobbyId)}/ready`, {
        method: 'POST',
      }))
    },

    async unready(lobbyId: string): Promise<Lobby> {
      return unwrapData(await client.request<ApiResult<Lobby>>(`/lobbies/${encodeURIComponent(lobbyId)}/unready`, {
        method: 'POST',
      }))
    },

    async start(lobbyId: string): Promise<Lobby> {
      return unwrapData(await client.request<ApiResult<Lobby>>(`/lobbies/${encodeURIComponent(lobbyId)}/start`, {
        method: 'POST',
      }))
    },
  }
}
