import { isLegendClassification, isMainDeckCard } from '../../cardUtils'
import type { Card, SavedDeck, SharedDeck } from '../../models'

export type SharedDeckFilters = {
  search: string
  visibility: 'all' | 'private' | 'public'
  tag: string
}

export function isDeckBuilderMainDeckCard(card: Card) {
  return !isLegendClassification(card) && (isMainDeckCard(card) || card.kind === 'champion')
}

export function isSharedDeck(value: unknown): value is SharedDeck {
  if (!value || typeof value !== 'object') return false
  const deck = value as Partial<SharedDeck>
  return typeof deck.id === 'string'
    && typeof deck.name === 'string'
    && typeof deck.legendId === 'string'
    && typeof deck.championId === 'string'
    && Array.isArray(deck.battlefieldDeckIds)
    && Array.isArray(deck.runeDeckIds)
    && Array.isArray(deck.mainDeckIds)
    && typeof deck.author === 'string'
    && Array.isArray(deck.tags)
}

function uniqueValues(values: string[]) {
  return [...new Set(values.filter(Boolean))]
}

export function deckToPrivateSharedDeck(deck: SavedDeck, cards: Card[]): SharedDeck {
  const allCards = [...deck.mainDeckIds, ...deck.runeDeckIds, ...deck.battlefieldDeckIds, deck.legendId, deck.championId]
    .map((id) => cards.find((card) => card.id === id))
    .filter((card): card is Card => Boolean(card))
  const legend = cards.find((card) => card.id === deck.legendId)
  const champion = cards.find((card) => card.id === deck.championId)
  const tags = uniqueValues(allCards.flatMap((card) => card.tags))
  const domains = [...new Set(allCards.flatMap((card) => card.domains))]

  return {
    ...deck,
    ownerUserId: deck.ownerUserId ?? 'local-user',
    visibility: deck.visibility ?? 'private',
    author: 'You',
    tags,
    domains,
    legendName: legend?.name ?? '',
    championName: champion?.name ?? '',
    cardCounts: {
      main: deck.mainDeckIds.length,
      runes: deck.runeDeckIds.length,
      battlefields: deck.battlefieldDeckIds.length,
    },
    description: `${deck.name} uses ${domains.join(' / ') || 'no'} domains with ${legend?.name ?? 'no Legend'} and ${champion?.name ?? 'no Champion'}.`,
    updatedAt: new Date().toISOString(),
  }
}

export function normalizeSharedDeck(deck: SharedDeck): SharedDeck {
  return {
    ...deck,
    ownerUserId: deck.ownerUserId ?? 'local-user',
    visibility: deck.visibility === 'public' ? 'public' : 'private',
    domains: deck.domains ?? [],
    legendName: deck.legendName ?? '',
    championName: deck.championName ?? '',
    cardCounts: deck.cardCounts ?? {
      main: deck.mainDeckIds.length,
      runes: deck.runeDeckIds.length,
      battlefields: deck.battlefieldDeckIds.length,
    },
  }
}

export function userCanAccessDeck(deck: SavedDeck, userId: string) {
  return deck.visibility === 'public' || deck.ownerUserId === userId
}

export function filterSharedDecks(decks: SharedDeck[], filters: SharedDeckFilters, activeUserId = 'local-user') {
  const search = filters.search.trim().toLowerCase()
  const tag = filters.tag.trim().toLowerCase()
  return decks.filter((deck) => {
    const haystack = [
      deck.name,
      deck.author,
      deck.visibility,
      deck.description ?? '',
      deck.tags.join(' '),
      deck.domains.join(' '),
      deck.legendName,
      deck.championName,
      deck.legendId,
      deck.championId,
    ].join(' ').toLowerCase()
    if (!userCanAccessDeck(deck, activeUserId)) return false
    if (filters.visibility !== 'all' && deck.visibility !== filters.visibility) return false
    if (search && !haystack.includes(search)) return false
    if (tag && ![...deck.tags, ...deck.domains].some((deckTag) => deckTag.toLowerCase().includes(tag))) return false
    return true
  }).sort((first, second) => first.name.localeCompare(second.name))
}
