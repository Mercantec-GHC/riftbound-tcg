import type { SharedDeck } from '../../shared/models'

export function DeckListPage({
  decks,
  search,
  status,
  tag,
  visibility,
  onClearFilters,
  onSearchChange,
  onTagChange,
  onVisibilityChange,
}: {
  decks: SharedDeck[]
  search: string
  status: string
  tag: string
  visibility: 'all' | 'private' | 'public'
  onClearFilters: () => void
  onSearchChange: (search: string) => void
  onTagChange: (tag: string) => void
  onVisibilityChange: (visibility: 'all' | 'private' | 'public') => void
}) {
  return (
    <section className="deck-list-page">
      <div>
        <p className="eyebrow">deck list</p>
        <h2>Browse public decks</h2>
        <p>
          This list comes from the API. Add eligible public decks and your own private decks to your active list from My Account.
        </p>
      </div>

      <section className="deck-list-filters">
        <label className="wide">
          Search decks
          <input value={search} onChange={(event) => onSearchChange(event.target.value)} placeholder="Name, author, tag, description..." />
        </label>
        <label>
          Visibility
          <select value={visibility} onChange={(event) => onVisibilityChange(event.target.value as 'all' | 'private' | 'public')}>
            <option value="all">All decks</option>
            <option value="private">Private</option>
            <option value="public">Public</option>
          </select>
        </label>
        <label>
          Tag/domain
          <input value={tag} onChange={(event) => onTagChange(event.target.value)} placeholder="e.g. aggro" />
        </label>
        <button type="button" onClick={onClearFilters}>
          Clear filters
        </button>
      </section>

      <p className="import-status">{status}</p>

      <section className="shared-decks">
        {decks.map((deck) => (
          <article className="shared-deck" key={deck.id}>
            <div>
              <span className="eyebrow">{deck.visibility}</span>
              <h3>{deck.name}</h3>
              <p>{deck.description || 'No description yet.'}</p>
            </div>
            <small>
              By {deck.author} · {deck.cardCounts.main} main · {deck.cardCounts.runes} rune · {deck.cardCounts.battlefields} battlefield
            </small>
            <small>Legend: {deck.legendName || 'None'} · Champion: {deck.championName || 'None'}</small>
            {deck.domains.length > 0 && <small>Domains: {deck.domains.join(', ')}</small>}
            {deck.tags.length > 0 && <small>Tags: {deck.tags.join(', ')}</small>}
          </article>
        ))}
        {decks.length === 0 && (
          <div className="empty-deck-list">
            <h3>No decks found</h3>
            <p>Public API decks will show up here after they are created.</p>
          </div>
        )}
      </section>
    </section>
  )
}
