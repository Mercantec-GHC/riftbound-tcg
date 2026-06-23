import { useState, type FormEvent } from 'react'
import type { AuthSession, RegisterRequest } from '../../shared/api'
import type { Page } from '../../shared/models'

type RegisterPageProps = {
  onNavigate: (page: Page) => void
  onRegister: (request: RegisterRequest) => Promise<AuthSession>
}

export function RegisterPage({ onNavigate, onRegister }: RegisterPageProps) {
  const [email, setEmail] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      await onRegister({ email, displayName: displayName || email, password })
      setPassword('')
      onNavigate('account')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Registration failed.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="auth-page">
      <form className="auth-card" onSubmit={(event) => void submit(event)}>
        <div>
          <p className="eyebrow">sign up</p>
          <h2>Create your profile</h2>
          <p>Set up an account for online play and synced decks.</p>
        </div>

        <label>
          Email
          <input value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="email" type="email" />
        </label>

        <label>
          Display name
          <input value={displayName} onChange={(event) => setDisplayName(event.target.value)} autoComplete="nickname" />
        </label>

        <label>
          Password
          <input value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="new-password" type="password" />
        </label>

        <div className="auth-actions">
          <button type="submit" disabled={busy || !email || !password}>Sign up</button>
          <button type="button" className="secondary-button" onClick={() => onNavigate('login')}>Log in</button>
        </div>
        {error && <small className="form-error">{error}</small>}
      </form>
    </section>
  )
}
