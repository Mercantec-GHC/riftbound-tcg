import { useEffect, useState } from 'react'
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
import { VisualStateLab } from '../features/dev/VisualStateLab'
import { CardPreviewModal } from '../features/game/CardPreviewModal'
import { HomePage } from '../features/home/HomePage'
import { LocalDataMigration } from '../features/migration/LocalDataMigration'
import { OnlineBattlePage } from '../features/online/OnlineBattlePage'
import type { Card, Page } from '../shared/models'
import { ActionMenuProvider } from '../shared/ui/actionMenu'
import { CardHoverPreviewProvider } from '../shared/ui/cardHoverPreview'
import { AppNav } from './AppNav'

const pagePaths: Record<Exclude<Page, 'not-found'>, string> = {
  home: '/',
  online: '/online',
  cards: '/cards',
  decks: '/decks',
  'deck-list': '/decks/browse',
  account: '/account',
  admin: '/admin',
  login: '/login',
  register: '/register',
  'dev-visual-lab': '/dev/visual-lab',
}

function normalizePath(pathname: string) {
  const normalized = pathname.replace(/\/+$/, '')
  return normalized === '' ? '/' : normalized
}

function pageFromPath(pathname: string): Page {
  const path = normalizePath(pathname)
  const entry = Object.entries(pagePaths).find(([, routePath]) => routePath === path)

  if (!entry) return 'not-found'
  if (entry[0] === 'dev-visual-lab' && !import.meta.env.DEV) return 'not-found'

  return entry[0] as Page
}

function App() {
  const [page, setPage] = useState<Page>(() => pageFromPath(window.location.pathname))
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

  function navigateToPath(path: string) {
    if (normalizePath(window.location.pathname) !== normalizePath(path)) {
      window.history.pushState(null, '', path)
    }

    setPage(pageFromPath(path))
  }

  function navigateToPage(nextPage: Page) {
    if (nextPage === 'not-found') return

    navigateToPath(pagePaths[nextPage])
  }

  useEffect(() => {
    function handlePopState() {
      setPage(pageFromPath(window.location.pathname))
    }

    window.addEventListener('popstate', handlePopState)
    return () => window.removeEventListener('popstate', handlePopState)
  }, [])

  return (
    <CardHoverPreviewProvider>
      <ActionMenuProvider>
        <main className="app-shell">
          <header className="app-header">
            <AppNav page={page} isAdmin={auth.session?.user.isAdmin === true} isSignedIn={Boolean(auth.session)} onNavigate={navigateToPath} />
            <AuthPanel
              session={auth.session}
              status={auth.status}
              onLogout={auth.logout}
              onNavigate={navigateToPage}
            />
          </header>
          <LocalDataMigration apiClient={auth.apiClient} session={auth.session} onImported={() => window.location.reload()} />

      {page === 'home' && (
        <HomePage
          cardCount={cardLibrary.customCards.length}
          savedDeckCount={deckBuilder.savedDecks.length}
          onNavigate={navigateToPage}
        />
      )}

      {page === 'login' && (
        <LoginPage
          onLogin={auth.login}
          onNavigate={navigateToPage}
        />
      )}

      {page === 'register' && (
        <RegisterPage
          onNavigate={navigateToPage}
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

      {import.meta.env.DEV && page === 'dev-visual-lab' && (
        <VisualStateLab cards={cardLibrary.cardLibrary} />
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

      {page === 'not-found' && (
        <section className="empty-state" aria-labelledby="not-found-title">
          <h2 id="not-found-title">Route not found</h2>
          <p>The requested app route does not exist.</p>
          <button type="button" onClick={() => navigateToPage('home')}>Go to frontpage</button>
        </section>
      )}
        </main>
      </ActionMenuProvider>
    </CardHoverPreviewProvider>
  )
}

export default App
