import { RunePool } from '../../shared/ui/RunePool'
import { readyRuneCount, totalRuneCount } from './rules/gameRules'
import type { GameState } from '../../shared/models'

export function GameHero({ game }: { game: GameState }) {
  return (
    <section className="hero-panel">
      <div>
        <p className="eyebrow">fan-made multiplayer tabletop prototype</p>
        <h1>Riftbound Baybeeeee</h1>
        <p>
          Hot-seat multiplayer with guided setup, mulligans, official turn phases, battlefield control, and manual tools
          for card text the prototype does not automate yet.
        </p>
      </div>
      <div className="scoreboard">
        {game.players.map((player) => (
          <article className={game.turnPlayerId === player.id ? 'player active' : 'player'} key={player.id}>
            <span>{player.name}</span>
            <strong>{player.points} pts</strong>
            {game.mode === 'teams-2v2' && <small>Team {game.teamIds[player.id] + 1}</small>}
            <RunePool player={player} />
            <small>
              {readyRuneCount(player)}/{totalRuneCount(player)} runes available · {player.runeDeck.length} rune deck · {player.hand.length} hand · {player.deck.length} deck
            </small>
          </article>
        ))}
      </div>
    </section>
  )
}
