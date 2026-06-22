import { useState } from 'react'
import type { AuthSession, LoginRequest, RegisterRequest } from '../../shared/api'

type AuthPanelProps = {
  session: AuthSession | null
  status: string
  onLogin: (request: LoginRequest) => Promise<AuthSession>
  onLogout: () => Promise<void>
  onRegister: (request: RegisterRequest) => Promise<AuthSession>
}

export function AuthPanel({ session, status, onLogin, onLogout, onRegister }: AuthPanelProps) {
  const [email, setEmail] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function submit() {
    setBusy(true)
    setError('')
    try {
      if (mode === 'register') {
        await onRegister({ email, displayName: displayName || email, password })
      } else {
        await onLogin({ email, password })
      }
      setPassword('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Authentication failed.')
    } finally {
      setBusy(false)
    }
  }

  if (session) {
    return (
      <div className="auth-panel signed-in">
        <span>{session.user.displayName}</span>
        <small>{session.user.email}</small>
        <button type="button" onClick={() => void onLogout()} disabled={busy}>Sign out</button>
      </div>
    )
  }

  return (
    <div className="auth-panel">
      <label>
        Email
        <input value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="email" />
      </label>
      {mode === 'register' && (
        <label>
          Name
          <input value={displayName} onChange={(event) => setDisplayName(event.target.value)} autoComplete="nickname" />
        </label>
      )}
      <label>
        Password
        <input value={password} onChange={(event) => setPassword(event.target.value)} type="password" autoComplete={mode === 'login' ? 'current-password' : 'new-password'} />
      </label>
      <button type="button" onClick={() => void submit()} disabled={busy || !email || !password}>
        {mode === 'login' ? 'Sign in' : 'Register'}
      </button>
      <button type="button" className="secondary-button" onClick={() => setMode(mode === 'login' ? 'register' : 'login')}>
        {mode === 'login' ? 'New account' : 'Have account'}
      </button>
      <small>{error || status}</small>
    </div>
  )
}
