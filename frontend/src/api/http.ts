import type { ApiErrorPayload, ApiResult } from './types'

export type ApiClientOptions = {
  baseUrl?: string
  fetcher?: typeof fetch
  getAccessToken?: () => string | null | Promise<string | null>
}

export class ApiError extends Error {
  readonly status: number
  readonly payload: ApiErrorPayload | null

  constructor(status: number, payload: ApiErrorPayload | null) {
    super(payload?.message ?? `API request failed with status ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.payload = payload
  }
}

export type ApiClient = {
  request<T>(path: string, init?: RequestInit): Promise<T>
}

function joinUrl(baseUrl: string, path: string) {
  const normalizedBase = baseUrl.replace(/\/$/, '')
  const normalizedPath = path.startsWith('/') ? path : `/${path}`
  return `${normalizedBase}${normalizedPath}`
}

async function readError(response: Response): Promise<ApiErrorPayload | null> {
  try {
    const payload = await response.json() as unknown
    if (!payload || typeof payload !== 'object') return null
    const candidate = payload as Partial<ApiErrorPayload>
    if (typeof candidate.code === 'string' && typeof candidate.message === 'string') return candidate as ApiErrorPayload
    const result = payload as { data?: Partial<ApiErrorPayload> }
    if (result.data && typeof result.data.code === 'string' && typeof result.data.message === 'string') {
      return result.data as ApiErrorPayload
    }
    return null
  } catch {
    return null
  }
}

export function createApiClient({
  baseUrl = '/api/v1',
  fetcher = fetch,
  getAccessToken,
}: ApiClientOptions = {}): ApiClient {
  return {
    async request<T>(path: string, init: RequestInit = {}) {
      const token = await getAccessToken?.()
      const headers = new Headers(init.headers)
      if (!headers.has('Accept')) headers.set('Accept', 'application/json')
      if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
      if (token) headers.set('Authorization', `Bearer ${token}`)

      const response = await fetcher(joinUrl(baseUrl, path), { ...init, headers })
      if (!response.ok) throw new ApiError(response.status, await readError(response))
      if (response.status === 204) return undefined as T
      return await response.json() as T
    },
  }
}

export function unwrapData<T>(result: ApiResult<T>): T {
  return result.data
}

export function queryString(query: Record<string, string | number | boolean | string[] | null | undefined> = {}) {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue
    if (Array.isArray(value)) {
      for (const item of value) params.append(key, item)
    } else {
      params.set(key, String(value))
    }
  }
  const serialized = params.toString()
  return serialized ? `?${serialized}` : ''
}
