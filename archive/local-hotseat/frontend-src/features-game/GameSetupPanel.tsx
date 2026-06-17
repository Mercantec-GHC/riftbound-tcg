import type { Dispatch, SetStateAction } from 'react'
import { orderedPlayerIds, selectedBattlefieldIdsForSetup, validateSetup } from '../../gameRules'
import { userCanAccessDeck } from '../../domain/decks/deckRules'
import { gameModes, type Card, type GameDeckAssignment, type GameMode, type SavedDeck, type SetupState, type UserProfile } from '../../models'

type BattlefieldOption = {
  id: string
  name: string
  claim: number
}

export function GameSetupPanel({
  battlefieldOptions,
  cards,
  deckAssignments,
  decks,
  profiles,
  setSetup,
  setup,
  startConfiguredGame,
  updateSetup,
}: {
  battlefieldOptions: BattlefieldOption[]
  cards: Card[]
  deckAssignments: (GameDeckAssignment | null)[]
  decks: SavedDeck[]
  profiles: UserProfile[]
  setSetup: Dispatch<SetStateAction<SetupState>>
  setup: SetupState
  startConfiguredGame: () => void
  updateSetup: (setup: SetupState) => void
}) {
  const mode = gameModes[setup.mode]
  const errors = validateSetup(setup, deckAssignments, cards)
  const selectedBattlefields = selectedBattlefieldIdsForSetup(setup, deckAssignments)

  return (
    <section className="setup-panel">
      <label>
        Mode
        <select
          value={setup.mode}
          onChange={(event) => updateSetup({ ...setup, mode: event.target.value as GameMode })}
        >
          {Object.entries(gameModes).map(([id, config]) => (
            <option key={id} value={id}>
              {config.label}
            </option>
          ))}
        </select>
      </label>
      <label>
        First player
        <select
          value={setup.firstPlayerId}
          onChange={(event) => updateSetup({ ...setup, firstPlayerId: Number(event.target.value) })}
        >
          {Array.from({ length: mode.playerCount }, (_, index) => (
            <option key={index} value={index}>
              {setup.names[index] || `Player ${index + 1}`}
            </option>
          ))}
        </select>
      </label>
      <div className="setup-summary">
        <strong>{mode.victoryScore} point victory</strong>
        <small>Turn order: {orderedPlayerIds(setup).map((id) => setup.names[id] || `P${id + 1}`).join(' -> ')}</small>
        <small>Battlefields: {selectedBattlefields.length}/{mode.battlefieldCount}</small>
      </div>
      {Array.from({ length: mode.playerCount }, (_, index) => {
        const selectedUserId = setup.userIds[index] || profiles[0]?.id || ''
        const contributesBattlefield = !(mode.firstPlayerSkipsBattlefield && index === setup.firstPlayerId)
        return (
          <div className="setup-player" key={index}>
            <label>
              Profile
              <select
                value={selectedUserId}
                onChange={(event) => {
                  const userIds = [...setup.userIds]
                  const deckIds = [...setup.deckIds]
                  userIds[index] = event.target.value
                  if (!decks.some((deck) => deck.id === deckIds[index] && userCanAccessDeck(deck, event.target.value))) deckIds[index] = ''
                  updateSetup({ ...setup, userIds, deckIds })
                }}
              >
                {profiles.map((profile) => (
                  <option key={profile.id} value={profile.id}>
                    {profile.displayName}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Player {index + 1}
              <input
                value={setup.names[index]}
                onChange={(event) => {
                  const names = [...setup.names]
                  names[index] = event.target.value
                  setSetup({ ...setup, names })
                }}
              />
            </label>
            {mode.teams && (
              <label>
                Team
                <select
                  value={setup.teamIds[index]}
                  onChange={(event) => {
                    const teamIds = [...setup.teamIds]
                    teamIds[index] = Number(event.target.value)
                    updateSetup({ ...setup, teamIds })
                  }}
                >
                  <option value={0}>Team 1</option>
                  <option value={1}>Team 2</option>
                </select>
              </label>
            )}
            <label>
              Deck
              <select
                value={setup.deckIds[index]}
                onChange={(event) => {
                  const deckIds = [...setup.deckIds]
                  deckIds[index] = event.target.value
                  const selected = decks.find((deck) => deck.id === event.target.value)
                  const selectedBattlefieldIds = [...setup.selectedBattlefieldIds]
                  if (selected?.battlefieldDeckIds[0]) selectedBattlefieldIds[index] = selected.battlefieldDeckIds[0]
                  updateSetup({ ...setup, deckIds, selectedBattlefieldIds })
                }}
              >
                <option value="">No deck</option>
                {decks
                  .filter((deck) => userCanAccessDeck(deck, selectedUserId))
                  .map((deck) => (
                    <option key={deck.id} value={deck.id}>
                      {deck.name} ({deck.visibility})
                    </option>
                  ))}
              </select>
            </label>
            <label>
              Battlefield
              <select
                value={setup.selectedBattlefieldIds[index] || setup.battlefieldIds[index]}
                disabled={!contributesBattlefield}
                onChange={(event) => {
                  const selectedBattlefieldIds = [...setup.selectedBattlefieldIds]
                  selectedBattlefieldIds[index] = event.target.value
                  updateSetup({ ...setup, selectedBattlefieldIds })
                }}
              >
                {battlefieldOptions.map((field) => (
                  <option key={field.id} value={field.id}>
                    {field.name} ({field.claim})
                  </option>
                ))}
              </select>
            </label>
            {!contributesBattlefield && <small className="rune-note">First player contributes no battlefield in this mode.</small>}
          </div>
        )
      })}
      <div className="setup-actions">
        {errors.length > 0 && <p className="deck-errors">{errors.join(' ')}</p>}
        <button type="button" disabled={errors.length > 0} onClick={startConfiguredGame}>
          Start setup / mulligan
        </button>
      </div>
    </section>
  )
}
