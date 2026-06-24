import type { Card } from '../../shared/models'
import { DeckCardPool } from './DeckCardPool'
import { DeckForm } from './DeckForm'
import { SavedDecksPanel } from './SavedDecksPanel'
import { SelectedDeckList } from './SelectedDeckList'
import type { useDeckBuilder } from './useDeckBuilder'

type DeckBuilderState = ReturnType<typeof useDeckBuilder>

export function DeckBuilderPage({
  cardLibrary,
  deckBuilder,
}: {
  cardLibrary: Card[]
  deckBuilder: DeckBuilderState
}) {
  return (
    <section className="deck-builder">
      <div className="deck-workspace">
        <div className="deck-main">
          <div>
            <p className="eyebrow">deck builder</p>
            <h2>Create, save, export, and import decks</h2>
            <p>
              A deck has a main deck, rune deck, and battlefield deck. Champion and Legend must share the same tag.
            </p>
          </div>

          <DeckForm
            deckDraft={deckBuilder.deckDraft}
            deckImportText={deckBuilder.deckImportText}
            deckStatus={deckBuilder.deckStatus}
            deckValidation={deckBuilder.deckValidation}
            selectedChampion={deckBuilder.selectedChampion}
            selectedChampionTags={deckBuilder.selectedChampionTags}
            selectedLegend={deckBuilder.selectedLegend}
            selectedLegendTags={deckBuilder.selectedLegendTags}
            onDeckDraftChange={deckBuilder.updateDeckDraft}
            onDeckImportTextChange={deckBuilder.setDeckImportText}
            onExportDeck={() => deckBuilder.exportDeck()}
            onImportDeck={deckBuilder.importDeck}
            onNewDeck={deckBuilder.newDeck}
            onSaveDeck={deckBuilder.saveDeck}
          />

          <SavedDecksPanel
            decks={deckBuilder.savedDecks}
            onDeleteDeck={deckBuilder.deleteDeck}
            onExportDeck={deckBuilder.exportDeck}
            onLoadDeck={deckBuilder.loadDeck}
          />

          <DeckCardPool
            cardLibrary={cardLibrary}
            deckDraft={deckBuilder.deckDraft}
            deckDomainFilter={deckBuilder.deckDomainFilter}
            deckMaxCost={deckBuilder.deckMaxCost}
            deckMinMight={deckBuilder.deckMinMight}
            deckSearch={deckBuilder.deckSearch}
            deckSort={deckBuilder.deckSort}
            deckTab={deckBuilder.deckTab}
            deckTagFilter={deckBuilder.deckTagFilter}
            filteredBattlefields={deckBuilder.filteredBattlefields}
            filteredChampions={deckBuilder.filteredChampions}
            filteredLegends={deckBuilder.filteredLegends}
            filteredMainDeckCards={deckBuilder.filteredMainDeckCards}
            filteredRunes={deckBuilder.filteredRunes}
            selectedChampion={deckBuilder.selectedChampion}
            selectedLegend={deckBuilder.selectedLegend}
            onDeckDomainFilterChange={deckBuilder.setDeckDomainFilter}
            onDeckDraftChange={deckBuilder.updateDeckDraft}
            onDeckMaxCostChange={deckBuilder.setDeckMaxCost}
            onDeckMinMightChange={deckBuilder.setDeckMinMight}
            onDeckSearchChange={deckBuilder.setDeckSearch}
            onDeckSortChange={deckBuilder.setDeckSort}
            onDeckTabChange={deckBuilder.setDeckTab}
            onDeckTagFilterChange={deckBuilder.setDeckTagFilter}
          />
        </div>

        <SelectedDeckList
          cardLibrary={cardLibrary}
          deckDraft={deckBuilder.deckDraft}
          sections={deckBuilder.selectedDeckSections}
          onDeckDraftChange={deckBuilder.updateDeckDraft}
        />
      </div>
    </section>
  )
}
