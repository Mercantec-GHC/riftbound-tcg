import { useEffect, useMemo, useState } from 'react'
import { createDecksApi, type ApiBrowseDeck, type ApiClient, type ApiUserProfile, type AuthSession, type UpdateUserRequest } from '../../shared/api'
import type { SavedDeck } from '../../shared/models'

export function AccountPage({
  activeDecks,
  apiClient,
  session,
  onDecksChanged,
  onUpdateMe,
}: {
  activeDecks: SavedDeck[]
  apiClient: ApiClient
  session: AuthSession | null
  onDecksChanged: () => Promise<void>
  onUpdateMe: (request: UpdateUserRequest) => Promise<ApiUserProfile>
}) {
  const deckApi = useMemo(() => createDecksApi(apiClient), [apiClient])
  const [displayNameDraft, setDisplayNameDraft] = useState<{ userId: string, value: string } | null>(null)
  const [browseDecks, setBrowseDecks] = useState<ApiBrowseDeck[]>([])
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('Manage your profile and active online decks.')
  const displayName = displayNameDraft && displayNameDraft.userId === session?.user.id
    ? displayNameDraft.value
    : (session?.user.displayName ?? '')

  useEffect(() => {
    if (!session) return
    void refreshBrowseDecks()
  }, [session])

  async function refreshBrowseDecks() {
    try {
      setBrowseDecks(await deckApi.listBrowseDecks())
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Could not load eligible decks.')
    }
  }

  async function saveProfile() {
    try {
      await onUpdateMe({ displayName: displayName.trim() || session?.user.displayName })
      setDisplayNameDraft(null)
      setStatus('Profile updated.')
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Could not update profile.')
    }
  }

  async function addActiveDeck(deckId: string) {
    try {
      await deckApi.addActiveDeck(deckId)
      await onDecksChanged()
      await refreshBrowseDecks()
      setStatus('Deck added to active list.')
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Could not add deck.')
    }
  }

  async function removeActiveDeck(deckId: string) {
    try {
      await deckApi.removeActiveDeck(deckId)
      await onDecksChanged()
      await refreshBrowseDecks()
      setStatus('Deck removed from active list.')
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Could not remove deck.')
    }
  }

  if (!session) {
    return (
      <section className="admin-page">
        <header>
          <div>
            <p className="eyebrow">my account</p>
            <h2>Sign in required</h2>
            <p>Sign in to manage your profile, stats, and active decks.</p>
          </div>
        </header>
      </section>
    )
  }

  const stats = session.user.stats
  const activeDeckIds = new Set(activeDecks.map((deck) => deck.id))
  const filteredBrowseDecks = browseDecks.filter((deck) => {
    const text = `${deck.name} ${deck.author} ${deck.legendName} ${deck.championName} ${deck.tags.join(' ')} ${deck.domains.join(' ')}`.toLowerCase()
    return text.includes(search.trim().toLowerCase())
  })

  return (
    <section className="admin-page">
      <header>
        <div>
          <p className="eyebrow">my account</p>
          <h2>{session.user.displayName}</h2>
          <p>Manage your profile and the decks available for online quick queue and lobbies.</p>
        </div>
      </header>

      <p className="import-status">{status}</p>

      <div className="admin-grid">
        <section className="admin-panel">
          <h3>Profile</h3>
          <label>
            Email
            <input value={session.user.email} readOnly />
          </label>
          <label>
            Display name
            <input value={displayName} onChange={(event) => setDisplayNameDraft({ userId: session.user.id, value: event.target.value })} />
          </label>
          <button type="button" onClick={saveProfile}>Save profile</button>
        </section>

        <section className="admin-panel">
          <h3>Stats</h3>
          <div className="account-stats">
            <span>Games played <strong>{stats.gamesPlayed}</strong></span>
            <span>Wins <strong>{stats.wins}</strong></span>
            <span>Losses <strong>{stats.losses}</strong></span>
            <span>Points scored <strong>{stats.pointsScored}</strong></span>
            <span>Last played <strong>{stats.lastPlayedAt ? new Date(stats.lastPlayedAt).toLocaleString() : 'Never'}</strong></span>
          </div>
        </section>
      </div>

      <div className="admin-grid">
        <section className="admin-panel">
          <h3>Active Decks</h3>
          <div className="admin-list">
            {activeDecks.map((deck) => (
              <article key={deck.id}>
                <div>
                  <strong>{deck.name}</strong>
                  <small>{deck.visibility} · {deck.ownerUserId === session.user.id ? 'Owned by you' : 'Public deck'}</small>
                </div>
                <button type="button" onClick={() => removeActiveDeck(deck.id)}>Remove</button>
              </article>
            ))}
            {activeDecks.length === 0 && <p>No active decks yet.</p>}
          </div>
        </section>

        <section className="admin-panel">
          <h3>Add Decks</h3>
          <label>
            Search eligible decks
            <input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Name, author, champion, tag..." />
          </label>
          <div className="admin-list">
            {filteredBrowseDecks.map((deck) => {
              const isActive = activeDeckIds.has(deck.id) || deck.isActive
              return (
                <article key={deck.id}>
                  <div>
                    <strong>{deck.name}</strong>
                    <small>{deck.visibility} · {deck.author} · {deck.cardCounts.main} main · {deck.cardCounts.battlefields} battlefield</small>
                    <small>{deck.legendName || 'No legend'} / {deck.championName || 'No champion'}</small>
                  </div>
                  {isActive
                    ? <button type="button" onClick={() => removeActiveDeck(deck.id)}>Active</button>
                    : <button type="button" onClick={() => addActiveDeck(deck.id)}>Add</button>}
                </article>
              )
            })}
            {filteredBrowseDecks.length === 0 && <p>No eligible decks found.</p>}
          </div>
        </section>
      </div>
    </section>
  )
}
