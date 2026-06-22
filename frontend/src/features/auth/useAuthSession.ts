import { useEffect, useMemo, useState } from 'react'
import { createApiClient, createAuthApi } from '../../shared/api'
import type { AuthSession, LoginRequest, RegisterRequest, UpdateUserRequest } from '../../shared/api'

const refreshTokenKey = 'riftbound-refresh-token-v1'

export function useAuthSession() {
  const [session, setSession] = useState<AuthSession | null>(null)
  const [status, setStatus] = useState('Sign in to sync decks and play online.')
  const apiClient = useMemo(() => createApiClient({ getAccessToken: () => session?.accessToken ?? null }), [session?.accessToken])
  const authApi = useMemo(() => createAuthApi(apiClient), [apiClient])

  useEffect(() => {
    if (session) return
    void refresh().catch(() => {
      localStorage.removeItem(refreshTokenKey)
      setStatus('Sign in to sync decks and play online.')
    })
  }, [session])

  async function login(request: LoginRequest) {
    const next = await authApi.login(request)
    setSession(next)
    localStorage.setItem(refreshTokenKey, next.refreshToken)
    setStatus(`Signed in as ${next.user.displayName}.`)
    return next
  }

  async function register(request: RegisterRequest) {
    const next = await authApi.register(request)
    setSession(next)
    localStorage.setItem(refreshTokenKey, next.refreshToken)
    setStatus(`Registered ${next.user.displayName}.`)
    return next
  }

  async function refresh() {
    const refreshToken = localStorage.getItem(refreshTokenKey)
    if (!refreshToken) return null
    const next = await authApi.refresh(refreshToken)
    setSession(next)
    localStorage.setItem(refreshTokenKey, next.refreshToken)
    setStatus(`Signed in as ${next.user.displayName}.`)
    return next
  }

  async function logout() {
    const refreshToken = localStorage.getItem(refreshTokenKey)
    if (refreshToken) await authApi.logout(refreshToken)
    localStorage.removeItem(refreshTokenKey)
    setSession(null)
    setStatus('Signed out.')
  }

  async function updateMe(request: UpdateUserRequest) {
    const user = await authApi.updateMe(request)
    setSession((current) => current ? { ...current, user } : current)
    setStatus(`Updated ${user.displayName}.`)
    return user
  }

  return {
    apiClient,
    login,
    logout,
    refresh,
    register,
    session,
    status,
    updateMe,
  }
}
