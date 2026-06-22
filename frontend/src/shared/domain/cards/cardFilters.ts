import type { Card, Domain } from '../../models'

export type DeckSort = 'name-asc' | 'name-desc' | 'cost-asc' | 'cost-desc' | 'might-asc' | 'might-desc'

export type CardLibraryFilters = {
  search: string
  tag: string
  domain: '' | Domain
  maxCost: string
  minMight: string
  sort: DeckSort
}

export function filterAndSortCards(cards: Card[], filters: CardLibraryFilters) {
  const search = filters.search.trim().toLowerCase()
  const tag = filters.tag.trim().toLowerCase()
  const maxCost = filters.maxCost === '' ? null : Number(filters.maxCost)
  const minMight = filters.minMight === '' ? null : Number(filters.minMight)

  return [...cards]
    .filter((card) => {
      const haystack = [
        card.name,
        card.kind,
        card.cardType,
        card.domain,
        card.domains.join(' '),
        card.tags.join(' '),
        card.text,
        String(card.cost),
        String(card.might),
      ].join(' ').toLowerCase()
      if (search && !haystack.includes(search)) return false
      if (tag && !card.tags.some((cardTag) => cardTag.toLowerCase().includes(tag))) return false
      if (filters.domain && !card.domains.includes(filters.domain)) return false
      if (maxCost !== null && card.cost > maxCost) return false
      if (minMight !== null && card.might < minMight) return false
      return true
    })
    .sort((first, second) => {
      switch (filters.sort) {
        case 'name-desc':
          return second.name.localeCompare(first.name)
        case 'cost-asc':
          return first.cost - second.cost || first.name.localeCompare(second.name)
        case 'cost-desc':
          return second.cost - first.cost || first.name.localeCompare(second.name)
        case 'might-asc':
          return first.might - second.might || first.name.localeCompare(second.name)
        case 'might-desc':
          return second.might - first.might || first.name.localeCompare(second.name)
        case 'name-asc':
        default:
          return first.name.localeCompare(second.name)
      }
    })
}
