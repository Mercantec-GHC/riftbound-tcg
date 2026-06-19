import type { Player, SetupState } from '../../models'

export function HomePage({
  activePlayer,
  cardCount,
  savedDeckCount,
  setup,
  onNavigate,
}: {
  activePlayer: Player
  cardCount: number
  savedDeckCount: number
  setup: SetupState
  onNavigate: (page: 'game' | 'cards' | 'decks' | 'deck-list') => void
}) {
  return (
    <section className="hero-panel frontpage">
      <div>
        <p className="eyebrow">fan-made multiplayer tabletop prototype</p>
        <h1>Riftbound Baybeeeee</h1>
        <p>
          Play the hot-seat prototype, manage imported Riftbound cards, or browse the cached card library without
          cramming everything onto one giant page.
        </p>
        <div className="button-row">
          <button type="button" onClick={() => onNavigate('game')}>
            Play game
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
          <span>Players</span>
          <strong>{setup.playerCount}</strong>
        </article>
        <article className="player">
          <span>Cached cards</span>
          <strong>{cardCount}</strong>
        </article>
        <article className="player">
          <span>Saved decks</span>
          <strong>{savedDeckCount}</strong>
        </article>
        <article className="player">
          <span>Active turn</span>
          <strong>{activePlayer.name}</strong>
        </article>
      </div>
    </section>
  )
}
