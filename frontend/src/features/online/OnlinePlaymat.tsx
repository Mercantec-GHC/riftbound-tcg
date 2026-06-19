import { CardFace } from '../../components/CardFace'
import type { Battlefield, Card, GameState, Player, Unit } from '../../models'

function hydrateCard<T extends Card>(card: T, cardsById: Map<string, Card>): T {
  if (!card.catalogId) return card
  const catalogCard = cardsById.get(card.catalogId)
  if (!catalogCard) return card
  return {
    ...card,
    ...catalogCard,
    id: card.id,
    catalogId: card.catalogId,
  } as T
}

function hydrateUnit(unit: Unit, cardsById: Map<string, Card>): Unit {
  return hydrateCard(unit, cardsById)
}

function hydratePlayer(player: Player, cardsById: Map<string, Card>): Player {
  return {
    ...player,
    base: player.base.map((unit) => hydrateUnit(unit, cardsById)),
    champion: player.champion ? hydrateCard(player.champion, cardsById) : null,
    deck: player.deck.map((card) => hydrateCard(card, cardsById)),
    hand: player.hand.map((card) => hydrateCard(card, cardsById)),
    legend: player.legend ? hydrateCard(player.legend, cardsById) : null,
    runeDeck: player.runeDeck.map((card) => hydrateCard(card, cardsById)),
    runes: {
      ready: player.runes.ready.map((card) => hydrateCard(card, cardsById)),
      exhausted: player.runes.exhausted.map((card) => hydrateCard(card, cardsById)),
    },
    trash: player.trash.map((card) => hydrateCard(card, cardsById)),
  }
}

function hydrateBattlefield(field: Battlefield, cardsById: Map<string, Card>): Battlefield {
  return {
    ...field,
    units: field.units.map((unit) => hydrateUnit(unit, cardsById)),
  }
}

function battlefieldCatalogCard(field: Battlefield, cardsById: Map<string, Card>) {
  return field.catalogId ? cardsById.get(field.catalogId) ?? null : null
}

function ReadOnlyArtCard({ card, className = '', title }: { card: Card; className?: string; title?: string }) {
  return (
    <div className={`online-art-card ${className}`.trim()} title={title ?? card.name}>
      <CardFace artOnly card={card} />
    </div>
  )
}

function ReadOnlyUnit({ unit }: { unit: Unit }) {
  return (
    <div className="online-unit-card-wrap">
      <ReadOnlyArtCard card={unit} className="online-unit-card" />
      {(unit.damage > 0 || unit.exhausted) && (
        <div className="online-card-badges">
          {unit.damage > 0 && <small>{unit.damage} dmg</small>}
          {unit.exhausted && <small>Exhausted</small>}
        </div>
      )}
    </div>
  )
}

function OnlineBattlefieldLane({ cardsById, field }: { cardsById: Map<string, Card>; field: Battlefield }) {
  const catalogCard = battlefieldCatalogCard(field, cardsById)

  return (
    <article className="online-battlefield" aria-label={field.name}>
      {catalogCard ? (
        <ReadOnlyArtCard card={catalogCard} className="online-battlefield-art" title={field.name} />
      ) : (
        <div className="empty-slot online-battlefield-fallback">{field.name}</div>
      )}
    </article>
  )
}

function OnlineHandZone({ isViewer, player }: { isViewer: boolean; player: Player }) {
  if (!isViewer) {
    return (
      <div className="hidden-hand" aria-label={`${player.name} hidden hand`}>
        <strong>{player.hand.length}</strong>
        <span>cards in hand</span>
      </div>
    )
  }

  return (
    <div className="hand online-hand">
      {player.hand.map((card, index) => (
        <ReadOnlyArtCard card={card} className="online-hand-card" key={`${card.id}-${index}`} />
      ))}
      {player.hand.length === 0 && <span className="no-runes">No cards in hand</span>}
    </div>
  )
}

function ReadOnlyRunePool({ player }: { player: Player }) {
  const runeCount = player.runes.ready.length + player.runes.exhausted.length
  return (
    <div className="rune-pool" aria-label={`${player.name} rune pool`}>
      {player.runes.ready.map((rune, index) => (
        <ReadOnlyArtCard card={rune} className="rune-card ready" key={`${rune.id}-ready-${index}`} title={`Ready ${rune.name}`} />
      ))}
      {player.runes.exhausted.map((rune, index) => (
        <ReadOnlyArtCard card={rune} className="rune-card exhausted" key={`${rune.id}-exhausted-${index}`} title={`Exhausted ${rune.name}`} />
      ))}
      {runeCount === 0 && <span className="no-runes">No runes</span>}
    </div>
  )
}

function OnlinePlayerMat({ isViewer, player, placement, victoryScore }: { isViewer: boolean; player: Player; placement: 'opponent' | 'viewer' | 'shared'; victoryScore: number }) {
  return (
    <section className={`online-player-mat ${isViewer ? 'viewer-player-mat' : ''} ${placement}-player-mat`.trim()}>
      <PlayerVictoryTrack player={player} reverse={placement === 'opponent'} victoryScore={victoryScore} />
      <div className="online-player-mat-body">
        <header className="online-player-mat-header">
          <div>
            <span>{isViewer ? 'Your mat' : 'Opponent mat'}</span>
            <h3>{player.name}</h3>
          </div>
          <strong>{player.points} pts</strong>
        </header>

        <div className="online-player-zones">
          <section className="mat-zone champion-zone">
            <span className="zone-label">Champion</span>
            {player.champion ? <ReadOnlyArtCard card={player.champion} className="online-zone-card" /> : <div className="empty-slot">No champion</div>}
          </section>

          <section className="mat-zone legend-zone">
            <span className="zone-label">Legend</span>
            {player.legend ? <ReadOnlyArtCard card={player.legend} className="online-zone-card" /> : <div className="empty-slot">No legend</div>}
          </section>

          <section className="mat-zone base-zone">
            <span className="zone-label">Base</span>
            <div className="unit-row">
              {player.base.map((unit) => (
                <ReadOnlyUnit key={unit.uid} unit={unit} />
              ))}
              {player.base.length === 0 && <span className="zone-empty-text">No units</span>}
            </div>
          </section>

          <section className="mat-zone main-deck-zone">
            <span className="zone-label">Main Deck</span>
            <div className="deck-stack">{player.deck.length}</div>
          </section>

          <section className="mat-zone rune-deck-zone">
            <span className="zone-label">Rune Deck</span>
            <div className="deck-stack rune-stack">{player.runeDeck.length}</div>
          </section>

          <section className="mat-zone runes-zone">
            <span className="zone-label">Runes / Rune Pool</span>
            <ReadOnlyRunePool player={player} />
          </section>

          <section className="mat-zone trash-zone">
            <span className="zone-label">Trash</span>
            <div className="deck-stack trash-stack">{player.trash.length}</div>
          </section>

          <section className="mat-zone hand-zone">
            <span className="zone-label">Hand</span>
            <OnlineHandZone isViewer={isViewer} player={player} />
          </section>
        </div>
      </div>
    </section>
  )
}

function PlayerVictoryTrack({ player, reverse, victoryScore }: { player: Player; reverse: boolean; victoryScore: number }) {
  const maxPoints = Math.max(victoryScore, player.points)
  const scores = Array.from({ length: maxPoints + 1 }, (_, index) => reverse ? index : maxPoints - index)
  return (
    <div className="victory-track online-victory-track" aria-label={`${player.name} victory score track`}>
      {scores.map((score) => (
        <span className={score === player.points ? 'score-dot current' : 'score-dot'} key={score} title={`${player.name}: ${score}`}>
          {score}
        </span>
      ))}
    </div>
  )
}

function BattlefieldZone({ cardsById, game }: { cardsById: Map<string, Card>; game: GameState }) {
  return (
    <section className="online-battlefields-zone" aria-label="Battlefields">
      <div className="online-battlefields-row" style={{ gridTemplateColumns: `repeat(${game.battlefields.length}, max-content)` }}>
        {game.battlefields.map((field) => (
          <OnlineBattlefieldLane cardsById={cardsById} field={field} key={field.id} />
        ))}
      </div>
    </section>
  )
}

export function OnlinePlaymat({ cards, game, viewerPlayerId }: { cards: Card[]; game: GameState; viewerPlayerId: number }) {
  const cardsById = new Map(cards.map((card) => [card.id, card]))
  const hydratedGame: GameState = {
    ...game,
    battlefields: game.battlefields.map((field) => hydrateBattlefield(field, cardsById)),
    players: game.players.map((player) => hydratePlayer(player, cardsById)),
  }
  const viewer = hydratedGame.players.find((player) => player.id === viewerPlayerId) ?? null
  const opponents = hydratedGame.players.filter((player) => player.id !== viewerPlayerId)
  const isDuel = hydratedGame.mode === 'duel-1v1' && hydratedGame.players.length === 2

  if (isDuel) {
    const opponent = opponents[0]
    return (
      <section className="online-shared-playmat duel-playmat">
        {opponent && <OnlinePlayerMat isViewer={false} placement="opponent" player={opponent} victoryScore={hydratedGame.victoryScore} />}
        <BattlefieldZone cardsById={cardsById} game={hydratedGame} />
        {viewer && <OnlinePlayerMat isViewer placement="viewer" player={viewer} victoryScore={hydratedGame.victoryScore} />}
      </section>
    )
  }

  const orderedPlayers = [...opponents, ...(viewer ? [viewer] : [])]
  return (
    <section className="online-shared-playmat shared-table-playmat">
      <BattlefieldZone cardsById={cardsById} game={hydratedGame} />

      <section className="online-player-mats" aria-label="Player play spaces">
        {orderedPlayers.map((player) => (
          <OnlinePlayerMat isViewer={player.id === viewerPlayerId} key={player.id} placement="shared" player={player} victoryScore={hydratedGame.victoryScore} />
        ))}
      </section>
    </section>
  )
}
