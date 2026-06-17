import { useMemo, useState } from 'react'
import { createDecksApi, type ApiClient, type AuthSession } from '../../api'
import { isSavedDeck, normalizeDeck } from '../../cardUtils'
import { customCardsKey, savedDecksKey, type SavedDeck } from '../../models'

const migrationCompleteKey = 'riftbound-api-migration-complete-v1'

function loadLocalDeckCandidates(ownerUserId: string) {
  try {
    const raw = localStorage.getItem(savedDecksKey)
    if (!raw) return []
    const payload = JSON.parse(raw) as { decks?: unknown[] }
    return Array.isArray(payload.decks)
      ? payload.decks.map((deck) => normalizeDeck(deck as Partial<SavedDeck>, ownerUserId)).filter(isSavedDeck)
      : []
  } catch {
    return []
  }
}

function hasLocalCards() {
  try {
    const raw = localStorage.getItem(customCardsKey)
    if (!raw) return false
    const payload = JSON.parse(raw) as { cards?: unknown[] }
    return Array.isArray(payload.cards) && payload.cards.length > 0
  } catch {
    return false
  }
}

export function LocalDataMigration({ apiClient, session, onImported }: { apiClient: ApiClient; session: AuthSession | null; onImported: () => void }) {
  const deckApi = useMemo(() => createDecksApi(apiClient), [apiClient])
  const [status, setStatus] = useState('')
  const [complete, setComplete] = useState(() => localStorage.getItem(migrationCompleteKey) === 'true')

  if (!session || complete) return null

  const localDecks = loadLocalDeckCandidates(session.user.id)
  const localCardNote = hasLocalCards()
  if (localDecks.length === 0 && !localCardNote) return null

  async function importLocalData() {
    if (!session) return
    let imported = 0
    for (const deck of localDecks) {
      await deckApi.createDeck({
        name: deck.name,
        visibility: deck.visibility,
        legendId: deck.legendId,
        championId: deck.championId,
        battlefieldDeckIds: deck.battlefieldDeckIds,
        runeDeckIds: deck.runeDeckIds,
        mainDeckIds: deck.mainDeckIds,
      })
      imported += 1
    }
    localStorage.setItem(migrationCompleteKey, 'true')
    setComplete(true)
    setStatus(`Imported ${imported} deck${imported === 1 ? '' : 's'} to the API.${localCardNote ? ' Local custom cards were left in browser storage because card writes are admin/dev only.' : ''}`)
    onImported()
  }

  return (
    <section className="migration-panel">
      <strong>Local data found</strong>
      <span>{localDecks.length} local deck{localDecks.length === 1 ? '' : 's'} can be imported to this account.</span>
      {localCardNote && <span>Local custom cards were detected; card API writes are not enabled for normal users.</span>}
      <button type="button" onClick={() => void importLocalData()}>Import local decks</button>
      {status && <small>{status}</small>}
    </section>
  )
}
