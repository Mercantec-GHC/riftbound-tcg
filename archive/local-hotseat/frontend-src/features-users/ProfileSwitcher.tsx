import { useState } from 'react'
import type { UserProfile } from '../../models'

export function ProfileSwitcher({
  activeUser,
  onCreateProfile,
  onSetActiveUser,
  profiles,
}: {
  activeUser: UserProfile
  onCreateProfile: (displayName: string) => void
  onSetActiveUser: (userId: string) => void
  profiles: UserProfile[]
}) {
  const [displayName, setDisplayName] = useState('')
  const [open, setOpen] = useState(false)

  return (
    <section className="profile-switcher" aria-label="Local profile">
      <button className="profile-menu-button" type="button" aria-expanded={open} onClick={() => setOpen((current) => !current)}>
        <span className="profile-avatar" aria-hidden="true">
          {activeUser.displayName.trim().charAt(0).toUpperCase() || 'P'}
        </span>
        <span>{activeUser.displayName}</span>
      </button>

      {open && (
        <div className="profile-menu" role="menu">
          <div className="profile-menu-header">
            <strong>{activeUser.displayName}</strong>
            <small>
              {activeUser.stats.gamesPlayed} games · {activeUser.stats.wins}W/{activeUser.stats.losses}L · {activeUser.stats.pointsScored} pts
            </small>
          </div>

          <label>
            Active profile
            <select value={activeUser.id} onChange={(event) => onSetActiveUser(event.target.value)}>
              {profiles.map((profile) => (
                <option key={profile.id} value={profile.id}>
                  {profile.displayName}
                </option>
              ))}
            </select>
          </label>

          <div className="profile-create-row">
            <label>
              New profile
              <input value={displayName} onChange={(event) => setDisplayName(event.target.value)} placeholder="Name" />
            </label>
            <button
              type="button"
              onClick={() => {
                onCreateProfile(displayName)
                setDisplayName('')
              }}
            >
              Add
            </button>
          </div>
        </div>
      )}
    </section>
  )
}
