import type { Card } from '../models'
import { useCardHoverPreview } from './cardHoverPreviewContext'

function CardArt({ card, disableHoverPreview }: { card: Card; disableHoverPreview: boolean }) {
  const hoverPreview = useCardHoverPreview()
  const { image, name } = card
  const isUrl = /^https?:\/\//i.test(image)
  const isHorizontal = card.kind === 'battlefield'
  const className = [
    'card-art',
    isUrl ? 'full-card-art' : '',
    isUrl && isHorizontal ? 'horizontal-card-art' : '',
  ].filter(Boolean).join(' ')

  return (
    <div
      className={className}
      onMouseEnter={disableHoverPreview ? undefined : (event) => hoverPreview?.showPreview(card, event)}
      onMouseLeave={disableHoverPreview ? undefined : hoverPreview?.hidePreview}
      onMouseMove={disableHoverPreview ? undefined : hoverPreview?.movePreview}
    >
      {isUrl ? <img src={image} alt="" /> : <span aria-label={name}>{image || '✨'}</span>}
    </div>
  )
}

export function CardFace({ card, compact = false, disableHoverPreview = false }: { card: Card; compact?: boolean; disableHoverPreview?: boolean }) {
  const isFullCardImage = /^https?:\/\//i.test(card.image)
  if (isFullCardImage) {
    return <CardArt card={card} disableHoverPreview={disableHoverPreview} />
  }

  return (
    <>
      <CardArt card={card} disableHoverPreview={disableHoverPreview} />
      <span>{card.domain}</span>
      <strong>{card.name}</strong>
      <small>
        {card.kind} · {card.cost} energy
      </small>
      {!compact && <p>{card.text}</p>}
      {card.kind === 'unit' && <b>{card.might} might</b>}
    </>
  )
}
