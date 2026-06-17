import { useEffect, useMemo, useState } from 'react'
import './App.css'
import { createDecksApi } from './api'
import { AppNav } from './app/AppNav'
import { CardHoverPreviewProvider } from './components/cardHoverPreview'
import { filterSharedDecks } from './domain/decks/deckRules'
import { CardViewerPage } from './features/cards/CardViewerPage'
import { useCardLibrary } from './features/cards/useCardLibrary'
import { AccountPage } from './features/account/AccountPage'
import { DeckListPage } from './features/deck-list/DeckListPage'
import { DeckForm } from './features/decks/DeckForm'
import { SavedDecksPanel } from './features/decks/SavedDecksPanel'
import { SelectedDeckList } from './features/decks/SelectedDeckList'
import { CardPreviewModal } from './features/game/CardPreviewModal'
import { HomePage } from './features/home/HomePage'
import { OnlineBattlePage } from './features/online/OnlineBattlePage'
import { AuthPanel } from './features/auth/AuthPanel'
import { useAuthSession } from './features/auth/useAuthSession'
import { LocalDataMigration } from './features/migration/LocalDataMigration'
import { AdminPage } from './features/admin/AdminPage'
import {
  type Card,
  type Page,
  type SharedDeck,
} from './models'
import { DeckCardPool } from './features/decks/DeckCardPool'
import { useDeckBuilder } from './features/decks/useDeckBuilder'

function App() {
  const [page, setPage] = useState<Page>('home')
  const [sharedDecks, setSharedDecks] = useState<SharedDeck[]>([])
  const [deckListStatus, setDeckListStatus] = useState('Loading deck list...')
  const [deckListSearch, setDeckListSearch] = useState('')
  const [deckListVisibility, setDeckListVisibility] = useState<'all' | 'private' | 'public'>('all')
  const [deckListTag, setDeckListTag] = useState('')
  const [previewCard, setPreviewCard] = useState<Card | null>(null)

  const auth = useAuthSession()
  const deckApi = useMemo(() => createDecksApi(auth.apiClient), [auth.apiClient])
  const cardLibrary = useCardLibrary({ apiClient: auth.apiClient })
  const deckBuilder = useDeckBuilder({
    apiClient: auth.apiClient,
    cards: cardLibrary.cardLibrary,
    session: auth.session,
  })

  useEffect(() => {
    let cancelled = false
    async function loadPublicDecks() {
      try {
        const decks = await deckApi.listPublicDecks()
        if (cancelled) return
        setSharedDecks(decks)
        setDeckListStatus(`Loaded ${decks.length} public API deck${decks.length === 1 ? '' : 's'}.`)
      } catch (error) {
        if (!cancelled) setDeckListStatus(error instanceof Error ? error.message : 'Public API deck list is unavailable.')
      }
    }
    void loadPublicDecks()
    return () => {
      cancelled = true
    }
  }, [deckApi])

  const filteredSharedDecks = filterSharedDecks(sharedDecks, {
    search: deckListSearch,
    visibility: deckListVisibility,
    tag: deckListTag,
  }, auth.session?.user.id ?? '')

  return (
    <CardHoverPreviewProvider>
      <main className="app-shell">
        <header className="app-header">
          <AppNav page={page} isAdmin={auth.session?.user.isAdmin === true} isSignedIn={Boolean(auth.session)} onPageChange={setPage} />
          <AuthPanel
            session={auth.session}
            status={auth.status}
            onLogin={auth.login}
            onLogout={auth.logout}
            onRegister={auth.register}
          />
        </header>
        <LocalDataMigration apiClient={auth.apiClient} session={auth.session} onImported={() => window.location.reload()} />

      {page === 'home' && (
        <HomePage
          cardCount={cardLibrary.customCards.length}
          savedDeckCount={deckBuilder.savedDecks.length}
          onNavigate={setPage}
        />
      )}

      {page === 'online' && (
        <OnlineBattlePage
          apiClient={auth.apiClient}
          cards={cardLibrary.cardLibrary}
          session={auth.session}
          decks={deckBuilder.activeDecks}
        />
      )}

      {previewCard && (
        <CardPreviewModal card={previewCard} onClose={() => setPreviewCard(null)} />
      )}

      {page === 'cards' && (
        <CardViewerPage
          cacheStatus={cardLibrary.cacheStatus}
          cardLibrary={cardLibrary.cardLibrary}
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

      {page === 'admin' && (
        <AdminPage
          apiClient={auth.apiClient}
          currentUser={auth.session?.user ?? null}
          onCardsChanged={() => undefined}
        />
      )}

      {page === 'account' && (
        <AccountPage
          activeDecks={deckBuilder.activeDecks}
          apiClient={auth.apiClient}
          session={auth.session}
          onDecksChanged={deckBuilder.refreshDecks}
          onUpdateMe={auth.updateMe}
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
