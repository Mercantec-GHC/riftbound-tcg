# Riftbound Core Rules

Source: Riftbound Core Rules.pdf, last updated 2026-03-30.

This directory contains the core rules split into focused Markdown documents. The files keep the original rule numbers so implementation work can reference stable rule IDs.

## Files

- [Golden and Silver Rules](./000-golden-and-silver-rules.md) - rules 000-099 (18 entries)
- [Deck Construction, Setup, and Zones](./100-deck-construction-setup-and-zones.md) - rules 100-119 (124 entries)
- [Game Objects and Cards](./120-game-objects-and-cards.md) - rules 120-299 (370 entries)
- [Turn Structure, Priority, and Cleanups](./300-turn-structure-priority-and-cleanups.md) - rules 300-324 (143 entries)
- [Chains, Showdowns, and Playing Cards](./325-chains-showdowns-and-playing-cards.md) - rules 325-359 (235 entries)
- [Abilities](./360-abilities.md) - rules 360-406 (181 entries)
- [Game Actions](./407-game-actions.md) - rules 407-448 (363 entries)
- [Movement, Combat, Scoring, and Layers](./449-movement-combat-scoring-and-layers.md) - rules 449-475 (149 entries)
- [Modes of Play and Conceding](./476-modes-of-play-and-conceding.md) - rules 476-699 (112 entries)
- [Additional Rules and Terms](./700-additional-rules-and-terms.md) - rules 700-799 (110 entries)
- [Keywords](./800-keywords.md) - rules 800-899 (282 entries)

## Implementation Notes

- Treat these documents as rules references for engine implementation, not as client authority.
- Preserve deterministic behavior when converting a rule into engine logic.
- Add or update engine tests when implementing rules behavior from these documents.
