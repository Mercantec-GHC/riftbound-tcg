import { CardFace } from '../../shared/ui/CardFace'
import { DeckStack } from '../../shared/ui/DeckStack'
import { readDragData, useDragData } from '../../shared/dragDrop'
import type { MatchPlayer } from '../../shared/api'
import type { Battlefield, Card, GameState, Player, Unit } from '../../shared/models'

function canAffordCard(player: Player, card: Card): boolean {
  return player.runes.ready.length + player.runePool.energy >= card.cost
}

function unitOwnerId(unit: Unit): number {
  return (unit as Unit & { ownerId?: number }).ownerId ?? unit.owner
}

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

function ReadOnlyUnit({ unit, draggable, onDragStart }: { unit: Unit; draggable?: boolean; onDragStart?: (event: React.DragEvent) => void }) {
  return (
    <div className={`online-unit-card-wrap ${draggable ? 'movable' : ''}`.trim()} draggable={draggable} onDragStart={onDragStart}>
      <ReadOnlyArtCard card={unit} className={`online-unit-card ${unit.exhausted ? 'exhausted' : ''}`.trim()} />
      {(unit.damage > 0 || unit.exhausted) && (
        <div className="online-card-badges">
          {unit.damage > 0 && <small>{unit.damage} dmg</small>}
          {unit.exhausted && <small>Exhausted</small>}
        </div>
      )}
    </div>
  )
}

type BattlefieldController = {
  avatarImageHash?: string | null
  displayName: string
}

function BattlefieldControllerBadge({ controller }: { controller: BattlefieldController }) {
  const label = controller.displayName.trim() || 'Player'
  const initial = label.charAt(0).toUpperCase() || '?'

  return (
    <span className="online-battlefield-controller-badge" aria-label={`Controlled by ${label}`} title={`Controlled by ${label}`}>
      {controller.avatarImageHash
        ? <img src={`/api/v1/profile-images/${encodeURIComponent(controller.avatarImageHash)}`} alt="" />
        : <span aria-hidden="true">{initial}</span>}
    </span>
  )
}

function OnlineBattlefieldLane({
  cardsById,
  controller,
  field,
  viewerPlayerId,
  isShowdown,
  canDropPlayedUnit,
  canDropMovedUnit,
  canMoveUnit,
  onPlayUnit,
  onMoveUnit,
}: {
  cardsById: Map<string, Card>
  controller: BattlefieldController | null
  field: Battlefield
  viewerPlayerId: number
  isShowdown: boolean
  canDropPlayedUnit: boolean
  canDropMovedUnit: boolean
  canMoveUnit?: boolean
  onPlayUnit?: (handIndex: number, battlefieldId?: string) => void
  onMoveUnit?: (unitId: string, battlefieldId: string) => void
}) {
  const dragData = useDragData()
  const catalogCard = battlefieldCatalogCard(field, cardsById)
  const canDropUnit = canDropPlayedUnit || canDropMovedUnit
  const viewerUnits = field.units.filter((unit) => unitOwnerId(unit) === viewerPlayerId)
  const opponentUnits = field.units.filter((unit) => unitOwnerId(unit) !== viewerPlayerId)

  const renderUnitSide = (units: Unit[], side: 'opponent' | 'viewer') => (
    <div className={`unit-row online-battlefield-units online-battlefield-units-${side}`}>
      {units.map((unit) => {
        const isMovable = Boolean(canMoveUnit && side === 'viewer' && !unit.exhausted)
        return (
          <ReadOnlyUnit
            key={unit.uid}
            unit={unit}
            draggable={isMovable}
            onDragStart={isMovable ? (event) => dragData(event, { type: 'unit', unitId: unit.uid }) : undefined}
          />
        )
      })}
    </div>
  )

  return (
    <article
      className={`online-battlefield ${canDropUnit ? 'drop-zone' : ''}`.trim()}
      aria-label={field.name}
      onDragOver={canDropUnit ? (event) => event.preventDefault() : undefined}
      onDrop={canDropUnit ? (event) => {
        event.preventDefault()
        const payload = readDragData(event)
        if (payload?.type === 'card' && canDropPlayedUnit) onPlayUnit?.(payload.handIndex, field.id)
        if (payload?.type === 'unit' && canDropMovedUnit) onMoveUnit?.(payload.unitId, field.id)
      } : undefined}
    >
      {renderUnitSide(opponentUnits, 'opponent')}
      <div className="online-battlefield-art-wrap">
        {controller && <BattlefieldControllerBadge controller={controller} />}
        {isShowdown && <div className="online-showdown-banner">Showdown!</div>}
        {catalogCard ? (
          <ReadOnlyArtCard card={catalogCard} className="online-battlefield-art" title={field.name} />
        ) : (
          <div className="empty-slot online-battlefield-fallback">{field.name}</div>
        )}
      </div>
      {renderUnitSide(viewerUnits, 'viewer')}
    </article>
  )
}

function OnlineHandZone({
  isViewer,
  player,
  mulliganSelection,
  canPlayUnit,
}: {
  isViewer: boolean
  player: Player
  mulliganSelection?: { selectedIndexes: number[]; onToggle: (index: number) => void }
  canPlayUnit?: boolean
}) {
  const dragData = useDragData()
  if (!isViewer) {
    return (
      <div className="hidden-hand" aria-label={`${player.name} hidden hand`}>
        <strong>{player.hand.length}</strong>
        <span>cards in hand</span>
      </div>
    )
  }

  if (mulliganSelection) {
    return (
      <div className="hand online-hand">
        {player.hand.map((card, index) => {
          const isSelected = mulliganSelection.selectedIndexes.includes(index)
          return (
            <button
              type="button"
              key={`${card.id}-${index}`}
              className={`online-hand-card-button ${isSelected ? 'selected' : ''}`.trim()}
              onClick={() => mulliganSelection.onToggle(index)}
              aria-pressed={isSelected}
              title={isSelected ? `Deselect ${card.name}` : `Select ${card.name} to mulligan`}
            >
              <ReadOnlyArtCard card={card} className="online-hand-card" />
            </button>
          )
        })}
        {player.hand.length === 0 && <span className="no-runes">No cards in hand</span>}
      </div>
    )
  }

  return (
    <div className="hand online-hand">
      {player.hand.map((card, index) => {
        const isPlayableUnit = canPlayUnit && card.kind === 'unit' && canAffordCard(player, card)
        return (
          <button
            type="button"
            key={`${card.id}-${index}`}
            className={`online-hand-card-button ${isPlayableUnit ? 'playable' : ''}`.trim()}
            draggable={isPlayableUnit}
            disabled={!isPlayableUnit}
            onDragStart={(event) => dragData(event, { type: 'card', handIndex: index, playerId: player.id })}
            title={card.kind === 'unit' && !canAffordCard(player, card) ? `Needs ${card.cost} energy to play ${card.name}` : card.name}
          >
            <ReadOnlyArtCard card={card} className="online-hand-card" />
          </button>
        )
      })}
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

function OnlinePlayerMat({
  isViewer,
  player,
  placement,
  victoryScore,
  mulliganSelection,
  canPlayUnit,
  onPlayUnit,
  canMoveUnit,
}: {
  isViewer: boolean
  player: Player
  placement: 'opponent' | 'viewer' | 'shared'
  victoryScore: number
  mulliganSelection?: { selectedIndexes: number[]; onToggle: (index: number) => void }
  canPlayUnit?: boolean
  onPlayUnit?: (handIndex: number, battlefieldId?: string) => void
  canMoveUnit?: boolean
}) {
  const dragData = useDragData()
  const handZone = (
    <section className="online-hand-zone" aria-label={`${player.name} hand`}>
      <OnlineHandZone
        isViewer={isViewer}
        player={player}
        mulliganSelection={isViewer ? mulliganSelection : undefined}
        canPlayUnit={isViewer ? canPlayUnit : false}
      />
    </section>
  )

  const championZone = (
    <section className="mat-zone champion-zone fixed-card-zone">
      <span className="zone-label">Champion</span>
      {player.champion ? <ReadOnlyArtCard card={player.champion} className="online-zone-card" /> : <div className="empty-slot">No champion</div>}
    </section>
  )

  const legendZone = (
    <section className="mat-zone legend-zone fixed-card-zone">
      <span className="zone-label">Legend</span>
      {player.legend ? <ReadOnlyArtCard card={player.legend} className="online-zone-card" /> : <div className="empty-slot">No legend</div>}
    </section>
  )

  const canDropOnBase = Boolean(isViewer && canPlayUnit)
  const baseZone = (
    <section
      className={`mat-zone base-zone flexible-card-zone ${canDropOnBase ? 'drop-zone' : ''}`.trim()}
      onDragOver={canDropOnBase ? (event) => event.preventDefault() : undefined}
      onDrop={canDropOnBase ? (event) => {
        event.preventDefault()
        const payload = readDragData(event)
        if (payload?.type === 'card') onPlayUnit?.(payload.handIndex)
      } : undefined}
    >
      <span className="zone-label">Base</span>
      <div className="unit-row">
        {player.base.map((unit) => {
          const isMovable = Boolean(isViewer && canMoveUnit && !unit.exhausted)
          return (
            <ReadOnlyUnit
              key={unit.uid}
              unit={unit}
              draggable={isMovable}
              onDragStart={isMovable ? (event) => dragData(event, { type: 'unit', unitId: unit.uid }) : undefined}
            />
          )
        })}
        {player.base.length === 0 && <span className="zone-empty-text">No units</span>}
      </div>
    </section>
  )

  const mainDeckZone = (
    <section className="mat-zone main-deck-zone fixed-card-zone">
      <span className="zone-label">Main Deck</span>
      <DeckStack count={player.deck.length} kind="main" />
    </section>
  )

  const runeDeckZone = (
    <section className="mat-zone rune-deck-zone fixed-card-zone">
      <span className="zone-label">Rune Deck</span>
      <DeckStack count={player.runeDeck.length} kind="rune" />
    </section>
  )

  const runesZone = (
    <section className="mat-zone runes-zone flexible-card-zone">
      <span className="zone-label">Runes</span>
      <ReadOnlyRunePool player={player} />
    </section>
  )

  const trashZone = (
    <section className="mat-zone trash-zone fixed-card-zone">
      <span className="zone-label">Trash</span>
      <DeckStack count={player.trash.length} kind="trash" />
    </section>
  )

  const matRows = isViewer ? (
    <>
      <div className="online-player-zone-row online-player-primary-row">
        {championZone}
        {legendZone}
        {baseZone}
        {mainDeckZone}
      </div>
      <div className="online-player-zone-row online-player-secondary-row">
        {runeDeckZone}
        {runesZone}
        {trashZone}
      </div>
    </>
  ) : (
    <>
      <div className="online-player-zone-row online-player-secondary-row">
        {trashZone}
        {runesZone}
        {runeDeckZone}
      </div>
      <div className="online-player-zone-row online-player-primary-row">
        {mainDeckZone}
        {baseZone}
        {legendZone}
        {championZone}
      </div>
    </>
  )

  return (
    <section className={`online-player-mat ${isViewer ? 'viewer-player-mat' : ''} ${placement}-player-mat`.trim()}>
      <PlayerVictoryTrack player={player} reverse={!isViewer} victoryScore={victoryScore} />
      <div className="online-player-mat-body">
        <header className="online-player-mat-header">
          <div>
            <span>{isViewer ? 'Your mat' : 'Opponent mat'}</span>
            <h3>{player.name}</h3>
          </div>
          <strong>{player.points} pts</strong>
        </header>

        <div className={`online-player-zones ${isViewer ? 'viewer-player-zones' : 'opponent-player-zones'}`}>
          {!isViewer && handZone}
          {matRows}
          {isViewer && handZone}
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

function BattlefieldZone({
  cardsById,
  game,
  matchPlayers,
  viewerPlayerId,
  canPlayUnit,
  onPlayUnit,
  canMoveUnit,
  onMoveUnit,
}: {
  cardsById: Map<string, Card>
  game: GameState
  matchPlayers: MatchPlayer[]
  viewerPlayerId: number
  canPlayUnit?: boolean
  onPlayUnit?: (handIndex: number, battlefieldId?: string) => void
  canMoveUnit?: boolean
  onMoveUnit?: (unitId: string, battlefieldId: string) => void
}) {
  const matchPlayersById = new Map(matchPlayers.map((player) => [player.playerId, player]))
  const gamePlayersById = new Map(game.players.map((player) => [player.id, player]))

  return (
    <section className="online-battlefields-zone" aria-label="Battlefields">
      <div className="online-battlefields-row" style={{ gridTemplateColumns: `repeat(${game.battlefields.length}, max-content)` }}>
        {game.battlefields.map((field) => {
          const matchPlayer = field.controllerId === null ? null : matchPlayersById.get(field.controllerId) ?? null
          const gamePlayer = field.controllerId === null ? null : gamePlayersById.get(field.controllerId) ?? null
          const controller = field.controllerId === null
            ? null
            : {
              avatarImageHash: matchPlayer?.avatarImageHash ?? null,
              displayName: matchPlayer?.displayName ?? gamePlayer?.name ?? `Player ${field.controllerId + 1}`,
            }
          return (
            <OnlineBattlefieldLane
              cardsById={cardsById}
              controller={controller}
              field={field}
              key={field.id}
              viewerPlayerId={viewerPlayerId}
              isShowdown={game.activeShowdown?.battlefieldId === field.id}
              canDropPlayedUnit={Boolean(canPlayUnit && field.controllerId === viewerPlayerId)}
              canDropMovedUnit={Boolean(canMoveUnit)}
              canMoveUnit={canMoveUnit}
              onPlayUnit={onPlayUnit}
              onMoveUnit={onMoveUnit}
            />
          )
        })}
      </div>
    </section>
  )
}

export function OnlinePlaymat({
  cards,
  game,
  matchPlayers,
  viewerPlayerId,
  mulliganSelection,
  canPlayUnit,
  onPlayUnit,
  canMoveUnit,
  onMoveUnit,
}: {
  cards: Card[]
  game: GameState
  matchPlayers?: MatchPlayer[]
  viewerPlayerId: number
  mulliganSelection?: { selectedIndexes: number[]; onToggle: (index: number) => void }
  canPlayUnit?: boolean
  onPlayUnit?: (handIndex: number, battlefieldId?: string) => void
  canMoveUnit?: boolean
  onMoveUnit?: (unitId: string, battlefieldId: string) => void
}) {
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
        <BattlefieldZone cardsById={cardsById} game={hydratedGame} matchPlayers={matchPlayers ?? []} viewerPlayerId={viewerPlayerId} canPlayUnit={canPlayUnit} onPlayUnit={onPlayUnit} canMoveUnit={canMoveUnit} onMoveUnit={onMoveUnit} />
        {viewer && (
          <OnlinePlayerMat
            isViewer
            placement="viewer"
            player={viewer}
            victoryScore={hydratedGame.victoryScore}
            mulliganSelection={mulliganSelection}
            canPlayUnit={canPlayUnit}
            onPlayUnit={onPlayUnit}
            canMoveUnit={canMoveUnit}
          />
        )}
      </section>
    )
  }

  const orderedPlayers = [...opponents, ...(viewer ? [viewer] : [])]
  return (
    <section className="online-shared-playmat shared-table-playmat">
      <BattlefieldZone cardsById={cardsById} game={hydratedGame} matchPlayers={matchPlayers ?? []} viewerPlayerId={viewerPlayerId} canPlayUnit={canPlayUnit} onPlayUnit={onPlayUnit} canMoveUnit={canMoveUnit} onMoveUnit={onMoveUnit} />

      <section className="online-player-mats" aria-label="Player play spaces">
        {orderedPlayers.map((player) => (
          <OnlinePlayerMat
            isViewer={player.id === viewerPlayerId}
            key={player.id}
            placement="shared"
            player={player}
            victoryScore={hydratedGame.victoryScore}
            mulliganSelection={player.id === viewerPlayerId ? mulliganSelection : undefined}
            canPlayUnit={player.id === viewerPlayerId ? canPlayUnit : false}
            onPlayUnit={onPlayUnit}
            canMoveUnit={player.id === viewerPlayerId ? canMoveUnit : false}
          />
        ))}
      </section>
    </section>
  )
}
