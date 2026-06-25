import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import type { Card } from '../models'
import { CardFace } from './CardFace'
import { CardHoverPreviewContext } from './cardHoverPreviewContext'

type HoverPreview = {
  card: Card
  mouseX: number
  mouseY: number
  x: number
  y: number
  isPositioned: boolean
}

const PREVIEW_OFFSET_X = 22
const PREVIEW_OFFSET_Y = 18
const VIEWPORT_PADDING = 12

function clamp(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), max)
}

export function CardHoverPreviewProvider({ children }: { children: ReactNode }) {
  const [preview, setPreview] = useState<HoverPreview | null>(null)
  const previewRef = useRef<HTMLDivElement | null>(null)

  const clampPreviewPosition = useCallback((mouseX: number, mouseY: number) => {
    const previewElement = previewRef.current
    if (!previewElement) {
      return null
    }

    const previewBounds = previewElement.getBoundingClientRect()
    const maxX = Math.max(VIEWPORT_PADDING, window.innerWidth - previewBounds.width - VIEWPORT_PADDING)
    const maxY = Math.max(VIEWPORT_PADDING, window.innerHeight - previewBounds.height - VIEWPORT_PADDING)

    return {
      x: clamp(mouseX + PREVIEW_OFFSET_X, VIEWPORT_PADDING, maxX),
      y: clamp(mouseY - previewBounds.height - PREVIEW_OFFSET_Y, VIEWPORT_PADDING, maxY),
    }
  }, [])

  function showPreview(card: Card, event: React.MouseEvent) {
    const { x, y } = clampPreviewPosition(event.clientX, event.clientY)
      ?? { x: 0, y: 0 }
    setPreview({ card, mouseX: event.clientX, mouseY: event.clientY, x, y, isPositioned: false })
  }

  function movePreview(event: React.MouseEvent) {
    setPreview((current) => {
      if (!current) return current

      const { x, y } = clampPreviewPosition(event.clientX, event.clientY)
        ?? { x: current.x, y: current.y }
      return { ...current, mouseX: event.clientX, mouseY: event.clientY, x, y, isPositioned: Boolean(previewRef.current) }
    })
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

  useEffect(() => {
    function repositionPreview() {
      setPreview((current) => {
        if (!current) return current

        const { x, y } = clampPreviewPosition(current.mouseX, current.mouseY)
          ?? { x: current.x, y: current.y }
        if (x === current.x && y === current.y) return current

        return { ...current, x, y, isPositioned: Boolean(previewRef.current) }
      })
    }

    window.addEventListener('resize', repositionPreview)
    return () => window.removeEventListener('resize', repositionPreview)
  }, [clampPreviewPosition])

  useEffect(() => {
    if (!preview) return

    const frameId = window.requestAnimationFrame(() => {
      setPreview((current) => {
        if (!current) return current

        const position = clampPreviewPosition(current.mouseX, current.mouseY)
        if (!position) return current

        if (position.x === current.x && position.y === current.y && current.isPositioned) return current

        return { ...current, x: position.x, y: position.y, isPositioned: true }
      })
    })

    return () => window.cancelAnimationFrame(frameId)
  }, [clampPreviewPosition, preview])

  useEffect(() => {
    const previewElement = previewRef.current
    if (!preview || !previewElement || typeof ResizeObserver === 'undefined') return

    const observer = new ResizeObserver(() => {
      setPreview((current) => {
        if (!current) return current

        const { x, y } = clampPreviewPosition(current.mouseX, current.mouseY)
          ?? { x: current.x, y: current.y }
        if (x === current.x && y === current.y) return current

        return { ...current, x, y, isPositioned: true }
      })
    })

    observer.observe(previewElement)
    return () => observer.disconnect()
  }, [clampPreviewPosition, preview])

  return (
    <CardHoverPreviewContext.Provider value={{ hidePreview, movePreview, showPreview }}>
      {children}
      {preview && (
        <div
          ref={previewRef}
          className="hover-card-preview"
          style={{
            left: preview.x,
            top: preview.y,
            visibility: preview.isPositioned ? 'visible' : 'hidden',
          }}
        >
          <CardFace card={preview.card} disableHoverPreview />
        </div>
      )}
    </CardHoverPreviewContext.Provider>
  )
}
