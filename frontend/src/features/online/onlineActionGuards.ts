import type { LegalAction } from '../../shared/api'

function payloadValueMatches(expected: unknown, actual: unknown): boolean {
  if (Array.isArray(expected) || Array.isArray(actual)) {
    return JSON.stringify(expected) === JSON.stringify(actual)
  }

  return expected === actual
}

function payloadMatchesServerSchema(action: LegalAction, payload: Record<string, unknown>): boolean {
  const schema = action.payloadSchema ?? {}
  return Object.entries(schema).every(([key, expected]) => {
    if (payloadValueMatches(expected, payload[key])) return true

    if (key.endsWith('Ids') && Array.isArray(expected)) {
      const singularKey = `${key.slice(0, -3)}Id`
      return expected.some((item) => payloadValueMatches(item, payload[singularKey]))
    }

    return false
  })
}

export function findServerApprovedAction(
  legalActions: LegalAction[],
  playerId: number,
  type: string,
  payload: Record<string, unknown> = {},
): LegalAction | null {
  const candidates = legalActions.filter((action) => action.type === type && action.playerId === playerId)
  return candidates.find((action) => payloadMatchesServerSchema(action, payload)) ?? null
}

export function hasServerApprovedAction(legalActions: LegalAction[], playerId: number, type: string): boolean {
  return findServerApprovedAction(legalActions, playerId, type) !== null
}

export function serverApprovedHandIndexes(legalActions: LegalAction[], playerId: number, type: string): number[] {
  return legalActions
    .filter((action) => action.type === type && action.playerId === playerId)
    .map((action) => Number(action.payloadSchema?.handIndex))
    .filter((index) => Number.isInteger(index))
}
