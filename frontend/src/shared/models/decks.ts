import type { Domain } from './cards'

export type SavedDeck = {
  id: string
  name: string
  ownerUserId: string
  visibility: 'private' | 'public'
  legendId: string
  championId: string
  battlefieldDeckIds: string[]
  runeDeckIds: string[]
  mainDeckIds: string[]
}

export type SharedDeck = SavedDeck & {
  author: string
  visibility: 'private' | 'public'
  tags: string[]
  domains: Domain[]
  legendName: string
  championName: string
  cardCounts: {
    main: number
    runes: number
    battlefields: number
  }
  description?: string
  updatedAt?: string
}

export const schemaVersion = 2
export const savedDecksKey = 'rift-prototype-saved-decks-v1'
export const localDecksEndpoint = '/api/local-decks'
