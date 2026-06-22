import type { Card, SavedDeck } from '../../shared/models'

export function DeckForm({
  deckDraft,
  deckImportText,
  deckStatus,
  deckValidation,
  selectedChampion,
  selectedLegend,
  selectedChampionTags,
  selectedLegendTags,
  onDeckDraftChange,
  onDeckImportTextChange,
  onExportDeck,
  onImportDeck,
  onNewDeck,
  onSaveDeck,
}: {
  deckDraft: SavedDeck
  deckImportText: string
  deckStatus: string
  deckValidation: string[]
  selectedChampion: Card | undefined
  selectedLegend: Card | undefined
  selectedChampionTags: string[]
  selectedLegendTags: string[]
  onDeckDraftChange: (deck: SavedDeck) => void
  onDeckImportTextChange: (text: string) => void
  onExportDeck: () => void
  onImportDeck: () => void
  onNewDeck: () => void
  onSaveDeck: () => void
}) {
  return (
    <section className="panel-card deck-form">
      <label className="wide">
        Deck name
        <input value={deckDraft.name} onChange={(event) => onDeckDraftChange({ ...deckDraft, name: event.target.value })} />
      </label>
      <small className="wide">
        Legend: {selectedLegend?.name ?? 'none'} · Champion: {selectedChampion?.name ?? 'none'}
      </small>
      <small className="wide">
        Legend tags: {selectedLegend ? selectedLegendTags.join(', ') : 'none'} · Champion tags: {selectedChampion ? selectedChampionTags.join(', ') : 'none'}
      </small>
      <label>
        Visibility
        <select value={deckDraft.visibility} onChange={(event) => onDeckDraftChange({ ...deckDraft, visibility: event.target.value as 'private' | 'public' })}>
          <option value="private">Private</option>
          <option value="public">Public</option>
        </select>
      </label>
      <div className="button-row wide">
        <button type="button" onClick={onSaveDeck}>Save deck</button>
        <button type="button" onClick={onNewDeck}>New deck</button>
        <button type="button" onClick={onExportDeck}>Export deck</button>
        <button type="button" onClick={onImportDeck}>Import deck</button>
      </div>
      <p className="import-status wide">{deckStatus}</p>
      {deckValidation.length > 0 && <p className="deck-errors wide">{deckValidation.join(' ')}</p>}
      <label className="wide">
        Import / export JSON
        <textarea value={deckImportText} onChange={(event) => onDeckImportTextChange(event.target.value)} />
      </label>
    </section>
  )
}
