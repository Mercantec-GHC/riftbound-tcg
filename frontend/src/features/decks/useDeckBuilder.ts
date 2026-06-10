import { useState } from 'react'
import {
  blankDeck,
  cardTags,
  cardsShareTag,
  deckValidationMessages,
  loadSavedDecks,
  normalizeDeck,
  saveSavedDecks,
} from '../../cardUtils'
import { filterAndSortCards, type DeckSort } from '../../domain/cards/cardFilters'
import { deckToPrivateSharedDeck, isDeckBuilderMainDeckCard, userCanAccessDeck } from '../../domain/decks/deckRules'
import { localDecksEndpoint, schemaVersion, type Card, type Domain, type SavedDeck, type SharedDeck, type UserProfile } from '../../models'
import type { DeckTab } from './deckBuilderTypes'

function importableDeckPayload(payload: unknown) {
  if (payload && typeof payload === 'object' && 'deck' in payload) return payload.deck
  return payload
}

export function useDeckBuilder({
  cards,
  activeUser,
  setDeckListStatus,
  setSharedDecks,
  sharedDecks,
}: {
  cards: Card[]
  activeUser: UserProfile
  setDeckListStatus: (status: string) => void
  setSharedDecks: (decks: SharedDeck[]) => void
  sharedDecks: SharedDeck[]
}) {
  const [deckTab, setDeckTab] = useState<DeckTab>('legend')
  const [deckSearch, setDeckSearch] = useState('')
  const [deckTagFilter, setDeckTagFilter] = useState('')
  const [deckDomainFilter, setDeckDomainFilter] = useState<'' | Domain>('')
  const [deckMaxCost, setDeckMaxCost] = useState('')
  const [deckMinMight, setDeckMinMight] = useState('')
  const [deckSort, setDeckSort] = useState<DeckSort>('name-asc')
  const [savedDecks, setSavedDecks] = useState<SavedDeck[]>(() => loadSavedDecks(activeUser.id))
  const [deckDraft, setDeckDraft] = useState<SavedDeck>(() => ({ ...blankDeck(), ownerUserId: activeUser.id }))
  const [deckImportText, setDeckImportText] = useState('')
  const [deckStatus, setDeckStatus] = useState('Build a deck, save it locally, or export it as JSON.')

  function updateDeckDraft(next: SavedDeck) {
    setDeckDraft(next)
    setDeckStatus('Deck draft updated.')
  }

  function newDeck() {
    setDeckDraft({ ...blankDeck(), ownerUserId: activeUser.id })
    setDeckStatus('Started a new deck.')
  }

  function validDeckCardIds(deck: SavedDeck): SavedDeck {
    return {
      ...deck,
      name: deck.name.trim() || 'Untitled deck',
      ownerUserId: deck.ownerUserId || activeUser.id,
      visibility: deck.visibility === 'public' ? 'public' : 'private',
      championId: cards.some((card) => card.id === deck.championId && card.kind === 'champion') ? deck.championId : '',
      legendId: cards.some((card) => card.id === deck.legendId && card.kind === 'legend') ? deck.legendId : '',
      battlefieldDeckIds: deck.battlefieldDeckIds.filter((id) => cards.some((card) => card.id === id && card.kind === 'battlefield')).slice(0, 3),
      runeDeckIds: deck.runeDeckIds.filter((id) => cards.some((card) => card.id === id && card.kind === 'rune')),
      mainDeckIds: deck.mainDeckIds.filter((id) => cards.some((card) => card.id === id && isDeckBuilderMainDeckCard(card))).slice(0, 40),
    }
  }

  async function saveDeck() {
    const deck = validDeckCardIds(deckDraft)
    const validation = deckValidationMessages(deck, cards)
    if (validation.length > 0) {
      setDeckDraft(deck)
      setDeckStatus(validation.join(' '))
      return
    }
    const deckForOwner = { ...deck, ownerUserId: activeUser.id }
    const next = [...savedDecks.filter((saved) => saved.id !== deckForOwner.id), deckForOwner]
    setSavedDecks(next)
    saveSavedDecks(next)
    setDeckDraft(deckForOwner)
    const privateDeck = deckToPrivateSharedDeck(deckForOwner, cards)
    const nextSharedDecks = [...sharedDecks.filter((saved) => saved.id !== privateDeck.id), privateDeck]
    setSharedDecks(nextSharedDecks)
    try {
      await fetch(localDecksEndpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ schemaVersion, source: 'local', savedAt: new Date().toISOString(), data: nextSharedDecks }, null, 2),
      })
      setDeckListStatus(`Loaded ${nextSharedDecks.length} deck${nextSharedDecks.length === 1 ? '' : 's'} from data\\riftbound-decks.json.`)
      setDeckStatus(`Saved ${deckForOwner.name} and added it to the ${deckForOwner.visibility} deck list.`)
    } catch {
      setDeckStatus(`Saved ${deckForOwner.name} in browser storage, but could not update data\\riftbound-decks.json.`)
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

  function deleteDeck(id: string) {
    const next = savedDecks.filter((deck) => deck.id !== id)
    setSavedDecks(next)
    saveSavedDecks(next)
    if (deckDraft.id === id) newDeck()
    setDeckStatus('Deleted deck.')
  }

  function exportDeck(deck = deckDraft) {
    const payload = JSON.stringify({ schemaVersion, deck }, null, 2)
    void navigator.clipboard?.writeText(payload)
    setDeckImportText(payload)
    setDeckStatus('Deck JSON copied to the import/export box.')
  }

  function importDeck() {
    try {
      const payload = JSON.parse(deckImportText) as unknown
      const imported = importableDeckPayload(payload)
      if (!imported || typeof imported !== 'object') throw new Error('Invalid deck JSON.')
      const deck = { ...validDeckCardIds(normalizeDeck(imported, activeUser.id)), ownerUserId: activeUser.id }
      const validation = deckValidationMessages(deck, cards)
      if (validation.length > 0) throw new Error(validation.join(' '))
      const importedDeck = { ...deck, ownerUserId: activeUser.id }
      const next = [...savedDecks.filter((saved) => saved.id !== importedDeck.id), importedDeck]
      setSavedDecks(next)
      saveSavedDecks(next)
      setDeckDraft(importedDeck)
      setDeckStatus(`Imported ${importedDeck.name}.`)
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
  const ownedDecks = savedDecks.filter((deck) => deck.ownerUserId === activeUser.id)
  const accessibleDecks = savedDecks.filter((deck) => userCanAccessDeck(deck, activeUser.id))

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
