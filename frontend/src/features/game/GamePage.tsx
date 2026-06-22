import { useState, type Dispatch, type SetStateAction } from 'react'
import { CardFace } from '../../shared/ui/CardFace'
import { DeckStack } from '../../shared/ui/DeckStack'
import { RunePool } from '../../shared/ui/RunePool'
import { advancePhase, applyManualAction, confirmMulligan, handleDrop, openNextStagedConflict, passFocus, resolveCombat, resolveShowdown } from './rules/gameRules'
import type { Card, GameDeckAssignment, GameState, Player, SavedDeck, SetupState, UserProfile } from '../../shared/models'
import { readDragData, useDragData } from '../../shared/dragDrop'
import { BattlefieldLane } from './BattlefieldLane'
import { GameHero } from './GameHero'
import { GameSetupPanel } from './GameSetupPanel'
import { PassShield } from './PassShield'
import { PlayerHand } from './PlayerHand'
import { UnitButton } from './UnitButton'

type BattlefieldOption = {
  id: string
  name: string
  claim: number
}

function MulliganPanel({ game, setGame }: { game: GameState; setGame: Dispatch<SetStateAction<GameState>> }) {
  const playerId = game.turnOrder[game.mulliganPlayerIndex] ?? 0
  const player = game.players[playerId]
  const [selected, setSelected] = useState<number[]>([])
  if (!player) return null
  return (
    <section className="table mulligan-panel">
      <p className="eyebrow">mulligan</p>
      <h2>{player.name}</h2>
      <p>Select up to 2 cards to replace, then pass to the next player.</p>
      <div className="hand">
        {player.hand.map((card, index) => (
          <button
            className={selected.includes(index) ? `card ${card.domain.toLowerCase()} selected` : `card ${card.domain.toLowerCase()}`}
            key={`${card.id}-${index}`}
            type="button"
            onClick={() => setSelected((current) => current.includes(index) ? current.filter((item) => item !== index) : current.length < 2 ? [...current, index] : current)}
          >
            <CardFace card={card} />
          </button>
        ))}
      </div>
      <button
        type="button"
        onClick={() => {
          setGame((state) => confirmMulligan(state, player.id, selected))
          setSelected([])
        }}
      >
        Confirm mulligan
      </button>
    </section>
  )
}

function ManualTools({ game, setGame }: { game: GameState; setGame: Dispatch<SetStateAction<GameState>> }) {
  const selectedUnitId = game.selectedUnit?.unitId
  return (
    <section className="manual-tools">
      <p className="eyebrow">manual rules tools</p>
      <div className="button-row">
        <button type="button" onClick={() => setGame((state) => applyManualAction(state, { type: 'adjust-points', playerId: state.turnPlayerId, amount: 1 }))}>+1 point</button>
        <button type="button" onClick={() => setGame((state) => applyManualAction(state, { type: 'adjust-points', playerId: state.turnPlayerId, amount: -1 }))}>-1 point</button>
        <button type="button" disabled={!selectedUnitId} onClick={() => selectedUnitId && setGame((state) => applyManualAction(state, { type: 'ready-unit', unitId: selectedUnitId }))}>Ready unit</button>
        <button type="button" disabled={!selectedUnitId} onClick={() => selectedUnitId && setGame((state) => applyManualAction(state, { type: 'exhaust-unit', unitId: selectedUnitId }))}>Exhaust unit</button>
        <button type="button" disabled={!selectedUnitId} onClick={() => selectedUnitId && setGame((state) => applyManualAction(state, { type: 'damage-unit', unitId: selectedUnitId, amount: 1 }))}>+1 damage</button>
        <button type="button" disabled={!selectedUnitId} onClick={() => selectedUnitId && setGame((state) => applyManualAction(state, { type: 'damage-unit', unitId: selectedUnitId, amount: -1 }))}>-1 damage</button>
        <button type="button" disabled={!selectedUnitId} onClick={() => selectedUnitId && setGame((state) => applyManualAction(state, { type: 'kill-unit', unitId: selectedUnitId }))}>Kill</button>
        <button type="button" disabled={!selectedUnitId} onClick={() => selectedUnitId && setGame((state) => applyManualAction(state, { type: 'recall-unit', unitId: selectedUnitId }))}>Recall</button>
      </div>
      <div className="button-row">
        {game.battlefields.map((field) => (
          <button key={field.id} type="button" onClick={() => setGame((state) => applyManualAction(state, { type: 'set-controller', battlefieldId: field.id, controllerId: state.turnPlayerId }))}>
            Control {field.name}
          </button>
        ))}
      </div>
    </section>
  )
}

export function GamePage({
  activePlayer,
  battlefieldOptions,
  cards,
  deckAssignments,
  decks,
  game,
  profiles,
  restart,
  setGame,
  setSetup,
  setup,
  startConfiguredGame,
  updateSetup,
}: {
  activePlayer: Player
  battlefieldOptions: BattlefieldOption[]
  cards: Card[]
  deckAssignments: (GameDeckAssignment | null)[]
  decks: SavedDeck[]
  game: GameState
  profiles: UserProfile[]
  restart: () => void
  setGame: Dispatch<SetStateAction<GameState>>
  setSetup: Dispatch<SetStateAction<SetupState>>
  setup: SetupState
  startConfiguredGame: () => void
  updateSetup: (setup: SetupState) => void
}) {
  const dragData = useDragData()
  const activeShowdownField = game.activeShowdown ? game.battlefields.find((field) => field.id === game.activeShowdown?.battlefieldId) : null

  return (
    <>
      <GameHero game={game} />

      <GameSetupPanel
        battlefieldOptions={battlefieldOptions}
        cards={cards}
        deckAssignments={deckAssignments}
        decks={decks}
        profiles={profiles}
        setSetup={setSetup}
        setup={setup}
        startConfiguredGame={startConfiguredGame}
        updateSetup={updateSetup}
      />

      {game.passShield && (
        <PassShield activePlayer={activePlayer} onReady={() => setGame((state) => ({ ...state, passShield: false }))} />
      )}

      {game.stage === 'mulligan' && <MulliganPanel game={game} setGame={setGame} />}

      {game.stage !== 'mulligan' && (
        <section className="table">
          <div className="phase-bar">
            <strong>
              Turn {game.turnNumber}: {activePlayer.name} · {game.turnPhase}
            </strong>
            <div className="button-row">
              <button type="button" onClick={() => setGame(advancePhase)} disabled={game.stage !== 'playing' || Boolean(game.activeShowdown)}>
                {game.turnPhase === 'main' ? 'End turn' : 'Advance phase'}
              </button>
              <button type="button" onClick={() => setGame(openNextStagedConflict)}>
                Open staged conflict
              </button>
              <button type="button" onClick={restart}>
                New game
              </button>
            </div>
          </div>

          {activeShowdownField && (
            <section className="showdown-panel">
              <strong>
                {game.activeShowdown?.kind === 'combat' ? 'Combat showdown' : 'Showdown'} at {activeShowdownField.name}
              </strong>
              <span>Focus: {game.focusPlayerId !== null ? game.players[game.focusPlayerId]?.name : 'none'}</span>
              <div className="button-row">
                <button type="button" onClick={() => game.focusPlayerId !== null && setGame((state) => passFocus(state, game.focusPlayerId as number))}>Pass focus</button>
                <button type="button" onClick={() => setGame(resolveShowdown)}>Close showdown</button>
                {game.activeCombat && <button type="button" onClick={() => setGame(resolveCombat)}>Resolve combat</button>}
              </div>
            </section>
          )}

          <ManualTools game={game} setGame={setGame} />

          <section className="board playmat">
            <div className="victory-track" aria-label="Victory score track">
              {Array.from({ length: game.victoryScore + 1 }, (_, index) => (
                <span className={activePlayer.points === game.victoryScore - index ? 'score-dot current' : 'score-dot'} key={game.victoryScore - index}>
                  {game.victoryScore - index}
                </span>
              ))}
            </div>

            <section className="mat-zone battlefields-zone">
              <span className="zone-label">Battlefields</span>
              <div className="battlefields" style={{ gridTemplateColumns: `repeat(${game.battlefields.length}, minmax(0, 1fr))` }}>
                {game.battlefields.map((field) => (
                  <BattlefieldLane field={field} game={game} key={field.id} setGame={setGame} />
                ))}
              </div>
            </section>

            <section className="mat-zone champion-zone">
              <span className="zone-label">Champion</span>
              {activePlayer.champion ? (
                <div className="zone-card">
                  <button
                    className="clickable-card"
                    draggable={!activePlayer.championSummoned && game.stage === 'playing' && game.turnPhase === 'main'}
                    type="button"
                    onDragStart={(event) => dragData(event, { type: 'champion' })}
                  >
                    <CardFace card={activePlayer.champion} compact />
                  </button>
                </div>
              ) : (
                <div className="empty-slot">No champion imported</div>
              )}
            </section>

            <section className="mat-zone legend-zone">
              <span className="zone-label">Legend</span>
              {activePlayer.legend ? <div className="zone-card"><div className="clickable-card"><CardFace card={activePlayer.legend} compact /></div></div> : <div className="empty-slot">No legend imported</div>}
            </section>

            <section
              className="mat-zone base-zone drop-zone"
              onDragOver={(event) => event.preventDefault()}
              onDrop={(event) => {
                event.preventDefault()
                const payload = readDragData(event)
                if (payload) setGame((state) => handleDrop(state, payload))
              }}
            >
              <span className="zone-label">Base</span>
              <div className="unit-row">
                {activePlayer.base.map((unit) => (
                  <UnitButton game={game} key={unit.uid} setGame={setGame} unit={unit} />
                ))}
              </div>
            </section>

            <section className="mat-zone main-deck-zone"><span className="zone-label">Main Deck</span><DeckStack count={activePlayer.deck.length} kind="main" /></section>
            <section className="mat-zone rune-deck-zone"><span className="zone-label">Rune Deck</span><DeckStack count={activePlayer.runeDeck.length} kind="rune" /></section>
            <section className="mat-zone runes-zone"><span className="zone-label">Runes / Rune Pool</span><RunePool player={activePlayer} /></section>
            <section className="mat-zone trash-zone"><span className="zone-label">Trash</span><DeckStack count={activePlayer.trash.length} kind="trash" /></section>

            <PlayerHand activePlayer={activePlayer} game={game} restart={restart} setGame={setGame} />
          </section>
        </section>
      )}
    </>
  )
}
