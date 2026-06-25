import { useMemo, useState } from 'react'
import type { Card, GameState, Gear, Player, Unit } from '../../shared/models'
import { OnlinePlaymat, type BoardActionChoice, type HandCardIntent, type UnitMoveIntent } from '../online/OnlinePlaymat'
import './VisualStateLab.css'

type ScenarioId = 'spell-actions' | 'gear-actions' | 'crowded-board' | 'combat-window'

type VisualScenario = {
  id: ScenarioId
  label: string
  description: string
  game: GameState
  canAttachCard?: boolean
  canMoveUnit?: boolean
  canPlayUnit?: boolean
  canSummonChampion?: boolean
  handCardIntents: Record<number, HandCardIntent>
  hideableCardHandIndexes?: number[]
  playableCardHandIndexes?: number[]
  unitMoveIntents?: Record<string, UnitMoveIntent>
}

const scenarioOptions: Array<{ id: ScenarioId; label: string }> = [
  { id: 'spell-actions', label: 'Spell action popup' },
  { id: 'gear-actions', label: 'Gear attachment popup' },
  { id: 'crowded-board', label: 'Crowded board' },
  { id: 'combat-window', label: 'Combat window' },
]

function makeCard(
  id: string,
  name: string,
  kind: Card['kind'],
  image: string,
  overrides: Partial<Card> = {},
): Card {
  return {
    id,
    name,
    kind,
    tags: [],
    domain: 'Fury',
    domains: ['Fury'],
    cost: 1,
    might: kind === 'unit' || kind === 'champion' ? 2 : 0,
    text: '',
    image,
    cardType: kind.charAt(0).toUpperCase() + kind.slice(1),
    supertype: null,
    effect: { type: 'rally', amount: 0 },
    ...overrides,
  }
}

const labCards: Card[] = [
  makeCard('dev-battlefield-bridge', 'Test Bridge', 'battlefield', 'BR', { cost: 0, might: 0 }),
  makeCard('dev-battlefield-vault', 'Signal Vault', 'battlefield', 'SV', { cost: 0, might: 0 }),
  makeCard('dev-battlefield-yard', 'Glass Yard', 'battlefield', 'GY', { cost: 0, might: 0 }),
  makeCard('dev-legend', 'Dev Legend', 'legend', 'LG', { cost: 0, might: 0, text: 'Visual lab legend.' }),
  makeCard('dev-champion', 'Dev Champion', 'champion', 'CH', { cost: 3, might: 4, text: 'Summon test champion.' }),
  makeCard('dev-unit-vanguard', 'Ember Vanguard', 'unit', 'EV', { cost: 2, might: 3 }),
  makeCard('dev-unit-scout', 'Signal Scout', 'unit', 'SS', { domain: 'Mind', domains: ['Mind'], cost: 1, might: 2 }),
  makeCard('dev-unit-guard', 'Stone Guard', 'unit', 'SG', { domain: 'Body', domains: ['Body'], cost: 3, might: 4 }),
  makeCard('dev-enemy-unit', 'Mirror Raider', 'unit', 'MR', { domain: 'Chaos', domains: ['Chaos'], cost: 2, might: 3 }),
  makeCard('dev-spell-bolt', 'Arc Bolt', 'spell', 'AB', { cost: 2, text: 'Choose a test spell mode.' }),
  makeCard('dev-spell-shield', 'Pulse Shield', 'spell', 'PS', { domain: 'Order', domains: ['Order'], cost: 1 }),
  makeCard('dev-gear-blade', 'Anchor Blade', 'gear', 'GB', { domain: 'Body', domains: ['Body'], cost: 1 }),
  makeCard('dev-gear-lens', 'Focus Lens', 'gear', 'FL', { domain: 'Mind', domains: ['Mind'], cost: 2 }),
  makeCard('dev-rune-fury', 'Fury Rune', 'rune', 'FR', { cost: 0, might: 0 }),
  makeCard('dev-rune-calm', 'Calm Rune', 'rune', 'CR', { domain: 'Calm', domains: ['Calm'], cost: 0, might: 0 }),
]

function catalogCard(cardId: string): Card {
  const card = labCards.find((candidate) => candidate.id === cardId)
  if (!card) throw new Error(`Unknown visual lab card: ${cardId}`)
  return card
}

function cardInstance(cardId: string, id: string): Card {
  return {
    ...catalogCard(cardId),
    id,
    catalogId: cardId,
  }
}

function unit(
  cardId: string,
  uid: string,
  owner: number,
  location: Unit['location'],
  overrides: Partial<Unit> = {},
): Unit {
  return {
    ...cardInstance(cardId, uid),
    uid,
    owner,
    location,
    exhausted: false,
    damage: 0,
    attachedMight: 0,
    attachedCards: [],
    statusEffects: [],
    ...overrides,
  }
}

function gear(
  cardId: string,
  uid: string,
  ownerId: number,
  location: Gear['location'],
  attachedUnitId: string | null,
): Gear {
  return {
    ...cardInstance(cardId, uid),
    uid,
    ownerId,
    location,
    exhausted: false,
    attachedUnitId,
  }
}

function player(id: number, name: string, overrides: Partial<Player> = {}): Player {
  return {
    id,
    name,
    points: id === 0 ? 3 : 2,
    xp: id === 0 ? 4 : 3,
    runes: {
      ready: [cardInstance('dev-rune-fury', `p${id}-ready-rune-1`), cardInstance('dev-rune-calm', `p${id}-ready-rune-2`)],
      exhausted: [cardInstance('dev-rune-fury', `p${id}-exhausted-rune-1`)],
    },
    runeDeck: [cardInstance('dev-rune-calm', `p${id}-rune-deck-1`)],
    runePool: { energy: 2 },
    deck: [cardInstance('dev-unit-vanguard', `p${id}-deck-1`), cardInstance('dev-spell-bolt', `p${id}-deck-2`)],
    hand: [],
    trash: [],
    base: [],
    baseGear: [],
    champion: cardInstance('dev-champion', `p${id}-champion`),
    legend: cardInstance('dev-legend', `p${id}-legend`),
    championSummoned: false,
    battlefieldId: id === 0 ? 'bridge' : 'vault',
    ...overrides,
  }
}

function baseGame(players: Player[]): GameState {
  return {
    id: 'dev-visual-state',
    mode: 'duel-1v1',
    victoryScore: 8,
    players,
    battlefields: [
      {
        id: 'bridge',
        catalogId: 'dev-battlefield-bridge',
        name: 'Test Bridge',
        claim: 2,
        chosenBy: 0,
        controllerId: 0,
        units: [],
      },
      {
        id: 'vault',
        catalogId: 'dev-battlefield-vault',
        name: 'Signal Vault',
        claim: 3,
        chosenBy: 1,
        controllerId: 1,
        units: [],
      },
    ],
    stage: 'playing',
    turnPhase: 'main',
    turnNumber: 4,
    firstPlayerId: 0,
    turnPlayerId: 0,
    activePlayer: 0,
    priorityPlayerId: 0,
    focusPlayerId: 0,
    winner: null,
    winningTeamId: null,
    turnOrder: [0, 1],
    teamIds: [0, 1],
    hasPassedFocusByPlayer: { 0: false, 1: false },
    scoredBattlefieldIdsThisTurn: { 0: [], 1: [] },
    firstTurnCompletedByPlayer: { 0: true, 1: true },
    mulliganPlayerIndex: 0,
    mulliganConfirmedPlayerIds: [0, 1],
    activeShowdown: null,
    activeCombat: null,
    selectedCard: null,
    selectedUnit: null,
    nextUid: 100,
    nextLogId: 1,
    log: [{ id: 1, text: 'Visual lab state loaded.' }],
    passShield: false,
    effectStack: [],
    chainWindow: null,
  }
}

function choice(key: string, label: string, type: string, payload: Record<string, unknown>): BoardActionChoice {
  return { key, label, type, payload }
}

function emptyIntent(actions: BoardActionChoice[] = []): HandCardIntent {
  return {
    actions,
    attachUnitIds: [],
    battlefieldIds: [],
    canUseBase: false,
  }
}

function createScenario(id: ScenarioId): VisualScenario {
  const viewerBaseUnit = unit('dev-unit-vanguard', 'viewer-base-vanguard', 0, { type: 'base' }, {
    attachedCards: [cardInstance('dev-gear-blade', 'viewer-attached-blade')],
    attachedMight: 1,
  })
  const viewerScout = unit('dev-unit-scout', 'viewer-scout', 0, { type: 'battlefield', battlefieldId: 'bridge' })
  const enemyRaider = unit('dev-enemy-unit', 'enemy-raider', 1, { type: 'battlefield', battlefieldId: 'bridge' }, { damage: 1 })
  const opponentBaseUnit = unit('dev-unit-guard', 'enemy-base-guard', 1, { type: 'base' })
  const game = baseGame([
    player(0, 'Visual Tester', {
      hand: [
        cardInstance('dev-spell-bolt', 'hand-spell-bolt'),
        cardInstance('dev-gear-lens', 'hand-gear-lens'),
        cardInstance('dev-unit-scout', 'hand-unit-scout'),
        cardInstance('dev-spell-shield', 'hand-spell-shield'),
      ],
      base: [viewerBaseUnit],
      baseGear: [gear('dev-gear-blade', 'viewer-base-gear', 0, { type: 'base' }, null)],
    }),
    player(1, 'Mirror Seat', {
      base: [opponentBaseUnit],
      championSummoned: true,
      champion: null,
    }),
  ])

  game.battlefields[0].units = [enemyRaider, viewerScout]
  game.battlefields[1].units = [
    unit('dev-unit-guard', 'viewer-vault-guard', 0, { type: 'battlefield', battlefieldId: 'vault' }, { exhausted: true }),
    unit('dev-enemy-unit', 'enemy-vault-raider', 1, { type: 'battlefield', battlefieldId: 'vault' }),
  ]

  const spellIntent = emptyIntent([
    choice('dev-play-spell-damage', 'Cast Arc Bolt: damage enemy unit', 'play-card', { handIndex: 0, mode: 'damage', targetUnitId: 'enemy-raider' }),
    choice('dev-play-spell-score', 'Cast Arc Bolt: score pressure at Test Bridge', 'play-card', { handIndex: 0, mode: 'score', battlefieldId: 'bridge' }),
    choice('dev-play-spell-chain', 'Cast Arc Bolt: add to chain', 'play-card', { handIndex: 0, mode: 'chain' }),
  ])
  const gearIntent = emptyIntent([
    choice('dev-attach-lens-base', 'Attach Focus Lens to Ember Vanguard', 'attach-card', { handIndex: 1, targetUnitId: 'viewer-base-vanguard' }),
    choice('dev-attach-lens-scout', 'Attach Focus Lens to Signal Scout', 'attach-card', { handIndex: 1, targetUnitId: 'viewer-scout' }),
  ])
  gearIntent.attachUnitIds = ['viewer-base-vanguard', 'viewer-scout']

  const unitIntent = emptyIntent([
    choice('dev-play-scout-base', 'Play Signal Scout to base', 'play-unit', { handIndex: 2 }),
    choice('dev-play-scout-bridge', 'Play Signal Scout to Test Bridge', 'play-unit', { handIndex: 2, battlefieldId: 'bridge' }),
  ])
  unitIntent.canUseBase = true
  unitIntent.battlefieldIds = ['bridge', 'vault']

  const shieldIntent = emptyIntent([
    choice('dev-shield-base', 'Cast Pulse Shield on Ember Vanguard', 'play-card', { handIndex: 3, targetUnitId: 'viewer-base-vanguard' }),
  ])

  if (id === 'gear-actions') {
    return {
      id,
      label: 'Gear attachment popup',
      description: 'Click or drag Focus Lens to test multi-target gear action menus and attachment drop highlights.',
      game,
      canAttachCard: true,
      canMoveUnit: true,
      canPlayUnit: true,
      handCardIntents: {
        0: spellIntent,
        1: gearIntent,
        2: unitIntent,
        3: shieldIntent,
      },
      playableCardHandIndexes: [0, 3],
      unitMoveIntents: {
        'viewer-base-vanguard': { battlefieldIds: ['bridge', 'vault'], canMoveToBase: false },
        'viewer-scout': { battlefieldIds: ['vault'], canMoveToBase: true },
      },
    }
  }

  if (id === 'crowded-board') {
    game.players[0].hand = [
      cardInstance('dev-spell-bolt', 'crowded-hand-spell'),
      cardInstance('dev-gear-lens', 'crowded-hand-gear'),
    ]
    game.players[0].base = [
      viewerBaseUnit,
      unit('dev-unit-scout', 'crowded-base-scout-a', 0, { type: 'base' }, { damage: 1 }),
      unit('dev-unit-guard', 'crowded-base-guard-a', 0, { type: 'base' }),
      unit('dev-unit-scout', 'crowded-base-scout-b', 0, { type: 'base' }, { exhausted: true }),
    ]
    game.players[1].base = [
      opponentBaseUnit,
      unit('dev-enemy-unit', 'crowded-enemy-base-a', 1, { type: 'base' }),
      unit('dev-enemy-unit', 'crowded-enemy-base-b', 1, { type: 'base' }, { damage: 2 }),
    ]
    game.battlefields = [
      ...game.battlefields,
      {
        id: 'yard',
        catalogId: 'dev-battlefield-yard',
        name: 'Glass Yard',
        claim: 2,
        chosenBy: 0,
        controllerId: null,
        units: [],
      },
    ]
    game.battlefields[0].units = [
      ...game.battlefields[0].units,
      unit('dev-unit-vanguard', 'crowded-bridge-vanguard', 0, { type: 'battlefield', battlefieldId: 'bridge' }),
      unit('dev-enemy-unit', 'crowded-bridge-raider', 1, { type: 'battlefield', battlefieldId: 'bridge' }),
    ]
    game.battlefields[2].units = [
      unit('dev-unit-scout', 'crowded-yard-scout', 0, { type: 'battlefield', battlefieldId: 'yard' }),
      unit('dev-enemy-unit', 'crowded-yard-raider', 1, { type: 'battlefield', battlefieldId: 'yard' }),
    ]

    return {
      id,
      label: 'Crowded board',
      description: 'Stress state for card spacing, status badges, gear icons, and horizontal overflow.',
      game,
      canAttachCard: true,
      canMoveUnit: true,
      handCardIntents: {
        0: spellIntent,
        1: gearIntent,
      },
      playableCardHandIndexes: [0],
      unitMoveIntents: {
        'crowded-base-scout-a': { battlefieldIds: ['bridge', 'vault', 'yard'], canMoveToBase: false },
        'viewer-scout': { battlefieldIds: ['vault', 'yard'], canMoveToBase: true },
      },
    }
  }

  if (id === 'combat-window') {
    game.activeShowdown = { battlefieldId: 'bridge', kind: 'combat' }
    game.activeCombat = {
      battlefieldId: 'bridge',
      attackerPlayerId: 0,
      defenderPlayerId: 1,
      damageStep: true,
    }
    game.effectStack = [
      {
        id: 'dev-stack-bolt',
        cardId: 'dev-spell-bolt',
        cardName: 'Arc Bolt',
        playerId: 0,
        effect: { type: 'damage', amount: 2 },
        targetUnitId: 'enemy-raider',
      },
    ]
    game.chainWindow = {
      priorityPlayerId: 1,
      startedByPlayerId: 0,
      passedByPlayer: { 0: true, 1: false },
    }

    return {
      id,
      label: 'Combat window',
      description: 'State with active combat, chain data, damaged units, and context actions still available.',
      game,
      canAttachCard: true,
      canMoveUnit: true,
      handCardIntents: {
        0: spellIntent,
        1: gearIntent,
        3: shieldIntent,
      },
      playableCardHandIndexes: [0, 3],
      unitMoveIntents: {
        'viewer-scout': { battlefieldIds: ['vault'], canMoveToBase: true },
      },
    }
  }

  return {
    id,
    label: 'Spell action popup',
    description: 'Click Arc Bolt to test the context action popup for spell modes and chain options.',
    game,
    canAttachCard: true,
    canMoveUnit: true,
    canPlayUnit: true,
    canSummonChampion: true,
    handCardIntents: {
      0: spellIntent,
      1: gearIntent,
      2: unitIntent,
      3: shieldIntent,
    },
    playableCardHandIndexes: [0, 3],
    unitMoveIntents: {
      'viewer-base-vanguard': { battlefieldIds: ['bridge', 'vault'], canMoveToBase: false },
      'viewer-scout': { battlefieldIds: ['vault'], canMoveToBase: true },
    },
  }
}

function stringifyGame(game: GameState): string {
  return JSON.stringify(game, null, 2)
}

export function VisualStateLab({ cards }: { cards: Card[] }) {
  const [scenarioId, setScenarioId] = useState<ScenarioId>('spell-actions')
  const [scenario, setScenario] = useState(() => createScenario('spell-actions'))
  const [gameDraft, setGameDraft] = useState(() => stringifyGame(scenario.game))
  const [viewerPlayerId, setViewerPlayerId] = useState(0)
  const [status, setStatus] = useState('Synthetic visual state loaded.')
  const [lastAction, setLastAction] = useState('No action selected yet.')

  const cardPool = useMemo(() => {
    const existingIds = new Set(cards.map((card) => card.id))
    return [...cards, ...labCards.filter((card) => !existingIds.has(card.id))]
  }, [cards])

  function loadScenario(nextId: ScenarioId) {
    const nextScenario = createScenario(nextId)
    setScenarioId(nextId)
    setScenario(nextScenario)
    setGameDraft(stringifyGame(nextScenario.game))
    setViewerPlayerId(nextScenario.game.players[0]?.id ?? 0)
    setStatus('Synthetic visual state loaded.')
    setLastAction('No action selected yet.')
  }

  function applyJsonState() {
    try {
      const parsed = JSON.parse(gameDraft) as GameState
      if (!parsed || !Array.isArray(parsed.players) || !Array.isArray(parsed.battlefields)) {
        setStatus('JSON must include players and battlefields arrays.')
        return
      }

      setScenario((current) => ({ ...current, game: parsed }))
      setViewerPlayerId(parsed.players.some((player) => player.id === viewerPlayerId) ? viewerPlayerId : parsed.players[0]?.id ?? 0)
      setStatus('Custom JSON state applied to the playmat.')
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to parse JSON.')
    }
  }

  function logChoice(choiceLabel: string) {
    setLastAction(choiceLabel)
    setStatus('Action was selected locally in the visual lab.')
  }

  return (
    <section className="visual-state-lab">
      <header className="visual-lab-header">
        <div>
          <p className="eyebrow">dev only</p>
          <h2>Visual state lab</h2>
          <p>{scenario.description}</p>
        </div>
        <div className="visual-lab-status">
          <span>{status}</span>
          <strong>{lastAction}</strong>
        </div>
      </header>

      <section className="visual-lab-controls" aria-label="Visual lab controls">
        <label>
          Scenario
          <select value={scenarioId} onChange={(event) => loadScenario(event.target.value as ScenarioId)}>
            {scenarioOptions.map((option) => <option key={option.id} value={option.id}>{option.label}</option>)}
          </select>
        </label>
        <label>
          Viewer
          <select value={viewerPlayerId} onChange={(event) => setViewerPlayerId(Number(event.target.value))}>
            {scenario.game.players.map((playerItem) => (
              <option key={playerItem.id} value={playerItem.id}>{playerItem.name}</option>
            ))}
          </select>
        </label>
        <button type="button" onClick={applyJsonState}>Apply JSON state</button>
        <button type="button" onClick={() => loadScenario(scenarioId)}>Reset scenario</button>
      </section>

      <section className="visual-lab-workspace">
        <div className="visual-lab-playmat">
          <OnlinePlaymat
            cards={cardPool}
            game={scenario.game}
            viewerPlayerId={viewerPlayerId}
            canAttachCard={scenario.canAttachCard}
            canMoveUnit={scenario.canMoveUnit}
            canPlayUnit={scenario.canPlayUnit}
            canSummonChampion={scenario.canSummonChampion}
            handCardIntents={scenario.handCardIntents}
            hideableCardHandIndexes={scenario.hideableCardHandIndexes}
            playableCardHandIndexes={scenario.playableCardHandIndexes}
            unitMoveIntents={scenario.unitMoveIntents}
            onAttachCard={(handIndex, targetUnitId) => logChoice(`Attach hand ${handIndex} to ${targetUnitId}`)}
            onChooseHandAction={(choice) => logChoice(choice.label)}
            onDropCardOnBase={(handIndex) => logChoice(`Drop hand ${handIndex} on base`)}
            onDropCardOnBattlefield={(handIndex, battlefieldId) => logChoice(`Drop hand ${handIndex} on ${battlefieldId}`)}
            onDropCardOnUnit={(handIndex, targetUnitId) => logChoice(`Drop hand ${handIndex} on ${targetUnitId}`)}
            onMoveUnit={(unitId, battlefieldId) => logChoice(`Move ${unitId} to ${battlefieldId}`)}
            onPlayCard={(handIndex) => logChoice(`Play hand ${handIndex}`)}
            onPlayUnit={(handIndex, battlefieldId) => logChoice(`Play hand ${handIndex}${battlefieldId ? ` to ${battlefieldId}` : ' to base'}`)}
            onSummonChampion={() => logChoice('Summon champion')}
          />
        </div>

        <aside className="visual-lab-json-panel">
          <header>
            <h3>Game state JSON</h3>
            <span>Local only</span>
          </header>
          <textarea
            spellCheck={false}
            value={gameDraft}
            onChange={(event) => setGameDraft(event.target.value)}
          />
        </aside>
      </section>
    </section>
  )
}
