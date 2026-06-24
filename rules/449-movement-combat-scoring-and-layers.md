# Movement, Combat, Scoring, and Layers

Source: Riftbound Core Rules.pdf, last updated 2026-03-30.

These notes preserve the numbered rule structure from the source PDF while normalizing spacing and PDF extraction artifacts.

### 449. Recalls

**450.** A Recall is when a Permanent is relocated from anywhere to its Base without it being a Move.

**451.** Recalls are not Moves.

**451.1.** They do not cause Triggered Abilities to trigger that are triggered by Move actions.

**451.2.** A Recall causes a Permanent to change locations.

**451.3.** A Recall cannot be prevented by actions and Game Effects that restrict or block Movement.

**452.** Gear can be Recalled.

**452.1.** When an un-attached Gear is created or played at a battlefield, or is at a battlefield for any other reason, it is Recalled to its controller's base during the next Cleanup. Example: An Equipment is attached to a unit at a battlefield, so the Equipment is present at that battlefield. If the unit dies, the Equipment will be recalled during the next cleanup.

**453.** Recalls do not affect the state of the Permanent being recalled.

**453.1.** Unless otherwise stated by the source of the Recall, Damage, Exhausted Status, Buffed Status, and applied Layer alterations will all remain unaffected by a Recall.

### 454. Combat

**455.** A Combat occurs when a Cleanup occurs, there are no items on the Chain, there is a staged Combat at a Battlefield, and no Showdown or Combat is ongoing at any other Battlefield. See rule 318. Cleanups for more information.

**455.1.** If there is an ongoing Showdown at the Battlefield where the Combat is staged, that Showdown will become a Combat Showdown and a Combat will be initiated there.

**456.** Combat is considered Staged if there are units controlled by two opposing players at a Battlefield but the Steps of Combat have not been initiated.

**456.1.** If more than one Battlefield has Units controlled by opposing players at it at the same time, the Turn Player decides which Combat to resolve first.

**456.2.** If Staged Combats stop being Staged before the Steps of Combat are initiated, they are not resolved or executed.

**456.3.** If a Combat and Showdown are staged at the same Battlefield and the turn player initiates the Showdown, it will open as a Combat Showdown.

**457.** Combat can only occur between Units controlled by exactly two players.

**457.1.** In Modes of Play with more than two players, Battlefields with Staged Combats or Combats in Progress are Invalid Destinations for Moves of all kinds (Standard Moves or otherwise) by Units controlled by Players not involved in those Combats or who don't already have Units at that Battlefield. See rule 442.2.a. for more information on Invalid Destinations.

**457.2.** In Modes of Play with more than two players, Battlefields with Staged Combats or Combats in Progress are Invalid to be chosen as a location to play one or more Units by a player not involved in that Combat by any means,

**457.2.a.** If an effect would require a Unit be played to a Battlefield with a Staged Combat or a Combat in Progress, where the controller of the played unit is not a participant, instead the Unit is played to its controller's Base.

**457.2.b.** Any subsequent reference to "here" in the corresponding effect is reassigned to the Controller's Base, where the Unit was played. Any further effects that may be invalidated are invalidated as if the effect was mistargeted. See rule 355.6. Targeting for more information on Mistargeting.

**457.3.** All choices that would result in a Combat occurring between more than two players simultaneously are invalid and ineligible to be completed.

### 458. The Steps of Combat

**459.** Step 1: The Combat Showdown Step

**459.1.** Combat will open in one of two ways: when a Combat and Showdown are staged at the same Battlefield and the turn player initiates the Showdown; or when the turn is in a Showdown Open State and a Combat is staged at the Battlefield where the current Showdown is ongoing. See rule 318. Cleanups for more information.

**459.2.** When Combat opens, it either opens with a Combat Showdown, or the current Showdown becomes a Combat Showdown.

**459.2.a.** The following Tasks become Outstanding, in the order described:

**459.2.b.** 1. Establish who is Attacker and who is Defender.

**459.2.b.1.** The Attacker is the player whose unit(s) applied the Contested status to the Battlefield. They gain the Attacker designation now.

**459.2.b.1.a.** This player gains Focus as the showdown begins.

**459.2.b.2.** The Defender is the player who did not apply the Contested status to the Battlefield. They gain the Defender designation now.

**459.2.b.3.** Units at the Contested Battlefield controlled by the Attacker gain the Attacker designation now.

**459.2.b.3.a.** If a Unit controlled by the Attacker becomes present at this Battlefield after this moment, it will gain the Attacker designation during the Cleanup phase following the action that caused it to become present

**459.2.b.4.** Units at the Contested Battlefield controlled by the Defender gain the Defender designation now.

**459.2.b.4.a.** If a Unit controlled by the Defender becomes present at this Battlefield after this moment it will gain the Defender designation during the Cleanup phase following the action that caused it to become present

**459.2.c.** 2. The Attacker gains Focus.

**459.2.d.** 3. Add items to the Combat Chain if establishing Attacker and Defender has caused Triggered Abilities to become Pending.

**459.2.d.1.** The Attacking player, who has Focus, places Triggered Abilities on the Chain first, followed by all non- Defender players in Turn Order, followed by the Defending Player.

**459.2.e.** The State Closes if a Combat Chain was created.

**459.2.e.1.** Otherwise the Combat Showdown continues, with the State Open as normal.

**459.2.f.** Players proceed with any play on the Chain as normal.

**459.2.g.** Focus does not pass upon closure of the Combat Chain, if any.

**460.** Step 2: The Combat Damage Step

**460.1.** If both Attacking and Defending units remain at this battlefield, the following Tasks become Outstanding, in the specified order:

**460.2.** 1. When the Showdown closes, Attackers and Defenders resolve Combat Damage at the Battlefield that was attacked, using their current Might.

**460.2.a.** Sum the Might of all Attacking Units.

**460.2.b.** Sum the Might of all Defending Units.

**460.2.c.** Starting with the Attacker, each player assigns an amount of damage equal to their summed Might among the other's Units.

**460.2.c.1.** Assigning Damage is not Dealing Damage.

**460.2.c.1.a.** When all Damage is assigned, it will be Dealt simultaneously. These actions are not synonymous.

**460.2.c.2.** Abilities or effects may influence the order in which damage is assigned. Reminder: Lethal Damage is non-zero damage equaling or exceeding the Might of a Unit.

**460.2.c.3.** Units must have lethal damage assigned to them in full before damage is assigned to a different Unit. Example: If a player has 5 damage to distribute among four 3 Might units, they may not choose to assign 2 damage to one of the units and 1 damage to each of the remaining 3. They must assign at least 3 damage to one, and the remaining 2 to another.

**460.2.c.4.** Units cannot have more damage assigned to them than the minimum required to constitute lethal damage unless no further units remain to have damage assigned to them.

**460.2.c.5.** A player must obey all requirements and restrictions on damage assignment if able. Example: A player is assigning damage to the following units: a unit with Tank ("I must be assigned combat damage first. "); Caitlyn, Patrolling ("I must be assigned combat damage last. "); and another unit without any abilities. That player must assign combat damage first to the unit with Tank, then to the unit with no abilities, then to Caitlyn.

**460.2.c.6.** If multiple Units have abilities or effects that require a player to assign them damage with the same priority, that player may assign damage to those units in any order. Example: A player is assigning damage to the following units: two units with Tank ("I must be assigned combat damage first. ") and one unit with no abilities. That player chooses one of the units with Tank and assigns combat damage to it. Then they must assign any remaining damage first to the other unit with Tank, then to the unit with no abilities.

**460.2.c.7.** If a Unit has one or more Abilities or effects applying to it that demand it be assigned damage in a specific way that is exclusionary, then the assigning player chooses only one of those abilities to apply when assigning damage. Example: Caitlyn, Patrolling ("I must be assigned combat damage last. ") has been given the Tank ability ("I must be assigned combat damage first. "). A player is assigning damage to this Caitlyn with Tank and two units with no abilities. That player can't fulfill both of Caitlyn's damage requirements, so they may choose to assign damage to Caitlyn first, fulfilling the Tank requirement, or last, fulfilling Caitlyn's printed requirement. They can't choose to apply damage to Caitlyn in between the other two units, because that wouldn't fulfill either requirement.

**460.2.c.8.** If there is more than one unit in which this situation applies to, each unit is dealt with individually. The assigning player chooses which ability or effect applies, and then resolves the assignment. If this creates a situation where now more than one unit must be assigned with the same priority, those units may be assigned damage in any order as normal within that priority. Example: Two copies of Caitlyn, Patrolling ("I must be assigned combat damage last. ") have been given the Tank ability ("I must be assigned combat damage first. "). A player assigning damage to these two Caitlyns and one unit with no abilities could choose to fulfill both Caitlyns' Tank requirements by assigning them both damage before the other unit.

**460.2.c.9.** If a unit cannot be dealt damage, then no amount of damage can be considered lethal. Such a unit is exempt from any considerations of mandatory assignment. Example: Kayn, Unleashed says "If I have moved twice this turn, I don't take damage." While Kayn can't take damage, it is ignored for the purposes of assigning lethal damage in combat.

**460.2.d.** Deal Damage to each unit equal to the amount assigned to it.

**460.3.** 2. Skip the FEPR process and proceed to the Resolution Step.

**461.** Step 3: The Resolution Step

**461.1.** 1. Perform a Combat Cleanup.

**461.1.a.** Invoke a Combat Special Cleanup.

**461.1.a.1.** Insert "3c. Heal all Units."

**461.1.a.2.** Insert "3d. Recall Attackers present at the Battlefield if Defenders are still present." See rule 449. Recalls for more information.

**461.2.** The following Task becomes Outstanding:

**461.3.** 1. Determine Combat Result

**461.3.a.** A Player has won a combat if they received either the attacker or defender designation and are the only Player that has units remaining at this battlefield during this step.

**461.3.b.** A Player has lost a combat if they received either the attacker or defender designation and are the only Player that does not have any units remaining at this battlefield during this step.

**461.3.c.** Units at this battlefield inherit the same combat result as their controllers

**461.3.d.** There is "No Result" if either both Players have units present during this step, or neither player has units present during this step.

**461.3.d.1.** If "No Result" was reached, and both players have units remaining, stage a Combat at this battlefield. 461.4 The following Task becomes Outstanding:

**461.5.** 1. If no Showdown or Combat is staged at this location, the player with Units remaining here Establishes Control.

**461.5.a.** Clear the Contested Status.

**461.5.b.** If there are no Units remaining here controlled by any player, the Battlefield becomes Uncontrolled.

**461.5.c.** Remove all Hidden cards from this Battlefield that do not share a controller with the Battlefield.

**461.5.d.** Establishing Control results in a Conquer if that player has not yet scored this Battlefield this turn. See rule 185. Control for more information on Control. See rule 464.1. for more information on Conquering.

**461.5.e.** This does not have to be the player that applied Contested to the Battlefield.

**461.6.** The following Task becomes Outstanding:

**461.7.** 1. Combat ends.

**461.7.a.** Remove Attacker and Defender Designation from all Units and Players. 461.7.b All "this combat" effects expire simultaneously.

### 462. Scoring

**463.** Scoring is the act of a Player gaining a point through the process of seizing or maintaining control over Battlefields.

**463.1.** Every instance of Scoring is also an instance of Gaining points

**464.** A player Scores in one of two ways:

**464.1.** Conquer: A player gains Control of a Battlefield they did not yet Score this turn.

**464.1.a.** In Modes of Play with teammates, Battlefields under the Control of a teammate during the Beginning Phase are also disqualified from being Scored through Conquer by any means.

**464.1.b.** A player will gain control of a Battlefield after establishing Control by applying Contested first.

**464.2.** Hold: A player maintains Control of a Battlefield during their Beginning Phase.

**465.** A player may only Score, from either method, once per Battlefield per turn.

**466.** When a player Scores, two things occur:

**466.1.** The player Gains up to one Point, depending on their current score.

**466.1.a.** The Winning Point has additional restrictions.

**466.1.a.1.** Notably, points Gained from sources that are not Conquer or Hold are not beholden to these restrictions.

**466.1.b.** When a player tries to Gain a Point through a Score, and their current Point Total is 1 point from the Victory Score of the Mode of Play or higher, the following occurs:

**466.1.b.1.** If the player has Scored through Hold, that player Gains the Winning Point.

**466.1.b.2.** If the player has Scored through a Conquer and has Scored every Battlefield through either method this turn, that player Gains the Winning Point. If the player has Scored through a Conquer and has not Scored every Battlefield this turn, that player draws a card.

**466.2.** Trigger Score abilities at the Battlefield that Scored.

**466.2.a.** Conquer abilities trigger at a Battlefield that was Conquered.

**466.2.b.** Hold abilities trigger at a Battlefield that was Held.

**466.2.c.** These will only trigger when the Battlefield is Scored; I.E. These cannot be triggered more than once per turn for a player.

**467.** When a cleanup occurs and a player has accrued Points greater than or equal to the Victory Score for their Mode of Play, and if they have more points than any opponent, they Win the Game.

### 468. Layers

**469.** Layers are the mechanism in which Game Effects alter the Traits, Intrinsic Abilities, or other properties of Game Objects.

**470.** Layers are an organizational structure.

**470.1.** Layers only serve to structure the application and order that Game Effects apply to Game Objects to maintain consistency.

**471.** The layers are applied repeatedly until all effects operating on objects have been applied once and no changes have been processed.

**471.1.** Layers are applied in sequence. Each effect in them is applied as soon as able, and only a single time across all sequences.

**471.2.** When a sequence of applications completes, recur the process, and evaluate each layer again applying any effects that may now be applicable.

**471.3.** The removal or disqualification of an effect is separate from the application of the effect, but still can only be applied once. Example: Fiora, Victorious has printed Might 4 and says "While I'm Mighty, I have Deflect, Ganking, and Shield." If a player places a buff on Fiora, her Might is increased in the Arithmetic layer, after the layer for Ability-Altering Effects. The Ability-Altering Effect layer is then re-checked and the abilities Deflect, Ganking, and Shield applied. Since each effect has been applied once and there are no other effects to apply, Fiora's characteristics are finalized as 5 Might with Deflect, Ganking, and Shield. While a buffed Fiora, Victorious is in combat as a defender, an additional +1 Might will be applied in the Arithmetic layer, giving her 6 Might and the 3 keywords. Example: A buffed Fiora, Victorious is in combat as a defender when her buff is removed. Reevaluating the layers in sequence, she no longer gains Deflect, Ganking, and Shield during the Ability-Altering Effect layer, so when the Arithmetic layer is evaluated, neither the buff (which is gone) nor Shield (which she no longer has) apply. She goes directly from 6 Might with three keywords to 4 Might with no keywords.

**472.** Layers are applied in the following order:

**472.1.** 1. Trait-Altering Effects

**472.1.a.** This layer deals with effects that grant, remove, or replace inherent traits of Game Objects. Name Super Type Type Tags Controller Cost Domain

**472.1.a.1.** Assignment of Might is dealt with in this layer. Example: A spell reads "A unit's Might becomes 4 this turn." The unit's Might is set to 4 in this layer.

**472.1.b.** Copy effects are applied in this layer.

**472.1.b.1.** When one Game Object becomes a copy of another, all Traits, including the Rules Text, replace or are added to those of the original Game Object as specified by the Game Effect directing the Copy. This is applied in this layer.

**472.1.b.2.** Some Game Effects may specify copying certain traits of a card - only the traits specified by the Game Effect will be copied.

**472.1.b.3.** Copy effects will copy the copyable traits of a Game Object: by default, those are the printed traits of the Game Object. When a Game Object becomes a copy of something, its copyable traits are updated to the new traits it has received. Example: A player triggers Leblanc, Deceiver's hold effect and plays a Reflection token, making it a copy of Honest Broker. That player then plays Mirror Image, targeting the Reflection token. When the Mirror Image Reflection token is played, it copies all of the copyable traits of the original Reflection token - which are currently those of Honest Broker which it is a copy of. That player will have three units named Honest Broker in play, two of which are token Copies with Temporary.

**472.1.c.** Effects for this layer can be identified by the phrase "become(s)", "give," "is," or "are" in the text. Example: A permanent has the ability "Other friendly units are Yordles." Other friendly units gain the Yordle tag in this layer.

**472.2.** 2. Ability-Altering Effects

**472.2.a.** This layer deals with effects that grant, remove, or replace the abilities or rules text of Game Objects. Keywords Passive Abilities Appending rules text Removing rules text Duplicating Rules Text from one Game Object to another

**472.2.b.** Effects for this layer can be identified by the phrase "become(s)," "give," "lose(s)," "have," "has," "is," or "are" in the text. Example: A permanent has the ability "Other friendly units have [Vision]." Other friendly units gain the Vision keyword in this layer.

**472.2.c.** Abilities of Effect Text of Attached cards are appended in this layer.

**472.3.** 3. Arithmetic

**472.3.a.** This layer deals with the mathematics of increasing and decreasing the numeric values of the traits of Game Objects. Might Energy Cost Power Cost

**472.3.b.** When an arithmetic effect has a limitation that applies, it is limited at the time of its application, and is "remembered" at that limited level for the duration of its effect. This process is called "snapshotting." Example: If an effect gives a unit "-4 [M] to a min of 1 this turn" choosing a unit with 2 [M], then the effect will generate -1 [M] this turn.

**472.3.c.** Might Bonuses of Attached cards are applied in this layer.

**472.3.d.** This layer applies arithmetic in the following way.

**472.3.d.1.** 1. Increases

**472.3.d.1.a.** Positive values, or increases, to Might are applied first.

**472.3.d.1.b.** If there is a restriction or limitation to this increase, the limitation is "snapshotted" for the duration of the effect.

**472.3.d.2.** 2. Decreases

**472.3.d.2.a.** Negative values, or decreases, to Might are applied last.

**472.3.d.2.b.** If there is a restriction or limitation to this decrease, the limitation is snapshotted for the duration of the effect.

**473.** If more than one effect applies to the same Game Object in the Same Layer, or to each other in the same layer, then both effects will apply but their order may be determined by Dependency.

**473.1.** A Dependency is established if:

**473.1.a.** Applying one of the effects alters the existence of the other; or

**473.1.b.** Applying one of the effects alters the number of objects the other effect can influence

**473.1.c.** Applying one of the effects alters the outcome when applying the other.

**474.** To determine which effect Depends on another, determine which of the prior criteria applies, and then also which effect's evaluation is altered by the sequence of applications. That effect is said to Depend on the other.

**474.1.** To resolve a dependency, the effects within the same layer that created the dependency must be applied such that: 1. Identify which effect Depends on the other within the Layer. 2. Apply the effect that is depended on first. 3. Immediately apply the effect that Depends on the first effect next.

**475.** If more than one effect applies in the same layer but no dependency is established, then Timestamp order is applied to the effects within that layer and sublayer

**475.1.** When an effect begins applying, it establishes a time for which it is compared against other Game Effects for purposes of resolving Layered effects as its Timestamp.

**475.1.a.** Timestamps are not rote values.

**475.1.b.** Timestamps are relative comparisons between effects and when they began applying to the game.

**475.1.c.** Timestamps are not referenced by Game Effects in any way. They are only used to finalize layered effects.

**475.2.** When Rules Text becomes Inactive for any reason, it loses its Timestamp. When it ceases to be Inactive, a new Timestamp is established.

**475.3.** Effects are applied such that the earliest Timestamp within each Layer and Sublayer applies first, followed by other Effects in that Layer and Sublayer in chronological order.
