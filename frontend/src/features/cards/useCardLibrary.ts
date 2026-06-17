import { useEffect, useMemo, useState } from 'react'
import { createCardsApi, type ApiClient } from '../../api'
import { blankCard, type Card } from '../../models'

const noopCardsChanged = () => undefined

export function useCardLibrary({ apiClient, onCardsChanged = noopCardsChanged }: { apiClient: ApiClient; onCardsChanged?: (cards: Card[]) => void }) {
  const cardsApi = useMemo(() => createCardsApi(apiClient), [apiClient])
  const [cards, setCards] = useState<Card[]>([])
  const [draft, setDraft] = useState<Card>(blankCard)
  const [cacheStatus, setCacheStatus] = useState('Loading API cards...')
  const cardLibrary = useMemo(() => cards, [cards])

  useEffect(() => {
    let cancelled = false
    async function loadApiCards() {
      try {
        const next = await cardsApi.listCards()
        if (cancelled) return
        setCards(next)
        onCardsChanged(next)
        setCacheStatus(`Loaded ${next.length} API card${next.length === 1 ? '' : 's'}.`)
      } catch (error) {
        if (!cancelled) setCacheStatus(error instanceof Error ? error.message : 'API card library is unavailable.')
      }
    }
    void loadApiCards()
    return () => {
      cancelled = true
    }
  }, [cardsApi, onCardsChanged])

  return {
    addCustomCard: noopCardsChanged,
    cacheStatus,
    cardLibrary,
    customCards: cards,
    draft,
    removeCustomCard: noopCardsChanged,
    setDraft,
  }
}
