import { useState } from 'react'
import './app.css'
import { AccountPage } from '../features/account/AccountPage'
import { AdminPage } from '../features/admin/AdminPage'
import { AuthPanel } from '../features/auth/AuthPanel'
import { LoginPage } from '../features/auth/LoginPage'
import { RegisterPage } from '../features/auth/RegisterPage'
import { useAuthSession } from '../features/auth/useAuthSession'
import { CardViewerPage } from '../features/cards/CardViewerPage'
import { useCardLibrary } from '../features/cards/useCardLibrary'
import { DeckListPage } from '../features/deck-list/DeckListPage'
import { usePublicDecks } from '../features/deck-list/usePublicDecks'
import { DeckBuilderPage } from '../features/decks/DeckBuilderPage'
import { useDeckBuilder } from '../features/decks/useDeckBuilder'
import { CardPreviewModal } from '../features/game/CardPreviewModal'
import { HomePage } from '../features/home/HomePage'
import { LocalDataMigration } from '../features/migration/LocalDataMigration'
import { OnlineBattlePage } from '../features/online/OnlineBattlePage'
import type { Card, Page } from '../shared/models'
import { CardHoverPreviewProvider } from '../shared/ui/cardHoverPreview'
import { AppNav } from './AppNav'

function App() {
  const [page, setPage] = useState<Page>('home')
  const [previewCard, setPreviewCard] = useState<Card | null>(null)

  const auth = useAuthSession()
  const cardLibrary = useCardLibrary({ apiClient: auth.apiClient })
  const deckBuilder = useDeckBuilder({
    apiClient: auth.apiClient,
    cards: cardLibrary.cardLibrary,
    session: auth.session,
  })
  const publicDecks = usePublicDecks({
    apiClient: auth.apiClient,
    currentUserId: auth.session?.user.id ?? '',
  })

  return (
    <CardHoverPreviewProvider>
      <main className="app-shell">
        <header className="app-header">
          <AppNav page={page} isAdmin={auth.session?.user.isAdmin === true} isSignedIn={Boolean(auth.session)} onPageChange={setPage} />
          <AuthPanel
            session={auth.session}
            status={auth.status}
            onLogout={auth.logout}
            onNavigate={setPage}
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

      {page === 'login' && (
        <LoginPage
          onLogin={auth.login}
          onNavigate={setPage}
        />
      )}

      {page === 'register' && (
        <RegisterPage
          onNavigate={setPage}
          onRegister={auth.register}
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
          decks={publicDecks.decks}
          search={publicDecks.search}
          status={publicDecks.status}
          tag={publicDecks.tag}
          visibility={publicDecks.visibility}
          onClearFilters={publicDecks.clearFilters}
          onSearchChange={publicDecks.setSearch}
          onTagChange={publicDecks.setTag}
          onVisibilityChange={publicDecks.setVisibility}
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
          onChangePassword={auth.changePassword}
          onDecksChanged={deckBuilder.refreshDecks}
          onDeleteAvatar={auth.deleteAvatar}
          onUpdateMe={auth.updateMe}
          onUploadAvatar={auth.uploadAvatar}
        />
      )}

      {page === 'decks' && (
        <DeckBuilderPage cardLibrary={cardLibrary.cardLibrary} deckBuilder={deckBuilder} />
      )}
      </main>
    </CardHoverPreviewProvider>
  )
}

export default App
