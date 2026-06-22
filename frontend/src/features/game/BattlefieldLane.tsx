import type { Dispatch, SetStateAction } from 'react'
import { handleDrop, totalMight } from './rules/gameRules'
import type { Battlefield, GameState } from '../../shared/models'
import { readDragData } from '../../shared/dragDrop'
import { UnitButton } from './UnitButton'

export function BattlefieldLane({
  field,
  game,
  setGame,
}: {
  field: Battlefield
  game: GameState
  setGame: Dispatch<SetStateAction<GameState>>
}) {
  return (
    <article
      className="battlefield drop-zone"
      onDragOver={(event) => event.preventDefault()}
      onDrop={(event) => {
        event.preventDefault()
        const payload = readDragData(event)
        if (payload) setGame((state) => handleDrop(state, payload, field.id))
      }}
    >
      <header>
        <div>
          <span>Chosen by {game.players[field.chosenBy]?.name}</span>
          <h3>{field.name}</h3>
          <small>Control: {field.controllerId === null ? 'Uncontrolled' : game.players[field.controllerId]?.name}</small>
        </div>
        <strong>{field.claim} pts</strong>
      </header>
      <div className="contest">
        {game.players.map((player) => (
          <div className="army" key={player.id}>
            <span>
              {player.name}: {totalMight(field.units, player.id)}
            </span>
            <div className="unit-row">
              {field.units
                .filter((unit) => unit.owner === player.id)
                .map((unit) => (
                  <UnitButton game={game} key={unit.uid} setGame={setGame} unit={unit} />
                ))}
            </div>
          </div>
        ))}
      </div>
    </article>
  )
}
