import { CardFace } from '../../shared/ui/CardFace'
import type { Card } from '../../shared/models'

export function CardViewerPage({
  cacheStatus,
  cardLibrary,
}: {
  cacheStatus: string
  cardLibrary: Card[]
}) {
  return (
    <>
      <section className="importer">
        <div>
          <p className="eyebrow">card viewer</p>
          <h2>API card catalog</h2>
          <p>Cards load from the backend catalog. Card editing is reserved for admin/dev import workflows.</p>
        </div>
        <p className="import-status">{cacheStatus}</p>
      </section>

      <section className="editor">
        <div>
          <p className="eyebrow">catalog</p>
          <h2>Available cards</h2>
        </div>

        <div className="library">
          {cardLibrary.map((card) => (
            <article className={`mini-card ${card.domain.toLowerCase()}`} key={card.id}>
              <CardFace card={card} compact />
            </article>
          ))}
        </div>
      </section>
    </>
  )
}
