import type { AuthSession } from '../../shared/api'
import type { Page } from '../../shared/models'
import { UserAvatar } from '../../shared/ui/UserAvatar'

type AuthPanelProps = {
  session: AuthSession | null
  status: string
  onLogout: () => Promise<void>
  onNavigate: (page: Page) => void
}

export function AuthPanel({ session, status, onLogout, onNavigate }: AuthPanelProps) {
  if (session) {
    return (
      <div className="auth-panel signed-in">
        <UserAvatar user={session.user} />
        <span>{session.user.displayName}</span>
        <small>{session.user.email}</small>
        <button type="button" onClick={() => void onLogout()}>Sign out</button>
      </div>
    )
  }

  return (
    <div className="auth-panel signed-out">
      <button type="button" onClick={() => onNavigate('login')}>Log in</button>
      <button type="button" className="secondary-button" onClick={() => onNavigate('register')}>Sign up</button>
      <small>{status}</small>
    </div>
  )
}
