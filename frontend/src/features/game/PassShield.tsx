import type { Player } from '../../shared/models'

export function PassShield({ activePlayer, onReady }: { activePlayer: Player; onReady: () => void }) {
  return (
    <section className="pass-shield">
      <h2>Pass to {activePlayer.name}</h2>
      <p>Hands are hidden between turns so every player gets the good dramatic reveal.</p>
      <button type="button" onClick={onReady}>
        Ready to play
      </button>
    </section>
  )
}
