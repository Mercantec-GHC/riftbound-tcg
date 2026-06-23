export type UserStats = {
  gamesPlayed: number
  wins: number
  losses: number
  pointsScored: number
  lastPlayedAt?: string
}

export type UserProfile = {
  id: string
  displayName: string
  avatarImageHash?: string | null
  createdAt: string
  stats: UserStats
}

export const userProfilesKey = 'rift-prototype-user-profiles-v1'
export const activeUserKey = 'rift-prototype-active-user-v1'
