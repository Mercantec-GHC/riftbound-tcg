import { useEffect, useMemo, useState } from 'react'
import { createDecksApi, type ApiClient } from '../../shared/api'
import { filterSharedDecks } from '../../shared/domain/decks/deckRules'
import type { SharedDeck } from '../../shared/models'

export function usePublicDecks({
  apiClient,
  currentUserId,
}: {
  apiClient: ApiClient
  currentUserId: string
}) {
  const deckApi = useMemo(() => createDecksApi(apiClient), [apiClient])
  const [sharedDecks, setSharedDecks] = useState<SharedDeck[]>([])
  const [status, setStatus] = useState('Loading deck list...')
  const [search, setSearch] = useState('')
  const [visibility, setVisibility] = useState<'all' | 'private' | 'public'>('all')
  const [tag, setTag] = useState('')

  useEffect(() => {
    let cancelled = false
    async function loadPublicDecks() {
      try {
        const decks = await deckApi.listPublicDecks()
        if (cancelled) return
        setSharedDecks(decks)
        setStatus(`Loaded ${decks.length} public API deck${decks.length === 1 ? '' : 's'}.`)
      } catch (error) {
        if (!cancelled) setStatus(error instanceof Error ? error.message : 'Public API deck list is unavailable.')
      }
    }
    void loadPublicDecks()
    return () => {
      cancelled = true
    }
  }, [deckApi])

  const decks = filterSharedDecks(sharedDecks, {
    search,
    visibility,
    tag,
  }, currentUserId)

  return {
    decks,
    search,
    setSearch,
    status,
    tag,
    setTag,
    visibility,
    setVisibility,
    clearFilters() {
      setSearch('')
      setVisibility('all')
      setTag('')
    },
  }
}
