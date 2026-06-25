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
  const matches = candidates
    .filter((action) => payloadMatchesServerSchema(action, payload))
    .sort((left, right) =>
      Object.keys(right.payloadSchema ?? {}).length - Object.keys(left.payloadSchema ?? {}).length)
  return matches[0] ?? null
}

export function hasServerApprovedAction(legalActions: LegalAction[], playerId: number, type: string): boolean {
  return legalActions.some((action) => action.type === type && action.playerId === playerId)
}

export function serverApprovedHandIndexes(legalActions: LegalAction[], playerId: number, type: string): number[] {
  return legalActions
    .filter((action) => action.type === type && action.playerId === playerId)
    .map((action) => Number(action.payloadSchema?.handIndex))
    .filter((index) => Number.isInteger(index))
}
