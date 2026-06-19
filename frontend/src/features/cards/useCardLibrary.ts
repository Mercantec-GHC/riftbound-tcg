import { useEffect, useMemo, useState } from 'react'
import {
  loadCustomCards,
  localCacheToGameCards,
  mergeCards,
  saveCustomCards,
} from '../../cardUtils'
import { blankCard, localCardsEndpoint, type Card } from '../../models'
import { randomId } from '../../utils/randomId'

const noopCardsChanged = () => undefined

export function useCardLibrary({ onCardsChanged = noopCardsChanged }: { onCardsChanged?: (cards: Card[]) => void } = {}) {
  const [customCards, setCustomCards] = useState<Card[]>(loadCustomCards)
  const [draft, setDraft] = useState<Card>(blankCard)
  const [cacheStatus, setCacheStatus] = useState('Loading cached Riftbound cards...')
  const cardLibrary = useMemo(() => customCards, [customCards])

  useEffect(() => {
    let cancelled = false
    async function loadLocalCache() {
      try {
        const response = await fetch(localCardsEndpoint)
        if (!response.ok) return
        const payload = (await response.json()) as { data?: unknown[] }
        if (!Array.isArray(payload.data) || payload.data.length === 0 || cancelled) return
        const imported = localCacheToGameCards(payload.data)
        if (imported.length === 0) return
        setCustomCards((current) => {
          const next = mergeCards(current, imported)
          saveCustomCards(next)
          onCardsChanged(next)
          return next
        })
        setCacheStatus(`Loaded ${imported.length} cached card${imported.length === 1 ? '' : 's'} from data\\riftbound-cards.json.`)
      } catch {
        setCacheStatus('Local dev cache is unavailable.')
      }
    }
    loadLocalCache()
    return () => {
      cancelled = true
    }
  }, [onCardsChanged])

  function addCustomCard() {
    if (!draft.name.trim()) return
    const card: Card = {
      ...draft,
      id: randomId('custom'),
      name: draft.name.trim(),
      cost: Math.max(0, Math.floor(draft.cost)),
      might: Math.max(0, Math.floor(draft.might)),
      image: draft.image.trim() || '✨',
      text: draft.text.trim() || 'A custom card ready for the table.',
      cardType: draft.kind,
      effect: { ...draft.effect, amount: Math.max(0, Math.floor(draft.effect.amount)) },
    }
    const next = [...customCards, card]
    setCustomCards(next)
    saveCustomCards(next)
    setDraft(blankCard)
    onCardsChanged(next)
  }

  function removeCustomCard(id: string) {
    const next = customCards.filter((card) => card.id !== id)
    setCustomCards(next)
    saveCustomCards(next)
    onCardsChanged(next)
  }

  return {
    addCustomCard,
    cacheStatus,
    cardLibrary,
    customCards,
    draft,
    removeCustomCard,
    setDraft,
  }
}
