import { CardFace } from '../../components/CardFace'
import { domains, kinds, type Card, type CardKind, type Domain, type EffectType } from '../../models'

export function CardViewerPage({
  cacheStatus,
  cardLibrary,
  customCards,
  draft,
  onAddCard,
  onDraftChange,
  onRemoveCard,
}: {
  cacheStatus: string
  cardLibrary: Card[]
  customCards: Card[]
  draft: Card
  onAddCard: () => void
  onDraftChange: (draft: Card) => void
  onRemoveCard: (id: string) => void
}) {
  return (
    <>
      <section className="importer">
        <div>
          <p className="eyebrow">card viewer</p>
          <h2>Cached Riftbound cards</h2>
          <p>
            Cards now load automatically from <code> data\riftbound-cards.json</code>. No manual import button, no
            endpoint fiddling, no bullshit.
          </p>
        </div>
        <p className="import-status">{cacheStatus}</p>
      </section>

      <section className="editor">
        <div>
          <p className="eyebrow">card forge</p>
          <h2>Make your own card</h2>
          <p>Use emoji or an image URL for card art. Custom cards save in this browser and shuffle into every deck.</p>
        </div>
        <form
          onSubmit={(event) => {
            event.preventDefault()
            onAddCard()
          }}
        >
          <label>
            Name
            <input value={draft.name} onChange={(event) => onDraftChange({ ...draft, name: event.target.value })} />
          </label>
          <label>
            Art emoji or URL
            <input value={draft.image} onChange={(event) => onDraftChange({ ...draft, image: event.target.value })} />
          </label>
          <label>
            Type
            <select value={draft.kind} onChange={(event) => onDraftChange({ ...draft, kind: event.target.value as CardKind })}>
              {kinds.map((kind) => (
                <option key={kind}>{kind}</option>
              ))}
            </select>
          </label>
          <label>
            Domain
            <select value={draft.domain} onChange={(event) => onDraftChange({ ...draft, domain: event.target.value as Domain })}>
              {domains.map((domain) => (
                <option key={domain}>{domain}</option>
              ))}
            </select>
          </label>
          <label>
            Cost
            <input min="0" type="number" value={draft.cost} onChange={(event) => onDraftChange({ ...draft, cost: Number(event.target.value) })} />
          </label>
          <label>
            Might
            <input min="0" type="number" value={draft.might} onChange={(event) => onDraftChange({ ...draft, might: Number(event.target.value) })} />
          </label>
          <label>
            Effect
            <select
              value={draft.effect.type}
              onChange={(event) => onDraftChange({ ...draft, effect: { type: event.target.value as EffectType, amount: draft.effect.amount } })}
            >
              <option value="rally">rally / ready</option>
              <option value="draw">draw</option>
              <option value="damage">damage</option>
              <option value="buff">buff</option>
            </select>
          </label>
          <label>
            Effect amount
            <input
              min="0"
              type="number"
              value={draft.effect.amount}
              onChange={(event) => onDraftChange({ ...draft, effect: { ...draft.effect, amount: Number(event.target.value) } })}
            />
          </label>
          <label className="wide">
            Rules text
            <textarea value={draft.text} onChange={(event) => onDraftChange({ ...draft, text: event.target.value })} />
          </label>
          <button type="submit">Add card</button>
        </form>

        <div className="library">
          {cardLibrary.map((card) => (
            <article className={`mini-card ${card.domain.toLowerCase()}`} key={card.id}>
              <CardFace card={card} compact />
              {customCards.some((custom) => custom.id === card.id) && (
                <button type="button" onClick={() => onRemoveCard(card.id)}>
                  Delete
                </button>
              )}
            </article>
          ))}
        </div>
      </section>
    </>
  )
}
