import { useCallback, useEffect, useRef, useState } from 'react'
import { createGameFromSetup, orderedPlayerIds } from '../../gameRules'
import { defaultSetup, gameModes, type Card, type GameDeckAssignment, type GameState, type SavedDeck, type SetupState, type UserProfile } from '../../models'

function setupForMode(setup: SetupState, mode = setup.mode): SetupState {
  const config = gameModes[mode]
  const next = {
    ...setup,
    mode,
    playerCount: config.playerCount,
    firstPlayerId: Math.min(setup.firstPlayerId, config.playerCount - 1),
    setupStatus: 'configuring' as const,
  }
  return { ...next, turnOrder: orderedPlayerIds(next) }
}

export function useGameController({
  cards,
  decks,
  profiles,
  updateStats,
}: {
  cards: Card[]
  decks: SavedDeck[]
  profiles: UserProfile[]
  updateStats: (mapper: (profiles: UserProfile[]) => UserProfile[]) => void
}) {
  const [setup, setSetupState] = useState<SetupState>(() => setupForMode({
    ...defaultSetup,
    userIds: defaultSetup.userIds.map((userId) => userId || profiles[0]?.id || ''),
  }))
  const deckAssignments = useCallback((nextSetup = setup): (GameDeckAssignment | null)[] =>
    Array.from({ length: nextSetup.playerCount }, (_, index) => {
      const userId = nextSetup.userIds[index] || profiles[0]?.id || ''
      const deck = decks.find((candidate) => candidate.id === nextSetup.deckIds[index]) ?? null
      return { userId, deck }
    }), [decks, profiles, setup])

  const [game, setGame] = useState<GameState>(() => createGameFromSetup(cards, setup, deckAssignments(setup)))
  const recordedGameId = useRef('')
  const cardLibraryRef = useRef(cards)

  const activePlayer = game.players.find((player) => player.id === game.activePlayer) ?? game.players[0]

  const restart = useCallback((nextCards = cards, nextSetup = setup) => {
    recordedGameId.current = ''
    setGame(createGameFromSetup(nextCards, nextSetup, deckAssignments(nextSetup)))
  }, [cards, deckAssignments, setup])

  function setSetup(next: SetupState | ((current: SetupState) => SetupState)) {
    setSetupState((current) => typeof next === 'function' ? next(current) : next)
  }

  function updateSetup(next: SetupState) {
    const normalized = setupForMode(next)
    setSetupState(normalized)
    restart(cards, normalized)
  }

  function startConfiguredGame() {
    const normalized = setupForMode({ ...setup, setupStatus: 'mulligan' })
    setSetupState(normalized)
    restart(cards, normalized)
  }

  useEffect(() => {
    if (cardLibraryRef.current === cards) return
    cardLibraryRef.current = cards
    recordedGameId.current = ''
    setGame(createGameFromSetup(cards, setup, deckAssignments(setup)))
  }, [cards, deckAssignments, setup])

  useEffect(() => {
    if (game.stage !== 'game-over' || game.winner === null || recordedGameId.current === game.id) return
    const now = new Date().toISOString()
    const seatedUsers = Array.from({ length: setup.playerCount }, (_, index) => setup.userIds[index] || profiles[0]?.id || '')
    const winningSeatIds = game.winningTeamId === null
      ? [game.winner]
      : game.players.filter((player) => game.teamIds[player.id] === game.winningTeamId).map((player) => player.id)
    updateStats((currentProfiles) =>
      currentProfiles.map((profile) => {
        const seatIndexes = seatedUsers
          .map((userId, index) => userId === profile.id ? index : -1)
          .filter((index) => index >= 0)
        if (seatIndexes.length === 0) return profile
        const pointsScored = seatIndexes.reduce((sum, index) => sum + (game.players[index]?.points ?? 0), 0)
        const wins = seatIndexes.filter((index) => winningSeatIds.includes(index)).length
        const losses = seatIndexes.length - wins
        return {
          ...profile,
          stats: {
            ...profile.stats,
            gamesPlayed: profile.stats.gamesPlayed + seatIndexes.length,
            wins: profile.stats.wins + wins,
            losses: profile.stats.losses + losses,
            pointsScored: profile.stats.pointsScored + pointsScored,
            lastPlayedAt: now,
          },
        }
      }),
    )
    recordedGameId.current = game.id
  }, [game, profiles, setup.playerCount, setup.userIds, updateStats])

  return {
    activePlayer,
    deckAssignments: deckAssignments(setup),
    game,
    restart,
    setGame,
    setSetup,
    setup,
    startConfiguredGame,
    updateSetup,
  }
}
