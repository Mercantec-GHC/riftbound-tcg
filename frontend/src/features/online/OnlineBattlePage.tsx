import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useEffect, useMemo, useRef, useState } from 'react'
import { createCardsApi, createLobbiesApi, createMatchesApi, createMatchmakingApi, type ApiClient } from '../../shared/api'
import type { AuthSession, LegalAction, Lobby, MatchEvent, MatchSnapshot, MatchmakingTicket } from '../../shared/api'
import { gameModes, type Card, type GameMode, type GameState, type SavedDeck } from '../../shared/models'
import { OnlinePlaymat } from './OnlinePlaymat'

type OnlineBattlePageProps = {
  apiClient: ApiClient
  cards: Card[]
  decks: SavedDeck[]
  session: AuthSession | null
}

const allModes = Object.keys(gameModes) as GameMode[]

export function OnlineBattlePage({ apiClient, cards, decks, session }: OnlineBattlePageProps) {
  const cardsApi = useMemo(() => createCardsApi(apiClient), [apiClient])
  const lobbiesApi = useMemo(() => createLobbiesApi(apiClient), [apiClient])
  const matchmakingApi = useMemo(() => createMatchmakingApi(apiClient), [apiClient])
  const matchApi = useMemo(() => createMatchesApi(apiClient), [apiClient])
  const [view, setView] = useState<'queue' | 'lobbies'>('lobbies')
  const [deckId, setDeckId] = useState(decks[0]?.id ?? '')
  const [ticket, setTicket] = useState<MatchmakingTicket | null>(null)
  const [lobbies, setLobbies] = useState<Lobby[]>([])
  const [lobby, setLobby] = useState<Lobby | null>(null)
  const [lobbyName, setLobbyName] = useState('Riftbound lobby')
  const [includeReadyDummy, setIncludeReadyDummy] = useState(false)
  const [selectedMode, setSelectedMode] = useState<GameMode>('duel-1v1')
  const [allowedModes, setAllowedModes] = useState<GameMode[]>(['duel-1v1'])
  const [selectedBattlefieldId, setSelectedBattlefieldId] = useState('')
  const [match, setMatch] = useState<MatchSnapshot | null>(null)
  const [state, setState] = useState<GameState | null>(null)
  const [legalActions, setLegalActions] = useState<LegalAction[]>([])
  const [mulliganHandIndexes, setMulliganHandIndexes] = useState<number[]>([])
  const [events, setEvents] = useState<MatchEvent[]>([])
  const [battlefieldNames, setBattlefieldNames] = useState<Record<string, string>>({})
  const [status, setStatus] = useState('Create or join a lobby, or use quick queue for 1v1.')
  const connectionRef = useRef<HubConnection | null>(null)
  const playerIdRef = useRef(0)

  const selectedDeckId = deckId || decks[0]?.id || ''
  const selectedDeck = decks.find((deck) => deck.id === selectedDeckId) ?? null
  const battlefieldOptions = selectedDeck
    ? selectedDeck.battlefieldDeckIds.map((id) => ({
      id,
      name: cards.find((card) => card.id === id)?.name ?? battlefieldNames[id] ?? 'Loading battlefield...',
    }))
    : []
  const effectiveSelectedBattlefieldId = selectedDeck?.battlefieldDeckIds.includes(selectedBattlefieldId)
    ? selectedBattlefieldId
    : selectedDeck?.battlefieldDeckIds[0] ?? ''
  const playerId = match?.players.find((player) => player.userId === session?.user.id)?.playerId ?? 0
  const isMulliganTurn =
    state?.stage === 'mulligan' && !(state.mulliganConfirmedPlayerIds ?? []).includes(playerId)
  const currentLobbyPlayer = lobby?.players.find((player) => player.userId === session?.user.id) ?? null
  const isHost = lobby?.hostUserId === session?.user.id
  const isAdmin = session?.user.isAdmin === true
  const canReady = Boolean(lobby && selectedDeck && effectiveSelectedBattlefieldId)

  useEffect(() => {
    playerIdRef.current = playerId
  }, [playerId])

  useEffect(() => {
    if (!session) return
    void loadLobbies()
  }, [session])

  useEffect(() => {
    const unresolvedIds = selectedDeck?.battlefieldDeckIds.filter((id) => !cards.some((card) => card.id === id) && !battlefieldNames[id]) ?? []
    if (unresolvedIds.length === 0) {
      return
    }

    let cancelled = false
    async function loadBattlefieldNames() {
      const entries = await Promise.all(unresolvedIds.map(async (id) => {
        try {
          const card = await cardsApi.getCard(id)
          return [id, card.name] as const
        } catch {
          return [id, id] as const
        }
      }))

      if (!cancelled) {
        setBattlefieldNames((current) => ({
          ...current,
          ...Object.fromEntries(entries),
        }))
      }
    }

    void loadBattlefieldNames()
    return () => {
      cancelled = true
    }
  }, [battlefieldNames, cards, cardsApi, selectedDeck])

  useEffect(() => {
    return () => {
      void connectionRef.current?.stop()
    }
  }, [])

  async function ensureConnection() {
    if (!session) throw new Error('Sign in before connecting to online battles.')
    if (connectionRef.current?.state === HubConnectionState.Connected) return connectionRef.current

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/matches', { accessTokenFactory: () => session.accessToken })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('matchmaking.ticketUpdated', async (nextTicket: MatchmakingTicket) => {
      setTicket(nextTicket)
      if (nextTicket.matchId) {
        setStatus('Match found.')
        await loadMatch(connection, nextTicket.matchId)
      }
    })

    const updateLobby = async (nextLobby: Lobby) => {
      setLobby(nextLobby)
      setLobbies((current) => current.map((item) => item.id === nextLobby.id ? nextLobby : item))
      if (nextLobby.matchId) {
        setStatus('Lobby matched.')
        await loadMatch(connection, nextLobby.matchId)
      } else if (nextLobby.status === 'cancelled') {
        setStatus('Lobby cancelled.')
      }
    }
    connection.on('lobby.updated', updateLobby)
    connection.on('lobby.playerJoined', updateLobby)
    connection.on('lobby.playerLeft', updateLobby)
    connection.on('lobby.loadoutUpdated', updateLobby)
    connection.on('lobby.readyChanged', updateLobby)
    connection.on('lobby.matched', updateLobby)
    connection.on('lobby.cancelled', updateLobby)

    connection.on('match.joined', (nextMatch: MatchSnapshot) => {
      setMatch(nextMatch)
      setState(nextMatch.state)
    })
    connection.on('match.state', (matchId: string, nextState: GameState, sequenceNumber: number) => {
      setState(nextState)
      setMatch((current) => current ? { ...current, state: nextState, sequenceNumber } : current)
      void connection.invoke('RequestLegalActions', matchId, playerIdRef.current)
    })
    connection.on('match.legalActions', (_matchId: string, _nextPlayerId: number, actions: LegalAction[]) => {
      setLegalActions(actions)
    })
    connection.on('match.eventAppended', (_matchId: string, event: MatchEvent) => {
      setEvents((current) => [event, ...current].slice(0, 20))
    })
    connection.on('match.actionRejected', (_matchId: string, _playerId: number, error: unknown) => {
      setStatus(`Action rejected: ${JSON.stringify(error)}`)
    })
    connection.on('match.completed', () => {
      setStatus('Match completed.')
    })
    connection.on('error', (error: unknown) => {
      setStatus(`Realtime error: ${JSON.stringify(error)}`)
    })

    await connection.start()
    connectionRef.current = connection
    return connection
  }

  async function loadLobbies() {
    try {
      setLobbies(await lobbiesApi.listLobbies())
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to load lobbies.')
    }
  }

  async function createLobby() {
    if (!session) {
      setStatus('Sign in before creating a lobby.')
      return
    }

    try {
      const nextLobby = await lobbiesApi.createLobby({ name: lobbyName, allowedModes, selectedMode, includeReadyDummy: isAdmin && includeReadyDummy })
      setLobby(nextLobby)
      setStatus('Lobby created.')
      const connection = await ensureConnection()
      await connection.invoke('SubscribeLobby', nextLobby.id)
      await loadLobbies()
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to create lobby.')
    }
  }

  async function joinLobby(lobbyId: string) {
    try {
      const nextLobby = await lobbiesApi.joinLobby(lobbyId)
      setLobby(nextLobby)
      setStatus('Joined lobby.')
      const connection = await ensureConnection()
      await connection.invoke('SubscribeLobby', nextLobby.id)
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to join lobby.')
    }
  }

  async function saveLobbySettings() {
    if (!lobby) return
    try {
      const nextLobby = await lobbiesApi.updateSettings(lobby.id, { name: lobbyName, allowedModes, selectedMode })
      setLobby(nextLobby)
      setStatus('Lobby settings updated.')
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to update lobby settings.')
    }
  }

  async function saveLoadout() {
    if (!lobby || !selectedDeck || !effectiveSelectedBattlefieldId) return
    try {
      const nextLobby = await lobbiesApi.updateLoadout(lobby.id, { deckId: selectedDeck.id, selectedBattlefieldIds: [effectiveSelectedBattlefieldId] })
      setLobby(nextLobby)
      setStatus('Loadout saved.')
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to save loadout.')
    }
  }

  async function setReady(ready: boolean) {
    if (!lobby) return
    try {
      if (!currentLobbyPlayer?.isReady) await saveLoadout()
      const nextLobby = ready ? await lobbiesApi.ready(lobby.id) : await lobbiesApi.unready(lobby.id)
      setLobby(nextLobby)
      setStatus(ready ? 'Ready.' : 'Not ready.')
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to update ready state.')
    }
  }

  async function startLobby() {
    if (!lobby) return
    try {
      const nextLobby = await lobbiesApi.start(lobby.id)
      setLobby(nextLobby)
      if (nextLobby.matchId) {
        const connection = await ensureConnection()
        await loadMatch(connection, nextLobby.matchId)
      }
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to start lobby.')
    }
  }

  async function leaveLobby() {
    if (!lobby) return
    try {
      await lobbiesApi.leaveLobby(lobby.id)
      await connectionRef.current?.invoke('LeaveLobby', lobby.id)
      setLobby(null)
      setStatus('Left lobby.')
      await loadLobbies()
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to leave lobby.')
    }
  }

  async function joinQueue() {
    if (!session) {
      setStatus('Sign in before entering online queue.')
      return
    }

    if (!selectedDeck) {
      setStatus('Choose a deck first.')
      return
    }

    setStatus('Joining quick queue...')
    const connection = await ensureConnection()
    const nextTicket = await matchmakingApi.joinQueue({ deckId: selectedDeck.id, mode: 'duel-1v1' })
    setTicket(nextTicket)
    await connection.invoke('SubscribeTicket', nextTicket.id)
    if (nextTicket.matchId) {
      await loadMatch(connection, nextTicket.matchId)
    } else {
      setStatus('Queued. Open another browser or machine with a different user and deck.')
    }
  }

  async function loadMatch(connection: HubConnection, matchId: string) {
    const nextMatch = await matchApi.getMatch(matchId)
    setMatch(nextMatch)
    setState(nextMatch.state)
    setEvents(await matchApi.listEvents(matchId))
    await connection.invoke('JoinMatch', matchId)
    const seat = nextMatch.players.find((player) => player.userId === session?.user.id)
    if (seat) {
      const actions = await matchApi.listLegalActions(matchId, seat.playerId)
      setLegalActions(actions)
      await connection.invoke('RequestLegalActions', matchId, seat.playerId)
    }
  }

  async function submitAction(action: LegalAction) {
    if (!match) return
    const connection = await ensureConnection()
    await connection.invoke('SubmitAction', match.id, {
      actionId: action.id,
      type: action.type,
      playerId: action.playerId,
      payload: action.type === 'confirm-mulligan' ? { handIndexes: mulliganHandIndexes } : {},
      expectedSequenceNumber: match.sequenceNumber,
    })
    setMulliganHandIndexes([])
    await connection.invoke('RequestLegalActions', match.id, action.playerId)
  }

  function toggleMulliganHandIndex(index: number) {
    setMulliganHandIndexes((current) => {
      if (current.includes(index)) return current.filter((item) => item !== index)
      if (current.length >= 2) return current
      return [...current, index]
    })
  }

  function toggleAllowedMode(mode: GameMode) {
    setAllowedModes((current) => {
      const next = current.includes(mode) ? current.filter((item) => item !== mode) : [...current, mode]
      return next.length === 0 ? [mode] : next
    })
  }

  return (
    <section className="online-page">
      <div className="online-toolbar">
        <div>
          <p className="eyebrow">online battle</p>
          <h2>{view === 'queue' ? 'Quick queue' : 'Lobbies'}</h2>
          <p>{status}</p>
        </div>
        <div className="online-account">
          <span>Account</span>
          <strong>{session?.user.displayName ?? 'Not signed in'}</strong>
        </div>
        <div className="deck-tabs">
          <button className={view === 'lobbies' ? 'active' : ''} type="button" onClick={() => setView('lobbies')}>Lobbies</button>
          <button className={view === 'queue' ? 'active' : ''} type="button" onClick={() => setView('queue')}>Quick queue</button>
        </div>
      </div>

      {view === 'queue' && (
        <div className="online-toolbar compact-online-toolbar">
          <label>
            Deck
            <select value={selectedDeckId} onChange={(event) => setDeckId(event.target.value)}>
              {decks.map((deck) => <option key={deck.id} value={deck.id}>{deck.name}</option>)}
            </select>
          </label>
          <button type="button" onClick={() => void joinQueue()} disabled={!session || !selectedDeck}>Enter queue</button>
          {ticket && <span>{ticket.status}: {ticket.matchId ?? ticket.id}</span>}
        </div>
      )}

      {view === 'lobbies' && (
        <div className="lobby-layout">
          <section className="online-board lobby-browser">
            <header>
              <h3>Create lobby</h3>
              <button type="button" onClick={() => void loadLobbies()}>Refresh</button>
            </header>
            <label>
              Name
              <input value={lobbyName} onChange={(event) => setLobbyName(event.target.value)} />
            </label>
            <label>
              Selected mode
              <select value={selectedMode} onChange={(event) => setSelectedMode(event.target.value as GameMode)}>
                {allModes.map((mode) => <option key={mode} value={mode}>{gameModes[mode].label}</option>)}
              </select>
            </label>
            <div className="lobby-mode-grid">
              {allModes.map((mode) => (
                <label className="admin-check" key={mode}>
                  <input checked={allowedModes.includes(mode)} type="checkbox" onChange={() => toggleAllowedMode(mode)} />
                  {gameModes[mode].label}
                </label>
              ))}
            </div>
            {isAdmin && (
              <label className="admin-check">
                <input checked={includeReadyDummy} type="checkbox" onChange={(event) => setIncludeReadyDummy(event.target.checked)} />
                Add ready dummy player
              </label>
            )}
            <button type="button" disabled={!session} onClick={() => void createLobby()}>Create lobby</button>

            <h3>Open lobbies</h3>
            <div className="lobby-list">
              {lobbies.length === 0 && <p>No open lobbies.</p>}
              {lobbies.map((item) => (
                <button className={lobby?.id === item.id ? 'admin-list-item active' : 'admin-list-item'} key={item.id} type="button" onClick={() => void joinLobby(item.id)}>
                  <strong>{item.name}</strong>
                  <small>{gameModes[item.selectedMode].label} · {item.players.length}/{item.requiredPlayerCount}</small>
                  <span>{item.status}</span>
                </button>
              ))}
            </div>
          </section>

          {lobby && (
            <section className="online-board lobby-room">
              <header>
                <h3>{lobby.name}</h3>
                <span>{gameModes[lobby.selectedMode].label} · {lobby.players.length}/{lobby.requiredPlayerCount}</span>
                <button type="button" onClick={() => void leaveLobby()}>Leave</button>
              </header>

              {isHost && (
                <div className="online-toolbar compact-online-toolbar">
                  <label>
                    Lobby name
                    <input value={lobbyName} onChange={(event) => setLobbyName(event.target.value)} />
                  </label>
                  <label>
                    Mode
                    <select value={selectedMode} onChange={(event) => setSelectedMode(event.target.value as GameMode)}>
                      {allowedModes.map((mode) => <option key={mode} value={mode}>{gameModes[mode].label}</option>)}
                    </select>
                  </label>
                  <button type="button" onClick={() => void saveLobbySettings()} disabled={lobby.players.some((player) => player.isReady)}>Save settings</button>
                </div>
              )}

              <div className="online-players">
                {Array.from({ length: lobby.requiredPlayerCount }, (_, index) => {
                  const player = lobby.players.find((candidate) => candidate.seatIndex === index)
                  return (
                    <article className="player" key={index}>
                      <span>Seat {index + 1}{player?.teamId !== null && player?.teamId !== undefined ? ` · Team ${player.teamId + 1}` : ''}</span>
                      <strong>{player?.displayName ?? 'Open'}</strong>
                      <small>{player?.isReady ? 'Ready' : 'Not ready'} · {player?.deckId ?? 'No deck'}</small>
                    </article>
                  )
                })}
              </div>

              <div className="online-toolbar compact-online-toolbar">
                <label>
                  Deck
                  <select value={selectedDeckId} onChange={(event) => {
                    const nextDeck = decks.find((deck) => deck.id === event.target.value)
                    setDeckId(event.target.value)
                    setSelectedBattlefieldId(nextDeck?.battlefieldDeckIds[0] ?? '')
                  }}>
                    {decks.map((deck) => <option key={deck.id} value={deck.id}>{deck.name}</option>)}
                  </select>
                </label>
                <label>
                  Battlefield
                  <select value={effectiveSelectedBattlefieldId} onChange={(event) => setSelectedBattlefieldId(event.target.value)}>
                    {battlefieldOptions.map((battlefield) => <option key={battlefield.id} value={battlefield.id}>{battlefield.name}</option>)}
                  </select>
                </label>
                <button type="button" disabled={!canReady} onClick={() => void saveLoadout()}>Save loadout</button>
                <button type="button" disabled={!canReady} onClick={() => void setReady(!currentLobbyPlayer?.isReady)}>
                  {currentLobbyPlayer?.isReady ? 'Unready' : 'Ready'}
                </button>
                {isHost && <button type="button" disabled={!lobby.canStart} onClick={() => void startLobby()}>Start game</button>}
              </div>
            </section>
          )}
        </div>
      )}

      {state && (
        <div className="online-match">
          <section className="online-board">
            <header>
              <span>{state.stage}</span>
              <strong>Turn {state.turnNumber}</strong>
              <span>{state.turnPhase}</span>
            </header>
            <div className="online-players">
              {state.players.map((player) => (
                <article className={player.id === state.turnPlayerId ? 'active player' : 'player'} key={player.id}>
                  <span>{player.name}</span>
                  <strong>{player.points} pts</strong>
                  <small>{player.hand.length} hand · {player.deck.length} deck · {player.runeDeck.length} runes</small>
                </article>
              ))}
            </div>
            <OnlinePlaymat
              cards={cards}
              game={state}
              viewerPlayerId={playerId}
              mulliganSelection={
                isMulliganTurn ? { selectedIndexes: mulliganHandIndexes, onToggle: toggleMulliganHandIndex } : undefined
              }
            />
          </section>

          <aside className="online-actions">
            <h3>Actions</h3>
            {isMulliganTurn && (
              <p>Select up to 2 cards in your hand to exchange ({mulliganHandIndexes.length}/2 selected).</p>
            )}
            {legalActions.length === 0 && <p>No legal actions for your seat right now.</p>}
            {legalActions.map((action) => (
              <button key={action.id} type="button" onClick={() => void submitAction(action)}>
                {action.label}
              </button>
            ))}
          </aside>

          <aside className="online-log">
            <h3>Log</h3>
            {state.log.map((entry) => <p key={entry.id}>{entry.text}</p>)}
            {events.map((event) => <p key={event.id}>#{event.sequenceNumber} {event.actionType}</p>)}
          </aside>
        </div>
      )}
    </section>
  )
}
