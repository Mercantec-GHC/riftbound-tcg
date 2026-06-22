import type { ApiClient } from '../http'
import { unwrapData } from '../http'
import type { ApiResult, AuthSession, LoginRequest, RegisterRequest, UpdateUserRequest, ApiUserProfile } from '../types'

export function createAuthApi(client: ApiClient) {
  return {
    async register(request: RegisterRequest): Promise<AuthSession> {
      return unwrapData(await client.request<ApiResult<AuthSession>>('/auth/register', {
        method: 'POST',
        body: JSON.stringify(request),
      }))
    },

    async login(request: LoginRequest): Promise<AuthSession> {
      return unwrapData(await client.request<ApiResult<AuthSession>>('/auth/login', {
        method: 'POST',
        body: JSON.stringify(request),
      }))
    },

    async refresh(refreshToken: string): Promise<AuthSession> {
      return unwrapData(await client.request<ApiResult<AuthSession>>('/auth/refresh', {
        method: 'POST',
        body: JSON.stringify({ refreshToken }),
      }))
    },

    async logout(refreshToken: string): Promise<void> {
      await client.request<void>('/auth/logout', {
        method: 'POST',
        body: JSON.stringify({ refreshToken }),
      })
    },

    async me(): Promise<ApiUserProfile> {
      return unwrapData(await client.request<ApiResult<ApiUserProfile>>('/me'))
    },

    async updateMe(request: UpdateUserRequest): Promise<ApiUserProfile> {
      return unwrapData(await client.request<ApiResult<ApiUserProfile>>('/me', {
        method: 'PATCH',
        body: JSON.stringify(request),
      }))
    },
  }
}
