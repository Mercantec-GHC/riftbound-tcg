import type { ApiUserProfile } from '../api'

type UserAvatarProps = {
  user: Pick<ApiUserProfile, 'avatarImageHash' | 'displayName' | 'email'> | null
  size?: 'small' | 'large'
}

export function UserAvatar({ user, size = 'small' }: UserAvatarProps) {
  const label = user?.displayName || user?.email || 'User'
  const initial = label.trim().charAt(0).toUpperCase() || '?'

  return (
    <span className={`user-avatar ${size === 'large' ? 'large' : ''}`} aria-label={`${label} avatar`}>
      {user?.avatarImageHash
        ? <img src={`/api/v1/profile-images/${encodeURIComponent(user.avatarImageHash)}`} alt="" />
        : <span aria-hidden="true">{initial}</span>}
    </span>
  )
}
