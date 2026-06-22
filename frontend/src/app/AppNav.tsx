import type { Page } from '../shared/models'

const pages: { page: Page; label: string }[] = [
  { page: 'home', label: 'Frontpage' },
  { page: 'online', label: 'Online' },
  { page: 'cards', label: 'Card viewer' },
  { page: 'decks', label: 'Deck builder' },
  { page: 'deck-list', label: 'Deck list' },
]

export function AppNav({
  page,
  isAdmin,
  isSignedIn,
  onPageChange,
}: {
  page: Page
  isAdmin: boolean
  isSignedIn: boolean
  onPageChange: (page: Page) => void
}) {
  const visiblePages = [
    ...pages,
    ...(isSignedIn ? [{ page: 'account' as const, label: 'My account' }] : []),
    ...(isAdmin ? [{ page: 'admin' as const, label: 'Admin' }] : []),
  ]
  return (
    <nav className="app-nav" aria-label="Main navigation">
      {visiblePages.map((item) => (
        <button className={page === item.page ? 'active' : ''} key={item.page} type="button" onClick={() => onPageChange(item.page)}>
          {item.label}
        </button>
      ))}
    </nav>
  )
}
