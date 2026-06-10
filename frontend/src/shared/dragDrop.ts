import type { DragPayload } from '../models'
import { useCardHoverPreview } from '../components/cardHoverPreviewContext'

export function dragData(event: React.DragEvent, payload: DragPayload) {
  event.dataTransfer.setData('application/json', JSON.stringify(payload))
  event.dataTransfer.effectAllowed = 'move'
}

export function useDragData() {
  const hoverPreview = useCardHoverPreview()
  return (event: React.DragEvent, payload: DragPayload) => {
    hoverPreview?.hidePreview()
    dragData(event, payload)
  }
}

export function readDragData(event: React.DragEvent): DragPayload | null {
  try {
    return JSON.parse(event.dataTransfer.getData('application/json')) as DragPayload
  } catch {
    return null
  }
}
