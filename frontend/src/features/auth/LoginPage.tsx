import { useState, type FormEvent } from 'react'
import type { AuthSession, LoginRequest } from '../../shared/api'
import type { Page } from '../../shared/models'

type LoginPageProps = {
  onLogin: (request: LoginRequest) => Promise<AuthSession>
  onNavigate: (page: Page) => void
}

export function LoginPage({ onLogin, onNavigate }: LoginPageProps) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      await onLogin({ email, password })
      setPassword('')
      onNavigate('account')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Authentication failed.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="auth-page">
      <form className="auth-card" onSubmit={(event) => void submit(event)}>
        <div>
          <p className="eyebrow">log in</p>
          <h2>Return to the table</h2>
          <p>Use your account to sync decks and play online.</p>
        </div>

        <label>
          Email
          <input value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="email" type="email" />
        </label>

        <label>
          Password
          <input value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="current-password" type="password" />
        </label>

        <div className="auth-actions">
          <button type="submit" disabled={busy || !email || !password}>Log in</button>
          <button type="button" className="secondary-button" onClick={() => onNavigate('register')}>Create account</button>
        </div>
        {error && <small className="form-error">{error}</small>}
      </form>
    </section>
  )
}
