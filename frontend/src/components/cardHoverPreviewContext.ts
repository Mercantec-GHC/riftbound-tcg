import { createContext, useContext } from 'react'
import type { Card } from '../models'

export type CardHoverPreviewContextValue = {
  hidePreview: () => void
  movePreview: (event: React.MouseEvent) => void
  showPreview: (card: Card, event: React.MouseEvent) => void
}

export const CardHoverPreviewContext = createContext<CardHoverPreviewContextValue | null>(null)

export function useCardHoverPreview() {
  return useContext(CardHoverPreviewContext)
}
