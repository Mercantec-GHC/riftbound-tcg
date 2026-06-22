import type { Dispatch, SetStateAction } from 'react'
import { CardFace } from '../../shared/ui/CardFace'
import type { GameState, Player } from '../../shared/models'
import { useDragData } from '../../shared/dragDrop'

export function PlayerHand({
  activePlayer,
  game,
  restart,
  setGame,
}: {
  activePlayer: Player
  game: GameState
  restart: () => void
  setGame: Dispatch<SetStateAction<GameState>>
}) {
  const dragData = useDragData()

  return (
    <section className="hand-zone">
      <h2>{activePlayer.name} Hand</h2>
      <div className="hand">
        {activePlayer.hand.map((card, index) => (
          <button
            className={game.selectedCard?.handIndex === index ? `card ${card.domain.toLowerCase()} selected` : `card ${card.domain.toLowerCase()}`}
            draggable={!game.passShield && game.stage === 'playing' && game.turnPhase === 'main'}
            key={`${card.id}-${index}`}
            type="button"
            onDragStart={(event) => dragData(event, { type: 'card', handIndex: index, playerId: activePlayer.id })}
            onClick={() =>
              setGame((state) => ({
                ...state,
                selectedCard: { player: activePlayer.id, handIndex: index },
                selectedUnit: null,
              }))
            }
          >
            <CardFace card={card} />
          </button>
        ))}
      </div>
      <div className="button-row hand-actions">
        <button type="button" onClick={restart}>
          New game
        </button>
      </div>
    </section>
  )
}
