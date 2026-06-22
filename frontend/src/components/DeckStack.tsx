type DeckStackKind = 'main' | 'rune' | 'trash'

export function DeckStack({ count, kind }: { count: number; kind: DeckStackKind }) {
  const isEmptyTrash = kind === 'trash' && count === 0
  const className = [
    'deck-stack',
    `${kind}-stack`,
    isEmptyTrash ? 'empty-deck-stack' : '',
  ].filter(Boolean).join(' ')

  return (
    <div className={className} aria-label={`${kind} deck has ${count} cards`}>
      {isEmptyTrash ? <span>Empty</span> : count}
    </div>
  )
}
