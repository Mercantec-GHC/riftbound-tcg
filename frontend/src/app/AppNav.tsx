import type { Page } from '../shared/models'

export type NavRoute = {
  label: string
  page: Page
  path: string
}

const pages: NavRoute[] = [
  { page: 'home', path: '/', label: 'Frontpage' },
  { page: 'online', path: '/online', label: 'Online' },
  { page: 'cards', path: '/cards', label: 'Card viewer' },
  { page: 'decks', path: '/decks', label: 'Deck builder' },
  { page: 'deck-list', path: '/decks/browse', label: 'Deck list' },
]

export function AppNav({
  page,
  isAdmin,
  isSignedIn,
  onNavigate,
}: {
  page: Page
  isAdmin: boolean
  isSignedIn: boolean
  onNavigate: (path: string) => void
}) {
  const visiblePages = [
    ...pages,
    ...(isSignedIn ? [{ page: 'account' as const, path: '/account', label: 'My account' }] : []),
    ...(isAdmin ? [{ page: 'admin' as const, path: '/admin', label: 'Admin' }] : []),
  ]
  return (
    <nav className="app-nav" aria-label="Main navigation">
      {visiblePages.map((item) => (
        <a
          className={page === item.page ? 'active' : ''}
          href={item.path}
          key={item.page}
          onClick={(event) => {
            event.preventDefault()
            onNavigate(item.path)
          }}
        >
          {item.label}
        </a>
      ))}
    </nav>
  )
}
