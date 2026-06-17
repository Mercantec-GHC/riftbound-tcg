import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useEffect, useMemo, useRef, useState } from 'react'
import { createDecksApi, createMatchesApi, createMatchmakingApi, type ApiClient } from '../../api'
import type { AuthSession, LegalAction, MatchEvent, MatchSnapshot, MatchmakingTicket } from '../../api'
import type { GameState, SavedDeck } from '../../models'

type OnlineBattlePageProps = {
  apiClient: ApiClient
  decks: SavedDeck[]
  session: AuthSession | null
}

export function OnlineBattlePage({ apiClient, decks, session }: OnlineBattlePageProps) {
  const deckApi = useMemo(() => createDecksApi(apiClient), [apiClient])
  const matchmakingApi = useMemo(() => createMatchmakingApi(apiClient), [apiClient])
  const matchApi = useMemo(() => createMatchesApi(apiClient), [apiClient])
  const [deckId, setDeckId] = useState(decks[0]?.id ?? '')
  const [ticket, setTicket] = useState<MatchmakingTicket | null>(null)
  const [match, setMatch] = useState<MatchSnapshot | null>(null)
  const [state, setState] = useState<GameState | null>(null)
  const [legalActions, setLegalActions] = useState<LegalAction[]>([])
  const [events, setEvents] = useState<MatchEvent[]>([])
  const [status, setStatus] = useState('Choose a deck and enter queue.')
  const connectionRef = useRef<HubConnection | null>(null)

  const selectedDeckId = deckId || decks[0]?.id || ''
  const selectedDeck = decks.find((deck) => deck.id === selectedDeckId) ?? null
  const playerId = match?.players.find((player) => player.userId === session?.user.id)?.playerId ?? 0

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
    connection.on('match.joined', (nextMatch: MatchSnapshot) => {
      setMatch(nextMatch)
      setState(nextMatch.state)
    })
    connection.on('match.state', (_matchId: string, nextState: GameState, sequenceNumber: number) => {
      setState(nextState)
      setMatch((current) => current ? { ...current, state: nextState, sequenceNumber } : current)
    })
    connection.on('match.legalActions', (_matchId: string, nextPlayerId: number, actions: LegalAction[]) => {
      if (nextPlayerId === playerId) setLegalActions(actions)
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

  async function joinQueue() {
    if (!session) {
      setStatus('Sign in before entering online queue.')
      return
    }

    if (!selectedDeck) {
      setStatus('Choose a deck first.')
      return
    }

    setStatus('Uploading deck and joining queue...')
    const serverDeck = await deckApi.createDeck(selectedDeck)
    const connection = await ensureConnection()
    const nextTicket = await matchmakingApi.joinQueue({ deckId: serverDeck.id, mode: 'duel-1v1' })
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
      payload: action.type === 'confirm-mulligan' ? { handIndexes: [] } : {},
      expectedSequenceNumber: match.sequenceNumber,
    })
    await connection.invoke('RequestLegalActions', match.id, action.playerId)
  }

  return (
    <section className="online-page">
      <div className="online-toolbar">
        <div>
          <p className="eyebrow">online battle</p>
          <h2>Matchmaking</h2>
          <p>{status}</p>
        </div>
        <div className="online-account">
          <span>Account</span>
          <strong>{session?.user.displayName ?? 'Not signed in'}</strong>
        </div>
        <label>
          Deck
          <select value={selectedDeckId} onChange={(event) => setDeckId(event.target.value)}>
            {decks.map((deck) => <option key={deck.id} value={deck.id}>{deck.name}</option>)}
          </select>
        </label>
        <button type="button" onClick={() => void joinQueue()} disabled={!session}>Enter queue</button>
      </div>

      {ticket && (
        <div className="online-status">
          <strong>{ticket.status}</strong>
          <span>{ticket.matchId ?? ticket.id}</span>
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
            <div className="online-battlefields">
              {state.battlefields.map((battlefield) => (
                <article key={battlefield.id}>
                  <strong>{battlefield.name}</strong>
                  <span>Control: {battlefield.controllerId === null ? 'none' : battlefield.controllerId}</span>
                  <small>{battlefield.units.length} units</small>
                </article>
              ))}
            </div>
          </section>

          <aside className="online-actions">
            <h3>Actions</h3>
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
