import { CardFace } from '../../shared/ui/CardFace'
import type { Card } from '../../shared/models'

export function CardPreviewModal({ card, onClose }: { card: Card; onClose: () => void }) {
  return (
    <div className="card-preview-backdrop" role="dialog" aria-modal="true" onClick={onClose}>
      <button className="card-preview" type="button" onClick={(event) => event.stopPropagation()}>
        <CardFace card={card} />
        <span>{card.name}</span>
      </button>
    </div>
  )
}
