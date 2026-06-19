export function HomePage({
  cardCount,
  savedDeckCount,
  onNavigate,
}: {
  cardCount: number
  savedDeckCount: number
  onNavigate: (page: 'online' | 'cards' | 'decks' | 'deck-list') => void
}) {
  return (
    <section className="hero-panel frontpage">
      <div>
        <p className="eyebrow">fan-made multiplayer tabletop prototype</p>
        <h1>Riftbound Baybeeeee</h1>
        <p>
          Manage API-backed cards and decks, then enter online matchmaking with saved account data.
        </p>
        <div className="button-row">
          <button type="button" onClick={() => onNavigate('online')}>
            Online battle
          </button>
          <button type="button" onClick={() => onNavigate('cards')}>
            Open card viewer
          </button>
          <button type="button" onClick={() => onNavigate('decks')}>
            Build a deck
          </button>
          <button type="button" onClick={() => onNavigate('deck-list')}>
            Browse decks
          </button>
        </div>
      </div>
      <div className="home-stats">
        <article className="player">
          <span>Mode</span>
          <strong>API</strong>
        </article>
        <article className="player">
          <span>Cards</span>
          <strong>{cardCount}</strong>
        </article>
        <article className="player">
          <span>Saved decks</span>
          <strong>{savedDeckCount}</strong>
        </article>
        <article className="player">
          <span>Hot-seat</span>
          <strong>Archived</strong>
        </article>
      </div>
    </section>
  )
}
