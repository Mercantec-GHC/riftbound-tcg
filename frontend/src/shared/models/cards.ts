export type Domain = 'Fury' | 'Calm' | 'Mind' | 'Body' | 'Chaos' | 'Order'
export type CardKind = 'unit' | 'spell' | 'gear' | 'champion' | 'legend' | 'battlefield' | 'token' | 'rune'
export type EffectType = 'damage' | 'draw' | 'buff' | 'rally' | 'kill' | 'banish' | 'stun'
export type SpellSubtype = 'action' | 'reaction'

export type EffectStep = {
  type: EffectType
  amount: number
}

export type Effect = {
  type: EffectType
  amount: number
  steps?: EffectStep[]
}

export type Card = {
  id: string
  catalogId?: string
  name: string
  kind: CardKind
  tags: string[]
  domain: Domain
  domains: Domain[]
  cost: number
  might: number
  text: string
  image: string
  cardType: string
  supertype: string | null
  effect: Effect
}

export type RiftCodexCard = {
  id: string
  name: string
  riftbound_id: string | null
  tcgplayer_id: string | null
  collector_number: number | null
  attributes: {
    energy: number | null
    might: number | null
    power: number | null
  }
  classification: {
    type: string | null
    supertype: string | null
    rarity: string | null
    domain: string[] | null
  }
  text: {
    rich: string | null
    plain: string | null
    flavour: string | null
  }
  set: {
    set_id: string | null
    label: string | null
  }
  media: {
    image_url: string | null
    artist: string | null
    accessibility_text: string | null
  }
  tags: string[]
  orientation: string | null
  metadata: {
    clean_name: string | null
    updated_on: string | null
    alternate_art: boolean | null
    overnumbered: boolean | null
    signature: boolean | null
  }
}

export const domains: Domain[] = ['Fury', 'Calm', 'Mind', 'Body', 'Chaos', 'Order']
export const kinds: CardKind[] = ['unit', 'spell', 'gear']
export const customCardsKey = 'rift-prototype-custom-cards-v2'
export const localCardsEndpoint = '/api/local-cards'

export const fallbackBattlefieldOptions = [
  { id: 'skybridge', name: 'Skybridge Spire', claim: 2 },
  { id: 'emberfield', name: 'Emberfield Crossing', claim: 3 },
  { id: 'tidegate', name: 'Tidegate Ruins', claim: 2 },
  { id: 'rootmaze', name: 'Rootmaze Vault', claim: 2 },
  { id: 'glassmarket', name: 'Glassmarket Gate', claim: 3 },
  { id: 'thunderdock', name: 'Thunder Dock', claim: 2 },
]

export const blankCard: Card = {
  id: '',
  name: '',
  kind: 'unit',
  tags: [],
  domain: 'Fury',
  domains: ['Fury'],
  cost: 1,
  might: 1,
  image: 'âœ¨',
  text: '',
  cardType: 'Unit',
  supertype: null,
  effect: { type: 'rally', amount: 0 },
}
