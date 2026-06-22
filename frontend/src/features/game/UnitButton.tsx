import type { Dispatch, SetStateAction } from 'react'
import { CardFace } from '../../shared/ui/CardFace'
import { handleDrop } from './rules/gameRules'
import type { GameState, Unit } from '../../shared/models'
import { readDragData, useDragData } from '../../shared/dragDrop'

export function UnitButton({ game, setGame, unit }: { game: GameState; setGame: Dispatch<SetStateAction<GameState>>; unit: Unit }) {
  const dragData = useDragData()

  return (
    <button
      className={game.selectedUnit?.unitId === unit.uid ? 'unit-card selected' : 'unit-card'}
      draggable={unit.owner === game.turnPlayerId && game.stage === 'playing' && game.turnPhase === 'main'}
      key={unit.uid}
      type="button"
      onDragStart={(event) => dragData(event, { type: 'unit', unitId: unit.uid })}
      onDragOver={(event) => event.preventDefault()}
      onDrop={(event) => {
        event.preventDefault()
        const payload = readDragData(event)
        if (payload) setGame((state) => handleDrop(state, payload, undefined, unit.uid))
      }}
      onClick={() => setGame((state) => ({ ...state, selectedUnit: { player: unit.owner, unitId: unit.uid } }))}
    >
      <CardFace card={unit} compact />
      {unit.damage > 0 && <small>{unit.damage} damage</small>}
    </button>
  )
}
