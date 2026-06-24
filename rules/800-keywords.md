# Keywords

Source: Riftbound Core Rules.pdf, last updated 2026-03-30.

These notes preserve the numbered rule structure from the source PDF while normalizing spacing and PDF extraction artifacts.

## 800. Keywords

**801.** A Keyword is a specific term that appears on Cards that acts as a shorthand for a specific game effect, or ability of any variety.

**801.1.** A Keyword can be an ability.

**801.2.** Keywords can be identified by having a colored highlight behind them.

**801.2.a.** The color of the highlight has no effect on gameplay.

**801.3.** Keywords can be referenced or specified by other Game Effects.

**801.3.a.** Other effects may grant Keywords.

**801.3.a.1.** The definition and rules of the specific Keyword will determine the behavior if a Keyword is granted while it is already present.

**801.3.a.2.** The effect that granted the Keyword will specify the duration for which it is granted.

**801.3.a.3.** If an effect that grants a Keyword does not specify a duration, the duration is as long as that Game Object remains on the Board or in its current Non-Board Zone.

**801.3.b.** Other effects may remove Keywords.

**801.3.b.1.** The effect that removed the Keyword will specify the duration it is removed.

**801.3.b.2.** If an effect that removes a Keyword does not specify a duration, the duration is as long as that Game Object remains on the Board or in its current Non-Board Zone.

**802.** A card can have any number of Keywords.

**803.** Similar to other rules text, execute any effects of Keywords in the order listed when reading the card from top to bottom of the rules text.

### 804. Keyword Glossary

### 805. Accelerate

**805.1.** Accelerate is a Unit ability.

**805.1.a.** Accelerate is functionally short for "As you play me, you may pay [1] and 1 Power as an additional cost. If you do, I enter ready."

**805.1.a.1.** If the unit has a single domain, the Power portion of the Accelerate cost can be paid only with a Power that matches the domain of the unit.

**805.1.a.2.** If the unit has no domain or more than one domain, the Power portion of the Accelerate cost can be paid with [A] (a Power of any domain).

**805.2.** Accelerate is an Optional Additional Cost to be paid as a player plays the unit with the ability.

**805.2.a.** Accelerate costs cannot be paid while the unit is on the board, only as part of the steps of playing a card.

**805.3.** Accelerate has no function while on the board.

**805.4.** Multiple instances of Accelerate are redundant.

**805.5.** Accelerate, and whether or not a unit has Accelerate, is a characteristic of the Unit and may be checked or referenced by other Game Effects.

**805.6.** Accelerate influences the state of the unit entering the Board. It does not enter exhausted and then become ready.

**805.6.a.** Accelerate will not interact with, or trigger, abilities that are affected by units becoming ready.

### 806. Action

**806.1.** Action is a Permissive keyword.

**806.1.a.** It can be present on Cards, Rune Abilities, Legend Abilities or Permanent Abilities.

**806.1.b.** Action grants the corresponding card or effect permission to be played or activated during Showdowns, even when it is not the Controlling player's turn.

**806.1.c.** Action is functionally short for the following:

**806.1.c.1.** On Cards: "This can be played during showdowns on any player's turn."

**806.1.c.2.** On Rune, Legend, or Permanent Abilities: "This can be activated during showdowns on any player's turn."

**806.1.d.** Action is formatted as "[Action]" on spells, or "[Action][>]" on abilities.

**806.2.** The card or effect with this keyword is not restricted to showdowns. This permission is inclusive of all other timings and options available to the ability as written or by default.

**806.3.** Action does not alter the function of any instruction of the corresponding card or effect it is on. It is only permission. Example: Playing a Unit with Action still has the inherent restrictions of playing Units without Action. It can only be played to the controlling player's base or a battlefield they control.

**806.4.** Some passive abilities may grant a card or ability Action under certain conditions. The card or ability does not have the Action keyword unless and until those circumstances are true.

**806.4.a.** Those conditions might only be fulfilled while the card or ability is on the chain. In such a case, it can still be played or activated at the appropriate timing as long as doing so could fulfill the conditions.

**806.4.b.** If the chain item does not fulfill the conditions by the time step 5: check legality has been reached, the actions taken while playing or activating the chain item are undone and it is returned to the zone it was played from if it is a card.

**806.5.** Action is a referenceable characteristic.

**806.5.a.** Whether or not a Game Object has Action is a characteristic of that Game Object and may be checked or referenced by other Game Effects.

**806.5.b.** Whether or not a Spell has Action is a characteristic of that Spell and may be checked or referenced by other Game Effects.

**806.5.c.** Whether or not an Ability has Action is a characteristic of that Ability and may be checked or referenced by other Game Effects.

### 807. Assault

**807.1.** Assault is a Passive Ability keyword.

**807.1.a.** It is present on Units.

**807.1.b.** Assault is formatted as "Assault [X]".

**807.1.b.1.** The X is referenced in the functional text of the ability.

**807.1.b.2.** The X is referred to as the Assault Value.

**807.1.b.3.** If X is omitted, it is presumed to be 1.

**807.1.c.** It is functionally short for "While I am an attacker, I have +X [M]."

**807.1.d.** Being an attacker means the Unit has gained the Attacker designation during Combat. See rule 454. Combat for more information.

**807.1.d.1.** Assault remains in effect as long as the Unit maintains the Attacker designation.

**807.2.** If a Unit has Assault or has been granted Assault and is granted Assault by an additional source, the Assault Value of all granted Assault keywords is summed. Example: Petty Officer has Assault. It is chosen as the target of Cleave, which says "Give a unit [Assault 3] this turn." After Cleave resolves, Petty Officer has Assault 4 this turn.

**807.3.** Assault, and whether or not a unit has Assault, is a characteristic of the Unit and may be checked or referenced by other Game Effects.

### 808. Deathknell

**808.1.** Deathknell is a Triggered Ability keyword.

**808.1.a.** It is present on Permanents.

**808.1.b.** It is formatted as "[Deathknell][>] [Effect]".

**808.1.b.1.** [Effect] is the rules text for the specific instance of Deathknell. This is referred to as the Deathknell effect.

**808.1.c.** It is functionally short for "When I die, [Effect]."

**808.1.c.1.** [Effect] is the rules text of the Deathknell effect.

**808.1.d.** The Trigger for this effect is the Permanent being Killed and sent to the Trash.

**808.1.d.1.** If the Permanent with the effect is not sent to the Trash, for example because its "killed" event was replaced with a recall, the Deathknell will not occur.

**808.1.d.2.** The trigger will be added to the chain as a Pending Item before the card with Deathknell is moved to the trash due to a Kill instruction or a Cleanup.

**808.1.d.3.** Before the card with Deathknell is moved to the Trash, note its location, its attributes, and any other details related to the effect of its Deathknell to process the trigger after it has been Finalized.

**808.2.** Each instance of Deathknell a Permanent may have will trigger separately.

**808.2.a.** The controller will choose the order to add these Triggers to the chain.

**808.3.** Deathknell, and whether or not a permanent has Deathknell, is a characteristic of the permanent and may be checked or referenced by other Game Effects.

**809.** Deflect

**809.1.** Deflect is a Passive Ability keyword.

**809.1.a.** It is present on Permanents.

**809.1.b.** It is formatted as "Deflect [X]".

**809.1.b.1.** The X is referenced in the functional text of the ability.

**809.1.b.2.** The X is referred to as the Deflect Value.

**809.1.b.3.** If X is omitted, it is presumed to be 1.

**809.1.c.** It is functionally short for "Spells and abilities an opponent controls that choose me cost an amount of Power equal to [Deflect Value] more to play as an additional cost for each time they choose me."

**809.1.c.1.** The Power used to pay this cost may always be of any Domain. Example: A Fury spell targets an Order unit with Deflect. The Power used to pay the Deflect cost can be any Domain; it does not need to match the Domain of the spell or the target.

**809.1.d.** It is an effect that imposes a Mandatory Additional Cost on Spells and Abilities that choose the permanent that has this ability. See rule 349. Playing Cards for more information.

**809.2.** If a Unit has Deflect, or has been granted Deflect, and is granted Deflect by an additional source, the Deflect Value of all granted Deflect keywords is summed.

**809.3.** Deflect, and whether or not a permanent has Deflect, is a characteristic of the permanent and may be checked or referenced by other Game Effects.

### 810. Ganking

**810.1.** Ganking is a Passive Ability keyword.

**810.1.a.** It is present on Units.

**810.1.b.** It is functionally short for "I may move to a battlefield from another battlefield with a standard move."

**810.1.c.** It is a passive ability that adds permissions to the Unit's Standard Move.

**810.1.c.1.** It does not restrict or remove options from the Unit's Standard Move.

**810.1.c.2.** It does not have an activation cost.

**810.1.c.3.** It does not give additional abilities or activations of Movement, only new options for the Standard Move.

**810.2.** Multiple instances of Ganking are redundant.

**810.3.** Ganking, and whether or not a unit has Ganking, is a characteristic of the Unit and may be checked or referenced by other Game Effects.

### 811. Hidden

**811.1.** Hidden is a keyword that acts as a prerequisite to perform the Hide Discretionary Action.

**811.1.a.** It is present on Spells, Units, and Gear.

**811.1.b.** It is functionally short for "While this card is in your hand or in your Champion Zone on your turn during an Open State, you may pay [A] to hide this facedown at a battlefield you control that doesn't already have a facedown card hidden there for as long as you control that battlefield. Beginning on the next turn, this gains [Reaction] and you may play this, ignoring its base cost."

**811.1.c.** It allows the player to take the Discretionary Action Hide.

**811.1.c.1.** Hide is not a subset of Play.

**811.1.c.2.** Hiding a card does not open a chain.

**811.1.c.3.** Playing a card from facedown (or "from Hidden ") does open a chain.

**811.1.d.** Some choices made while playing a card from Hidden are restricted to the battlefield where it was hidden. A card cannot be played from Hidden if it is a spell with no valid targets under these restrictions. See rule 355.6. Targeting for more information.

**811.1.d.1.** A hidden permanent must be played to that battlefield.

**811.1.d.1.a.** This includes hidden gear, and overrides the normal restriction that gear have in only being allowed to be played to base.

**811.1.d.2.** If a hidden spell or a play effect of a hidden permanent chooses any targets, those targets must be chosen from among options at that battlefield, unless the ability explicitly restricts targeting in a way that makes this impossible. Example: Blastcone Fae is a unit with Hidden and "When you play me, give a unit -2 [M] this turn, to a minimum of 1 [M]." Because this is a play effect, its target must be chosen from among units at the same battlefield if Blastcone Fae was played from Hidden. Example: Tideturner is a unit with Hidden and "When you play me, you may choose a unit you control at another location. Move me to its location and it to my original location." Because its play ability has a targeting restriction that can never be fulfilled by a unit at its battlefield, its target may be chosen freely from among the available options.

**811.1.d.3.** If a hidden spell or a play effect of a hidden permanent causes you to play a unit, you must choose to play that unit at that battlefield.

**811.2.** Abilities and instructions of hidden cards other than the choices listed above function as normal. Example: Stand United is a spell that has Hidden and says "Buff a friendly unit. Buffs give an additional +1 might to friendly units this turn." If it's played from Hidden, the first part of its ability must choose a friendly unit at the same battlefield, but the second part of its ability affects all friendly units with buffs, no matter where they are.

**811.3.** Instead of being hidden, a card with Hidden may be played for its cost as normal, at its normal timing with no restrictions on targeting.

**811.4.** Multiple instances of Hidden are redundant.

**811.5.** Hidden, and whether or not a card has Hidden, is a characteristic of the card and may be checked or referenced by other Game Effects.

**811.5.a.** This is independent of the state of being facedown.

**811.6.** A card that is Hidden gains Reaction while facedown or played from facedown, and may be played any time a card with Reaction may be played as a result.

**811.6.a.** The property is granted to the card in its facedown state, and is not publicly known.

### 812. Legion

**812.1.** Legion is a Dependent Keyword.

**812.1.a.** It is formatted as "[Legion][>] [Text]".

**812.1.b.** Starting from the Keyword to the end of the clause, the entire statement is the Legion Ability.

**812.1.b.1.** Legion is functionally short for "If you have played another card this turn, this card gains [Text]."

**812.1.b.2.** The [Text] is the Dependent Ability.

**812.1.c.** As long as a card different than the one with the Legion ability has been Finalized on the same turn then the Dependent Ability is Active on the card with Legion.

**812.2.** All instances of Legion on cards a player controls are satisfied by that player playing a single card. Example: One card has three different Legion Abilities. The Legion Text of all three abilities will be active as long as one card has been played by the card's controller earlier in the same turn.

**812.3.** Legion, and whether or not a card has Legion, is a characteristic of the card and may be checked or referenced by other Game Effects.

### 813. Reaction

**813.1.** Reaction is a Permissive keyword.

**813.1.a.** It can be present on Cards, Rune Abilities, Legend Abilities and Permanent Abilities.

**813.1.b.** Reaction grants the corresponding card or effect all abilities and permissions of Action.

**813.1.c.** Reaction, additionally, is functionally short for the following:

**813.1.c.1.** On Cards: "This can be played during Closed States on any player's turn."

**813.1.c.2.** On Rune, Legend, or Permanent Abilities: "This can be activated during Closed States on any player's turn."

**813.1.d.** Reaction is formatted as "[Reaction]" on cards, or "[Reaction][>]" on abilities.

**813.2.** The corresponding card or effect with this keyword is not restricted to Closed States or Showdowns. This permission is inclusive of all other timings and options available to the ability as written, Action's permissions, or by default.

**813.3.** Reaction does not alter the function of any instruction of the Card, Rune, or Effect it is on. It is only Permission.

**813.3.a.** Playing Units with Reaction still has the inherent restrictions of playing Units without Reaction. It can only be played to the controlling player's base or a battlefield they control.

**813.4.** Some passive abilities may grant a card or ability Reaction under certain conditions. The card or ability does not have the Reaction keyword unless and until those circumstances are true.

**813.4.a.** Those conditions might only be fulfilled while the card or ability is on the chain. In such a case, it can still be played or activated at the appropriate timing as long as doing so could fulfill the conditions.

**813.4.b.** If the chain item does not fulfill the conditions by the time step 5: check legality has been reached, the actions taken while playing or activating the chain item are undone and it is returned to the zone it was played from if it is a card.

**813.5.** Reaction is a referencable characteristic.

**813.5.a.** Whether or not a Game Object has Reaction is a characteristic of that Game Object and may be checked or referenced by other Game Effects.

**813.5.b.** Whether or not a Spell has Reaction is a characteristic of that Spell and may be checked or referenced by other Game Effects.

**813.5.c.** Whether or not an Ability has Reaction is a characteristic of that Ability and may be checked or referenced by other Game Effects.

### 814. Shield

**814.1.** Shield is a Passive Ability keyword.

**814.1.a.** It is present on Units.

**814.1.b.** Shield is formatted as "Shield [X]".

**814.1.b.1.** The X is referenced in the functional text of the ability.

**814.1.b.2.** The X is referred to as the Shield Value.

**814.1.b.3.** If X is omitted, it is presumed to be 1.

**814.1.c.** It is functionally short for "While I am a defender, I have +X [M]."

**814.1.d.** Being a defender means the Unit has gained the Defender designation during Combat. See rule 454. Combat for more information.

**814.1.d.1.** Shield remains in effect as long as the Unit maintains the Defender designation.

**814.2.** If a Unit has Shield, or has been granted Shield, and is granted Shield by an additional source, the Shield Value of all granted Shield keywords is summed. Example: Stalwart Poro has Shield. It is chosen as the target of Block, which says "Give a unit [Shield 3] and [Tank] this turn." After Block resolves, Stalwart Poro has Shield 4 this turn.

**814.3.** Shield, and whether or not a unit has Shield, is a characteristic of the Unit and may be checked or referenced by other Game Effects.

### 815. Tank

**815.1.** Tank is a Passive Ability keyword.

**815.1.a.** It is present on Units.

**815.1.b.** It is functionally short for "I must be assigned lethal damage before any other unit with the same controller as me that does not have [Tank] during the Combat Damage step."

**815.1.c.** It alters how players can elect to assign combat damage during combat.

**815.1.c.1.** Players must still assign lethal damage to a unit before moving to the next when assigning their damage.

**815.1.c.2.** If more than one unit with Tank is present with the same controller in Combat, damage may be assigned to any of them. Units without Tank are invalid assignments until all units with Tank have lethal damage assigned to them.

**815.2.** Multiple instances of Tank are redundant.

**815.3.** Tank, and whether or not a unit has Tank, is a characteristic of the Unit and may be checked or referenced by other Game Effects.

### 816. Temporary

**816.1.** Temporary is a Triggered Ability keyword.

**816.1.a.** It is present on Permanents.

**816.1.b.** It is functionally short for "At the start of this permanent's controller's Beginning Phase, before scoring, kill this."

**816.1.c.** The Trigger Condition is the controller of the permanent's Beginning Phase starting.

**816.2.** Multiple instances of Temporary are redundant.

**816.2.a.** Regardless of how many instances there are, the ability will only trigger once.

**816.3.** Temporary, and whether or not a permanent has Temporary, is a characteristic of the permanent and may be checked or referenced by other Game Effects.

### 817. Vision

**817.1.** Vision is a Triggered Ability keyword.

**817.1.a.** It is present on Permanents.

**817.1.b.** It is functionally short for "When this is played, look at the top card of your Main Deck. You may recycle it."

**817.1.c.** The trigger is the permanent entering the Board.

**817.2.** Multiple instances of Vision trigger separately.

**817.2.a.** The player may choose to recycle or not recycle for each instance of Vision separately.

**817.2.b.** If the player does not recycle the top card and nothing else happens in between the triggers resolving, each instance of Vision will see the same card.

**817.3.** Vision, and whether or not a permanent has Vision, is a characteristic of the permanent and may be checked or referenced by other Game Effects.

### 818. Equip

**818.1.** Equip is an Activated Ability keyword.

**818.1.a.** Equip is normally present on Gear with the tag Equipment.

**818.1.b.** Equip has a cost to activate and Attaches the card with Equip to a chosen Unit when the cost is paid.

**818.1.b.1.** Equip's choice is a Target.

**818.1.b.2.** The chosen Unit will become the Top-Most Card for the Attach action.

**818.1.c.** Equip is formatted as "Equip [Cost]"

**818.1.c.1.** If paying costs or making choices for this ability causes triggered abilities to trigger, they will be placed on the chain above this ability in a Pending state. See rule 376. Activated Abilities for more information.

**818.1.c.2.** Equip is functionally short for "[Cost]: Attach this gear to a unit you control."

**818.1.c.3.** Equip costs may include both resource costs and non-resource costs.

**818.1.c.4.** Equip abilities may also include text that alters the Equip cost. Such text is taken into account when determining a card's Equip cost for any reason.

**818.1.c.5.** Equip abilities may include text that alters the timing or targeting of the Equip ability.

**818.2.** When the Attach action completes from this keyword, the Unit that was chosen is considered to have been Equipped by the Gear with this ability.

**818.2.a.** This is an event other Game Effects and Triggered Abilities can reference.

**818.3.** Equipped is the state of a Top-Most Card being Attached by one or more cards that are Equipment.

**818.3.a.** The state of being Equipped is synchronous with that of the Attached state of the Equipment.

**818.3.b.** A Top-Most Card is Equipped as long as one or more of its Attached cards are Equipment.

**818.3.c.** The state of being Equipped corresponds to a Top-Most card having a card with Equip that is Attached to it.

**818.4.** Multiple instances of Equip are equivalent to multiple Activated Abilities and can each be activated separately by paying the corresponding costs.

**818.5.** Equip, and whether or not a Gear has Equip, is a characteristic of the Gear and may be checked or referenced by other Game Effects.

**818.5.a.** Whether or not a Gear has Equip may be referenced even if the Rules Text of the Gear is Inactive. See rule 716. Attachment for more information.

### 819. Quick-Draw

**819.1.** Quick-Draw is a Triggered Ability keyword. It is also a Permissive keyword.

**819.1.a.** Quick-Draw is present on Gear with Equip abilities.

**819.1.b.** Cards with Quick-Draw have Reaction inherently.

**819.1.c.** Quick-Draw allows cards to be played and Attached using Reaction timing.

**819.1.d.** Quick-Draw is functionally short for "[Reaction]" and "When you play this, attach it to a Unit you control." See rule 716. Attachment for more information.

**819.2.** Multiple instances of Quick-Draw do not trigger separately and have no effect beyond the first.

**819.3.** Quick-Draw, and whether or not a gear has Quick-Draw, is a characteristic of the Gear and may be checked or referenced by other Game Effects.

### 820. Repeat

**820.1.** Repeat is an Optional Additional Cost keyword.

**820.1.a.** Repeat is present on Spells.

**820.1.b.** Repeat is an optional cost that a player may pay to execute the effect of their spells a second time.

**820.1.c.** Repeat is formatted as "Repeat [Cost]"

**820.1.c.1.** The Cost is an Additional Cost to be paid during the steps of playing the spell.

**820.1.c.2.** If a spell has more than one instance of Repeat, each Cost may be paid or not paid individually.

**820.1.c.3.** Each Repeat Cost can be paid only a single time.

**820.1.d.** Repeat is functionally short for "You may pay [Cost] as an additional cost as you play this spell. If you do, execute the instructions of this spell one additional time."

**820.1.d.1.** When the additional cost is paid, the effect of the spell, upon resolution, will be performed an additional time. Example: Desert's Call is a spell with [Repeat] [2] and "Play a 2 [S] Sand Soldier unit token." If its controller pays its Repeat cost as they play it, the card's instruction to play a Sand Soldier is executed twice, as though the card says "Play a 2 [S] Sand Soldier unit token. Play a 2 [S] Sand Soldier unit token."

**820.2.** When a spell's effect is performed an additional time with Repeat, choices must be made at the usual time during the Make Relevant Choices step of Playing a Card. See rule 349. Playing Cards for more information.

**820.2.a.** Choices made for the additional execution do not have to be the same as the choices made for the initial execution. Example: Rocket Barrage is a spell with [Repeat] [4][C] and "Choose one - Deal 4 to a unit in a base. [or] Kill a gear." If Rocket Barrage's controller pays its Repeat cost as they play it, they may choose the same mode or a different one, and if they choose the same mode, may choose the same target or a different one. If they choose "Kill a gear" twice and choose two different gear, they must specify which gear is the first target and which is the second. As the spell resolves, those two gear will be killed in the chosen order.

**820.3.** Multiple instances of Repeat can be paid for separately. The spell's instructions will be executed an additional time for each instance of Repeat that is paid for.

**820.3.a.** Regardless of the number of times a spell's instructions are executed with this keyword, the spell is only Played once.

**820.4.** Repeat, and whether or not a spell has Repeat, is a characteristic of the Spell and may be checked or referenced by other Game Effects.

### 821. Weaponmaster

**821.1.** Weaponmaster is a Triggered Ability keyword.

**821.1.a.** Weaponmaster is present on Units.

**821.1.b.** Weaponmaster is a Play Effect that chooses an Equipment you control and allows you to pay its Equip cost at a discount, regardless of the usual timing of the Equip ability, to Attach that Equipment to the unit with Weaponmaster.

**821.1.c.** Weaponmaster is functionally short for: "When you play me, you may choose a Card you control with the Equipment tag. Necessary portions of its Rules Text are no longer Inactive if they are currently Inactive. Pay the cost of its Equip ability, reduced by [A], to attach it to this unit." See rule 716. Attachment for more information.

**821.1.c.1.** Weaponmaster can choose an Equipment whether it has an Equip ability or not.

**821.1.c.2.** The cost of the Equip ability is determined as though that Equip ability was being activated choosing the unit with the Weaponmaster ability, as modulated by any abilities that alter Equip costs.

**821.1.c.3.** If the chosen card's Equip cost does not contain [A], it can still be paid, but will not be reduced.

**821.1.c.4.** If the chosen card doesn't have an Equip cost, it can't be paid.

**821.1.c.5.** If the chosen card's Equip cost can't be paid, if it can't be detached from its current Top-Most card, or if it can't be attached to the unit with the Weaponmaster ability, it stays in its current location, Attached to anything it was already Attached to.

**821.1.c.6.** The Equip ability is not activated this way, and the unit with the Weaponmaster ability is not chosen.

**821.1.c.7.** Multiple instances of Weaponmaster trigger separately, and can choose different targets.

**821.1.d.** If you choose the same target with multiple instances of Weaponmaster, each will resolve separately.

**821.2.** Weaponmaster has no function while on the board.

**821.3.** Weaponmaster, and whether or not a unit has Weaponmaster, is a characteristic of the Unit and may be checked or referenced by other Game Effects.

### 822. Ambush

**822.1.** Ambush is a Passive Ability keyword.

**822.1.a.** It is present on Units.

**822.1.b.** It is functionally short for "I may be played to a battlefield where you control Units" and "I have [Reaction] as long as I'm being played to a battlefield where you control Units."

**822.1.c.** It is a passive ability that adds options to locations that are valid for a Unit to be played to during the Make Relevant Choices step of Playing a Card See rule 349. Playing Cards for more information

**822.2.** Multiple instances of Ambush are redundant.

**822.3.** If there are no units at the location chosen before Finalization completes for any reason, then it is no longer a valid location by Ambush 's reasoning and cannot be played there

**822.3.a.** Other effects and permissions may still enable this Unit to be able to be played to the selected location, but Ambush's permission will not be valid

**822.4.** Ambush, and whether or not a unit has Ambush, is a characteristic of the Unit and may be checked or referenced by other Game Effects.

### 823. Hunt

**823.1.** Hunt is a Triggered Ability keyword.

**823.1.a.** Hunt is present on Units.

**823.1.b.** Hunt is both a Conquer and a Hold effect.

**823.1.c.** Hunt is formatted as "Hunt X"

**823.1.c.1.** Hunt is functionally short for: "When I Conquer or Hold, my controller gains X XP." See rule 728. XP for more information

**823.1.c.2.** If X is omitted, it is presumed to be 1.

**823.1.c.3.** X is referred to as the Hunt Value.

**823.2.** If a Unit has Hunt, or has been granted Hunt, and is granted Hunt by an additional source, the Hunt Value of all granted Hunt keywords is summed.

**823.3.** Hunt, and whether or not a unit has Hunt, is a characteristic of the Unit and may be checked or referenced by other Game Effects.

### 824. Level

**824.1.** Level is a Dependent Keyword.

**824.1.a.** It is formatted as "[Level [N]][>] [Text]".

**824.1.b.** Starting from the Keyword to the end of the clause, the entire statement is the Level Ability.

**824.1.b.1.** It is functionally short for "While you have [N] or more XP, this card gains "[Text]"."

**824.1.b.2.** The [Text] here is the Dependent Ability

**824.1.c.** As long as the controlling player has [N] XP, then the Dependent Ability will be be Active on the card with Level

**824.1.c.1.** If the controller of the card with Level changes, then the Dependent Ability will be rendered Active or Inactive based on the new controller's XP.

**824.1.d.** The Dependent Ability will be Inactive as soon as the controlling player has less than [N] XP.

**824.2.** Level, and whether or not a card has Level is a characteristic of the card and may be checked or referenced by other Game Effects.

### 825. Unique

**825.1.** Unique is a Deck Constraint Permission.

**825.2.** Unique is not functionally short for any rules text, and instead provides a restriction to players during Deck Construction.

**825.2.a.** A Deck can contain only one card of a given name if the card has Unique

**825.2.b.** If a card is a Signature card and is also Unique, then that Deck can contain any combination of three Signature cards, but still only one of each named Unique card.

**825.3.** Cards with Unique have no additional effects during gameplay.

**825.4.** Unique, and whether or not a Card has Unique, is a characteristic of the Unit and may be checked or referenced by other Game Effects.

### 826. Backline

**826.1.** Backline is a Passive Ability keyword.

**826.2.** It is present on Units.

**826.3.** It is functionally short for "I must be assigned lethal damage after any other unit with the same controller as me that does not have [Backline] during the Combat Damage step."

**826.4.** It alters how players can elect to assign combat damage during combat.

**826.4.a.** Players must still assign lethal damage to a unit before moving to the next when assigning their damage.

**826.4.b.** If more than one unit with Backline is present with the same controller in Combat, damage may be assigned to any of them. Units with Backline are invalid assignments until all units without Backline have lethal damage assigned to them.

**826.5.** Multiple Instances of Backline are redundant.

**826.6.** Backline, and whether or not a unit has Backline, is a characteristic of the Unit and may be checked or referenced by other Game Effects.
