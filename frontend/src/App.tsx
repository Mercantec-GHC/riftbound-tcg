import { useEffect, useMemo, useState } from 'react'
import './App.css'
import { AppNav } from './app/AppNav'
import { battlefieldOptionsFromCards } from './cardUtils'
import { CardHoverPreviewProvider } from './components/cardHoverPreview'
import {
  filterSharedDecks,
  isSharedDeck,
  normalizeSharedDeck,
} from './domain/decks/deckRules'
import { CardViewerPage } from './features/cards/CardViewerPage'
import { useCardLibrary } from './features/cards/useCardLibrary'
import { DeckListPage } from './features/deck-list/DeckListPage'
import { DeckForm } from './features/decks/DeckForm'
import { SavedDecksPanel } from './features/decks/SavedDecksPanel'
import { SelectedDeckList } from './features/decks/SelectedDeckList'
import { CardPreviewModal } from './features/game/CardPreviewModal'
import { GamePage } from './features/game/GamePage'
import { useGameController } from './features/game/useGameController'
import { HomePage } from './features/home/HomePage'
import { OnlineBattlePage } from './features/online/OnlineBattlePage'
import { AuthPanel } from './features/auth/AuthPanel'
import { useAuthSession } from './features/auth/useAuthSession'
import {
  localDecksEndpoint,
  type Card,
  type Page,
  type SharedDeck,
} from './models'
import { DeckCardPool } from './features/decks/DeckCardPool'
import { useDeckBuilder } from './features/decks/useDeckBuilder'
import { ProfileSwitcher } from './features/users/ProfileSwitcher'
import { useUserProfiles } from './features/users/useUserProfiles'

function App() {
  const [page, setPage] = useState<Page>('home')
  const [sharedDecks, setSharedDecks] = useState<SharedDeck[]>([])
  const [deckListStatus, setDeckListStatus] = useState('Loading deck list...')
  const [deckListSearch, setDeckListSearch] = useState('')
  const [deckListVisibility, setDeckListVisibility] = useState<'all' | 'private' | 'public'>('all')
  const [deckListTag, setDeckListTag] = useState('')
  const [previewCard, setPreviewCard] = useState<Card | null>(null)

  const users = useUserProfiles()
  const auth = useAuthSession()
  const cardLibrary = useCardLibrary()
  const battlefieldOptions = useMemo(() => battlefieldOptionsFromCards(cardLibrary.cardLibrary), [cardLibrary.cardLibrary])
  const deckBuilder = useDeckBuilder({
    activeUser: users.activeUser,
    cards: cardLibrary.cardLibrary,
    setDeckListStatus,
    setSharedDecks,
    sharedDecks,
  })
  const gameController = useGameController({
    cards: cardLibrary.cardLibrary,
    decks: deckBuilder.accessibleDecks,
    profiles: users.profiles,
    updateStats: users.updateStats,
  })

  useEffect(() => {
    let cancelled = false
    async function loadLocalDecks() {
      try {
        const response = await fetch(localDecksEndpoint)
        if (!response.ok) return
        const payload = (await response.json()) as { data?: unknown[] }
        const decks = Array.isArray(payload.data) ? payload.data.filter(isSharedDeck).map(normalizeSharedDeck) : []
        if (cancelled) return
        setSharedDecks(decks)
        setDeckListStatus(`Loaded ${decks.length} deck${decks.length === 1 ? '' : 's'} from data\\riftbound-decks.json.`)
      } catch {
        if (!cancelled) setDeckListStatus('Local deck list is unavailable.')
      }
    }
    loadLocalDecks()
    return () => {
      cancelled = true
    }
  }, [])

  const filteredSharedDecks = filterSharedDecks(sharedDecks, {
    search: deckListSearch,
    visibility: deckListVisibility,
    tag: deckListTag,
  }, users.activeUser.id)

  return (
    <CardHoverPreviewProvider>
      <main className="app-shell">
        <header className="app-header">
          <AppNav page={page} onPageChange={setPage} />
          <ProfileSwitcher
            activeUser={users.activeUser}
            profiles={users.profiles}
            onCreateProfile={users.createProfile}
            onSetActiveUser={users.setActiveUserId}
          />
          <AuthPanel
            session={auth.session}
            status={auth.status}
            onLogin={auth.login}
            onLogout={auth.logout}
            onRegister={auth.register}
          />
        </header>

      {page === 'home' && (
        <HomePage
          activePlayer={gameController.activePlayer}
          cardCount={cardLibrary.customCards.length}
          savedDeckCount={deckBuilder.savedDecks.length}
          setup={gameController.setup}
          onNavigate={setPage}
        />
      )}

      {page === 'game' && (
        <GamePage
          activePlayer={gameController.activePlayer}
          battlefieldOptions={battlefieldOptions}
          cards={cardLibrary.cardLibrary}
          deckAssignments={gameController.deckAssignments}
          decks={deckBuilder.accessibleDecks}
          game={gameController.game}
          profiles={users.profiles}
          restart={gameController.restart}
          setGame={gameController.setGame}
          setSetup={gameController.setSetup}
          setup={gameController.setup}
          startConfiguredGame={gameController.startConfiguredGame}
          updateSetup={gameController.updateSetup}
        />
      )}

      {page === 'online' && (
        <OnlineBattlePage
          apiClient={auth.apiClient}
          session={auth.session}
          decks={deckBuilder.accessibleDecks}
        />
      )}

      {previewCard && (
        <CardPreviewModal card={previewCard} onClose={() => setPreviewCard(null)} />
      )}

      {page === 'cards' && (
        <CardViewerPage
          cacheStatus={cardLibrary.cacheStatus}
          cardLibrary={cardLibrary.cardLibrary}
          customCards={cardLibrary.customCards}
          draft={cardLibrary.draft}
          onAddCard={cardLibrary.addCustomCard}
          onDraftChange={cardLibrary.setDraft}
          onRemoveCard={cardLibrary.removeCustomCard}
        />
      )}

      {page === 'deck-list' && (
        <DeckListPage
          decks={filteredSharedDecks}
          search={deckListSearch}
          status={deckListStatus}
          tag={deckListTag}
          visibility={deckListVisibility}
          onClearFilters={() => {
            setDeckListSearch('')
            setDeckListVisibility('all')
            setDeckListTag('')
          }}
          onSearchChange={setDeckListSearch}
          onTagChange={setDeckListTag}
          onVisibilityChange={setDeckListVisibility}
        />
      )}

      {page === 'decks' && (
        <section className="deck-builder">
          <div className="deck-workspace">
            <div className="deck-main">
              <div>
                <p className="eyebrow">deck builder</p>
                <h2>Create, save, export, and import decks</h2>
                <p>
                  A deck has a main deck, rune deck, and battlefield deck. Champion and Legend must share the same tag.
                </p>
              </div>

              <DeckForm
                deckDraft={deckBuilder.deckDraft}
                deckImportText={deckBuilder.deckImportText}
                deckStatus={deckBuilder.deckStatus}
                deckValidation={deckBuilder.deckValidation}
                selectedChampion={deckBuilder.selectedChampion}
                selectedChampionTags={deckBuilder.selectedChampionTags}
                selectedLegend={deckBuilder.selectedLegend}
                selectedLegendTags={deckBuilder.selectedLegendTags}
                onDeckDraftChange={deckBuilder.updateDeckDraft}
                onDeckImportTextChange={deckBuilder.setDeckImportText}
                onExportDeck={() => deckBuilder.exportDeck()}
                onImportDeck={deckBuilder.importDeck}
                onNewDeck={deckBuilder.newDeck}
                onSaveDeck={deckBuilder.saveDeck}
              />

              <SavedDecksPanel
                decks={deckBuilder.savedDecks}
                onDeleteDeck={deckBuilder.deleteDeck}
                onExportDeck={deckBuilder.exportDeck}
                onLoadDeck={deckBuilder.loadDeck}
              />

              <DeckCardPool
                deckDraft={deckBuilder.deckDraft}
                deckDomainFilter={deckBuilder.deckDomainFilter}
                deckMaxCost={deckBuilder.deckMaxCost}
                deckMinMight={deckBuilder.deckMinMight}
                deckSearch={deckBuilder.deckSearch}
                deckSort={deckBuilder.deckSort}
                deckTab={deckBuilder.deckTab}
                deckTagFilter={deckBuilder.deckTagFilter}
                filteredBattlefields={deckBuilder.filteredBattlefields}
                filteredChampions={deckBuilder.filteredChampions}
                filteredLegends={deckBuilder.filteredLegends}
                filteredMainDeckCards={deckBuilder.filteredMainDeckCards}
                filteredRunes={deckBuilder.filteredRunes}
                selectedChampion={deckBuilder.selectedChampion}
                selectedLegend={deckBuilder.selectedLegend}
                onDeckDomainFilterChange={deckBuilder.setDeckDomainFilter}
                onDeckDraftChange={deckBuilder.updateDeckDraft}
                onDeckMaxCostChange={deckBuilder.setDeckMaxCost}
                onDeckMinMightChange={deckBuilder.setDeckMinMight}
                onDeckSearchChange={deckBuilder.setDeckSearch}
                onDeckSortChange={deckBuilder.setDeckSort}
                onDeckTabChange={deckBuilder.setDeckTab}
                onDeckTagFilterChange={deckBuilder.setDeckTagFilter}
              />
            </div>

            <SelectedDeckList
              cardLibrary={cardLibrary.cardLibrary}
              deckDraft={deckBuilder.deckDraft}
              sections={deckBuilder.selectedDeckSections}
              onDeckDraftChange={deckBuilder.updateDeckDraft}
            />
          </div>
        </section>
      )}
      </main>
    </CardHoverPreviewProvider>
  )
}

export default App
