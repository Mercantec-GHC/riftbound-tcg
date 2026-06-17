import type { Page } from '../models'

const pages: { page: Page; label: string }[] = [
  { page: 'home', label: 'Frontpage' },
  { page: 'online', label: 'Online' },
  { page: 'cards', label: 'Card viewer' },
  { page: 'decks', label: 'Deck builder' },
  { page: 'deck-list', label: 'Deck list' },
]

export function AppNav({ page, onPageChange }: { page: Page; onPageChange: (page: Page) => void }) {
  return (
    <nav className="app-nav" aria-label="Main navigation">
      {pages.map((item) => (
        <button className={page === item.page ? 'active' : ''} key={item.page} type="button" onClick={() => onPageChange(item.page)}>
          {item.label}
        </button>
      ))}
    </nav>
  )
}
