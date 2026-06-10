import { useMemo, useState } from 'react'
import {
  activeUserKey,
  userProfilesKey,
  type UserProfile,
  type UserStats,
} from '../../models'

const defaultUserId = 'local-user'

function blankStats(): UserStats {
  return {
    gamesPlayed: 0,
    wins: 0,
    losses: 0,
    pointsScored: 0,
  }
}

function createDefaultProfile(): UserProfile {
  return {
    id: defaultUserId,
    displayName: 'Local Player',
    createdAt: new Date().toISOString(),
    stats: blankStats(),
  }
}

function isUserProfile(value: unknown): value is UserProfile {
  if (!value || typeof value !== 'object') return false
  const profile = value as Partial<UserProfile>
  return typeof profile.id === 'string'
    && typeof profile.displayName === 'string'
    && typeof profile.createdAt === 'string'
    && typeof profile.stats === 'object'
}

function normalizeProfile(profile: UserProfile): UserProfile {
  return {
    ...profile,
    stats: {
      ...blankStats(),
      ...profile.stats,
    },
  }
}

function loadProfiles() {
  try {
    const raw = localStorage.getItem(userProfilesKey)
    const parsed = raw ? JSON.parse(raw) as unknown : null
    const profiles = Array.isArray(parsed) ? parsed.filter(isUserProfile).map(normalizeProfile) : []
    return profiles.length > 0 ? profiles : [createDefaultProfile()]
  } catch {
    return [createDefaultProfile()]
  }
}

function saveProfiles(profiles: UserProfile[]) {
  localStorage.setItem(userProfilesKey, JSON.stringify(profiles))
}

function loadActiveUserId(profiles: UserProfile[]) {
  const stored = localStorage.getItem(activeUserKey)
  return profiles.some((profile) => profile.id === stored) ? stored ?? profiles[0].id : profiles[0].id
}

export function useUserProfiles() {
  const [profiles, setProfiles] = useState<UserProfile[]>(loadProfiles)
  const [activeUserId, setActiveUserIdState] = useState(() => loadActiveUserId(profiles))

  const activeUser = useMemo(
    () => profiles.find((profile) => profile.id === activeUserId) ?? profiles[0],
    [activeUserId, profiles],
  )

  function setActiveUserId(userId: string) {
    if (!profiles.some((profile) => profile.id === userId)) return
    setActiveUserIdState(userId)
    localStorage.setItem(activeUserKey, userId)
  }

  function createProfile(displayName: string) {
    const profile: UserProfile = {
      id: `user-${crypto.randomUUID()}`,
      displayName: displayName.trim() || `Player ${profiles.length + 1}`,
      createdAt: new Date().toISOString(),
      stats: blankStats(),
    }
    const next = [...profiles, profile]
    setProfiles(next)
    saveProfiles(next)
    setActiveUserIdState(profile.id)
    localStorage.setItem(activeUserKey, profile.id)
  }

  function updateStats(mapper: (profiles: UserProfile[]) => UserProfile[]) {
    setProfiles((current) => {
      const next = mapper(current).map(normalizeProfile)
      saveProfiles(next)
      return next
    })
  }

  return {
    activeUser,
    createProfile,
    profiles,
    setActiveUserId,
    updateStats,
  }
}
