import { useEffect, useMemo, useState } from 'react'
import { createDecksApi, type ApiClient, type AuthSession } from '../../api'
import {
  blankDeck,
  cardTags,
  cardsShareTag,
  deckValidationMessages,
  normalizeDeck,
} from '../../cardUtils'
import { filterAndSortCards, type DeckSort } from '../../domain/cards/cardFilters'
import { isDeckBuilderMainDeckCard, userCanAccessDeck } from '../../domain/decks/deckRules'
import { schemaVersion, type Card, type Domain, type SavedDeck } from '../../models'
import type { DeckTab } from './deckBuilderTypes'

function importableDeckPayload(payload: unknown) {
  if (payload && typeof payload === 'object' && 'deck' in payload) return payload.deck
  return payload
}

export function useDeckBuilder({
  apiClient,
  cards,
  session,
}: {
  apiClient: ApiClient
  cards: Card[]
  session: AuthSession | null
}) {
  const deckApi = useMemo(() => createDecksApi(apiClient), [apiClient])
  const [deckTab, setDeckTab] = useState<DeckTab>('legend')
  const [deckSearch, setDeckSearch] = useState('')
  const [deckTagFilter, setDeckTagFilter] = useState('')
  const [deckDomainFilter, setDeckDomainFilter] = useState<'' | Domain>('')
  const [deckMaxCost, setDeckMaxCost] = useState('')
  const [deckMinMight, setDeckMinMight] = useState('')
  const [deckSort, setDeckSort] = useState<DeckSort>('name-asc')
  const [savedDecks, setSavedDecks] = useState<SavedDeck[]>([])
  const [deckDraft, setDeckDraft] = useState<SavedDeck>(() => ({ ...blankDeck(), ownerUserId: session?.user.id ?? '' }))
  const [deckImportText, setDeckImportText] = useState('')
  const [deckStatus, setDeckStatus] = useState('Sign in to load and save API decks.')

  useEffect(() => {
    let cancelled = false
    async function loadDecks() {
      if (!session) {
        setSavedDecks([])
        setDeckDraft({ ...blankDeck(), ownerUserId: '' })
        setDeckStatus('Sign in to load and save API decks.')
        return
      }

      try {
        const decks = await deckApi.listDecks({ ownerUserId: 'me' })
        if (cancelled) return
        setSavedDecks(decks)
        setDeckDraft((current) => ({ ...current, ownerUserId: session.user.id }))
        setDeckStatus(`Loaded ${decks.length} API deck${decks.length === 1 ? '' : 's'}.`)
      } catch (error) {
        if (!cancelled) setDeckStatus(error instanceof Error ? error.message : 'Could not load API decks.')
      }
    }
    void loadDecks()
    return () => {
      cancelled = true
    }
  }, [deckApi, session])

  function updateDeckDraft(next: SavedDeck) {
    setDeckDraft(next)
    setDeckStatus('Deck draft updated.')
  }

  function newDeck() {
    setDeckDraft({ ...blankDeck(), ownerUserId: session?.user.id ?? '' })
    setDeckStatus('Started a new deck.')
  }

  function validDeckCardIds(deck: SavedDeck): SavedDeck {
    return {
      ...deck,
      name: deck.name.trim() || 'Untitled deck',
      ownerUserId: deck.ownerUserId || session?.user.id || '',
      visibility: deck.visibility === 'public' ? 'public' : 'private',
      championId: cards.some((card) => card.id === deck.championId && card.kind === 'champion') ? deck.championId : '',
      legendId: cards.some((card) => card.id === deck.legendId && card.kind === 'legend') ? deck.legendId : '',
      battlefieldDeckIds: deck.battlefieldDeckIds.filter((id) => cards.some((card) => card.id === id && card.kind === 'battlefield')).slice(0, 3),
      runeDeckIds: deck.runeDeckIds.filter((id) => cards.some((card) => card.id === id && card.kind === 'rune')),
      mainDeckIds: deck.mainDeckIds.filter((id) => cards.some((card) => card.id === id && isDeckBuilderMainDeckCard(card))).slice(0, 40),
    }
  }

  async function saveDeck() {
    if (!session) {
      setDeckStatus('Sign in before saving decks.')
      return
    }

    const deck = validDeckCardIds(deckDraft)
    const validation = deckValidationMessages(deck, cards)
    if (validation.length > 0) {
      setDeckDraft(deck)
      setDeckStatus(validation.join(' '))
      return
    }
    try {
      const request = {
        name: deck.name,
        visibility: deck.visibility,
        legendId: deck.legendId,
        championId: deck.championId,
        battlefieldDeckIds: deck.battlefieldDeckIds,
        runeDeckIds: deck.runeDeckIds,
        mainDeckIds: deck.mainDeckIds,
      }
      const saved = savedDecks.some((candidate) => candidate.id === deck.id)
        ? await deckApi.updateDeck(deck.id, request)
        : await deckApi.createDeck(request)
      const next = [...savedDecks.filter((candidate) => candidate.id !== saved.id), saved]
      setSavedDecks(next)
      setDeckDraft(saved)
      setDeckStatus(`Saved ${saved.name} to the API.`)
    } catch (error) {
      setDeckStatus(error instanceof Error ? error.message : 'Could not save API deck.')
    }
  }

  function loadDeck(deck: SavedDeck) {
    setDeckDraft({
      ...deck,
      battlefieldDeckIds: [...deck.battlefieldDeckIds],
      runeDeckIds: [...deck.runeDeckIds],
      mainDeckIds: [...deck.mainDeckIds],
    })
    setDeckStatus(`Loaded ${deck.name}.`)
  }

  async function deleteDeck(id: string) {
    try {
      await deckApi.deleteDeck(id)
      const next = savedDecks.filter((deck) => deck.id !== id)
      setSavedDecks(next)
      if (deckDraft.id === id) newDeck()
      setDeckStatus('Deleted API deck.')
    } catch (error) {
      setDeckStatus(error instanceof Error ? error.message : 'Could not delete API deck.')
    }
  }

  function exportDeck(deck = deckDraft) {
    const payload = JSON.stringify({ schemaVersion, deck }, null, 2)
    void navigator.clipboard?.writeText(payload)
    setDeckImportText(payload)
    setDeckStatus('Deck JSON copied to the import/export box.')
  }

  async function importDeck() {
    try {
      const payload = JSON.parse(deckImportText) as unknown
      const imported = importableDeckPayload(payload)
      if (!imported || typeof imported !== 'object') throw new Error('Invalid deck JSON.')
      if (!session) throw new Error('Sign in before importing decks.')
      const deck = { ...validDeckCardIds(normalizeDeck(imported, session.user.id)), ownerUserId: session.user.id }
      const validation = deckValidationMessages(deck, cards)
      if (validation.length > 0) throw new Error(validation.join(' '))
      const saved = await deckApi.createDeck({
        name: deck.name,
        visibility: deck.visibility,
        legendId: deck.legendId,
        championId: deck.championId,
        battlefieldDeckIds: deck.battlefieldDeckIds,
        runeDeckIds: deck.runeDeckIds,
        mainDeckIds: deck.mainDeckIds,
      })
      setSavedDecks((current) => [...current.filter((candidate) => candidate.id !== saved.id), saved])
      setDeckDraft(saved)
      setDeckStatus(`Imported ${saved.name} to the API.`)
    } catch (error) {
      setDeckStatus(error instanceof Error ? error.message : 'Could not import deck JSON.')
    }
  }

  const selectedLegend = cards.find((card) => card.id === deckDraft.legendId)
  const selectedChampion = cards.find((card) => card.id === deckDraft.championId)
  const selectedDeckSections = [
    { title: 'Legend', ids: deckDraft.legendId ? [deckDraft.legendId] : [] },
    { title: 'Champion', ids: deckDraft.championId ? [deckDraft.championId] : [] },
    { title: 'Main deck', ids: deckDraft.mainDeckIds },
    { title: 'Rune deck', ids: deckDraft.runeDeckIds },
    { title: 'Battlefield deck', ids: deckDraft.battlefieldDeckIds },
  ]
  const deckValidation = deckValidationMessages(deckDraft, cards)
  const deckFilters = {
    search: deckSearch,
    tag: deckTagFilter,
    domain: deckDomainFilter,
    maxCost: deckMaxCost,
    minMight: deckMinMight,
    sort: deckSort,
  }
  const ownedDecks = savedDecks.filter((deck) => deck.ownerUserId === session?.user.id)
  const accessibleDecks = savedDecks.filter((deck) => userCanAccessDeck(deck, session?.user.id ?? ''))

  return {
    accessibleDecks,
    deckDomainFilter,
    deckDraft,
    deckImportText,
    deckMaxCost,
    deckMinMight,
    deckSearch,
    deckSort,
    deckStatus,
    deckTab,
    deckTagFilter,
    deckValidation,
    deleteDeck,
    exportDeck,
    filteredBattlefields: filterAndSortCards(cards.filter((card) => card.kind === 'battlefield'), deckFilters),
    filteredChampions: filterAndSortCards(
      cards.filter((card) => card.kind === 'champion' && (!selectedLegend || cardsShareTag(card, selectedLegend))),
      deckFilters,
    ),
    filteredLegends: filterAndSortCards(cards.filter((card) => card.kind === 'legend'), deckFilters),
    filteredMainDeckCards: filterAndSortCards(cards.filter(isDeckBuilderMainDeckCard), deckFilters),
    filteredRunes: filterAndSortCards(
      cards.filter((card) => card.kind === 'rune' && (!selectedLegend || selectedLegend.domains.includes(card.domain))),
      deckFilters,
    ),
    importDeck,
    loadDeck,
    newDeck,
    saveDeck,
    savedDecks: ownedDecks,
    selectedChampion,
    selectedChampionTags: cardTags(selectedChampion),
    selectedDeckSections,
    selectedLegend,
    selectedLegendTags: cardTags(selectedLegend),
    setDeckDomainFilter,
    setDeckDraft,
    setDeckImportText,
    setDeckMaxCost,
    setDeckMinMight,
    setDeckSearch,
    setDeckSort,
    setDeckTab,
    setDeckTagFilter,
    updateDeckDraft,
  }
}
