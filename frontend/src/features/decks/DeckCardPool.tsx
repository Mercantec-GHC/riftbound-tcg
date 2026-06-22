import { CardFace } from '../../shared/ui/CardFace'
import type { DeckSort } from '../../shared/domain/cards/cardFilters'
import { cardsShareTag } from '../../shared/domain/decks/deckUtils'
import { domains, type Card, type Domain, type SavedDeck } from '../../shared/models'
import type { DeckTab } from './deckBuilderTypes'

export function DeckCardPool({
  deckDraft,
  deckDomainFilter,
  deckMaxCost,
  deckMinMight,
  deckSearch,
  deckSort,
  deckTab,
  deckTagFilter,
  filteredBattlefields,
  filteredChampions,
  filteredLegends,
  filteredMainDeckCards,
  filteredRunes,
  selectedChampion,
  selectedLegend,
  onDeckDomainFilterChange,
  onDeckDraftChange,
  onDeckMaxCostChange,
  onDeckMinMightChange,
  onDeckSearchChange,
  onDeckSortChange,
  onDeckTabChange,
  onDeckTagFilterChange,
}: {
  deckDraft: SavedDeck
  deckDomainFilter: '' | Domain
  deckMaxCost: string
  deckMinMight: string
  deckSearch: string
  deckSort: DeckSort
  deckTab: DeckTab
  deckTagFilter: string
  filteredBattlefields: Card[]
  filteredChampions: Card[]
  filteredLegends: Card[]
  filteredMainDeckCards: Card[]
  filteredRunes: Card[]
  selectedChampion: Card | undefined
  selectedLegend: Card | undefined
  onDeckDomainFilterChange: (domain: '' | Domain) => void
  onDeckDraftChange: (deck: SavedDeck) => void
  onDeckMaxCostChange: (maxCost: string) => void
  onDeckMinMightChange: (minMight: string) => void
  onDeckSearchChange: (search: string) => void
  onDeckSortChange: (sort: DeckSort) => void
  onDeckTabChange: (tab: DeckTab) => void
  onDeckTagFilterChange: (tag: string) => void
}) {
  function clearFilters() {
    onDeckSearchChange('')
    onDeckTagFilterChange('')
    onDeckDomainFilterChange('')
    onDeckMaxCostChange('')
    onDeckMinMightChange('')
    onDeckSortChange('name-asc')
  }

  return (
    <section className="deck-card-pool">
      <div className="deck-tabs" role="tablist" aria-label="Deck builder sections">
        <button className={deckTab === 'legend' ? 'active' : ''} type="button" onClick={() => onDeckTabChange('legend')}>Legend</button>
        <button className={deckTab === 'champion' ? 'active' : ''} type="button" onClick={() => onDeckTabChange('champion')}>Champion</button>
        <button className={deckTab === 'main' ? 'active' : ''} type="button" onClick={() => onDeckTabChange('main')}>Main deck</button>
        <button className={deckTab === 'runes' ? 'active' : ''} type="button" onClick={() => onDeckTabChange('runes')}>Runes</button>
        <button className={deckTab === 'battlefields' ? 'active' : ''} type="button" onClick={() => onDeckTabChange('battlefields')}>Battlefields</button>
      </div>

      <div className="deck-filters">
        <label className="wide">
          Search name, text, type, domain, cost, might
          <input value={deckSearch} onChange={(event) => onDeckSearchChange(event.target.value)} placeholder="Search cards..." />
        </label>
        <label>
          Tag
          <input value={deckTagFilter} onChange={(event) => onDeckTagFilterChange(event.target.value)} placeholder="e.g. Jinx" />
        </label>
        <label>
          Domain
          <select value={deckDomainFilter} onChange={(event) => onDeckDomainFilterChange(event.target.value as '' | Domain)}>
            <option value="">Any domain</option>
            {domains.map((domain) => (
              <option key={domain} value={domain}>{domain}</option>
            ))}
          </select>
        </label>
        <label>
          Max cost
          <input min="0" type="number" value={deckMaxCost} onChange={(event) => onDeckMaxCostChange(event.target.value)} />
        </label>
        <label>
          Min might
          <input min="0" type="number" value={deckMinMight} onChange={(event) => onDeckMinMightChange(event.target.value)} />
        </label>
        <label>
          Sort
          <select value={deckSort} onChange={(event) => onDeckSortChange(event.target.value as DeckSort)}>
            <option value="name-asc">Name A-Z</option>
            <option value="name-desc">Name Z-A</option>
            <option value="cost-asc">Cost low-high</option>
            <option value="cost-desc">Cost high-low</option>
            <option value="might-asc">Might low-high</option>
            <option value="might-desc">Might high-low</option>
          </select>
        </label>
        <button type="button" onClick={clearFilters}>
          Clear filters
        </button>
      </div>

      {deckTab === 'legend' && (
        <>
          <h3>Choose Legend ({filteredLegends.length})</h3>
          <div className="library">
            {filteredLegends.map((card) => (
              <button
                className={`mini-card ${card.domain.toLowerCase()} ${deckDraft.legendId === card.id ? 'selected' : ''}`}
                key={card.id}
                type="button"
                onClick={() => onDeckDraftChange({ ...deckDraft, legendId: card.id, championId: selectedChampion && cardsShareTag(card, selectedChampion) ? deckDraft.championId : '' })}
              >
                <CardFace card={card} compact />
              </button>
            ))}
          </div>
        </>
      )}

      {deckTab === 'champion' && (
        <>
          <h3>Choose Champion ({filteredChampions.length})</h3>
          <p className="rune-note">Only Champions sharing a tag with the selected Legend are shown.</p>
          <div className="library">
            {filteredChampions.map((card) => (
              <button
                className={`mini-card ${card.domain.toLowerCase()} ${deckDraft.championId === card.id ? 'selected' : ''}`}
                key={card.id}
                type="button"
                onClick={() => onDeckDraftChange({ ...deckDraft, championId: card.id })}
              >
                <CardFace card={card} compact />
              </button>
            ))}
          </div>
        </>
      )}

      {deckTab === 'main' && (
        <>
          <h3>Main deck cards ({deckDraft.mainDeckIds.length}/40 · {filteredMainDeckCards.length} shown)</h3>
          <div className="library">
            {filteredMainDeckCards.map((card) => (
              <button
                className={`mini-card ${card.domain.toLowerCase()}`}
                disabled={deckDraft.mainDeckIds.length >= 40}
                key={card.id}
                type="button"
                onClick={() => onDeckDraftChange({ ...deckDraft, mainDeckIds: [...deckDraft.mainDeckIds, card.id] })}
              >
                <CardFace card={card} compact />
              </button>
            ))}
          </div>
        </>
      )}

      {deckTab === 'runes' && (
        <>
          <h3>Rune deck cards ({deckDraft.runeDeckIds.length}+ / min 12 · {filteredRunes.length} shown)</h3>
          <p className="rune-note">Must include at least one rune from each Legend domain: {selectedLegend?.domains.join(' / ') || 'choose a Legend'}.</p>
          <div className="library">
            {filteredRunes.map((card) => (
              <button
                className={`mini-card ${card.domain.toLowerCase()}`}
                key={card.id}
                type="button"
                onClick={() => onDeckDraftChange({ ...deckDraft, runeDeckIds: [...deckDraft.runeDeckIds, card.id] })}
              >
                <CardFace card={card} compact />
              </button>
            ))}
          </div>
        </>
      )}

      {deckTab === 'battlefields' && (
        <>
          <h3>Battlefield deck cards ({deckDraft.battlefieldDeckIds.length}/3 · {filteredBattlefields.length} shown)</h3>
          <p className="rune-note">Must contain at least 1 and at most 3 battlefields.</p>
          <div className="library">
            {filteredBattlefields.map((card) => (
              <button
                className={`mini-card ${card.domain.toLowerCase()} ${deckDraft.battlefieldDeckIds.includes(card.id) ? 'selected' : ''}`}
                disabled={deckDraft.battlefieldDeckIds.length >= 3 && !deckDraft.battlefieldDeckIds.includes(card.id)}
                key={card.id}
                type="button"
                onClick={() => onDeckDraftChange({ ...deckDraft, battlefieldDeckIds: deckDraft.battlefieldDeckIds.includes(card.id) ? deckDraft.battlefieldDeckIds.filter((id) => id !== card.id) : [...deckDraft.battlefieldDeckIds, card.id] })}
              >
                <CardFace card={card} compact />
              </button>
            ))}
          </div>
        </>
      )}
    </section>
  )
}
