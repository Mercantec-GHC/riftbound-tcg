import { useEffect, useState, type ReactNode } from 'react'
import type { Card } from '../models'
import { CardFace } from './CardFace'
import { CardHoverPreviewContext } from './cardHoverPreviewContext'

type HoverPreview = {
  card: Card
  x: number
  y: number
}

export function CardHoverPreviewProvider({ children }: { children: ReactNode }) {
  const [preview, setPreview] = useState<HoverPreview | null>(null)

  function showPreview(card: Card, event: React.MouseEvent) {
    setPreview({ card, x: event.clientX, y: event.clientY })
  }

  function movePreview(event: React.MouseEvent) {
    setPreview((current) => current ? { ...current, x: event.clientX, y: event.clientY } : current)
  }

  function hidePreview() {
    setPreview(null)
  }

  useEffect(() => {
    window.addEventListener('dragstart', hidePreview)
    window.addEventListener('drop', hidePreview)
    return () => {
      window.removeEventListener('dragstart', hidePreview)
      window.removeEventListener('drop', hidePreview)
    }
  }, [])

  return (
    <CardHoverPreviewContext.Provider value={{ hidePreview, movePreview, showPreview }}>
      {children}
      {preview && (
        <div
          className="hover-card-preview"
          style={{
            left: preview.x,
            top: preview.y,
          }}
        >
          <CardFace card={preview.card} disableHoverPreview />
        </div>
      )}
    </CardHoverPreviewContext.Provider>
  )
}
