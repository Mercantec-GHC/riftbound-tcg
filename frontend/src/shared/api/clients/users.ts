import type { ApiClient } from '../http'
import { unwrapData } from '../http'
import type { ApiResult, ApiUserProfile, CreateUserRequest, UpdateUserRequest } from '../types'

export function createUsersApi(client: ApiClient) {
  return {
    async listUsers(): Promise<ApiUserProfile[]> {
      return unwrapData(await client.request<ApiResult<ApiUserProfile[]>>('/users'))
    },

    async getUser(userId: string): Promise<ApiUserProfile> {
      return unwrapData(await client.request<ApiResult<ApiUserProfile>>(`/users/${encodeURIComponent(userId)}`))
    },

    async createUser(user: CreateUserRequest): Promise<ApiUserProfile> {
      return unwrapData(await client.request<ApiResult<ApiUserProfile>>('/users', {
        method: 'POST',
        body: JSON.stringify(user),
      }))
    },

    async updateUser(userId: string, user: UpdateUserRequest): Promise<ApiUserProfile> {
      return unwrapData(await client.request<ApiResult<ApiUserProfile>>(`/users/${encodeURIComponent(userId)}`, {
        method: 'PATCH',
        body: JSON.stringify(user),
      }))
    },
  }
}
