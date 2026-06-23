import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useEffect, useMemo, useRef, useState } from 'react'
import './OnlineBattlePage.css'
import { createCardsApi, createLobbiesApi, createMatchesApi, createMatchmakingApi, type ApiClient } from '../../shared/api'
import type { AuthSession, LegalAction, Lobby, MatchEvent, MatchSnapshot, MatchmakingTicket } from '../../shared/api'
import { gameModes, type Card, type GameMode, type GameState, type SavedDeck, type Unit } from '../../shared/models'
import { OnlinePlaymat } from './OnlinePlaymat'

type OnlineBattlePageProps = {
  apiClient: ApiClient
  cards: Card[]
  decks: SavedDeck[]
  session: AuthSession | null
}

const allModes = Object.keys(gameModes) as GameMode[]

type CombatAssignments = Record<string, number>

type ResolveCombatPayload = {
  battlefieldId: string
  assignments: CombatAssignments
}

function unitOwnerId(unit: Unit): number {
  return (unit as Unit & { ownerId?: number }).ownerId ?? unit.owner
}

function currentMight(unit: Unit): number {
  return (unit.might ?? 0) + (unit.attachedMight ?? 0)
}

function combatMight(unit: Unit): number {
  return Math.max(0, currentMight(unit))
}

function lethalDamage(unit: Unit): number {
  return Math.max(1, currentMight(unit))
}

function totalCombatMight(units: Unit[]): number {
  return units.reduce((total, unit) => total + combatMight(unit), 0)
}

function createEmptyAssignments(units: Unit[]): CombatAssignments {
  return Object.fromEntries(units.map((unit) => [unit.uid, 0]))
}

function createDefaultAssignments(assigningUnits: Unit[], targetUnits: Unit[]): CombatAssignments {
  const assignments = createEmptyAssignments(targetUnits)
  let remaining = totalCombatMight(assigningUnits)
  for (const unit of targetUnits) {
    if (remaining <= 0) break
    const assigned = Math.min(lethalDamage(unit), remaining)
    assignments[unit.uid] = assigned
    remaining -= assigned
  }

  if (remaining > 0 && targetUnits.length > 0) {
    const lastTarget = targetUnits[targetUnits.length - 1]
    assignments[lastTarget.uid] = (assignments[lastTarget.uid] ?? 0) + remaining
  }

  return assignments
}

function validateAssignments(assigningUnits: Unit[], targetUnits: Unit[], assignments: CombatAssignments, label: string): string[] {
  const messages: string[] = []
  const requiredTotal = totalCombatMight(assigningUnits)
  const assignedTotal = Object.values(assignments).reduce((total, amount) => total + amount, 0)
  if (assignedTotal !== requiredTotal) {
    messages.push(`${label} must assign exactly ${requiredTotal} damage.`)
  }

  if (Object.values(assignments).some((amount) => amount < 0 || !Number.isInteger(amount))) {
    messages.push(`${label} assignments must be whole numbers of 0 or more.`)
  }

  const targetIds = new Set(targetUnits.map((unit) => unit.uid))
  if (Object.keys(assignments).some((uid) => !targetIds.has(uid))) {
    messages.push(`${label} can only assign damage to opposing units.`)
  }

  const positiveAssignments = Object.entries(assignments).filter(([, amount]) => amount > 0)
  const nonLethalPositiveCount = positiveAssignments.filter(([uid, amount]) => {
    const unit = targetUnits.find((candidate) => candidate.uid === uid)
    return unit ? amount < lethalDamage(unit) : false
  }).length
  if (nonLethalPositiveCount > 1) {
    messages.push(`${label} must assign lethal damage before spreading damage to another unit.`)
  }

  const allTargetsLethal = targetUnits.every((unit) => (assignments[unit.uid] ?? 0) >= lethalDamage(unit))
  const overAssignedEarly = positiveAssignments.some(([uid, amount]) => {
    const unit = targetUnits.find((candidate) => candidate.uid === uid)
    return unit ? amount > lethalDamage(unit) && !allTargetsLethal : false
  })
  if (overAssignedEarly) {
    messages.push(`${label} cannot assign more than lethal damage until every opposing unit has lethal assigned.`)
  }

  return messages
}

function combatPanelKey(game: GameState): string {
  const combat = game.activeCombat
  const battlefield = combat ? game.battlefields.find((field) => field.id === combat.battlefieldId) : null
  return `${combat?.battlefieldId ?? 'none'}:${battlefield?.units.map((unit) => unit.uid).join(',') ?? ''}`
}

function playerName(game: GameState, playerId: number | null | undefined): string {
  if (playerId === null || playerId === undefined) return 'None'
  return game.players.find((player) => player.id === playerId)?.name ?? `Player ${playerId + 1}`
}

function currentWindowLabel(game: GameState): string {
  if (game.chainWindow) return 'Chain'
  if (game.activeCombat?.damageStep) return 'Combat damage'
  if (game.activeShowdown?.kind === 'combat') return 'Combat showdown'
  if (game.activeShowdown) return 'Showdown'
  return 'Neutral'
}

function passedPlayerNames(game: GameState, passedByPlayer: Record<number, boolean>): string {
  const names = Object.entries(passedByPlayer)
    .filter(([, passed]) => passed)
    .map(([id]) => playerName(game, Number(id)))
  return names.length > 0 ? names.join(', ') : 'None'
}

function CombatAssignmentInputs({
  assignments,
  label,
  onChange,
  targetUnits,
}: {
  assignments: CombatAssignments
  label: string
  onChange: (next: CombatAssignments) => void
  targetUnits: Unit[]
}) {
  return (
    <div className="combat-assignment-group">
      <h4>{label}</h4>
      {targetUnits.map((unit) => (
        <label className="combat-assignment-row" key={unit.uid}>
          <span>
            <strong>{unit.name}</strong>
            <small>{currentMight(unit)} might · {unit.damage} damage</small>
          </span>
          <input
            min={0}
            step={1}
            type="number"
            value={assignments[unit.uid] ?? 0}
            onChange={(event) => onChange({ ...assignments, [unit.uid]: Math.max(0, Math.floor(Number(event.target.value) || 0)) })}
          />
        </label>
      ))}
    </div>
  )
}

function CombatShowdownModal({
  game,
  onClose,
  onResolveCombat,
  viewerPlayerId,
}: {
  game: GameState
  onClose: () => void
  onResolveCombat: (payload: ResolveCombatPayload) => Promise<void>
  viewerPlayerId: number
}) {
  const combat = game.activeCombat
  const battlefield = combat ? game.battlefields.find((field) => field.id === combat.battlefieldId) : null
  const attackers = battlefield?.units.filter((unit) => unitOwnerId(unit) === combat?.attackerPlayerId) ?? []
  const defenders = battlefield?.units.filter((unit) => unitOwnerId(unit) === combat?.defenderPlayerId) ?? []
  const attackerName = game.players.find((player) => player.id === combat?.attackerPlayerId)?.name ?? 'Attacker'
  const defenderName = game.players.find((player) => player.id === combat?.defenderPlayerId)?.name ?? 'Defender'
  const isAttacker = viewerPlayerId === combat?.attackerPlayerId
  const assigningUnits = isAttacker ? attackers : defenders
  const targetUnits = isAttacker ? defenders : attackers
  const assigningName = isAttacker ? attackerName : defenderName
  const targetLabel = isAttacker ? 'defenders' : 'attackers'
  const [assignments, setAssignments] = useState<CombatAssignments>(() => createDefaultAssignments(assigningUnits, targetUnits))

  if (!combat || !battlefield) return null

  const validationMessages = validateAssignments(assigningUnits, targetUnits, assignments, assigningName)
  const canSubmit = validationMessages.length === 0 && assigningUnits.length > 0 && targetUnits.length > 0

  return (
    <div className="combat-modal-backdrop" role="presentation">
      <section aria-modal="true" className="combat-showdown-modal" role="dialog">
        <header>
          <div>
            <span>Combat showdown</span>
            <h3>{battlefield.name}</h3>
          </div>
          <button aria-label="Close combat assignment" className="combat-modal-close" type="button" onClick={onClose}>X</button>
        </header>

        <p className="combat-status-line">
          {attackerName} attacks with {totalCombatMight(attackers)} might. {defenderName} defends with {totalCombatMight(defenders)} might.
        </p>

        <div className="combat-modal-toolbar">
          <strong>{assigningName}: assign {totalCombatMight(assigningUnits)} damage to {targetLabel}</strong>
          <button type="button" onClick={() => setAssignments(createDefaultAssignments(assigningUnits, targetUnits))}>Auto assign</button>
        </div>

        <CombatAssignmentInputs
          assignments={assignments}
          label={`${assigningName} assigns damage`}
          onChange={setAssignments}
          targetUnits={targetUnits}
        />

        {validationMessages.length > 0 && (
          <div className="combat-validation" role="status">
            {validationMessages.map((message) => <p key={message}>{message}</p>)}
          </div>
        )}

        <footer>
          <button type="button" onClick={onClose}>Cancel</button>
          <button
            className="combat-submit"
            disabled={!canSubmit}
            type="button"
            onClick={() => void onResolveCombat({
              battlefieldId: combat.battlefieldId,
              assignments,
            })}
          >
            Submit assignment
          </button>
        </footer>
      </section>
    </div>
  )
}

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
  const [combatModalOpen, setCombatModalOpen] = useState(false)
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
  const playableCardHandIndexes = legalActions
    .filter((action) => action.type === 'play-card' && action.playerId === playerId)
    .map((action) => Number(action.payloadSchema?.handIndex))
    .filter((index) => Number.isInteger(index))
  const canResolveCombat = Boolean(state?.activeCombat && legalActions.some((action) => action.type === 'resolve-combat' && action.playerId === playerId))

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
    try {
      await connection.invoke('SubmitAction', match.id, {
        actionId: action.id,
        type: action.type,
        playerId: action.playerId,
        payload: action.type === 'confirm-mulligan' ? { handIndexes: mulliganHandIndexes } : action.payloadSchema ?? {},
        expectedSequenceNumber: match.sequenceNumber,
      })
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to submit action.')
    }
    setMulliganHandIndexes([])
    await connection.invoke('RequestLegalActions', match.id, action.playerId)
  }

  async function submitTypedAction(type: string, payload: Record<string, unknown>, failureMessage: string) {
    if (!match) return
    const action = legalActions.find((candidate) => candidate.type === type && candidate.playerId === playerId)
    if (!action) return
    const connection = await ensureConnection()
    try {
      await connection.invoke('SubmitAction', match.id, {
        actionId: action.id,
        type,
        playerId,
        payload,
        expectedSequenceNumber: match.sequenceNumber,
      })
    } catch (error) {
      setStatus(error instanceof Error ? error.message : failureMessage)
    }
    await connection.invoke('RequestLegalActions', match.id, playerId)
  }

  async function playUnit(handIndex: number, battlefieldId?: string) {
    await submitTypedAction('play-unit', battlefieldId ? { handIndex, battlefieldId } : { handIndex }, 'Unable to play unit.')
  }

  async function moveUnit(unitId: string, battlefieldId: string) {
    await submitTypedAction('move-unit', { unitId, battlefieldId }, 'Unable to move unit.')
  }

  async function playCard(handIndex: number) {
    await submitTypedAction('play-card', { handIndex }, 'Unable to play card.')
  }

  async function resolveCombat(payload: ResolveCombatPayload) {
    await submitTypedAction('resolve-combat', payload as unknown as Record<string, unknown>, 'Unable to resolve combat.')
    setCombatModalOpen(false)
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
                <label className="online-check" key={mode}>
                  <input checked={allowedModes.includes(mode)} type="checkbox" onChange={() => toggleAllowedMode(mode)} />
                  {gameModes[mode].label}
                </label>
              ))}
            </div>
            {isAdmin && (
              <label className="online-check">
                <input checked={includeReadyDummy} type="checkbox" onChange={(event) => setIncludeReadyDummy(event.target.checked)} />
                Add ready dummy player
              </label>
            )}
            <button type="button" disabled={!session} onClick={() => void createLobby()}>Create lobby</button>

            <h3>Open lobbies</h3>
            <div className="lobby-list">
              {lobbies.length === 0 && <p>No open lobbies.</p>}
              {lobbies.map((item) => (
                <button className={lobby?.id === item.id ? 'online-list-item active' : 'online-list-item'} key={item.id} type="button" onClick={() => void joinLobby(item.id)}>
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
            <div className="online-window-panel">
              <article>
                <span>Window</span>
                <strong>{currentWindowLabel(state)}</strong>
              </article>
              <article>
                <span>Priority</span>
                <strong>{playerName(state, state.chainWindow?.priorityPlayerId ?? state.priorityPlayerId)}</strong>
              </article>
              <article>
                <span>Focus</span>
                <strong>{playerName(state, state.focusPlayerId)}</strong>
              </article>
              <article>
                <span>Passed</span>
                <strong>{state.chainWindow ? passedPlayerNames(state, state.chainWindow.passedByPlayer) : passedPlayerNames(state, state.hasPassedFocusByPlayer)}</strong>
              </article>
            </div>
            <OnlinePlaymat
              cards={cards}
              game={state}
              matchPlayers={match?.players ?? []}
              viewerPlayerId={playerId}
              canPlayUnit={legalActions.some((action) => action.type === 'play-unit' && action.playerId === playerId)}
              onPlayUnit={playUnit}
              playableCardHandIndexes={playableCardHandIndexes}
              onPlayCard={playCard}
              canMoveUnit={legalActions.some((action) => action.type === 'move-unit' && action.playerId === playerId)}
              onMoveUnit={moveUnit}
              mulliganSelection={
                isMulliganTurn ? { selectedIndexes: mulliganHandIndexes, onToggle: toggleMulliganHandIndex } : undefined
              }
            />
          </section>

          <aside className="online-actions">
            <h3>Actions</h3>
            {state.effectStack.length > 0 && (
              <div className="online-stack">
                <strong>Chain</strong>
                {state.effectStack.map((item, index) => (
                  <p key={item.id}>{index === 0 ? 'Top' : `#${index + 1}`}: {item.cardName} ({playerName(state, item.playerId)})</p>
                ))}
              </div>
            )}
            {isMulliganTurn && (
              <p>Select up to 2 cards in your hand to exchange ({mulliganHandIndexes.length}/2 selected).</p>
            )}
            {state.activeCombat && (
              canResolveCombat
                ? <button type="button" onClick={() => setCombatModalOpen(true)}>Assign combat damage</button>
                : <p>{state.activeCombat.damageStep ? 'Waiting for combat damage assignments.' : 'Combat showdown is open.'}</p>
            )}
            {state.activeCombat && canResolveCombat && combatModalOpen && (
              <CombatShowdownModal
                key={combatPanelKey(state)}
                game={state}
                onClose={() => setCombatModalOpen(false)}
                onResolveCombat={resolveCombat}
                viewerPlayerId={playerId}
              />
            )}
            {legalActions.length === 0 && <p>No legal actions for your seat right now.</p>}
            {legalActions.filter((action) => !(canResolveCombat && action.type === 'resolve-combat')).map((action) => (
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
