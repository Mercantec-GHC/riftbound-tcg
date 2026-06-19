import { useEffect, useMemo, useState } from 'react'
import { ApiError, createAdminApi, createCardsApi, type ApiAdminDeck, type ApiClient, type ApiUserProfile, type RiftCodexImportResult } from '../../api'
import { blankCard, domains, type Card, type CardKind, type Domain, type EffectType } from '../../models'

type AdminTab = 'users' | 'cards' | 'decks' | 'import'

function formatDate(value?: string | null) {
  if (!value) return 'Never'
  return new Date(value).toLocaleString()
}

function toCsv(values: string[]) {
  return values.join(', ')
}

function fromCsv(value: string) {
  return value.split(',').map((item) => item.trim()).filter(Boolean)
}

function createBlankAdminCard(): Card {
  return {
    ...blankCard,
    id: '',
    name: '',
    tags: [],
    domains: ['Fury'],
    image: '*',
    supertype: null,
    effect: { type: 'rally', amount: 0 },
  }
}

export function AdminPage({
  apiClient,
  currentUser,
  onCardsChanged,
}: {
  apiClient: ApiClient
  currentUser: ApiUserProfile | null
  onCardsChanged: (cards: Card[]) => void
}) {
  const adminApi = useMemo(() => createAdminApi(apiClient), [apiClient])
  const cardsApi = useMemo(() => createCardsApi(apiClient), [apiClient])
  const [tab, setTab] = useState<AdminTab>('users')
  const [users, setUsers] = useState<ApiUserProfile[]>([])
  const [cards, setCards] = useState<Card[]>([])
  const [decks, setDecks] = useState<ApiAdminDeck[]>([])
  const [selectedUserId, setSelectedUserId] = useState('')
  const [selectedCardId, setSelectedCardId] = useState('')
  const [selectedDeckId, setSelectedDeckId] = useState('')
  const [userStatus, setUserStatus] = useState('Loading users...')
  const [cardStatus, setCardStatus] = useState('Loading cards...')
  const [deckStatus, setDeckStatus] = useState('Loading decks...')
  const [importStatus, setImportStatus] = useState('Ready to import RiftCodex cards.')
  const [importResult, setImportResult] = useState<RiftCodexImportResult | null>(null)
  const [cardSearch, setCardSearch] = useState('')
  const [deckSearch, setDeckSearch] = useState('')
  const [deckVisibilityFilter, setDeckVisibilityFilter] = useState<'all' | 'public' | 'private'>('all')
  const [cardDraft, setCardDraft] = useState<Card>(createBlankAdminCard)

  const selectedUser = users.find((user) => user.id === selectedUserId) ?? users[0] ?? null
  const selectedCard = cards.find((card) => card.id === selectedCardId) ?? null
  const selectedDeck = decks.find((deck) => deck.id === selectedDeckId) ?? decks[0] ?? null
  const filteredCards = cards.filter((card) => {
    const query = cardSearch.trim().toLowerCase()
    if (!query) return true
    return [card.id, card.name, card.kind, card.domain, card.cardType, ...card.tags].some((value) => value.toLowerCase().includes(query))
  })
  const filteredDecks = decks.filter((deck) => {
    const query = deckSearch.trim().toLowerCase()
    const matchesQuery = !query || [
      deck.id,
      deck.name,
      deck.ownerUserId,
      deck.ownerDisplayName,
      deck.legendName,
      deck.championName,
      ...deck.tags,
      ...deck.domains,
    ].some((value) => value.toLowerCase().includes(query))
    const matchesVisibility = deckVisibilityFilter === 'all' || deck.visibility === deckVisibilityFilter
    return matchesQuery && matchesVisibility
  })
  const enabledAdminCount = users.filter((user) => user.isAdmin && !user.isDisabled).length

  useEffect(() => {
    if (!currentUser?.isAdmin) return
    void loadUsers()
    void loadCards()
    void loadDecks()
  }, [currentUser?.isAdmin])

  useEffect(() => {
    if (!selectedCard) return
    setCardDraft(selectedCard)
  }, [selectedCard])

  if (!currentUser?.isAdmin) {
    return (
      <section className="admin-page">
        <div>
          <p className="eyebrow">admin</p>
          <h2>Forbidden</h2>
          <p>This page is only available to admin users.</p>
        </div>
      </section>
    )
  }

  async function loadUsers() {
    try {
      const next = await adminApi.listUsers()
      setUsers(next)
      setSelectedUserId((current) => current || next[0]?.id || '')
      setUserStatus(`Loaded ${next.length} user${next.length === 1 ? '' : 's'}.`)
    } catch (error) {
      setUserStatus(error instanceof Error ? error.message : 'Unable to load users.')
    }
  }

  async function loadCards() {
    try {
      const next = await cardsApi.listCards()
      setCards(next)
      onCardsChanged(next)
      setSelectedCardId((current) => current || next[0]?.id || '')
      setCardStatus(`Loaded ${next.length} card${next.length === 1 ? '' : 's'}.`)
    } catch (error) {
      setCardStatus(error instanceof Error ? error.message : 'Unable to load cards.')
    }
  }

  async function loadDecks() {
    try {
      const next = await adminApi.listDecks()
      setDecks(next)
      setSelectedDeckId((current) => current || next[0]?.id || '')
      setDeckStatus(`Loaded ${next.length} deck${next.length === 1 ? '' : 's'}.`)
    } catch (error) {
      setDeckStatus(error instanceof Error ? error.message : 'Unable to load decks.')
    }
  }

  async function updateUser(userId: string, patch: Partial<ApiUserProfile>) {
    try {
      const updated = await adminApi.updateUser(userId, {
        email: patch.email,
        displayName: patch.displayName,
        isAdmin: patch.isAdmin,
        isDisabled: patch.isDisabled,
      })
      setUsers((current) => current.map((user) => user.id === updated.id ? updated : user))
      setUserStatus(`Updated ${updated.displayName}.`)
    } catch (error) {
      setUserStatus(error instanceof Error ? error.message : 'Unable to update user.')
    }
  }

  async function saveCard() {
    try {
      const result = await adminApi.upsertCard(cardDraft)
      setCardStatus(`${result.created ? 'Created' : 'Updated'} ${result.card.name}.`)
      await loadCards()
      setSelectedCardId(result.card.id)
    } catch (error) {
      setCardStatus(error instanceof Error ? error.message : 'Unable to save card.')
    }
  }

  async function deleteCard() {
    if (!cardDraft.id) return
    try {
      await adminApi.deleteCard(cardDraft.id)
      setCardStatus(`Deleted ${cardDraft.name || cardDraft.id}.`)
      setSelectedCardId('')
      setCardDraft(createBlankAdminCard())
      await loadCards()
    } catch (error) {
      if (error instanceof ApiError && error.payload?.code === 'card.in_use') {
        setCardStatus(error.payload.message)
      } else {
        setCardStatus(error instanceof Error ? error.message : 'Unable to delete card.')
      }
    }
  }

  async function runImport() {
    try {
      setImportStatus('Importing RiftCodex catalog...')
      const result = await adminApi.importRiftCodex()
      setImportResult(result)
      setImportStatus(`Imported ${result.imported}, updated ${result.updated}, skipped ${result.skipped} across ${result.pages} page${result.pages === 1 ? '' : 's'}.`)
      await loadCards()
    } catch (error) {
      setImportStatus(error instanceof Error ? error.message : 'Unable to import RiftCodex cards.')
    }
  }

  async function updateDeckVisibility(deckId: string, visibility: 'public' | 'private') {
    try {
      const updated = await adminApi.updateDeck(deckId, { visibility })
      setDecks((current) => current.map((deck) => deck.id === updated.id ? updated : deck))
      setDeckStatus(`Updated ${updated.name} to ${updated.visibility}.`)
    } catch (error) {
      setDeckStatus(error instanceof Error ? error.message : 'Unable to update deck visibility.')
    }
  }

  async function deleteDeck(deckId: string) {
    try {
      const deckName = decks.find((deck) => deck.id === deckId)?.name ?? deckId
      await adminApi.deleteDeck(deckId)
      setDecks((current) => current.filter((deck) => deck.id !== deckId))
      setSelectedDeckId('')
      setDeckStatus(`Deleted ${deckName}.`)
      await loadDecks()
    } catch (error) {
      setDeckStatus(error instanceof Error ? error.message : 'Unable to delete deck.')
    }
  }

  return (
    <section className="admin-page">
      <header>
        <div>
          <p className="eyebrow">admin</p>
          <h2>Console</h2>
        </div>
        <div className="deck-tabs" role="tablist" aria-label="Admin sections">
          {(['users', 'cards', 'decks', 'import'] as const).map((item) => (
            <button className={tab === item ? 'active' : ''} key={item} type="button" onClick={() => setTab(item)}>
              {item}
            </button>
          ))}
        </div>
      </header>

      {tab === 'users' && (
        <div className="admin-grid">
          <div className="admin-list">
            <p className="import-status">{userStatus}</p>
            {users.map((user) => (
              <button className={selectedUser?.id === user.id ? 'admin-list-item active' : 'admin-list-item'} key={user.id} type="button" onClick={() => setSelectedUserId(user.id)}>
                <strong>{user.displayName}</strong>
                <small>{user.email}</small>
                <span>{user.isAdmin ? 'Admin' : 'Player'} · {user.isDisabled ? 'Disabled' : 'Enabled'}</span>
              </button>
            ))}
          </div>

          {selectedUser && (
            <form className="admin-editor" onSubmit={(event) => {
              event.preventDefault()
              void updateUser(selectedUser.id, selectedUser)
            }}>
              <label>
                Email
                <input value={selectedUser.email} onChange={(event) => setUsers((current) => current.map((user) => user.id === selectedUser.id ? { ...user, email: event.target.value } : user))} />
              </label>
              <label>
                Display name
                <input value={selectedUser.displayName} onChange={(event) => setUsers((current) => current.map((user) => user.id === selectedUser.id ? { ...user, displayName: event.target.value } : user))} />
              </label>
              <label>
                Created
                <input readOnly value={formatDate(selectedUser.createdAt)} />
              </label>
              <label>
                Last login
                <input readOnly value={formatDate(selectedUser.lastLoginAt)} />
              </label>
              <label className="admin-check">
                <input
                  checked={selectedUser.isAdmin}
                  disabled={selectedUser.id === currentUser.id || (selectedUser.isAdmin && !selectedUser.isDisabled && enabledAdminCount <= 1)}
                  type="checkbox"
                  onChange={(event) => void updateUser(selectedUser.id, { isAdmin: event.target.checked })}
                />
                Admin
              </label>
              <label className="admin-check">
                <input
                  checked={selectedUser.isDisabled}
                  disabled={selectedUser.id === currentUser.id || (selectedUser.isAdmin && !selectedUser.isDisabled && enabledAdminCount <= 1)}
                  type="checkbox"
                  onChange={(event) => void updateUser(selectedUser.id, { isDisabled: event.target.checked })}
                />
                Disabled
              </label>
              <button type="submit">Save user</button>
            </form>
          )}
        </div>
      )}

      {tab === 'cards' && (
        <div className="admin-grid">
          <div className="admin-list">
            <label>
              Search cards
              <input value={cardSearch} onChange={(event) => setCardSearch(event.target.value)} />
            </label>
            <button type="button" onClick={() => {
              setSelectedCardId('')
              setCardDraft(createBlankAdminCard())
            }}>
              New card
            </button>
            <p className="import-status">{cardStatus}</p>
            {filteredCards.slice(0, 200).map((card) => (
              <button className={selectedCardId === card.id ? 'admin-list-item active' : 'admin-list-item'} key={card.id} type="button" onClick={() => setSelectedCardId(card.id)}>
                <strong>{card.name}</strong>
                <small>{card.id}</small>
                <span>{card.kind} · {card.domain}</span>
              </button>
            ))}
          </div>

          <form className="admin-editor card-admin-editor" onSubmit={(event) => {
            event.preventDefault()
            void saveCard()
          }}>
            <label>
              ID
              <input value={cardDraft.id} onChange={(event) => setCardDraft({ ...cardDraft, id: event.target.value })} />
            </label>
            <label>
              Name
              <input value={cardDraft.name} onChange={(event) => setCardDraft({ ...cardDraft, name: event.target.value })} />
            </label>
            <label>
              Kind
              <select value={cardDraft.kind} onChange={(event) => setCardDraft({ ...cardDraft, kind: event.target.value as CardKind })}>
                {(['unit', 'spell', 'gear', 'champion', 'legend', 'battlefield', 'token', 'rune'] as const).map((kind) => <option key={kind} value={kind}>{kind}</option>)}
              </select>
            </label>
            <label>
              Domain
              <select value={cardDraft.domain} onChange={(event) => setCardDraft({ ...cardDraft, domain: event.target.value as Domain, domains: [event.target.value as Domain] })}>
                {domains.map((domain) => <option key={domain} value={domain}>{domain}</option>)}
              </select>
            </label>
            <label>
              Domains
              <input value={toCsv(cardDraft.domains)} onChange={(event) => setCardDraft({ ...cardDraft, domains: fromCsv(event.target.value) as Domain[] })} />
            </label>
            <label>
              Tags
              <input value={toCsv(cardDraft.tags)} onChange={(event) => setCardDraft({ ...cardDraft, tags: fromCsv(event.target.value) })} />
            </label>
            <label>
              Cost
              <input min={0} type="number" value={cardDraft.cost} onChange={(event) => setCardDraft({ ...cardDraft, cost: Number(event.target.value) })} />
            </label>
            <label>
              Might
              <input min={0} type="number" value={cardDraft.might} onChange={(event) => setCardDraft({ ...cardDraft, might: Number(event.target.value) })} />
            </label>
            <label>
              Card type
              <input value={cardDraft.cardType} onChange={(event) => setCardDraft({ ...cardDraft, cardType: event.target.value })} />
            </label>
            <label>
              Supertype
              <input value={cardDraft.supertype ?? ''} onChange={(event) => setCardDraft({ ...cardDraft, supertype: event.target.value || null })} />
            </label>
            <label>
              Effect type
              <select value={cardDraft.effect.type} onChange={(event) => setCardDraft({ ...cardDraft, effect: { ...cardDraft.effect, type: event.target.value as EffectType } })}>
                {(['rally', 'damage', 'draw', 'buff'] as const).map((effect) => <option key={effect} value={effect}>{effect}</option>)}
              </select>
            </label>
            <label>
              Effect amount
              <input min={0} type="number" value={cardDraft.effect.amount} onChange={(event) => setCardDraft({ ...cardDraft, effect: { ...cardDraft.effect, amount: Number(event.target.value) } })} />
            </label>
            <label className="wide">
              Image
              <input value={cardDraft.image} onChange={(event) => setCardDraft({ ...cardDraft, image: event.target.value })} />
            </label>
            <label className="wide">
              Text
              <textarea value={cardDraft.text} onChange={(event) => setCardDraft({ ...cardDraft, text: event.target.value })} />
            </label>
            <div className="button-row wide">
              <button disabled={!cardDraft.id.trim()} type="submit">Save card</button>
              <button className="secondary-button" disabled={!cardDraft.id.trim()} type="button" onClick={() => void deleteCard()}>Delete card</button>
            </div>
          </form>
        </div>
      )}

      {tab === 'decks' && (
        <div className="admin-grid">
          <div className="admin-list">
            <label>
              Search decks
              <input value={deckSearch} onChange={(event) => setDeckSearch(event.target.value)} placeholder="Name, id, owner, legend..." />
            </label>
            <label>
              Visibility
              <select value={deckVisibilityFilter} onChange={(event) => setDeckVisibilityFilter(event.target.value as 'all' | 'public' | 'private')}>
                <option value="all">All</option>
                <option value="public">Public</option>
                <option value="private">Private</option>
              </select>
            </label>
            <p className="import-status">{deckStatus}</p>
            {filteredDecks.map((deck) => (
              <button className={selectedDeck?.id === deck.id ? 'admin-list-item active' : 'admin-list-item'} key={deck.id} type="button" onClick={() => setSelectedDeckId(deck.id)}>
                <strong>{deck.name}</strong>
                <small>{deck.ownerDisplayName} · {deck.visibility}</small>
                <span>{deck.cardCounts.main} main · {deck.cardCounts.runes} runes · {deck.cardCounts.battlefields} battlefields</span>
              </button>
            ))}
          </div>

          {selectedDeck && (
            <section className="admin-editor">
              <label>
                Deck ID
                <input readOnly value={selectedDeck.id} />
              </label>
              <label>
                Owner
                <input readOnly value={`${selectedDeck.ownerDisplayName} (${selectedDeck.ownerUserId})`} />
              </label>
              <label>
                Visibility
                <select value={selectedDeck.visibility} onChange={(event) => void updateDeckVisibility(selectedDeck.id, event.target.value as 'public' | 'private')}>
                  <option value="public">public</option>
                  <option value="private">private</option>
                </select>
              </label>
              <label>
                Updated
                <input readOnly value={formatDate(selectedDeck.updatedAt)} />
              </label>
              <label>
                Legend
                <input readOnly value={selectedDeck.legendName || selectedDeck.legendId} />
              </label>
              <label>
                Champion
                <input readOnly value={selectedDeck.championName || selectedDeck.championId} />
              </label>
              <label>
                Active usage
                <input readOnly value={String(selectedDeck.activeUsageCount)} />
              </label>
              <label>
                Queued tickets
                <input readOnly value={String(selectedDeck.queuedTicketCount)} />
              </label>
              <label>
                Lobby selections
                <input readOnly value={String(selectedDeck.lobbySelectionCount)} />
              </label>
              <label>
                Created
                <input readOnly value={formatDate(selectedDeck.createdAt)} />
              </label>
              <label className="wide">
                Tags
                <input readOnly value={selectedDeck.tags.join(', ')} />
              </label>
              <label className="wide">
                Domains
                <input readOnly value={selectedDeck.domains.join(', ')} />
              </label>
              <label className="wide">
                Description
                <textarea readOnly value={selectedDeck.description ?? ''} />
              </label>
              <div className="button-row wide">
                <button type="button" onClick={() => void updateDeckVisibility(selectedDeck.id, selectedDeck.visibility === 'public' ? 'private' : 'public')}>
                  Make {selectedDeck.visibility === 'public' ? 'private' : 'public'}
                </button>
                <button className="secondary-button" type="button" onClick={() => void deleteDeck(selectedDeck.id)}>
                  Force delete
                </button>
              </div>
            </section>
          )}
        </div>
      )}

      {tab === 'import' && (
        <div className="admin-import">
          <p className="import-status">{importStatus}</p>
          <button type="button" onClick={() => void runImport()}>Import RiftCodex catalog</button>
          {importResult && (
            <div className="admin-import-result">
              <strong>{importResult.imported} imported</strong>
              <strong>{importResult.updated} updated</strong>
              <strong>{importResult.skipped} skipped</strong>
              <strong>{importResult.pages} pages</strong>
              {importResult.errors.length > 0 && (
                <textarea readOnly value={importResult.errors.join('\n')} />
              )}
            </div>
          )}
        </div>
      )}
    </section>
  )
}
