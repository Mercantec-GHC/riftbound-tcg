import type { SavedDeck } from '../../shared/models'

export function SavedDecksPanel({
  decks,
  onDeleteDeck,
  onExportDeck,
  onLoadDeck,
}: {
  decks: SavedDeck[]
  onDeleteDeck: (id: string) => void
  onExportDeck: (deck: SavedDeck) => void
  onLoadDeck: (deck: SavedDeck) => void
}) {
  return (
    <section className="panel-card">
      <h3>Saved decks</h3>
      <div className="saved-decks">
        {decks.map((deck) => (
          <article className="saved-deck" key={deck.id}>
            <strong>{deck.name}</strong>
            <small>{deck.mainDeckIds.length} main · {deck.runeDeckIds.length} rune · {deck.battlefieldDeckIds.length} battlefield</small>
            <div className="button-row">
              <button type="button" onClick={() => onLoadDeck(deck)}>Load</button>
              <button type="button" onClick={() => onExportDeck(deck)}>Export</button>
              <button type="button" onClick={() => onDeleteDeck(deck.id)}>Delete</button>
            </div>
          </article>
        ))}
        {decks.length === 0 && <p className="rune-note">No saved decks yet.</p>}
      </div>
    </section>
  )
}
