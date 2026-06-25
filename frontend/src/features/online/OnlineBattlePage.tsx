import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import './OnlineBattlePage.css'
import { createCardsApi, createLobbiesApi, createMatchesApi, createMatchmakingApi, type ApiClient } from '../../shared/api'
import type { AuthSession, LegalAction, Lobby, MatchEvent, MatchSnapshot, MatchmakingTicket } from '../../shared/api'
import { gameModes, type Card, type EffectType, type GameMode, type GameState, type SavedDeck, type Unit } from '../../shared/models'
import { findServerApprovedAction, hasServerApprovedAction, serverApprovedHandIndexes } from './onlineActionGuards'
import { OnlinePlaymat, type BoardActionChoice, type HandCardIntent, type UnitMoveIntent } from './OnlinePlaymat'

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

type DragActionChoice = BoardActionChoice

type DragActionPrompt = {
  cardName: string
  choices: DragActionChoice[]
}

function unitOwnerId(unit: Unit): number {
  return (unit as Unit & { ownerId?: number }).ownerId ?? unit.owner
}

function stringArrayPayload(action: LegalAction, key: string): string[] {
  const value = action.payloadSchema?.[key]
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : []
}

function stringPayload(action: LegalAction, key: string): string | null {
  const value = action.payloadSchema?.[key]
  return typeof value === 'string' ? value : null
}

function payloadFromServerAction(action: LegalAction, overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    ...(action.payloadSchema ?? {}),
    ...overrides,
  }
}

function legalActionsForHandIndex(legalActions: LegalAction[], playerId: number, type: string, handIndex: number): LegalAction[] {
  return legalActions.filter((action) =>
    action.playerId === playerId &&
    action.type === type &&
    Number(action.payloadSchema?.handIndex) === handIndex)
}

function battlefieldName(game: GameState, battlefieldId: string): string {
  return game.battlefields.find((field) => field.id === battlefieldId)?.name ?? 'battlefield'
}

function createEmptyHandIntent(): HandCardIntent {
  return { actions: [], attachUnitIds: [], battlefieldIds: [], canUseBase: false }
}

function buildHandCardIntents(
  game: GameState | null,
  legalActions: LegalAction[],
  playerId: number,
): Record<number, HandCardIntent> {
  const player = game?.players.find((candidate) => candidate.id === playerId)
  if (!game || !player) return {}

  return Object.fromEntries(player.hand.map((_, handIndex) => {
    const intent = createEmptyHandIntent()
    const actionKeys = new Set<string>()
    const addAction = (choice: BoardActionChoice) => {
      if (actionKeys.has(choice.key)) return
      actionKeys.add(choice.key)
      intent.actions.push(choice)
    }

    for (const action of legalActionsForHandIndex(legalActions, playerId, 'play-unit', handIndex)) {
      const battlefieldId = stringPayload(action, 'battlefieldId')
      const isAccelerated = action.payloadSchema?.accelerate === true
      const payload = payloadFromServerAction(action)

      if (battlefieldId) {
        intent.battlefieldIds.push(battlefieldId)
      } else {
        intent.canUseBase = true
      }

      addAction({
        key: action.id,
        label: battlefieldId
          ? `${isAccelerated ? 'Accelerate: ' : ''}${battlefieldName(game, battlefieldId)}`
          : isAccelerated ? 'Accelerate to base' : 'Base',
        type: 'play-unit',
        payload,
      })
    }

    for (const action of legalActionsForHandIndex(legalActions, playerId, 'play-card', handIndex)) {
      addAction({
        key: action.id,
        label: action.label.replace(/^Play\s+/i, 'Play '),
        type: action.type,
        payload: payloadFromServerAction(action),
      })
    }

    for (const action of legalActionsForHandIndex(legalActions, playerId, 'hide-card', handIndex)) {
      for (const battlefieldId of stringArrayPayload(action, 'battlefieldIds')) {
        intent.battlefieldIds.push(battlefieldId)
        addAction({
          key: `${action.id}-${battlefieldId}`,
          label: `Hidden: ${battlefieldName(game, battlefieldId)}`,
          type: action.type,
          payload: payloadFromServerAction(action, { battlefieldId }),
        })
      }
    }

    for (const action of legalActionsForHandIndex(legalActions, playerId, 'attach-card', handIndex)) {
      const targetUnitId = stringPayload(action, 'targetUnitId')
      if (!targetUnitId) {
        continue
      }

      intent.attachUnitIds.push(targetUnitId)
      addAction({
        key: action.id,
        label: action.label.replace(/^Attach\s+/i, 'Attach '),
        type: 'attach-card',
        payload: payloadFromServerAction(action),
      })
    }

    intent.battlefieldIds = Array.from(new Set(intent.battlefieldIds))
    intent.attachUnitIds = Array.from(new Set(intent.attachUnitIds))
    return [handIndex, intent] as const
  }).filter(([, intent]) =>
    intent.actions.length > 0 ||
    intent.attachUnitIds.length > 0 ||
    intent.battlefieldIds.length > 0 ||
    intent.canUseBase)) as Record<number, HandCardIntent>
}

function buildUnitMoveIntents(legalActions: LegalAction[], playerId: number): Record<string, UnitMoveIntent> {
  const intents: Record<string, UnitMoveIntent> = {}

  for (const action of legalActions.filter((candidate) => candidate.playerId === playerId && candidate.type === 'move-unit')) {
    const unitId = stringPayload(action, 'unitId')
    const destinationId = stringPayload(action, 'battlefieldId')
    if (!unitId || !destinationId) {
      continue
    }

    const intent = intents[unitId] ?? { battlefieldIds: [], canMoveToBase: false }
    if (destinationId === 'base') {
      intent.canMoveToBase = true
    } else {
      intent.battlefieldIds.push(destinationId)
    }

    intents[unitId] = intent
  }

  return Object.fromEntries(Object.entries(intents)
    .map(([unitId, intent]) => [unitId, {
      battlefieldIds: Array.from(new Set(intent.battlefieldIds)),
      canMoveToBase: intent.canMoveToBase,
    }] as const)
    .filter(([, intent]) => intent.battlefieldIds.length > 0 || intent.canMoveToBase))
}

function isBoardHandledAction(action: LegalAction): boolean {
  return action.type === 'play-unit' ||
    action.type === 'move-unit' ||
    action.type === 'play-card' ||
    action.type === 'hide-card' ||
    action.type === 'attach-card' ||
    action.type === 'summon-champion' ||
    action.type === 'resolve-combat'
}

const payloadlessGenericActionTypes = new Set([
  'advance-phase',
  'concede',
  'end-turn',
  'pass-chain-window',
  'pass-focus',
])

function canSubmitGenericAction(action: LegalAction): boolean {
  return action.type === 'confirm-mulligan' ||
    action.payloadSchema !== undefined ||
    payloadlessGenericActionTypes.has(action.type)
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

type TargetKind = 'none' | 'unit' | 'lane'
type UnitTargetQualifier = 'any' | 'friendly' | 'enemy'

const TARGETABLE_EFFECT_TYPES: EffectType[] = ['damage', 'buff', 'rally', 'kill', 'banish', 'stun']

// Mirrors the server's target-legality shape closely enough to drive UI: which effect actually
// needs a target (the first target-requiring step for multi-step cards), and whether it's a
// single unit or a whole battlefield lane. "Up to"/"any number" cards are left as 'none' here --
// they already work with zero targets today, so this only adds prompting for cards that
// currently can't be played online at all without a target.
function effectTargetKind(card: Card | undefined): TargetKind {
  if (!card) return 'none'
  const text = card.text ?? ''
  if (/\bup to\b|\bany number\b/i.test(text)) return 'none'

  const effectType = card.effect.steps?.find((step) => TARGETABLE_EFFECT_TYPES.includes(step.type))?.type ?? card.effect.type
  if (!TARGETABLE_EFFECT_TYPES.includes(effectType)) return 'none'

  if (effectType === 'damage' && /\bunits\b.*\bbattlefields?\b/i.test(text)) {
    return 'lane'
  }

  return 'unit'
}

const TARGET_COUNT_WORDS: Record<string, number> = { two: 2, three: 3, four: 4 }

function parseUnitTargetQualifier(value: string | undefined): UnitTargetQualifier {
  const normalized = (value ?? '').trim().toLowerCase()
  if (normalized === 'friendly' || normalized === 'enemy') return normalized
  return 'any'
}

function requiredUnitTargetQualifiers(card: Card | undefined): UnitTargetQualifier[] {
  if (!card) return []
  const text = card.text ?? ''
  const pairMatch = /\ba\s+(friendly|enemy)?\s*unit\s+and\s+an?\s+(friendly|enemy)?\s*unit\b/i.exec(text)
  if (pairMatch) return [parseUnitTargetQualifier(pairMatch[1]), parseUnitTargetQualifier(pairMatch[2])]

  const match = /\b(two|three|four|\d+)\b\s+((friendly|enemy)\s+)?units\b/i.exec(text)
  if (!match) return []
  const word = match[1].toLowerCase()
  const count = TARGET_COUNT_WORDS[word] ?? (Number.parseInt(word, 10) || 1)
  const qualifier = parseUnitTargetQualifier(match[3])
  return Array.from({ length: count }, () => qualifier)
}

// Mirrors the server's RequiredTargetCount: cards like "Give two friendly units each +2 Might"
// need that many distinct unit targets picked before the action can be submitted.
function requiredTargetCount(card: Card | undefined): number {
  const qualifiers = requiredUnitTargetQualifiers(card)
  return qualifiers.length > 0 ? qualifiers.length : 1
}

function canSatisfyUnitTargetQualifiers(required: UnitTargetQualifier[], selected: Array<'friendly' | 'enemy'>): boolean {
  if (selected.length > required.length) return false
  const used = new Array(required.length).fill(false)

  const matches = (qualifier: UnitTargetQualifier, side: 'friendly' | 'enemy') => qualifier === 'any' || qualifier === side

  const assign = (selectedIndex: number): boolean => {
    if (selectedIndex >= selected.length) return true
    for (let requiredIndex = 0; requiredIndex < required.length; requiredIndex += 1) {
      if (used[requiredIndex] || !matches(required[requiredIndex], selected[selectedIndex])) continue
      used[requiredIndex] = true
      if (assign(selectedIndex + 1)) return true
      used[requiredIndex] = false
    }
    return false
  }

  return assign(0)
}

function unitTargetOwnerFilter(
  game: GameState | null,
  viewerPlayerId: number,
  selection: { kind: 'unit' | 'lane'; requiredCount: number; selectedUnitIds: string[]; targetQualifiers: UnitTargetQualifier[] } | null,
): number[] | undefined {
  if (!game || !selection || selection.kind !== 'unit' || selection.requiredCount <= 1 || selection.targetQualifiers.length !== selection.requiredCount) {
    return undefined
  }

  const allUnits = [...game.players.flatMap((player) => player.base), ...game.battlefields.flatMap((battlefield) => battlefield.units)]
  const selectedSides = selection.selectedUnitIds
    .map((unitId) => allUnits.find((unit) => unit.uid === unitId))
    .filter((unit): unit is Unit => Boolean(unit))
    .map((unit) => (unitOwnerId(unit) === viewerPlayerId ? 'friendly' : 'enemy'))

  if (!canSatisfyUnitTargetQualifiers(selection.targetQualifiers, selectedSides)) return undefined

  const canPickFriendly = canSatisfyUnitTargetQualifiers(selection.targetQualifiers, [...selectedSides, 'friendly'])
  const canPickEnemy = canSatisfyUnitTargetQualifiers(selection.targetQualifiers, [...selectedSides, 'enemy'])
  if (canPickFriendly && canPickEnemy) return undefined
  if (!canPickFriendly && !canPickEnemy) return undefined
  if (canPickFriendly) return [viewerPlayerId]
  return game.players.map((player) => player.id).filter((id) => id !== viewerPlayerId)
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

function DragActionModal({
  prompt,
  onCancel,
  onChoose,
}: {
  prompt: DragActionPrompt
  onCancel: () => void
  onChoose: (choice: DragActionChoice) => void
}) {
  return createPortal(
    <div className="combat-modal-backdrop" role="presentation">
      <section aria-modal="true" className="drag-action-modal" role="dialog">
        <header>
          <div>
            <span>Drag action</span>
            <h3>{prompt.cardName}</h3>
          </div>
          <button aria-label="Cancel drag action" className="combat-modal-close" type="button" onClick={onCancel}>X</button>
        </header>
        <div className="drag-action-options">
          {prompt.choices.map((choice) => (
            <button key={choice.key} type="button" onClick={() => onChoose(choice)}>
              {choice.label}
            </button>
          ))}
        </div>
        <footer>
          <button type="button" onClick={onCancel}>Cancel</button>
        </footer>
      </section>
    </div>,
    document.body,
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
  const [targetSelection, setTargetSelection] = useState<{ handIndex: number; cardName: string; kind: 'unit' | 'lane'; requiredCount: number; selectedUnitIds: string[]; targetQualifiers: UnitTargetQualifier[] } | null>(null)
  const [dragActionPrompt, setDragActionPrompt] = useState<DragActionPrompt | null>(null)
  const [events, setEvents] = useState<MatchEvent[]>([])
  const [battlefieldNames, setBattlefieldNames] = useState<Record<string, string>>({})
  const [status, setStatus] = useState('Create or join a lobby, or use quick queue for 1v1.')
  const connectionRef = useRef<HubConnection | null>(null)
  const playerIdRef = useRef(0)

  const selectedDeckId = deckId || decks[0]?.id || ''
  const selectedDeck = decks.find((deck) => deck.id === selectedDeckId) ?? null
  const currentLobbyPlayer = lobby?.players.find((player) => player.userId === session?.user.id) ?? null
  const occupiedBattlefieldIds = new Set(
    lobby?.players
      .filter((player) =>
        currentLobbyPlayer?.teamId !== null &&
        currentLobbyPlayer?.teamId !== undefined &&
        player.userId !== session?.user.id &&
        player.teamId === currentLobbyPlayer.teamId)
      .flatMap((player) => player.selectedBattlefieldIds)
      .filter(Boolean) ?? [],
  )
  const battlefieldOptions = selectedDeck
    ? selectedDeck.battlefieldDeckIds
      .filter((id) => !occupiedBattlefieldIds.has(id) || currentLobbyPlayer?.selectedBattlefieldIds.includes(id))
      .map((id) => ({
        id,
        name: cards.find((card) => card.id === id)?.name ?? battlefieldNames[id] ?? 'Loading battlefield...',
      }))
    : []
  const effectiveSelectedBattlefieldId = battlefieldOptions.some((battlefield) => battlefield.id === selectedBattlefieldId)
    ? selectedBattlefieldId
    : battlefieldOptions[0]?.id ?? ''
  const playerId = useMemo(
    () => match?.players.find((player) => player.userId === session?.user.id)?.playerId ?? 0,
    [match, session?.user.id],
  )
  const isMulliganTurn =
    state?.stage === 'mulligan' && !(state.mulliganConfirmedPlayerIds ?? []).includes(playerId)
  const isHost = lobby?.hostUserId === session?.user.id
  const isAdmin = session?.user.isAdmin === true
  const canReady = Boolean(lobby && selectedDeck && effectiveSelectedBattlefieldId)
  const playableCardHandIndexes = serverApprovedHandIndexes(legalActions, playerId, 'play-card')
  const hideableCardHandIndexes = serverApprovedHandIndexes(legalActions, playerId, 'hide-card')
  const canPlayUnit = hasServerApprovedAction(legalActions, playerId, 'play-unit')
  const canAttachCard = hasServerApprovedAction(legalActions, playerId, 'attach-card')
  const canMoveUnit = hasServerApprovedAction(legalActions, playerId, 'move-unit')
  const canSummonChampion = hasServerApprovedAction(legalActions, playerId, 'summon-champion')
  const canResolveCombat = Boolean(state?.activeCombat && hasServerApprovedAction(legalActions, playerId, 'resolve-combat'))
  const handCardIntents = useMemo(
    () => buildHandCardIntents(state, legalActions, playerId),
    [legalActions, playerId, state],
  )
  const unitMoveIntents = useMemo(
    () => buildUnitMoveIntents(legalActions, playerId),
    [legalActions, playerId],
  )
  const visibleActionButtons = legalActions.filter((action) =>
    action.playerId === playerId &&
    !isBoardHandledAction(action) &&
    canSubmitGenericAction(action) &&
    !(canResolveCombat && action.type === 'resolve-combat'))

  useEffect(() => {
    playerIdRef.current = playerId
  }, [playerId])

  const loadLobbies = useCallback(
    async ({ silent = false }: { silent?: boolean } = {}) => {
      try {
        setLobbies(await lobbiesApi.listLobbies())
      } catch (error) {
        if (!silent) {
          setStatus(error instanceof Error ? error.message : 'Unable to load lobbies.')
        }
      }
    },
    [lobbiesApi],
  )

  useEffect(() => {
    if (!session || view !== 'lobbies' || match) return

    let cancelled = false
    const refresh = async (silent = true) => {
      if (cancelled) return
      await loadLobbies({ silent })
    }

    void refresh(false)
    const intervalId = window.setInterval(() => {
      void refresh(true)
    }, 5000)

    return () => {
      cancelled = true
      window.clearInterval(intervalId)
    }
  }, [loadLobbies, match, session, view])

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
        payload: action.type === 'confirm-mulligan' ? { handIndexes: mulliganHandIndexes } : payloadFromServerAction(action),
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
    const action = findServerApprovedAction(legalActions, playerId, type, payload)
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
    const action = findServerApprovedAction(legalActions, playerId, 'play-unit', battlefieldId ? { handIndex, battlefieldId } : { handIndex })
    if (!action) return
    await submitTypedAction('play-unit', payloadFromServerAction(action), 'Unable to play unit.')
  }

  async function moveUnit(unitId: string, battlefieldId: string) {
    const action = findServerApprovedAction(legalActions, playerId, 'move-unit', { unitId, battlefieldId })
    if (!action) return
    await submitTypedAction('move-unit', payloadFromServerAction(action), 'Unable to move unit.')
  }

  async function playCard(handIndex: number, targetUnitId?: string, targetLaneId?: string, targetUnitIds?: string[]) {
    const payload: Record<string, unknown> = { handIndex }
    if (targetUnitId) payload.targetUnitId = targetUnitId
    if (targetLaneId) payload.targetLaneId = targetLaneId
    if (targetUnitIds && targetUnitIds.length > 0) payload.targetUnitIds = targetUnitIds
    await submitTypedAction('play-card', payload, 'Unable to play card.')
  }

  function requestPlayCard(handIndex: number) {
    const card = state?.players.find((player) => player.id === playerId)?.hand[handIndex]
    const kind = effectTargetKind(card)
    if (kind === 'none') {
      void playCard(handIndex)
      return
    }

    setTargetSelection({
      handIndex,
      cardName: card?.name ?? 'this card',
      kind,
      requiredCount: requiredTargetCount(card),
      selectedUnitIds: [],
      targetQualifiers: requiredUnitTargetQualifiers(card),
    })
  }

  async function chooseTarget(targetUnitId?: string, targetLaneId?: string) {
    if (!targetSelection) return

    if (targetLaneId) {
      await playCard(targetSelection.handIndex, undefined, targetLaneId)
      setTargetSelection(null)
      return
    }

    if (!targetUnitId) return
    const allowedOwnerIds = unitTargetOwnerFilter(state, playerId, targetSelection)
    if (allowedOwnerIds) {
      const allUnits = state
        ? [...state.players.flatMap((player) => player.base), ...state.battlefields.flatMap((battlefield) => battlefield.units)]
        : []
      const selectedUnit = allUnits.find((unit) => unit.uid === targetUnitId)
      if (!selectedUnit || !allowedOwnerIds.includes(unitOwnerId(selectedUnit))) return
    }

    const selectedUnitIds = targetSelection.selectedUnitIds.includes(targetUnitId)
      ? targetSelection.selectedUnitIds
      : [...targetSelection.selectedUnitIds, targetUnitId]

    if (selectedUnitIds.length < targetSelection.requiredCount) {
      setTargetSelection({ ...targetSelection, selectedUnitIds })
      return
    }

    if (selectedUnitIds.length === 1) {
      await playCard(targetSelection.handIndex, selectedUnitIds[0])
    } else {
      await playCard(targetSelection.handIndex, undefined, undefined, selectedUnitIds)
    }
    setTargetSelection(null)
  }

  function cancelTargetSelection() {
    setTargetSelection(null)
  }

  async function attachCard(handIndex: number, targetUnitId: string) {
    const action = findServerApprovedAction(legalActions, playerId, 'attach-card', { handIndex, targetUnitId })
    if (!action) return
    await submitTypedAction('attach-card', payloadFromServerAction(action), 'Unable to attach card.')
  }

  async function summonChampion() {
    await submitTypedAction('summon-champion', {}, 'Unable to summon champion.')
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

  function queueOrSubmitDragChoices(cardName: string, choices: DragActionChoice[]) {
    if (choices.length === 0) return
    if (choices.length === 1) {
      const [choice] = choices
      void submitTypedAction(choice.type, choice.payload, `Unable to ${choice.label.toLowerCase()}.`)
      return
    }

    setDragActionPrompt({ cardName, choices })
  }

  function cardNameAt(handIndex: number): string {
    return state?.players.find((player) => player.id === playerId)?.hand[handIndex]?.name ?? 'Card'
  }

  function dropCardOnBattlefield(handIndex: number, battlefieldId: string) {
    const choices: DragActionChoice[] = []
    const playUnitAction = findServerApprovedAction(legalActions, playerId, 'play-unit', { handIndex, battlefieldId })
    if (playUnitAction) {
      choices.push({ key: playUnitAction.id, label: playUnitAction.label, type: 'play-unit', payload: payloadFromServerAction(playUnitAction) })
    }

    const playCardAction = findServerApprovedAction(legalActions, playerId, 'play-card', { handIndex })
    if (playCardAction) {
      choices.push({ key: playCardAction.id, label: playCardAction.label, type: 'play-card', payload: payloadFromServerAction(playCardAction) })
    }

    const hideCardAction = findServerApprovedAction(legalActions, playerId, 'hide-card', { handIndex, battlefieldId })
    if (hideCardAction) {
      choices.push({ key: `${hideCardAction.id}-${battlefieldId}`, label: 'Play hidden', type: 'hide-card', payload: payloadFromServerAction(hideCardAction, { battlefieldId }) })
    }

    queueOrSubmitDragChoices(cardNameAt(handIndex), choices)
  }

  function dropCardOnBase(handIndex: number) {
    const choices: DragActionChoice[] = []
    const playUnitAction = findServerApprovedAction(legalActions, playerId, 'play-unit', { handIndex })
    if (playUnitAction) {
      choices.push({ key: playUnitAction.id, label: playUnitAction.label, type: 'play-unit', payload: payloadFromServerAction(playUnitAction) })
    }

    const playCardAction = findServerApprovedAction(legalActions, playerId, 'play-card', { handIndex })
    if (playCardAction) {
      choices.push({ key: playCardAction.id, label: playCardAction.label, type: 'play-card', payload: payloadFromServerAction(playCardAction) })
    }

    queueOrSubmitDragChoices(cardNameAt(handIndex), choices)
  }

  function dropCardOnUnit(handIndex: number, targetUnitId: string) {
    const choices: DragActionChoice[] = []
    const attachAction = findServerApprovedAction(legalActions, playerId, 'attach-card', { handIndex, targetUnitId })
    if (attachAction) {
      choices.push({ key: attachAction.id, label: attachAction.label, type: 'attach-card', payload: payloadFromServerAction(attachAction) })
    }

    const playCardAction = findServerApprovedAction(legalActions, playerId, 'play-card', { handIndex })
    if (playCardAction) {
      choices.push({ key: playCardAction.id, label: playCardAction.label, type: 'play-card', payload: payloadFromServerAction(playCardAction) })
    }

    queueOrSubmitDragChoices(cardNameAt(handIndex), choices)
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
                    setSelectedBattlefieldId(nextDeck?.battlefieldDeckIds.find((id) => !occupiedBattlefieldIds.has(id) || currentLobbyPlayer?.selectedBattlefieldIds.includes(id)) ?? '')
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
              canPlayUnit={canPlayUnit}
              onPlayUnit={playUnit}
              onDropCardOnBase={dropCardOnBase}
              onDropCardOnBattlefield={dropCardOnBattlefield}
              onDropCardOnUnit={dropCardOnUnit}
              playableCardHandIndexes={playableCardHandIndexes}
              hideableCardHandIndexes={hideableCardHandIndexes}
              onPlayCard={requestPlayCard}
              canAttachCard={canAttachCard}
              handCardIntents={handCardIntents}
              unitMoveIntents={unitMoveIntents}
              onChooseHandAction={(choice) => {
                void submitTypedAction(choice.type, choice.payload, `Unable to ${choice.label.toLowerCase()}.`)
              }}
              onAttachCard={attachCard}
              canMoveUnit={canMoveUnit}
              onMoveUnit={moveUnit}
              canSummonChampion={canSummonChampion}
              onSummonChampion={summonChampion}
              mulliganSelection={
                isMulliganTurn ? { selectedIndexes: mulliganHandIndexes, onToggle: toggleMulliganHandIndex } : undefined
              }
              targetSelection={targetSelection ? {
                kind: targetSelection.kind,
                excludeUnitIds: targetSelection.selectedUnitIds,
                allowedOwnerIds: unitTargetOwnerFilter(state, playerId, targetSelection),
              } : undefined}
              onSelectUnitTarget={(unitId) => void chooseTarget(unitId, undefined)}
              onSelectLaneTarget={(laneId) => void chooseTarget(undefined, laneId)}
            />
          </section>

          <aside className="online-actions">
            <h3>Actions</h3>
            {targetSelection && (
              <div className="online-target-prompt" role="status">
                <p>
                  Choose {targetSelection.kind === 'lane' ? 'a battlefield' : 'a unit'} target for <strong>{targetSelection.cardName}</strong>
                  {targetSelection.requiredCount > 1 && ` (${targetSelection.selectedUnitIds.length}/${targetSelection.requiredCount} chosen)`}.
                </p>
                <button type="button" onClick={cancelTargetSelection}>Cancel</button>
              </div>
            )}
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
            {dragActionPrompt && (
              <DragActionModal
                prompt={dragActionPrompt}
                onCancel={() => setDragActionPrompt(null)}
                onChoose={(choice) => {
                  setDragActionPrompt(null)
                  void submitTypedAction(choice.type, choice.payload, `Unable to ${choice.label.toLowerCase()}.`)
                }}
              />
            )}
            {legalActions.length === 0 && <p>No legal actions for your seat right now.</p>}
            {visibleActionButtons.map((action) => (
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
