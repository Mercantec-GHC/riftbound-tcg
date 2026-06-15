import {
  customCardsKey,
  domains,
  fallbackBattlefieldOptions,
  savedDecksKey,
  schemaVersion,
  type Card,
  type CardKind,
  type Domain,
  type Effect,
  type RiftCodexCard,
  type SavedDeck,
} from './models'
import { randomId } from './utils/randomId'

export function inferTagsFromName(name: string) {
  const tag = name.split(' - ')[0]?.trim()
  return tag ? [tag] : []
}

export function cloneCard(card: Card): Card {
  return {
    ...card,
    tags: card.tags?.length ? [...card.tags] : inferTagsFromName(card.name),
    image: card.image || '✨',
    domains: card.domains?.length ? [...card.domains] : [card.domain],
    cardType: card.cardType || card.kind,
    supertype: card.supertype ?? null,
    effect: { ...card.effect },
  }
}

export function isMainDeckCard(card: Card) {
  return card.kind !== 'champion' && card.kind !== 'legend' && card.kind !== 'battlefield' && card.kind !== 'token' && card.kind !== 'rune'
}

export function battlefieldOptionsFromCards(cards: Card[]) {
  const imported = cards
    .filter((card) => card.kind === 'battlefield')
    .map((card) => ({
      id: card.id,
      name: card.name,
      claim: Math.max(1, card.cost || 2),
      image: card.image,
      text: card.text,
    }))
  return imported.length > 0 ? imported : fallbackBattlefieldOptions
}

export function makeDeck(extraCards: Card[], offset: number) {
  const pool = extraCards.filter(isMainDeckCard).map(cloneCard)
  if (pool.length === 0) return []
  return Array.from({ length: 4 }, (_, cycle) =>
    pool.map((card, index) => ({
      ...cloneCard(card),
      id: `${card.id}-${offset}-${cycle}-${index}`,
    })),
  ).flat()
}

export function makeRuneDeck(cards: Card[], legend: Card | null, playerId: number) {
  const allowedDomains = (legend?.domains?.length ? legend.domains : domains).slice(0, 2)
  const runePool = cards.filter((card) => card.kind === 'rune')
  const selected = allowedDomains.flatMap((domain, domainIndex) =>
    makeRuneCopiesForDomain(runePool, domain, playerId, domainIndex),
  )

  return shuffleCards(selected, playerId + 17)
}

function makeRuneCopiesForDomain(runePool: Card[], domain: Domain, playerId: number, domainIndex: number) {
  const domainRunes = runePool.filter((card) => card.domain === domain)
  if (domainRunes.length === 0) return []

  return Array.from({ length: 6 }, (_, index) => {
    const rune = domainRunes[(index + playerId + domainIndex) % domainRunes.length]
    return { ...cloneCard(rune), id: `${rune.id}-rune-${playerId}-${domain}-${index}` }
  })
}

function shuffleCards(cards: Card[], seed: number) {
  const shuffled = [...cards]
  let state = seed || 1
  for (let index = shuffled.length - 1; index > 0; index -= 1) {
    state = (state * 1664525 + 1013904223) % 4294967296
    const swapIndex = state % (index + 1)
    ;[shuffled[index], shuffled[swapIndex]] = [shuffled[swapIndex], shuffled[index]]
  }
  return shuffled
}

export function loadCustomCards(): Card[] {
  const raw = localStorage.getItem(customCardsKey)
  if (!raw) return []
  try {
    const payload = JSON.parse(raw) as { schemaVersion?: number; cards?: Card[] }
    if (payload.schemaVersion !== schemaVersion || !Array.isArray(payload.cards)) return []
    return payload.cards
      .filter((card) => card.name && ['unit', 'spell', 'gear', 'champion', 'legend', 'battlefield', 'token', 'rune'].includes(card.kind) && domains.includes(card.domain))
      .map(cloneCard)
  } catch {
    return []
  }
}

export function saveCustomCards(cards: Card[]) {
  localStorage.setItem(customCardsKey, JSON.stringify({ schemaVersion, cards }))
}

export function mergeCards(existing: Card[], incoming: Card[]) {
  const incomingIds = new Set(incoming.map((card) => card.id))
  return [...existing.filter((card) => !incomingIds.has(card.id)), ...incoming]
}

function stripMarkup(value: string) {
  return value.replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim()
}

function toNumber(value: unknown, fallback: number) {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback
}

function domainFromRaw(value: string | null | undefined): Domain {
  const raw = `${value ?? ''}`.toLowerCase()
  if (raw.includes('fury')) return 'Fury'
  if (raw.includes('calm')) return 'Calm'
  if (raw.includes('mind')) return 'Mind'
  if (raw.includes('body')) return 'Body'
  if (raw.includes('chaos')) return 'Chaos'
  return 'Order'
}

function domainsFromRaw(card: RiftCodexCard): Domain[] {
  const mapped = (card.classification.domain ?? [])
    .map(domainFromRaw)
    .filter((domain, index, list) => list.indexOf(domain) === index)
  return mapped.length > 0 ? mapped : [domainFromRaw(undefined)]
}

function kindFromRaw(card: RiftCodexCard): CardKind {
  const type = `${card.classification.type ?? ''}`.toLowerCase()
  const supertype = `${card.classification.supertype ?? ''}`.toLowerCase()
  if (supertype.includes('token')) return 'token'
  if (type.includes('rune')) return 'rune'
  if (type.includes('battlefield')) return 'battlefield'
  if (type.includes('champion') || supertype.includes('champion')) return 'champion'
  if (type.includes('legend')) return 'legend'
  if (type.includes('spell')) return 'spell'
  if (type.includes('gear') || type.includes('equipment') || type.includes('attachment')) return 'gear'
  return 'unit'
}

function effectFromText(text: string, kind: CardKind): Effect {
  const lower = text.toLowerCase()
  if (lower.includes('draw')) return { type: 'draw', amount: 1 }
  if (lower.includes('damage') || lower.includes('deal')) return { type: 'damage', amount: 2 }
  if (lower.includes('+') || kind === 'gear') return { type: 'buff', amount: 1 }
  return { type: 'rally', amount: lower.includes('ready') ? 1 : 0 }
}

function isRawRiftCodexCard(value: unknown): value is RiftCodexCard {
  if (!value || typeof value !== 'object') return false
  const card = value as Partial<RiftCodexCard>
  return typeof card.id === 'string'
    && typeof card.name === 'string'
    && typeof card.attributes === 'object'
    && typeof card.classification === 'object'
    && typeof card.text === 'object'
    && typeof card.media === 'object'
}

function rawCardToGameCard(card: RiftCodexCard): Card | null {
  if (!card.classification.type) return null
  const kind = kindFromRaw(card)
  const text = stripMarkup(card.text.plain ?? card.text.rich ?? 'Imported from Rift Codex.')
  const cost = toNumber(card.attributes.energy, kind === 'unit' || kind === 'champion' ? 2 : 1)
  const might = kind === 'unit' || kind === 'champion'
    ? Math.max(1, toNumber(card.attributes.might ?? card.attributes.power, 2))
    : Math.max(0, toNumber(card.attributes.might ?? card.attributes.power, 0))
  const cardDomains = domainsFromRaw(card)

  return {
    id: `api-${card.id}`,
    name: card.name,
    kind,
    tags: card.tags?.length ? [...card.tags] : inferTagsFromName(card.name),
    domain: cardDomains[0],
    domains: cardDomains,
    cost,
    might,
    image: card.media.image_url ?? '🃏',
    text,
    cardType: card.classification.type,
    supertype: card.classification.supertype,
    effect: effectFromText(text, kind),
  }
}

export function localCacheToGameCards(data: unknown[]) {
  return data
    .map((card) => {
      if (isRawRiftCodexCard(card)) return rawCardToGameCard(card)
      if (card && typeof card === 'object' && 'kind' in card) return cloneCard(card as Card)
      return null
    })
    .filter((card): card is Card => Boolean(card))
}

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

  if (!legend) messages.push('Choose a Legend.')
  if (!champion) messages.push('Choose a Champion.')
  if (legend && champion && !cardsShareTag(legend, champion)) messages.push('Champion tag must match the selected Legend tag.')
  if (deck.mainDeckIds.length > 40) messages.push('Main deck can contain at most 40 cards.')
  if (deck.battlefieldDeckIds.length < 1 || deck.battlefieldDeckIds.length > 3) messages.push('Battlefield deck must contain 1 to 3 cards.')
  if (deck.runeDeckIds.length < 12) messages.push('Rune deck must contain at least 12 cards.')
  legend?.domains.forEach((domain) => {
    if (!runeCards.some((card) => card.domain === domain)) messages.push(`Rune deck must include at least 1 ${domain} rune.`)
  })

  return messages
}
