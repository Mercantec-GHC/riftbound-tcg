import type { Player } from '../models'
import { CardFace } from './CardFace'

export function RunePool({ player }: { player: Player }) {
  return (
    <div className="rune-pool" aria-label={`${player.name} rune pool`}>
      {player.runes.ready.map((rune, index) => (
        <button className="rune-card ready" type="button" title={`Ready ${rune.name}; can exhaust to add 1 Energy`} key={`${rune.id}-ready-${index}`}>
          <CardFace card={rune} compact />
        </button>
      ))}
      {player.runes.exhausted.map((rune, index) => (
        <button className="rune-card exhausted" type="button" title={`Exhausted ${rune.name}`} key={`${rune.id}-exhausted-${index}`}>
          <CardFace card={rune} compact />
        </button>
      ))}
      {player.runes.ready.length + player.runes.exhausted.length === 0 && <span className="no-runes">No runes</span>}
    </div>
  )
}
