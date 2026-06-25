import { useState } from 'react'
import { CardFace } from '../../shared/ui/CardFace'
import { DeckStack } from '../../shared/ui/DeckStack'
import { readDragData, useDragData } from '../../shared/dragDrop'
import type { MatchPlayer } from '../../shared/api'
import { useActionMenu } from '../../shared/ui/actionMenuContext'
import { useCardHoverPreview } from '../../shared/ui/cardHoverPreviewContext'
import type { Battlefield, Card, CardStatusEffect, GameState, Gear, Player, Unit } from '../../shared/models'

export type BoardActionChoice = {
  key: string
  label: string
  payload: Record<string, unknown>
  type: string
}

export type HandCardIntent = {
  actions: BoardActionChoice[]
  attachUnitIds: string[]
  battlefieldIds: string[]
  canUseBase: boolean
}

export type UnitMoveIntent = {
  battlefieldIds: string[]
  canMoveToBase: boolean
}

type DragIntent =
  | { type: 'hand-card'; handIndex: number }
  | { type: 'unit'; unitId: string }
  | { type: 'champion' }

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
  const hydrated = hydrateCard(unit, cardsById)
  return {
    ...hydrated,
    attachedCards: hydrated.attachedCards?.map((card) => hydrateCard(card, cardsById)),
    statusEffects: hydrated.statusEffects?.map((effect) => ({
      ...effect,
      sourceCard: hydrateCard(effect.sourceCard, cardsById),
    })),
  }
}

function hydrateGear(gear: Gear, cardsById: Map<string, Card>): Gear {
  return hydrateCard(gear, cardsById)
}

function hydratePlayer(player: Player, cardsById: Map<string, Card>): Player {
  return {
    ...player,
    base: player.base.map((unit) => hydrateUnit(unit, cardsById)),
    baseGear: (player.baseGear ?? []).map((gear) => hydrateGear(gear, cardsById)),
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

function ReadOnlyArtCard({
  card,
  className = '',
  title,
  onClick,
  draggable,
  onDragStart,
  onDragEnd,
}: {
  card: Card
  className?: string
  title?: string
  onClick?: () => void
  draggable?: boolean
  onDragStart?: (event: React.DragEvent) => void
  onDragEnd?: () => void
}) {
  return (
    <div
      className={`online-art-card ${className}`.trim()}
      title={title ?? card.name}
      onClick={onClick}
      role={onClick ? 'button' : undefined}
      draggable={draggable}
      onDragStart={onDragStart}
      onDragEnd={onDragEnd}
    >
      <CardFace artOnly card={card} />
    </div>
  )
}

function StatusIcon({
  label,
  sourceCard,
  title,
}: {
  label: string
  sourceCard: Card
  title: string
}) {
  const hoverPreview = useCardHoverPreview()

  return (
    <span
      className="online-status-icon"
      onMouseEnter={(event) => hoverPreview?.showPreview(sourceCard, event)}
      onMouseLeave={hoverPreview?.hidePreview}
      onMouseMove={hoverPreview?.movePreview}
      title={title}
    >
      {label}
    </span>
  )
}

function effectTitle(effect: CardStatusEffect): string {
  const amount = effect.amount > 0 ? ` +${effect.amount}` : effect.amount < 0 ? ` ${effect.amount}` : ''
  return `${effect.sourceCard.name}: ${effect.type}${amount}`
}

function ReadOnlyUnit({
  unit,
  draggable,
  onDragStart,
  onDragEnd,
  canDropCard,
  onDropCard,
  targetable,
  onSelectTarget,
}: {
  unit: Unit
  draggable?: boolean
  onDragStart?: (event: React.DragEvent) => void
  onDragEnd?: () => void
  canDropCard?: boolean
  onDropCard?: (handIndex: number, unitId: string) => void
  targetable?: boolean
  onSelectTarget?: (unitId: string) => void
}) {
  const attachedCards = unit.attachedCards ?? []
  const statusEffects = unit.statusEffects ?? []
  const hasStatusIcons = attachedCards.length > 0 || statusEffects.length > 0

  return (
    <div
      className={`online-unit-card-wrap ${draggable ? 'movable' : ''} ${canDropCard ? 'drop-zone attach-drop-zone' : ''} ${targetable ? 'targetable' : ''}`.trim()}
      draggable={draggable}
      onDragStart={onDragStart}
      onDragEnd={onDragEnd}
      onDragOver={canDropCard ? (event) => event.preventDefault() : undefined}
      onDrop={canDropCard ? (event) => {
        event.preventDefault()
        event.stopPropagation()
        const payload = readDragData(event)
        if (payload?.type === 'card') onDropCard?.(payload.handIndex, unit.uid)
      } : undefined}
      onClick={targetable ? () => onSelectTarget?.(unit.uid) : undefined}
      role={targetable ? 'button' : undefined}
      title={targetable ? `Target ${unit.name}` : undefined}
    >
      <ReadOnlyArtCard card={unit} className={`online-unit-card ${unit.exhausted ? 'exhausted' : ''}`.trim()} />
      {hasStatusIcons && (
        <div className="online-status-icons" aria-label={`${unit.name} card-sourced effects`}>
          {attachedCards.map((card, index) => (
            <StatusIcon
              key={`${card.id}-${index}`}
              label="G"
              sourceCard={card}
              title={`Attached: ${card.name}`}
            />
          ))}
          {statusEffects.map((effect) => (
            <StatusIcon
              key={effect.id}
              label="FX"
              sourceCard={effect.sourceCard}
              title={effectTitle(effect)}
            />
          ))}
        </div>
      )}
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
  canDropCardPlay,
  canDropHiddenCard,
  canDropMovedUnit,
  canDropAttachedCard,
  canMoveUnit,
  dragIntent,
  handCardIntents,
  unitMoveIntents,
  onDragIntent,
  onPlayUnit,
  onDropCard,
  onDropCardOnUnit,
  onMoveUnit,
  targetSelection,
  onSelectUnitTarget,
  onSelectLaneTarget,
}: {
  cardsById: Map<string, Card>
  controller: BattlefieldController | null
  field: Battlefield
  viewerPlayerId: number
  isShowdown: boolean
  canDropPlayedUnit: boolean
  canDropCardPlay: boolean
  canDropHiddenCard: boolean
  canDropMovedUnit: boolean
  canDropAttachedCard: boolean
  canMoveUnit?: boolean
  dragIntent: DragIntent | null
  handCardIntents: Record<number, HandCardIntent>
  unitMoveIntents: Record<string, UnitMoveIntent>
  onDragIntent: (intent: DragIntent | null) => void
  onPlayUnit?: (handIndex: number, battlefieldId?: string) => void
  onDropCard?: (handIndex: number, battlefieldId: string) => void
  onDropCardOnUnit?: (handIndex: number, unitId: string) => void
  onMoveUnit?: (unitId: string, battlefieldId: string) => void
  targetSelection?: { kind: 'unit' | 'lane'; excludeUnitIds?: string[] }
  onSelectUnitTarget?: (unitId: string) => void
  onSelectLaneTarget?: (laneId: string) => void
}) {
  const dragData = useDragData()
  const catalogCard = battlefieldCatalogCard(field, cardsById)
  const draggedHandIntent = dragIntent?.type === 'hand-card' ? handCardIntents[dragIntent.handIndex] : null
  const draggedUnitIntent = dragIntent?.type === 'unit' ? unitMoveIntents[dragIntent.unitId] : null
  const acceptsDraggedHandCard = Boolean(draggedHandIntent?.battlefieldIds.includes(field.id))
  const acceptsDraggedUnit = Boolean(draggedUnitIntent?.battlefieldIds.includes(field.id))
  const hasPotentialHandDrop = Object.values(handCardIntents).some((intent) => intent.battlefieldIds.includes(field.id))
  const hasPotentialUnitDrop = Object.values(unitMoveIntents).some((intent) => intent.battlefieldIds.includes(field.id))
  const canDropUnit = acceptsDraggedHandCard || acceptsDraggedUnit
  const hasAnyDrop = canDropPlayedUnit || canDropCardPlay || canDropHiddenCard || canDropMovedUnit || hasPotentialHandDrop || hasPotentialUnitDrop
  const blocksCurrentDrag = Boolean(dragIntent && !canDropUnit)
  const viewerUnits = field.units.filter((unit) => unitOwnerId(unit) === viewerPlayerId)
  const opponentUnits = field.units.filter((unit) => unitOwnerId(unit) !== viewerPlayerId)
  const unitsTargetable = targetSelection?.kind === 'unit'
  const laneTargetable = targetSelection?.kind === 'lane'

  const renderUnitSide = (units: Unit[], side: 'opponent' | 'viewer') => (
    <div className={`unit-row online-battlefield-units online-battlefield-units-${side}`}>
      {units.map((unit) => {
        const moveIntent = unitMoveIntents[unit.uid]
        const isMovable = Boolean(canMoveUnit && side === 'viewer' && !targetSelection && moveIntent && (moveIntent.battlefieldIds.length > 0 || moveIntent.canMoveToBase))
        const acceptsDraggedCard = Boolean(draggedHandIntent?.attachUnitIds.includes(unit.uid))
        return (
          <ReadOnlyUnit
            key={unit.uid}
            unit={unit}
            draggable={isMovable}
            onDragStart={isMovable ? (event) => {
              onDragIntent({ type: 'unit', unitId: unit.uid })
              dragData(event, { type: 'unit', unitId: unit.uid })
            } : undefined}
            onDragEnd={() => onDragIntent(null)}
            canDropCard={acceptsDraggedCard || (!dragIntent && canDropAttachedCard)}
            onDropCard={onDropCardOnUnit}
            targetable={unitsTargetable && !targetSelection?.excludeUnitIds?.includes(unit.uid)}
            onSelectTarget={onSelectUnitTarget}
          />
        )
      })}
    </div>
  )

  return (
    <article
      className={[
        'online-battlefield',
        hasAnyDrop ? 'drop-zone' : '',
        canDropUnit ? 'drop-zone-active' : '',
        blocksCurrentDrag ? 'drop-zone-blocked' : '',
        laneTargetable ? 'targetable' : '',
      ].filter(Boolean).join(' ')}
      aria-label={field.name}
      onDragOver={canDropUnit ? (event) => event.preventDefault() : undefined}
      onDrop={canDropUnit ? (event) => {
        event.preventDefault()
        const payload = readDragData(event)
        if (payload?.type === 'card') {
          if (canDropCardPlay || canDropHiddenCard) onDropCard?.(payload.handIndex, field.id)
          else if (canDropPlayedUnit) onPlayUnit?.(payload.handIndex, field.id)
        }
        if (payload?.type === 'unit' && canDropMovedUnit) onMoveUnit?.(payload.unitId, field.id)
      } : undefined}
      onClick={laneTargetable ? () => onSelectLaneTarget?.(field.id) : undefined}
      role={laneTargetable ? 'button' : undefined}
      title={laneTargetable ? `Target ${field.name}` : undefined}
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
  playableCardHandIndexes,
  hideableCardHandIndexes,
  canAttachCard,
  handCardIntents,
  onPlayCard,
  onChooseHandAction,
  onDragIntent,
}: {
  isViewer: boolean
  player: Player
  mulliganSelection?: { selectedIndexes: number[]; onToggle: (index: number) => void }
  canPlayUnit?: boolean
  playableCardHandIndexes?: number[]
  hideableCardHandIndexes?: number[]
  canAttachCard?: boolean
  handCardIntents: Record<number, HandCardIntent>
  onPlayCard?: (handIndex: number) => void
  onChooseHandAction: (choice: BoardActionChoice) => void
  onDragIntent: (intent: DragIntent | null) => void
}) {
  const dragData = useDragData()
  const actionMenu = useActionMenu()
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
            <div
              key={`${card.id}-${index}`}
              className="online-hand-card-shell"
            >
              <button
                type="button"
                className={`online-hand-card-button ${isSelected ? 'selected' : ''}`.trim()}
                onClick={() => mulliganSelection.onToggle(index)}
                aria-pressed={isSelected}
                title={isSelected ? `Deselect ${card.name}` : `Select ${card.name} to mulligan`}
              >
                <ReadOnlyArtCard card={card} className="online-hand-card" />
              </button>
            </div>
          )
        })}
        {player.hand.length === 0 && <span className="no-runes">No cards in hand</span>}
      </div>
    )
  }

  return (
    <div className="hand online-hand">
      {player.hand.map((card, index) => {
        const intent = handCardIntents[index] ?? { actions: [], attachUnitIds: [], battlefieldIds: [], canUseBase: false }
        const isPlayableUnit = canPlayUnit && (card.kind === 'unit' || card.kind === 'champion')
        const isPlayableCard = (playableCardHandIndexes ?? []).includes(index)
        const isHideableCard = (hideableCardHandIndexes ?? []).includes(index)
        const isAttachableCard = Boolean(canAttachCard && (card.kind === 'gear' || card.cardType?.toLowerCase().includes('gear') || card.cardType?.toLowerCase().includes('equipment')))
        const isActionable = intent.actions.length > 0 || intent.canUseBase || intent.battlefieldIds.length > 0 || intent.attachUnitIds.length > 0
        const isDraggable = Boolean(isActionable && (isPlayableUnit || isPlayableCard || isHideableCard || isAttachableCard))
        return (
          <div
            key={`${card.id}-${index}`}
            className={`online-hand-card-shell ${isActionable ? 'playable' : ''}`.trim()}
          >
            <button
              type="button"
              className={`online-hand-card-button ${isActionable ? 'playable' : ''}`.trim()}
              draggable={isDraggable}
              disabled={!isActionable}
              onClick={() => {
                if (intent.actions.length > 1) {
                  actionMenu.openActionMenu({
                    title: card.name,
                    choices: intent.actions,
                    onChoose: onChooseHandAction,
                  })
                } else if (intent.actions.length === 1) {
                  onChooseHandAction(intent.actions[0])
                } else if (isPlayableCard) {
                  onPlayCard?.(index)
                }
              }}
              onDragStart={isDraggable ? (event) => {
                onDragIntent({ type: 'hand-card', handIndex: index })
                dragData(event, { type: 'card', handIndex: index, playerId: player.id })
              } : undefined}
              onDragEnd={() => onDragIntent(null)}
              title={card.name}
            >
              <ReadOnlyArtCard card={card} className="online-hand-card" />
            </button>
          </div>
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
  onDropCardOnBase,
  onDropCardOnUnit,
  playableCardHandIndexes,
  hideableCardHandIndexes,
  canAttachCard,
  onPlayCard,
  onAttachCard,
  canMoveUnit,
  onMoveUnit,
  canSummonChampion,
  dragIntent = null,
  handCardIntents = {},
  unitMoveIntents = {},
  onDragIntent = () => undefined,
  onChooseHandAction = () => undefined,
  onSummonChampion,
  targetSelection,
  onSelectUnitTarget,
}: {
  isViewer: boolean
  player: Player
  placement: 'opponent' | 'viewer' | 'shared'
  victoryScore: number
  mulliganSelection?: { selectedIndexes: number[]; onToggle: (index: number) => void }
  canPlayUnit?: boolean
  onPlayUnit?: (handIndex: number, battlefieldId?: string) => void
  onDropCardOnBase?: (handIndex: number) => void
  onDropCardOnUnit?: (handIndex: number, unitId: string) => void
  playableCardHandIndexes?: number[]
  hideableCardHandIndexes?: number[]
  canAttachCard?: boolean
  onPlayCard?: (handIndex: number) => void
  onAttachCard?: (handIndex: number, targetUnitId: string) => void
  canMoveUnit?: boolean
  onMoveUnit?: (unitId: string, battlefieldId: string) => void
  canSummonChampion?: boolean
  dragIntent?: DragIntent | null
  handCardIntents?: Record<number, HandCardIntent>
  unitMoveIntents?: Record<string, UnitMoveIntent>
  onDragIntent?: (intent: DragIntent | null) => void
  onChooseHandAction?: (choice: BoardActionChoice) => void
  onSummonChampion?: () => void
  targetSelection?: { kind: 'unit' | 'lane'; excludeUnitIds?: string[] }
  onSelectUnitTarget?: (unitId: string) => void
}) {
  const dragData = useDragData()
  const handZone = (
    <section className="online-hand-zone" aria-label={`${player.name} hand`}>
      <OnlineHandZone
        isViewer={isViewer}
        player={player}
        mulliganSelection={isViewer ? mulliganSelection : undefined}
        canPlayUnit={isViewer ? canPlayUnit : false}
        playableCardHandIndexes={isViewer ? playableCardHandIndexes : []}
        hideableCardHandIndexes={isViewer ? hideableCardHandIndexes : []}
        canAttachCard={isViewer ? canAttachCard : false}
        handCardIntents={isViewer ? handCardIntents : {}}
        onPlayCard={onPlayCard}
        onChooseHandAction={onChooseHandAction}
        onDragIntent={onDragIntent}
      />
    </section>
  )

  const isChampionPlayable = Boolean(isViewer && canSummonChampion && player.champion && !player.championSummoned)
  const championZone = (
    <section className="mat-zone champion-zone fixed-card-zone">
      <span className="zone-label">Champion</span>
      {player.champion && !player.championSummoned ? (
        <ReadOnlyArtCard
          card={player.champion}
          className={`online-zone-card ${isChampionPlayable ? 'playable' : ''}`.trim()}
          onClick={isChampionPlayable ? () => onSummonChampion?.() : undefined}
          draggable={isChampionPlayable}
          onDragStart={isChampionPlayable ? (event) => {
            onDragIntent({ type: 'champion' })
            dragData(event, { type: 'champion' })
          } : undefined}
          onDragEnd={() => onDragIntent(null)}
          title={isChampionPlayable ? `Summon ${player.champion.name}` : player.champion.name}
        />
      ) : (
        <div className="empty-slot">No champion</div>
      )}
    </section>
  )

  const legendZone = (
    <section className="mat-zone legend-zone fixed-card-zone">
      <span className="zone-label">Legend</span>
      {player.legend ? <ReadOnlyArtCard card={player.legend} className="online-zone-card" /> : <div className="empty-slot">No legend</div>}
    </section>
  )

  const draggedHandIntent = dragIntent?.type === 'hand-card' ? handCardIntents[dragIntent.handIndex] : null
  const canDropDraggedHandOnBase = Boolean(isViewer && draggedHandIntent?.canUseBase)
  const canDropDraggedChampionOnBase = Boolean(isViewer && dragIntent?.type === 'champion' && isChampionPlayable)
  const canDropDraggedUnitOnBase = Boolean(isViewer && dragIntent?.type === 'unit' && unitMoveIntents[dragIntent.unitId]?.canMoveToBase)
  const hasPotentialBaseDrop = Object.values(handCardIntents).some((intent) => intent.canUseBase)
  const hasPotentialUnitBaseDrop = Object.values(unitMoveIntents).some((intent) => intent.canMoveToBase)
  const canDropOnBase = canDropDraggedHandOnBase || canDropDraggedChampionOnBase || canDropDraggedUnitOnBase
  const baseBlocksCurrentDrag = Boolean(isViewer && dragIntent && !canDropOnBase)
  const baseZone = (
    <section
      className={[
        'mat-zone base-zone flexible-card-zone',
        isViewer && (hasPotentialBaseDrop || hasPotentialUnitBaseDrop || isChampionPlayable) ? 'drop-zone' : '',
        canDropOnBase ? 'drop-zone-active' : '',
        baseBlocksCurrentDrag ? 'drop-zone-blocked' : '',
      ].filter(Boolean).join(' ')}
      onDragOver={canDropOnBase ? (event) => event.preventDefault() : undefined}
      onDrop={canDropOnBase ? (event) => {
        event.preventDefault()
        const payload = readDragData(event)
        if (payload?.type === 'card') {
          if (onDropCardOnBase) onDropCardOnBase(payload.handIndex)
          else if (canPlayUnit) onPlayUnit?.(payload.handIndex)
        }
        if (payload?.type === 'champion' && isChampionPlayable) onSummonChampion?.()
        if (payload?.type === 'unit' && canDropDraggedUnitOnBase) onMoveUnit?.(payload.unitId, 'base')
      } : undefined}
    >
      <span className="zone-label">Base</span>
      <div className="unit-row">
        {player.base.map((unit) => {
          const moveIntent = unitMoveIntents[unit.uid]
          const isMovable = Boolean(isViewer && canMoveUnit && !targetSelection && moveIntent && (moveIntent.battlefieldIds.length > 0 || moveIntent.canMoveToBase))
          const acceptsDraggedCard = Boolean(draggedHandIntent?.attachUnitIds.includes(unit.uid))
          return (
            <ReadOnlyUnit
              key={unit.uid}
              unit={unit}
              draggable={isMovable}
              onDragStart={isMovable ? (event) => {
                onDragIntent({ type: 'unit', unitId: unit.uid })
                dragData(event, { type: 'unit', unitId: unit.uid })
              } : undefined}
              onDragEnd={() => onDragIntent(null)}
              canDropCard={acceptsDraggedCard || (!dragIntent && Boolean(isViewer && canAttachCard))}
              onDropCard={onDropCardOnUnit ?? onAttachCard}
              targetable={targetSelection?.kind === 'unit' && !targetSelection?.excludeUnitIds?.includes(unit.uid)}
              onSelectTarget={onSelectUnitTarget}
            />
          )
        })}
        {player.base.length === 0 && <span className="zone-empty-text">No units</span>}
      </div>
      {(player.baseGear ?? []).length > 0 && (
        <div className="unit-row online-gear-row">
          {player.baseGear.map((gear) => (
            <ReadOnlyArtCard key={gear.uid} card={gear} className="online-unit-card online-gear-card" title={gear.name} />
          ))}
        </div>
      )}
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
          <strong>{player.xp ?? 0} XP</strong>
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
  onDropCardOnBattlefield,
  onDropCardOnUnit,
  canDropCardPlay,
  canDropHiddenCard,
  canAttachCard,
  dragIntent,
  handCardIntents,
  unitMoveIntents,
  onDragIntent,
  canMoveUnit,
  onMoveUnit,
  targetSelection,
  onSelectUnitTarget,
  onSelectLaneTarget,
}: {
  cardsById: Map<string, Card>
  game: GameState
  matchPlayers: MatchPlayer[]
  viewerPlayerId: number
  canPlayUnit?: boolean
  onPlayUnit?: (handIndex: number, battlefieldId?: string) => void
  onDropCardOnBattlefield?: (handIndex: number, battlefieldId: string) => void
  onDropCardOnUnit?: (handIndex: number, unitId: string) => void
  canDropCardPlay?: boolean
  canDropHiddenCard?: boolean
  canAttachCard?: boolean
  dragIntent: DragIntent | null
  handCardIntents: Record<number, HandCardIntent>
  unitMoveIntents: Record<string, UnitMoveIntent>
  onDragIntent: (intent: DragIntent | null) => void
  canMoveUnit?: boolean
  onMoveUnit?: (unitId: string, battlefieldId: string) => void
  targetSelection?: { kind: 'unit' | 'lane'; excludeUnitIds?: string[] }
  onSelectUnitTarget?: (unitId: string) => void
  onSelectLaneTarget?: (laneId: string) => void
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
              canDropPlayedUnit={Boolean(canPlayUnit)}
              canDropCardPlay={Boolean(canDropCardPlay)}
              canDropHiddenCard={Boolean(canDropHiddenCard)}
              canDropMovedUnit={Boolean(canMoveUnit)}
              canDropAttachedCard={Boolean(canAttachCard)}
              canMoveUnit={canMoveUnit}
              dragIntent={dragIntent}
              handCardIntents={handCardIntents}
              unitMoveIntents={unitMoveIntents}
              onDragIntent={onDragIntent}
              onPlayUnit={onPlayUnit}
              onDropCard={onDropCardOnBattlefield}
              onDropCardOnUnit={onDropCardOnUnit}
              onMoveUnit={onMoveUnit}
              targetSelection={targetSelection}
              onSelectUnitTarget={onSelectUnitTarget}
              onSelectLaneTarget={onSelectLaneTarget}
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
  onDropCardOnBase,
  onDropCardOnBattlefield,
  onDropCardOnUnit,
  playableCardHandIndexes,
  hideableCardHandIndexes,
  onPlayCard,
  canAttachCard,
  handCardIntents = {},
  unitMoveIntents = {},
  onChooseHandAction,
  onAttachCard,
  canMoveUnit,
  onMoveUnit,
  canSummonChampion,
  onSummonChampion,
  targetSelection,
  onSelectUnitTarget,
  onSelectLaneTarget,
}: {
  cards: Card[]
  game: GameState
  matchPlayers?: MatchPlayer[]
  viewerPlayerId: number
  mulliganSelection?: { selectedIndexes: number[]; onToggle: (index: number) => void }
  canPlayUnit?: boolean
  onPlayUnit?: (handIndex: number, battlefieldId?: string) => void
  onDropCardOnBase?: (handIndex: number) => void
  onDropCardOnBattlefield?: (handIndex: number, battlefieldId: string) => void
  onDropCardOnUnit?: (handIndex: number, unitId: string) => void
  playableCardHandIndexes?: number[]
  hideableCardHandIndexes?: number[]
  onPlayCard?: (handIndex: number) => void
  canAttachCard?: boolean
  handCardIntents?: Record<number, HandCardIntent>
  unitMoveIntents?: Record<string, UnitMoveIntent>
  onChooseHandAction?: (choice: BoardActionChoice) => void
  onAttachCard?: (handIndex: number, targetUnitId: string) => void
  canMoveUnit?: boolean
  onMoveUnit?: (unitId: string, battlefieldId: string) => void
  canSummonChampion?: boolean
  onSummonChampion?: () => void
  targetSelection?: { kind: 'unit' | 'lane'; excludeUnitIds?: string[] }
  onSelectUnitTarget?: (unitId: string) => void
  onSelectLaneTarget?: (laneId: string) => void
}) {
  const [dragIntent, setDragIntent] = useState<DragIntent | null>(null)
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
        {opponent && (
          <OnlinePlayerMat
            isViewer={false}
            placement="opponent"
            player={opponent}
            victoryScore={hydratedGame.victoryScore}
            targetSelection={targetSelection}
            onSelectUnitTarget={onSelectUnitTarget}
          />
        )}
        <BattlefieldZone
          cardsById={cardsById}
          game={hydratedGame}
          matchPlayers={matchPlayers ?? []}
          viewerPlayerId={viewerPlayerId}
          canPlayUnit={canPlayUnit}
          onPlayUnit={onPlayUnit}
          onDropCardOnBattlefield={onDropCardOnBattlefield}
          onDropCardOnUnit={onDropCardOnUnit ?? onAttachCard}
          canDropCardPlay={Boolean(playableCardHandIndexes?.length)}
          canDropHiddenCard={Boolean(hideableCardHandIndexes?.length)}
          canAttachCard={canAttachCard}
          dragIntent={dragIntent}
          handCardIntents={handCardIntents}
          unitMoveIntents={unitMoveIntents}
          onDragIntent={setDragIntent}
          canMoveUnit={canMoveUnit}
          onMoveUnit={onMoveUnit}
          targetSelection={targetSelection}
          onSelectUnitTarget={onSelectUnitTarget}
          onSelectLaneTarget={onSelectLaneTarget}
        />
        {viewer && (
          <OnlinePlayerMat
            isViewer
            placement="viewer"
            player={viewer}
            victoryScore={hydratedGame.victoryScore}
            mulliganSelection={mulliganSelection}
            canPlayUnit={canPlayUnit}
            onPlayUnit={onPlayUnit}
            onDropCardOnBase={onDropCardOnBase}
            onDropCardOnUnit={onDropCardOnUnit ?? onAttachCard}
            playableCardHandIndexes={playableCardHandIndexes}
            hideableCardHandIndexes={hideableCardHandIndexes}
            onPlayCard={onPlayCard}
            canAttachCard={canAttachCard}
            dragIntent={dragIntent}
            handCardIntents={handCardIntents}
            unitMoveIntents={unitMoveIntents}
            onDragIntent={setDragIntent}
            onChooseHandAction={onChooseHandAction ?? (() => undefined)}
            onAttachCard={onAttachCard}
            canMoveUnit={canMoveUnit}
            onMoveUnit={onMoveUnit}
            canSummonChampion={canSummonChampion}
            onSummonChampion={onSummonChampion}
            targetSelection={targetSelection}
            onSelectUnitTarget={onSelectUnitTarget}
          />
        )}
      </section>
    )
  }

  const orderedPlayers = [...opponents, ...(viewer ? [viewer] : [])]
  return (
    <section className="online-shared-playmat shared-table-playmat">
      <BattlefieldZone
        cardsById={cardsById}
        game={hydratedGame}
        matchPlayers={matchPlayers ?? []}
        viewerPlayerId={viewerPlayerId}
        canPlayUnit={canPlayUnit}
        onPlayUnit={onPlayUnit}
        onDropCardOnBattlefield={onDropCardOnBattlefield}
        onDropCardOnUnit={onDropCardOnUnit ?? onAttachCard}
        canDropCardPlay={Boolean(playableCardHandIndexes?.length)}
        canDropHiddenCard={Boolean(hideableCardHandIndexes?.length)}
        canAttachCard={canAttachCard}
        dragIntent={dragIntent}
        handCardIntents={handCardIntents}
        unitMoveIntents={unitMoveIntents}
        onDragIntent={setDragIntent}
        canMoveUnit={canMoveUnit}
        onMoveUnit={onMoveUnit}
        targetSelection={targetSelection}
        onSelectUnitTarget={onSelectUnitTarget}
        onSelectLaneTarget={onSelectLaneTarget}
      />

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
            onDropCardOnBase={player.id === viewerPlayerId ? onDropCardOnBase : undefined}
            onDropCardOnUnit={player.id === viewerPlayerId ? onDropCardOnUnit ?? onAttachCard : undefined}
            playableCardHandIndexes={player.id === viewerPlayerId ? playableCardHandIndexes : []}
            hideableCardHandIndexes={player.id === viewerPlayerId ? hideableCardHandIndexes : []}
            onPlayCard={onPlayCard}
            canAttachCard={player.id === viewerPlayerId ? canAttachCard : false}
            dragIntent={dragIntent}
            handCardIntents={player.id === viewerPlayerId ? handCardIntents : {}}
            unitMoveIntents={player.id === viewerPlayerId ? unitMoveIntents : {}}
            onDragIntent={setDragIntent}
            onChooseHandAction={onChooseHandAction ?? (() => undefined)}
            onAttachCard={onAttachCard}
            canMoveUnit={player.id === viewerPlayerId ? canMoveUnit : false}
            onMoveUnit={onMoveUnit}
            canSummonChampion={player.id === viewerPlayerId ? canSummonChampion : false}
            onSummonChampion={onSummonChampion}
            targetSelection={targetSelection}
            onSelectUnitTarget={onSelectUnitTarget}
          />
        ))}
      </section>
    </section>
  )
}
