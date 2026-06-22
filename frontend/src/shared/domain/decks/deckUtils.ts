import { randomId } from '../../utils/randomId'
import { domains, savedDecksKey, schemaVersion, type Card, type SavedDeck } from '../../models'
import { inferTagsFromName, isLegendClassification, isMainDeckCard } from '../cards/cardUtils'

export function loadSavedDecks(ownerUserId = 'local-user'): SavedDeck[] {
  const raw = localStorage.getItem(savedDecksKey)
  if (!raw) return []
  try {
    const payload = JSON.parse(raw) as { schemaVersion?: number; decks?: Partial<SavedDeck>[] }
    if (payload.schemaVersion !== schemaVersion || !Array.isArray(payload.decks)) return []
    return payload.decks.map((deck) => normalizeDeck(deck, ownerUserId)).filter(isSavedDeck)
  } catch {
    return []
  }
}

export function saveSavedDecks(decks: SavedDeck[]) {
  localStorage.setItem(savedDecksKey, JSON.stringify({ schemaVersion, decks }))
}

export function isSavedDeck(value: unknown): value is SavedDeck {
  if (!value || typeof value !== 'object') return false
  const deck = value as Partial<SavedDeck>
  return typeof deck.id === 'string'
    && typeof deck.name === 'string'
    && typeof deck.ownerUserId === 'string'
    && (deck.visibility === 'private' || deck.visibility === 'public')
    && typeof deck.legendId === 'string'
    && typeof deck.championId === 'string'
    && Array.isArray(deck.battlefieldDeckIds)
    && Array.isArray(deck.runeDeckIds)
    && Array.isArray(deck.mainDeckIds)
}

export function normalizeDeck(deck: (Partial<SavedDeck> & { battlefieldId?: string }) | null | undefined, ownerUserId = 'local-user'): SavedDeck {
  return {
    id: deck?.id || randomId('deck'),
    name: deck?.name || 'Imported deck',
    ownerUserId: deck?.ownerUserId || ownerUserId,
    visibility: deck?.visibility === 'public' ? 'public' : 'private',
    legendId: deck?.legendId || '',
    championId: deck?.championId || '',
    battlefieldDeckIds: deck?.battlefieldDeckIds ?? (deck?.battlefieldId ? [deck.battlefieldId] : []),
    runeDeckIds: deck?.runeDeckIds ?? [],
    mainDeckIds: deck?.mainDeckIds ?? [],
  }
}

export function blankDeck(): SavedDeck {
  return {
    id: randomId('deck'),
    name: 'New deck',
    ownerUserId: 'local-user',
    visibility: 'private',
    legendId: '',
    championId: '',
    battlefieldDeckIds: [],
    runeDeckIds: [],
    mainDeckIds: [],
  }
}

export function cardTags(card: Card | undefined) {
  return (card?.tags?.length ? card.tags : card ? inferTagsFromName(card.name) : [])
    .map((tag) => tag.trim().toLowerCase())
    .filter(Boolean)
}

export function cardsShareTag(first: Card | undefined, second: Card | undefined) {
  const firstTags = cardTags(first)
  const secondTags = new Set(cardTags(second))
  return firstTags.some((tag) => secondTags.has(tag))
}

export function formatDeckLine(card: Card, amount: number) {
  return `${card.name} - Cost ${card.cost} x${amount}`
}

export function deckValidationMessages(deck: SavedDeck, cards: Card[]) {
  const messages: string[] = []
  const legend = cards.find((card) => card.id === deck.legendId)
  const champion = cards.find((card) => card.id === deck.championId)
  const runeCards = deck.runeDeckIds.map((id) => cards.find((card) => card.id === id)).filter((card): card is Card => Boolean(card))
  const mainDeckCards = deck.mainDeckIds.map((id) => cards.find((card) => card.id === id)).filter((card): card is Card => Boolean(card))

  if (!legend) messages.push('Choose a Legend.')
  if (!champion) messages.push('Choose a Champion.')
  if (legend && champion && !cardsShareTag(legend, champion)) messages.push('Champion tag must match the selected Legend tag.')
  if (deck.mainDeckIds.length > 40) messages.push('Main deck can contain at most 40 cards.')
  if (mainDeckCards.some((card) => !isMainDeckCard(card) && !(card.kind === 'champion' && !isLegendClassification(card)))) {
    messages.push('Main deck can contain units, spells, gear, and champions, but not legends, battlefields, runes, or tokens.')
  }
  if (deck.battlefieldDeckIds.length < 1 || deck.battlefieldDeckIds.length > 3) messages.push('Battlefield deck must contain 1 to 3 cards.')
  if (deck.runeDeckIds.length < 12) messages.push('Rune deck must contain at least 12 cards.')
  legend?.domains.forEach((domain) => {
    if (!domains.includes(domain)) return
    if (!runeCards.some((card) => card.domain === domain)) messages.push(`Rune deck must include at least 1 ${domain} rune.`)
  })

  return messages
}
