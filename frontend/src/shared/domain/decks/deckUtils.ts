import { randomId } from '../../utils/randomId'
import { savedDecksKey, schemaVersion, type Card, type SavedDeck } from '../../models'
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

export function cardFitsDomainIdentity(card: Card, legend: Card | undefined) {
  if (!legend) return true
  const identity = new Set((legend.domains.length > 0 ? legend.domains : [legend.domain]).map((domain) => domain.toLowerCase()))
  const cardDomains = card.domains.length > 0 ? card.domains : [card.domain]
  return cardDomains.every((domain) => identity.has(domain.toLowerCase()))
}

export function isSignatureCard(card: Card) {
  return card.supertype?.toLowerCase().includes('signature') ?? false
}

export function formatDeckLine(card: Card, amount: number) {
  return `${card.name} - Cost ${card.cost} x${amount}`
}

export function deckValidationMessages(deck: SavedDeck, cards: Card[]) {
  const messages: string[] = []
  const legend = cards.find((card) => card.id === deck.legendId)
  const champion = cards.find((card) => card.id === deck.championId)
  const battlefieldCards = deck.battlefieldDeckIds.map((id) => cards.find((card) => card.id === id)).filter((card): card is Card => Boolean(card))
  const runeCards = deck.runeDeckIds.map((id) => cards.find((card) => card.id === id)).filter((card): card is Card => Boolean(card))
  const mainDeckCards = deck.mainDeckIds.map((id) => cards.find((card) => card.id === id)).filter((card): card is Card => Boolean(card))

  if (!legend || legend.kind !== 'legend') messages.push('Choose a Legend.')
  if (!champion || champion.kind !== 'champion') messages.push('Choose a Champion.')
  if (legend && champion && !cardsShareTag(legend, champion)) messages.push('Champion tag must match the selected Legend tag.')
  if (champion && isSignatureCard(champion)) messages.push('Chosen Champion cannot be a Signature card.')
  if (deck.mainDeckIds.length < 40) messages.push('Main deck must contain at least 40 cards.')
  if (mainDeckCards.some((card) => !isMainDeckCard(card) && !(card.kind === 'champion' && !isLegendClassification(card)))) {
    messages.push('Main deck can contain units, spells, gear, and champions, but not legends, battlefields, runes, or tokens.')
  }
  if (deck.battlefieldDeckIds.length !== 3 || battlefieldCards.length !== deck.battlefieldDeckIds.length) messages.push('Battlefield deck must contain exactly 3 valid battlefield cards.')
  if (new Set(battlefieldCards.map((card) => card.name.trim().toLowerCase())).size !== battlefieldCards.length) messages.push('Battlefield deck cannot contain duplicate battlefield names.')
  if (deck.runeDeckIds.length !== 12 || runeCards.length !== deck.runeDeckIds.length) messages.push('Rune deck must contain exactly 12 valid rune cards.')

  if (legend) {
    const offDomain = [...mainDeckCards, ...runeCards, ...battlefieldCards].filter((card) => !cardFitsDomainIdentity(card, legend))
    if (offDomain.length > 0) messages.push(`Cards must match the selected Legend domain identity: ${[...new Set(offDomain.map((card) => card.name))].join(', ')}.`)
  }

  const copyNames = [...mainDeckCards, ...(champion ? [champion] : [])].reduce<Record<string, { name: string; count: number }>>((counts, card) => {
    const key = card.name.trim().toLowerCase()
    if (!key) return counts
    counts[key] = { name: card.name, count: (counts[key]?.count ?? 0) + 1 }
    return counts
  }, {})
  const overCopies = Object.values(copyNames).filter((entry) => entry.count > 3)
  if (overCopies.length > 0) messages.push(`Main deck can include up to 3 copies of the same named card, counting the chosen Champion: ${overCopies.map((entry) => entry.name).join(', ')}.`)

  const signatures = mainDeckCards.filter(isSignatureCard)
  if (signatures.length > 3) messages.push('Main deck can include up to 3 total Signature cards.')
  if (legend) {
    const offTagSignatures = signatures.filter((card) => !cardsShareTag(legend, card))
    if (offTagSignatures.length > 0) messages.push(`Signature cards must share a Champion tag with the selected Legend: ${[...new Set(offTagSignatures.map((card) => card.name))].join(', ')}.`)
  }

  return messages
}
