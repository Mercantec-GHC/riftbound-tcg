import { formatDeckLine } from '../../shared/domain/decks/deckUtils'
import type { Card, SavedDeck } from '../../shared/models'

type SelectedDeckSection = {
  title: string
  ids: string[]
}

export function SelectedDeckList({
  cardLibrary,
  deckDraft,
  sections,
  onDeckDraftChange,
}: {
  cardLibrary: Card[]
  deckDraft: SavedDeck
  sections: SelectedDeckSection[]
  onDeckDraftChange: (deck: SavedDeck) => void
}) {
  function removeCard(section: SelectedDeckSection, id: string) {
    const index = section.ids.indexOf(id)
    const nextIds = section.ids.filter((_, cardIndex) => cardIndex !== index)
    if (section.title === 'Legend') onDeckDraftChange({ ...deckDraft, legendId: '', championId: '' })
    if (section.title === 'Champion') onDeckDraftChange({ ...deckDraft, championId: '' })
    if (section.title === 'Main deck') onDeckDraftChange({ ...deckDraft, mainDeckIds: nextIds })
    if (section.title === 'Rune deck') onDeckDraftChange({ ...deckDraft, runeDeckIds: nextIds })
    if (section.title === 'Battlefield deck') onDeckDraftChange({ ...deckDraft, battlefieldDeckIds: nextIds })
  }

  return (
    <aside className="sticky-deck-list">
      <h3>Selected cards</h3>
      {sections.map((section) => (
        <details className="deck-summary-section" key={section.title} open>
          <summary>{section.title} ({section.ids.length})</summary>
          {Array.from(new Set(section.ids))
            .map((id) => {
              const card = cardLibrary.find((candidate) => candidate.id === id)
              return card ? { id, card, count: section.ids.filter((cardId) => cardId === id).length } : null
            })
            .filter((entry): entry is { id: string; card: Card; count: number } => Boolean(entry))
            .sort((first, second) => first.card.cost - second.card.cost || first.card.name.localeCompare(second.card.name))
            .map(({ id, card, count }) => (
              <button
                className="deck-line"
                key={`${section.title}-${id}`}
                type="button"
                onClick={() => removeCard(section, id)}
              >
                {formatDeckLine(card, count)}
              </button>
            ))}
          {section.ids.length === 0 && <p className="rune-note">Empty</p>}
        </details>
      ))}
    </aside>
  )
}
